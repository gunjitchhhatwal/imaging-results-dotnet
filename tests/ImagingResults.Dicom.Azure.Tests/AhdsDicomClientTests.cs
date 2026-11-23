using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using FellowOakDicom;
using ImagingResults.Dicom.Azure;
using ImagingResults.Tests;

namespace ImagingResults.Dicom.Azure.Tests;

public sealed class AhdsDicomClientTests
{
    [Fact]
    public void Options_validate_endpoint_and_use_dicom_audience()
    {
        Assert.Throws<ArgumentException>(() =>
            new AhdsDicomOptions(new Uri("/dicom", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() =>
            new AhdsDicomOptions(new Uri("http://example.test/dicom")));

        var options = new AhdsDicomOptions(
            new Uri("https://example.test/dicom"));

        Assert.Equal(
            "https://example.test/dicom/",
            options.ServiceUrl.AbsoluteUri);
        Assert.Equal(AhdsDicomOptions.DefaultScope, options.Scope);
        Assert.Equal(
            new Uri("https://example.test/dicom/v2/studies"),
            options.StoreEndpoint);
    }

    [Fact]
    public async System.Threading.Tasks.Task Single_sr_uses_authenticated_single_part_stow()
    {
        byte[]? requestBody = null;
        Uri? requestUri = null;
        HttpMethod? requestMethod = null;
        AuthenticationHeaderValue? authorization = null;
        MediaTypeHeaderValue? contentType = null;
        IReadOnlyList<MediaTypeWithQualityHeaderValue>? accept = null;
        var handler = new StubHandler(async (request, _, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            requestMethod = request.Method;
            authorization = request.Headers.Authorization;
            contentType = request.Content!.Headers.ContentType;
            accept = request.Headers.Accept.ToArray();
            requestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return DicomJsonResponse(HttpStatusCode.OK, SuccessfulResponseJson);
        });
        var credential = new StubCredential();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(credential, httpClient);

        var result = await client.StoreAsync(TestReportFactory.Create());
        var uploaded = DicomFile.Open(new MemoryStream(requestBody!));

        Assert.Equal(
            new Uri("https://example.test/dicom/v2/studies"),
            requestUri);
        Assert.Equal(HttpMethod.Post, requestMethod);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal("test-token", authorization.Parameter);
        Assert.Equal("application/dicom", contentType!.MediaType);
        Assert.Contains(
            accept!,
            value => value.MediaType == "application/dicom+json");
        Assert.Equal(
            DicomUID.ComprehensiveSRStorage.UID,
            uploaded.Dataset.GetSingleValue<string>(DicomTag.SOPClassUID));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("https://example.test/retrieve/study", result.RetrieveUrl);
        Assert.Equal("1.2.3.4", Assert.Single(result.StoredInstances).SopInstanceUid);
        Assert.False(result.IsPartial);
        Assert.Equal(
            AhdsDicomOptions.DefaultScope,
            Assert.Single(credential.RequestedScopes));
    }

    [Fact]
    public async System.Threading.Tasks.Task Multiple_instances_use_multipart_and_explicit_upsert()
    {
        string? mediaType = null;
        string? relatedType = null;
        string[]? partTypes = null;
        HttpMethod? method = null;
        var handler = new StubHandler((request, _, _) =>
        {
            method = request.Method;
            var multipart = Assert.IsType<MultipartContent>(request.Content);
            mediaType = multipart.Headers.ContentType!.MediaType;
            relatedType = multipart.Headers.ContentType.Parameters
                .Single(parameter => parameter.Name == "type")
                .Value;
            partTypes = multipart
                .Select(part => part.Headers.ContentType!.MediaType!)
                .ToArray();
            return System.Threading.Tasks.Task.FromResult(
                DicomJsonResponse(HttpStatusCode.OK, SuccessfulResponseJson));
        });
        var options = new AhdsDicomOptions(
            new Uri("https://example.test/dicom"))
        {
            StoreMode = AhdsDicomStoreMode.Upsert,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };
        using var httpClient = new HttpClient(handler);
        using var client = new AhdsDicomClient(
            options,
            new StubCredential(),
            httpClient);

        var first = new ImagingResults.Dicom.Tid1500Writer()
            .Write(TestReportFactory.Create());
        var secondReport = CreateSecondReport();
        var second = new ImagingResults.Dicom.Tid1500Writer()
            .Write(secondReport);
        await client.StoreAsync(new[] { first, second });

        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("multipart/related", mediaType);
        Assert.Equal("\"application/dicom\"", relatedType);
        Assert.Equal(
            new[] { "application/dicom", "application/dicom" },
            partTypes);
    }

    [Fact]
    public async System.Threading.Tasks.Task Accepted_response_exposes_partial_failures()
    {
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromResult(
                DicomJsonResponse(HttpStatusCode.Accepted, PartialResponseJson)));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        var result = await client.StoreAsync(TestReportFactory.Create());
        var failure = Assert.Single(result.FailedInstances);

        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
        Assert.True(result.IsPartial);
        Assert.Equal((ushort)272, failure.FailureReason);
        Assert.Equal((ushort)45063, failure.WarningReason);
        Assert.Equal("Dataset does not match the SOP class.", failure.ErrorComment);
    }

    [Fact]
    public async System.Threading.Tasks.Task Retryable_status_and_transport_failure_are_retried()
    {
        var handler = new StubHandler((_, attempt, _) =>
        {
            if (attempt == 1)
            {
                return System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("Temporary network failure."));
            }

            if (attempt == 2)
            {
                return System.Threading.Tasks.Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Headers =
                        {
                            RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero)
                        }
                    });
            }

