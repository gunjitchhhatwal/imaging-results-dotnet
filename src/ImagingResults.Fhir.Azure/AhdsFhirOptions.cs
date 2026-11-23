namespace ImagingResults.Fhir.Azure;

public sealed class AhdsFhirOptions
{
    public AhdsFhirOptions(Uri serviceUrl)
    {
        if (serviceUrl is null)
        {
            throw new ArgumentNullException(nameof(serviceUrl));
        }

        if (!serviceUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The AHDS FHIR service URL must be absolute.", nameof(serviceUrl));
        }

        if (!string.Equals(serviceUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The AHDS FHIR service URL must use HTTPS.", nameof(serviceUrl));
        }

        if (!string.IsNullOrEmpty(serviceUrl.Query) || !string.IsNullOrEmpty(serviceUrl.Fragment))
        {
            throw new ArgumentException(
                "The AHDS FHIR service URL cannot contain a query or fragment.",
                nameof(serviceUrl));
        }

        ServiceUrl = new Uri($"{serviceUrl.AbsoluteUri.TrimEnd('/')}/", UriKind.Absolute);
        Scope = $"{ServiceUrl.AbsoluteUri.TrimEnd('/')}/.default";
    }

    public Uri ServiceUrl { get; }

    public string Scope { get; init; }

    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Scope))
        {
            throw new InvalidOperationException("The AHDS token scope cannot be empty.");
        }

        if (MaxRetryAttempts < 0)
        {
            throw new InvalidOperationException("MaxRetryAttempts cannot be negative.");
        }

        if (RetryBaseDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("RetryBaseDelay cannot be negative.");
        }

        if (MaxRetryDelay < RetryBaseDelay)
        {
            throw new InvalidOperationException(
                "MaxRetryDelay cannot be less than RetryBaseDelay.");
        }
    }
}
