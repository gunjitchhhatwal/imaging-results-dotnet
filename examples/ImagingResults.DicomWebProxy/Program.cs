using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;

const string dicomScope =
    "https://dicom.healthcareapis.azure.com/.default";

var builder = WebApplication.CreateBuilder(args);
var configuredServiceUrl = builder.Configuration["dicom-service-url"];
if (string.IsNullOrWhiteSpace(configuredServiceUrl))
{
    throw new InvalidOperationException(
        "--dicom-service-url must identify the upstream AHDS DICOM service.");
}

var serviceUrl = new Uri(
    configuredServiceUrl,
    UriKind.Absolute);
var upstreamBase = new Uri(serviceUrl, "v2/");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Services.AddSingleton<TokenCredential>(
    new DefaultAzureCredential(
        new DefaultAzureCredentialOptions
        {
            TenantId = builder.Configuration["AZURE_TENANT_ID"]
        }));
builder.Services.AddHttpClient(
    "dicomweb",
    client => client.Timeout = TimeSpan.FromMinutes(5));

var app = builder.Build();
app.UseCors();

app.MapMethods(
    "/dicom/{**path}",
    new[] { "GET", "HEAD", "POST", "PUT", "DELETE" },
    ProxyAsync);
app.MapGet(
    "/health",
    () => Results.Ok(
        new
        {
            status = "ready",
            upstream = upstreamBase.ToString()
        }));

app.Run();

async Task ProxyAsync(
    HttpContext context,
    TokenCredential credential,
    IHttpClientFactory httpClientFactory)
{
    var path = context.Request.RouteValues["path"] as string ?? string.Empty;
    var target = new Uri(
        upstreamBase,
        path + context.Request.QueryString);
    using var request = new HttpRequestMessage(
        new HttpMethod(context.Request.Method),
        target);

    if (context.Request.ContentLength > 0
        || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
    }

    foreach (var header in context.Request.Headers)
    {
        if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!request.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value.ToArray()))
        {
            request.Content?.Headers.TryAddWithoutValidation(
                header.Key,
                header.Value.ToArray());
        }
    }

    var accept = context.Request.GetTypedHeaders().Accept;
    if (accept?.Any(value =>
            value is not null
            && string.Equals(
                value.MediaType.Value,
                "application/dicom+json",
                StringComparison.OrdinalIgnoreCase)) == true)
    {
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/dicom+json"));
    }
    else
    {
        foreach (var value in context.Request.Headers.Accept)
        {
            request.Headers.TryAddWithoutValidation("Accept", value);
        }
    }

    var token = await credential.GetTokenAsync(
        new TokenRequestContext(new[] { dicomScope }),
        context.RequestAborted);
    request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        token.Token);

    using var response = await httpClientFactory
        .CreateClient("dicomweb")
        .SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);
    context.Response.StatusCode = (int)response.StatusCode;

    foreach (var header in response.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }

    foreach (var header in response.Content.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }

    context.Response.Headers.Remove("transfer-encoding");
    await response.Content.CopyToAsync(
        context.Response.Body,
        context.RequestAborted);
}