            return System.Threading.Tasks.Task.FromResult(
                DicomJsonResponse(HttpStatusCode.OK, SuccessfulResponseJson));
        });
        var options = new AhdsDicomOptions(
            new Uri("https://example.test/dicom"))
        {
            StoreMode = AhdsDicomStoreMode.Upsert,
            MaxRetryAttempts = 2,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };
        using var httpClient = new HttpClient(handler);
        using var client = new AhdsDicomClient(
            options,
            new StubCredential(),
            httpClient);

        var result = await client.StoreAsync(TestReportFactory.Create());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_mode_does_not_retry_ambiguous_transport_failure()
    {
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Response was lost.")));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        await Assert.ThrowsAsync<AhdsDicomException>(() =>
            client.StoreAsync(TestReportFactory.Create()));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Upsert_mode_retries_http_client_timeout()
    {
        var handler = new StubHandler((_, attempt, _) =>
            attempt == 1
                ? System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException("HTTP client timeout."))
                : System.Threading.Tasks.Task.FromResult(
                    DicomJsonResponse(HttpStatusCode.OK, SuccessfulResponseJson)));
        var options = new AhdsDicomOptions(
            new Uri("https://example.test/dicom"))
        {
            StoreMode = AhdsDicomStoreMode.Upsert,
            MaxRetryAttempts = 1,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };
        using var httpClient = new HttpClient(handler);
        using var client = new AhdsDicomClient(
            options,
            new StubCredential(),
            httpClient);

        var result = await client.StoreAsync(TestReportFactory.Create());

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Rejected_stow_preserves_dicom_failure_details()
    {
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromResult(
                DicomJsonResponse(HttpStatusCode.Conflict, PartialResponseJson)));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        var exception = await Assert.ThrowsAsync<AhdsDicomException>(() =>
            client.StoreAsync(TestReportFactory.Create()));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal(
            "Dataset does not match the SOP class.",
            Assert.Single(exception.StoreResult!.FailedInstances).ErrorComment);
        Assert.Contains(
            "Dataset does not match",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Invalid_success_body_is_protocol_error()
    {
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "not-dicom-json",
                        Encoding.UTF8,
                        "text/plain")
                }));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        var exception = await Assert.ThrowsAsync<AhdsDicomException>(() =>
            client.StoreAsync(TestReportFactory.Create()));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("not-dicom-json", exception.ResponseBody);
    }

    [Fact]
    public async System.Threading.Tasks.Task Required_attributes_are_validated_before_http()
    {
        var handler = new StubHandler((_, _, _) =>
            throw new InvalidOperationException("HTTP must not be called."));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);
        var file = new ImagingResults.Dicom.Tid1500Writer()
            .Write(TestReportFactory.Create(patientId: null));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.StoreAsync(file));

        Assert.Contains(
            "missing required attribute",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.StoreAsync(Array.Empty<DicomFile>()));
    }

    [Fact]
    public async System.Threading.Tasks.Task Cancellation_is_propagated_without_retry()
    {
        var handler = new StubHandler((_, _, cancellationToken) =>
            System.Threading.Tasks.Task.FromCanceled<HttpResponseMessage>(
                cancellationToken));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.StoreAsync(
                TestReportFactory.Create(),
                cancellationSource.Token));

        Assert.True(handler.RequestCount <= 1);
    }

    private static AhdsDicomClient CreateClient(
        TokenCredential credential,
        HttpClient httpClient) =>
        new(TestOptions(), credential, httpClient);

    private static AhdsDicomOptions TestOptions() =>
        new(new Uri("https://example.test/dicom"))
        {
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };

    private static MeasurementReport CreateSecondReport()
    {
        var source = TestReportFactory.Create();
        return new MeasurementReport(
            source.StudyInstanceUid,
            "1.2.826.0.1.3680043.10.511.22",
            "1.2.826.0.1.3680043.10.511.23",
            source.CreatedAt,
            source.Groups,
            source.Observer,
            source.ProcedureReported,
            source.PatientId);
    }

    private static HttpResponseMessage DicomJsonResponse(
        HttpStatusCode statusCode,
        string body) =>
        new(statusCode)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/dicom+json")
        };

    private const string SuccessfulResponseJson =
        """
        [{
          "00081190": { "vr": "UR", "Value": ["https://example.test/retrieve/study"] },
          "00081199": {
            "vr": "SQ",
            "Value": [{
              "00081150": { "vr": "UI", "Value": ["1.2.840.10008.5.1.4.1.1.88.34"] },
              "00081155": { "vr": "UI", "Value": ["1.2.3.4"] }
            }]
          }
        }]
        """;

    private const string PartialResponseJson =
        """
        [{
          "00081199": {
            "vr": "SQ",
            "Value": [{
              "00081150": { "vr": "UI", "Value": ["1.2.840.10008.5.1.4.1.1.88.34"] },
              "00081155": { "vr": "UI", "Value": ["1.2.3.4"] }
            }]
          },
          "00081198": {
            "vr": "SQ",
            "Value": [{
              "00081150": { "vr": "UI", "Value": ["1.2.840.10008.5.1.4.1.1.88.34"] },
              "00081155": { "vr": "UI", "Value": ["1.2.3.5"] },
              "00081197": { "vr": "US", "Value": [272] },
              "00081196": { "vr": "US", "Value": [45063] },
              "00000902": { "vr": "LO", "Value": ["Dataset does not match the SOP class."] }
            }]
          }
        }]
        """;

    private sealed class StubCredential : TokenCredential
    {
        public List<string> RequestedScopes { get; } = new();

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            CreateToken(requestContext);

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(CreateToken(requestContext));

        private AccessToken CreateToken(TokenRequestContext requestContext)
        {
            RequestedScopes.AddRange(requestContext.Scopes);
            return new AccessToken(
                "test-token",
                DateTimeOffset.UtcNow.AddMinutes(10));
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, int, CancellationToken, System.Threading.Tasks.Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return send(request, RequestCount, cancellationToken);
        }
    }
}
