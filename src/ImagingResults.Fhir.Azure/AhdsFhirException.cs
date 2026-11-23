using System.Net;
using Hl7.Fhir.Model;

namespace ImagingResults.Fhir.Azure;

public sealed class AhdsFhirException : Exception
{
    public AhdsFhirException(
        string message,
        HttpStatusCode? statusCode = null,
        OperationOutcome? operationOutcome = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        OperationOutcome = operationOutcome;
        ResponseBody = responseBody;
    }

    public HttpStatusCode? StatusCode { get; }

    public OperationOutcome? OperationOutcome { get; }

    public string? ResponseBody { get; }
}
