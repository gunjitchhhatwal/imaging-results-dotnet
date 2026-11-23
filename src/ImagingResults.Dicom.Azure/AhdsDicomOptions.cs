namespace ImagingResults.Dicom.Azure;

public sealed class AhdsDicomOptions
{
    public const string DefaultScope =
        "https://dicom.healthcareapis.azure.com/.default";

    public AhdsDicomOptions(Uri serviceUrl)
    {
        if (serviceUrl is null)
        {
            throw new ArgumentNullException(nameof(serviceUrl));
        }

        if (!serviceUrl.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The AHDS DICOM service URL must be absolute.",
                nameof(serviceUrl));
        }

        if (!string.Equals(
                serviceUrl.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The AHDS DICOM service URL must use HTTPS.",
                nameof(serviceUrl));
        }

        if (!string.IsNullOrEmpty(serviceUrl.Query)
            || !string.IsNullOrEmpty(serviceUrl.Fragment))
        {
            throw new ArgumentException(
                "The AHDS DICOM service URL cannot contain a query or fragment.",
                nameof(serviceUrl));
        }

        ServiceUrl = new Uri(
            $"{serviceUrl.AbsoluteUri.TrimEnd('/')}/",
            UriKind.Absolute);
    }

    public Uri ServiceUrl { get; }

    public int ApiVersion { get; init; } = 2;

    public string Scope { get; init; } = DefaultScope;

    public AhdsDicomStoreMode StoreMode { get; init; } =
        AhdsDicomStoreMode.Create;

    public Uri StoreEndpoint =>
        new(ServiceUrl, $"v{ApiVersion}/studies");

    public int MaxRetryAttempts { get; init; } = 3;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (ApiVersion <= 0)
        {
            throw new InvalidOperationException("ApiVersion must be positive.");
        }

        if (string.IsNullOrWhiteSpace(Scope))
        {
            throw new InvalidOperationException(
                "The AHDS DICOM token scope cannot be empty.");
        }

        if (!Enum.IsDefined(StoreMode))
        {
            throw new InvalidOperationException("StoreMode is invalid.");
        }

        if (MaxRetryAttempts < 0)
        {
            throw new InvalidOperationException(
                "MaxRetryAttempts cannot be negative.");
        }

        if (RetryBaseDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "RetryBaseDelay cannot be negative.");
        }

        if (MaxRetryDelay < RetryBaseDelay)
        {
            throw new InvalidOperationException(
                "MaxRetryDelay cannot be less than RetryBaseDelay.");
        }
    }
}
