using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FellowOakDicom;

namespace ImagingResults.Dicom.Azure;

public sealed class AhdsDicomClient : IDisposable
{
    private const string DicomMediaType = "application/dicom";
    private const string DicomJsonMediaType = "application/dicom+json";

    private readonly AhdsDicomOptions _options;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AhdsDicomClient(AhdsDicomOptions options)
        : this(options, new DefaultAzureCredential(), new HttpClient(), true)
    {
    }

    public AhdsDicomClient(
        AhdsDicomOptions options,
        TokenCredential credential,
        HttpClient httpClient)
        : this(options, credential, httpClient, false)
    {
    }

    private AhdsDicomClient(
        AhdsDicomOptions options,
        TokenCredential credential,
        HttpClient httpClient,
        bool ownsHttpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _options.Validate();
    }

    public Task<AhdsDicomStoreResult> StoreAsync(
        MeasurementReport report,
        CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return StoreAsync(
            new ImagingResults.Dicom.Tid1500Writer().Write(report),
            cancellationToken);
    }

    public Task<AhdsDicomStoreResult> StoreAsync(
        DicomFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return StoreAsync(new[] { file }, cancellationToken);
    }

    public async Task<AhdsDicomStoreResult> StoreAsync(
        IEnumerable<DicomFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files is null)
        {
            throw new ArgumentNullException(nameof(files));
        }

