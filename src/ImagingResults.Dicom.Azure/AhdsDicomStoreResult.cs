using System.Net;

namespace ImagingResults.Dicom.Azure;

public sealed class AhdsDicomStoreResult
{
    public AhdsDicomStoreResult(
        HttpStatusCode statusCode,
        string? retrieveUrl,
        IReadOnlyList<AhdsDicomInstanceResult> storedInstances,
        IReadOnlyList<AhdsDicomInstanceResult> failedInstances,
        string responseBody)
    {
        StatusCode = statusCode;
        RetrieveUrl = retrieveUrl;
        StoredInstances = storedInstances
            ?? throw new ArgumentNullException(nameof(storedInstances));
        FailedInstances = failedInstances
            ?? throw new ArgumentNullException(nameof(failedInstances));
        ResponseBody = responseBody ?? throw new ArgumentNullException(nameof(responseBody));
    }

    public HttpStatusCode StatusCode { get; }

    public string? RetrieveUrl { get; }

    public IReadOnlyList<AhdsDicomInstanceResult> StoredInstances { get; }

    public IReadOnlyList<AhdsDicomInstanceResult> FailedInstances { get; }

    public string ResponseBody { get; }

    public bool IsPartial => FailedInstances.Count > 0;
}
