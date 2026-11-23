using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using ImagingResults.Fhir.Azure;
using ImagingResults.Fhir.R4;
using ImagingResults.Tests;

namespace ImagingResults.Fhir.Azure.Tests;

public sealed class AhdsFhirClientTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions().ForFhir();

    [Fact]
    public void Options_require_secure_absolute_service_url_and_create_default_scope()
    {
        Assert.Throws<ArgumentException>(() =>
            new AhdsFhirOptions(new Uri("/fhir", UriKind.Relative)));
        Assert.Throws<ArgumentException>(() =>
            new AhdsFhirOptions(new Uri("http://example.test/fhir")));

        var options = new AhdsFhirOptions(
            new Uri("https://example.test/fhir"));

        Assert.Equal(
            "https://example.test/fhir/",
            options.ServiceUrl.AbsoluteUri);
        Assert.Equal(
            "https://example.test/fhir/.default",
            options.Scope);
    }

    [Fact]
    public void Collection_is_converted_to_deterministic_put_transaction()
    {
        var collection = new FhirR4Mapper().Map(TestReportFactory.Create()).Bundle;

        var transaction = AhdsFhirClient.CreateTransactionBundle(collection);

        Assert.Equal(Bundle.BundleType.Transaction, transaction.Type);
        Assert.Equal(collection.Entry.Count, transaction.Entry.Count);
        Assert.Equal(
            collection.Entry.Select(entry => entry.FullUrl),
            transaction.Entry.Select(entry => entry.FullUrl));
        Assert.All(transaction.Entry, entry =>
        {
            Assert.Equal(Bundle.HTTPVerb.PUT, entry.Request!.Method);
            Assert.Equal(
                $"{entry.Resource!.TypeName}/{entry.Resource.Id}",
                entry.Request.Url);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task Upload_posts_authenticated_fhir_transaction()
    {
        string? requestBody = null;
        AuthenticationHeaderValue? authorization = null;
        MediaTypeHeaderValue? contentType = null;
        Uri? requestUri = null;
        var handler = new StubHandler(async (request, _, cancellationToken) =>
        {
            requestUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            contentType = request.Content!.Headers.ContentType;
            requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return FhirResponse(HttpStatusCode.OK, new Bundle
            {
                Type = Bundle.BundleType.TransactionResponse
            });
        });
        var credential = new StubCredential();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(credential, httpClient);

        var result = await client.UploadAsync(TestReportFactory.Create());
        var posted = JsonSerializer.Deserialize<Bundle>(requestBody!, JsonOptions);

        Assert.Equal(new Uri("https://example.test/fhir/"), requestUri);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal("test-token", authorization.Parameter);
        Assert.Equal("application/fhir+json", contentType!.MediaType);
        Assert.Equal(Bundle.BundleType.Transaction, posted!.Type);
        Assert.All(posted.Entry, entry =>
            Assert.Equal(Bundle.HTTPVerb.PUT, entry.Request!.Method));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(Bundle.BundleType.TransactionResponse, result.ResponseBundle!.Type);
        Assert.Equal(
            "https://example.test/fhir/.default",
            Assert.Single(credential.RequestedScopes));
        Assert.Contains(result.MappingDiagnostics, diagnostic =>
            diagnostic.Code == "FHIR.R4.IMAGING_EVIDENCE_IDENTIFIER_REFERENCE");
    }

    [Fact]
    public async System.Threading.Tasks.Task Retryable_response_honors_bounded_retry_policy()
    {
        var handler = new StubHandler((_, attempt, _) =>
            System.Threading.Tasks.Task.FromResult(
                attempt == 1
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) }
                    }
                    : FhirResponse(HttpStatusCode.OK, new Bundle
                    {
                        Type = Bundle.BundleType.TransactionResponse
                    })));
        var options = new AhdsFhirOptions(new Uri("https://example.test/fhir"))
        {
            MaxRetryAttempts = 2,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };
        using var httpClient = new HttpClient(handler);
        using var client = new AhdsFhirClient(options, new StubCredential(), httpClient);

        var result = await client.UploadAsync(
            new FhirR4Mapper().Map(TestReportFactory.Create()));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Transient_transport_failure_is_retried()
    {
        var handler = new StubHandler((_, attempt, _) =>
            attempt == 1
                ? System.Threading.Tasks.Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("Temporary network failure."))
                : System.Threading.Tasks.Task.FromResult(
                    FhirResponse(HttpStatusCode.OK, new Bundle
                    {
                        Type = Bundle.BundleType.TransactionResponse
                    })));
        var options = new AhdsFhirOptions(new Uri("https://example.test/fhir"))
        {
            MaxRetryAttempts = 1,
            RetryBaseDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero
        };
        using var httpClient = new HttpClient(handler);
        using var client = new AhdsFhirClient(options, new StubCredential(), httpClient);

        var result = await client.UploadAsync(
            new FhirR4Mapper().Map(TestReportFactory.Create()));

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, handler.RequestCount);
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
            client.UploadAsync(
                new FhirR4Mapper().Map(TestReportFactory.Create()),
                cancellationSource.Token));

        Assert.True(handler.RequestCount <= 1);
    }

    [Fact]
    public async System.Threading.Tasks.Task Nonretryable_operation_outcome_is_preserved()
    {
        var outcome = new OperationOutcome
        {
            Issue = new List<OperationOutcome.IssueComponent>
            {
                new()
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.Invalid,
                    Diagnostics = "Invalid imaging reference."
                }
            }
        };
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromResult(
                FhirResponse(HttpStatusCode.BadRequest, outcome)));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        var exception = await Assert.ThrowsAsync<AhdsFhirException>(() =>
            client.UploadAsync(new FhirR4Mapper().Map(TestReportFactory.Create())));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(
            "Invalid imaging reference.",
            Assert.Single(exception.OperationOutcome!.Issue).Diagnostics);
        Assert.Contains("Invalid imaging reference", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task Invalid_success_body_is_reported_as_protocol_error()
    {
        var handler = new StubHandler((_, _, _) =>
            System.Threading.Tasks.Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-fhir", Encoding.UTF8, "text/plain")
            }));
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(new StubCredential(), httpClient);

        var exception = await Assert.ThrowsAsync<AhdsFhirException>(() =>
            client.UploadAsync(new FhirR4Mapper().Map(TestReportFactory.Create())));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("not-fhir", exception.ResponseBody);
    }

    [Fact]
    public void Transaction_rejects_noncollection_or_unidentified_resources()
    {
        Assert.Throws<ArgumentException>(() =>
            AhdsFhirClient.CreateTransactionBundle(new Bundle
            {
                Type = Bundle.BundleType.Batch
            }));

        var collection = new Bundle { Type = Bundle.BundleType.Collection };
        collection.Entry.Add(new Bundle.EntryComponent
        {
            Resource = new Observation()
        });
        Assert.Throws<ArgumentException>(() =>
            AhdsFhirClient.CreateTransactionBundle(collection));
    }

    private static AhdsFhirClient CreateClient(
        TokenCredential credential,
        HttpClient httpClient) =>
        new(
            new AhdsFhirOptions(new Uri("https://example.test/fhir"))
            {
                RetryBaseDelay = TimeSpan.Zero,
                MaxRetryDelay = TimeSpan.Zero
            },
            credential,
            httpClient);

    private static HttpResponseMessage FhirResponse(
        HttpStatusCode statusCode,
        Resource resource) =>
        new(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(resource, JsonOptions),
                Encoding.UTF8,
                "application/fhir+json")
        };

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
