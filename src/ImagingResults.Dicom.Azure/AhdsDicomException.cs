using System.Net;

namespace ImagingResults.Dicom.Azure;

public sealed class AhdsDicomException : Exception
{
    public AhdsDicomException(
        string message,
        HttpStatusCode? statusCode = null,
        AhdsDicomStoreResult? storeResult = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        StoreResult = storeResult;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }

    public AhdsDicomStoreResult? StoreResult { get; }

    public string? ResponseBody { get; }
}
