using System.Net;
using Hl7.Fhir.Model;
using ImagingResults.Validation;

namespace ImagingResults.Fhir.Azure;

public sealed record AhdsFhirUploadResult(
    Bundle TransactionBundle,
    Bundle? ResponseBundle,
    HttpStatusCode StatusCode,
    IReadOnlyList<ValidationDiagnostic> MappingDiagnostics);
