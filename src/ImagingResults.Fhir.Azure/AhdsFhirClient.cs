using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using ImagingResults.Fhir.R4;
using ImagingResults.Validation;

namespace ImagingResults.Fhir.Azure;

public sealed class AhdsFhirClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions().ForFhir();

    private readonly AhdsFhirOptions _options;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AhdsFhirClient(AhdsFhirOptions options)
        : this(options, new DefaultAzureCredential(), new HttpClient(), true)
    {
    }

    public AhdsFhirClient(
        AhdsFhirOptions options,
        TokenCredential credential,
        HttpClient httpClient)
        : this(options, credential, httpClient, false)
    {
    }

    private AhdsFhirClient(
        AhdsFhirOptions options,
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

    public Task<AhdsFhirUploadResult> UploadAsync(
        MeasurementReport report,
        CancellationToken cancellationToken = default)
    {
        var mapping = new FhirR4Mapper().Map(report);
        return UploadAsync(mapping, cancellationToken);
    }

    public Task<AhdsFhirUploadResult> UploadAsync(
        FhirR4MappingResult mapping,
        CancellationToken cancellationToken = default)
    {
        if (mapping is null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        return UploadAsync(mapping.Bundle, mapping.Diagnostics, cancellationToken);
    }

    public Task<AhdsFhirUploadResult> UploadAsync(
        Bundle collectionBundle,
        CancellationToken cancellationToken = default) =>
        UploadAsync(
            collectionBundle,
            Array.Empty<ValidationDiagnostic>(),
            cancellationToken);

    public static Bundle CreateTransactionBundle(Bundle collectionBundle)
    {
        if (collectionBundle is null)
        {
            throw new ArgumentNullException(nameof(collectionBundle));
        }

        if (collectionBundle.Type != Bundle.BundleType.Collection)
        {
            throw new ArgumentException(
                "Only a FHIR collection Bundle can be converted to an AHDS transaction.",
                nameof(collectionBundle));
        }

        var transaction = new Bundle
        {
            Type = Bundle.BundleType.Transaction,
            Timestamp = collectionBundle.Timestamp
        };

        foreach (var sourceEntry in collectionBundle.Entry)
        {
            var resource = sourceEntry.Resource
                ?? throw new ArgumentException(
                    "Every collection Bundle entry must contain a resource.",
                    nameof(collectionBundle));
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                throw new ArgumentException(
                    "Every resource must have a deterministic ID before upload.",
                    nameof(collectionBundle));
            }

            transaction.Entry.Add(new Bundle.EntryComponent
            {
                FullUrl = sourceEntry.FullUrl,
                Resource = resource,
                Request = new Bundle.RequestComponent
                {
                    Method = Bundle.HTTPVerb.PUT,
                    Url = $"{resource.TypeName}/{resource.Id}"
                }
            });
        }

        return transaction;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<AhdsFhirUploadResult> UploadAsync(
        Bundle collectionBundle,
        IReadOnlyList<ValidationDiagnostic> mappingDiagnostics,
        CancellationToken cancellationToken)
    {
        var transaction = CreateTransactionBundle(collectionBundle);
        var payload = JsonSerializer.Serialize(transaction, JsonOptions);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var token = await _credential.GetTokenAsync(
                    new TokenRequestContext(new[] { _options.Scope }),
                    cancellationToken).ConfigureAwait(false);
                using var request = CreateRequest(payload, token.Token);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseBundle = string.IsNullOrWhiteSpace(responseBody)
                        ? null
                        : DeserializeBundle(responseBody, response.StatusCode);
                    return new AhdsFhirUploadResult(
                        transaction,
                        responseBundle,
                        response.StatusCode,
                        mappingDiagnostics);
                }

                if (IsRetryable(response.StatusCode) && attempt < _options.MaxRetryAttempts)
                {
                    await System.Threading.Tasks.Task.Delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw CreateServiceException(response.StatusCode, responseBody);
            }
            catch (HttpRequestException)
                when (attempt < _options.MaxRetryAttempts)
            {
                await System.Threading.Tasks.Task.Delay(
                    GetRetryDelay(null, attempt),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                throw new AhdsFhirException(
                    "The AHDS FHIR request failed before a service response was received.",
                    innerException: exception);
            }
        }
    }

    private HttpRequestMessage CreateRequest(string payload, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.ServiceUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/fhir+json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        return request;
    }

    private Bundle DeserializeBundle(string responseBody, HttpStatusCode statusCode)
    {
        try
        {
            return JsonSerializer.Deserialize<Bundle>(responseBody, JsonOptions)
                ?? throw new JsonException("The response did not contain a FHIR Bundle.");
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new AhdsFhirException(
                "AHDS returned a successful response that was not a valid FHIR transaction-response Bundle.",
                statusCode,
                responseBody: responseBody,
                innerException: exception);
        }
    }

    private AhdsFhirException CreateServiceException(
        HttpStatusCode statusCode,
        string responseBody)
    {
        OperationOutcome? outcome = null;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                outcome = JsonSerializer.Deserialize<OperationOutcome>(responseBody, JsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                // Preserve the original body when AHDS or an intermediary returns non-FHIR content.
            }
        }

        var detail = outcome?.Issue
            .Select(issue => issue.Diagnostics ?? issue.Details?.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"AHDS rejected the FHIR transaction with HTTP {(int)statusCode} ({statusCode})."
            : $"AHDS rejected the FHIR transaction: {detail}";
        return new AhdsFhirException(message, statusCode, outcome, responseBody);
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
        {
            return delta <= _options.MaxRetryDelay ? delta : _options.MaxRetryDelay;
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
}