        var payloads = files
            .Select((file, index) => Serialize(file, index, nameof(files)))
            .ToArray();
        if (payloads.Length == 0)
        {
            throw new ArgumentException(
                "At least one DICOM instance is required.",
                nameof(files));
        }

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var token = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { _options.Scope }),
                    cancellationToken).ConfigureAwait(false);
                using var request = CreateRequest(payloads, token.Token);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return ParseStoreResult(response.StatusCode, responseBody);
                }

                if (IsRetryable(response.StatusCode) && CanRetry(attempt))
                {
                    await Task.Delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw CreateServiceException(response.StatusCode, responseBody);
            }
            catch (HttpRequestException)
                when (CanRetry(attempt))
            {
                await Task.Delay(
                    GetRetryDelay(null, attempt),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested
                    && CanRetry(attempt))
            {
                await Task.Delay(
                    GetRetryDelay(null, attempt),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new AhdsDicomException(
                    "The AHDS DICOM request failed before a service response was received.",
                    innerException: exception);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new AhdsDicomException(
                    "The AHDS DICOM request timed out before a service response was received.",
                    innerException: exception);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private HttpRequestMessage CreateRequest(
        IReadOnlyList<byte[]> payloads,
        string token)
    {
        var method = _options.StoreMode == AhdsDicomStoreMode.Upsert
            ? HttpMethod.Put
            : HttpMethod.Post;
        var request = new HttpRequestMessage(method, _options.StoreEndpoint)
        {
            Content = CreateContent(payloads)
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(DicomJsonMediaType));
        return request;
    }

    private static HttpContent CreateContent(IReadOnlyList<byte[]> payloads)
    {
        if (payloads.Count == 1)
        {
            var content = new ByteArrayContent(payloads[0]);
            content.Headers.ContentType =
                new MediaTypeHeaderValue(DicomMediaType);
            return content;
        }

        var multipart = new MultipartContent(
            "related",
            $"imaging-results-{Guid.NewGuid():N}");
        multipart.Headers.ContentType!.Parameters.Add(
            new NameValueHeaderValue("type", $"\"{DicomMediaType}\""));
        foreach (var payload in payloads)
        {
            var part = new ByteArrayContent(payload);
            part.Headers.ContentType =
                new MediaTypeHeaderValue(DicomMediaType);
            multipart.Add(part);
        }

        return multipart;
    }

    private static byte[] Serialize(
        DicomFile? file,
        int index,
        string parameterName)
    {
        if (file is null)
        {
            throw new ArgumentException(
                $"DICOM instance at index {index} is null.",
                parameterName);
        }

        ValidateRequiredAttributes(file.Dataset, index, parameterName);
        using var stream = new MemoryStream();
        file.Save(stream);
        return stream.ToArray();
    }

    private static void ValidateRequiredAttributes(
        DicomDataset dataset,
        int index,
        string parameterName)
    {
        var requiredUidTags = new[]
        {
            DicomTag.StudyInstanceUID,
            DicomTag.SeriesInstanceUID,
            DicomTag.SOPInstanceUID,
            DicomTag.SOPClassUID
        };
        foreach (var tag in requiredUidTags)
        {
            if (!dataset.Contains(tag)
                || string.IsNullOrWhiteSpace(
                    dataset.GetSingleValueOrDefault(tag, string.Empty)))
            {
                throw new ArgumentException(
                    $"DICOM instance at index {index} is missing required attribute {tag}.",
                    parameterName);
            }
        }

        if (!dataset.Contains(DicomTag.PatientID))
        {
            throw new ArgumentException(
                $"DICOM instance at index {index} is missing required attribute {DicomTag.PatientID}.",
                parameterName);
        }
    }

    private AhdsDicomException CreateServiceException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        AhdsDicomStoreResult? result = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                result = ParseStoreResult(statusCode, responseBody);
            }
            catch (AhdsDicomException)
            {
                // Preserve non-DICOM JSON response bodies from gateways and intermediaries.
            }
        }

        var failed = result?.FailedInstances.FirstOrDefault();
        var detail = failed?.ErrorComment
            ?? (failed?.FailureReason is { } reason
                ? $"DICOM failure reason 0x{reason:X4}."
                : null);
        var message = detail is null
            ? $"AHDS rejected the DICOM STOW-RS request with HTTP {(int)statusCode} ({statusCode})."
            : $"AHDS rejected the DICOM STOW-RS request: {detail}";
        return new AhdsDicomException(
            message,
            statusCode,
            result,
            responseBody);
    }

    private static AhdsDicomStoreResult ParseStoreResult(
        HttpStatusCode statusCode,
        string responseBody)
    {
        if (statusCode == HttpStatusCode.NoContent
            && string.IsNullOrWhiteSpace(responseBody))
        {
            return new AhdsDicomStoreResult(
                statusCode,
                null,
                Array.Empty<AhdsDicomInstanceResult>(),
                Array.Empty<AhdsDicomInstanceResult>(),
                responseBody);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var dataset = GetResponseDataset(document.RootElement);
            var retrieveUrl = GetStringValue(dataset, "00081190");
            var stored = GetSequence(dataset, "00081199")
                .Select(ParseInstance)
                .ToArray();
            var failed = GetSequence(dataset, "00081198")
                .Select(ParseFailedInstance)
                .ToArray();
            return new AhdsDicomStoreResult(
                statusCode,
                retrieveUrl,
                stored,
                failed,
                responseBody);
        }
        catch (Exception exception)
            when (exception is JsonException
                or InvalidOperationException
                or FormatException)
        {
            throw new AhdsDicomException(
                "AHDS returned a response that was not valid DICOM JSON.",
                statusCode,
                responseBody: responseBody,
                innerException: exception);
        }
    }

    private static JsonElement GetResponseDataset(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Array
            && root.GetArrayLength() > 0
            && root[0].ValueKind == JsonValueKind.Object)
        {
            return root[0];
        }

        throw new JsonException(
            "The DICOM JSON response must contain a dataset object.");
    }

    private static IReadOnlyList<JsonElement> GetSequence(
        JsonElement dataset,
        string tag)
    {
        if (!dataset.TryGetProperty(tag, out var element)
            || !element.TryGetProperty("Value", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .ToArray();
    }

    private static AhdsDicomInstanceResult ParseInstance(JsonElement item) =>
        new(
            GetStringValue(item, "00081150"),
            GetStringValue(item, "00081155"));

    private static AhdsDicomInstanceResult ParseFailedInstance(
        JsonElement item)
    {
        var failedAttributes = GetSequence(item, "00741048");
        var errorComment = GetStringValue(item, "00000902")
            ?? failedAttributes
                .Select(attribute => GetStringValue(attribute, "00000902"))
                .FirstOrDefault(value => value is not null);
        return new AhdsDicomInstanceResult(
            GetStringValue(item, "00081150"),
            GetStringValue(item, "00081155"),
            GetUShortValue(item, "00081197"),
            GetUShortValue(item, "00081196"),
            errorComment);
    }

    private static string? GetStringValue(
        JsonElement dataset,
        string tag)
    {
        if (!dataset.TryGetProperty(tag, out var element)
            || !element.TryGetProperty("Value", out var value)
            || value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() == 0)
        {
            return null;
        }

        var first = value[0];
        return first.ValueKind == JsonValueKind.String
            ? first.GetString()
            : first.ToString();
    }

    private static ushort? GetUShortValue(
        JsonElement dataset,
        string tag)
    {
        var value = GetStringValue(dataset, tag);
        return ushort.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private TimeSpan GetRetryDelay(
        HttpResponseMessage? response,
        int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
        {
            return delta <= _options.MaxRetryDelay
                ? delta
                : _options.MaxRetryDelay;
        }

        if (retryAfter?.Date is { } date)
        {
            var dateDelay = date - DateTimeOffset.UtcNow;
            if (dateDelay > TimeSpan.Zero)
            {
                return dateDelay <= _options.MaxRetryDelay
                    ? dateDelay
                    : _options.MaxRetryDelay;
            }
        }

        var multiplier = Math.Pow(2, attempt);
        var milliseconds = Math.Min(
            _options.RetryBaseDelay.TotalMilliseconds * multiplier,
            _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private bool CanRetry(int attempt) =>
        _options.StoreMode == AhdsDicomStoreMode.Upsert
        && attempt < _options.MaxRetryAttempts;
}
