namespace ImagingResults.Validation;

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ValidationDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Path,
    string Message,
    string Source);

public interface ITerminologyValidator
{
    ValidationDiagnostic? Validate(CodedConcept concept, string path);
}

public sealed class NullTerminologyValidator : ITerminologyValidator
{
    public static NullTerminologyValidator Instance { get; } = new();

    private NullTerminologyValidator()
    {
    }

    public ValidationDiagnostic? Validate(CodedConcept concept, string path) => null;
}

public sealed class MeasurementReportValidator
{
    private readonly ITerminologyValidator _terminology;

    public MeasurementReportValidator(ITerminologyValidator? terminology = null)
    {
        _terminology = terminology ?? NullTerminologyValidator.Instance;
    }

    public IReadOnlyList<ValidationDiagnostic> Validate(MeasurementReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }
        var diagnostics = new List<ValidationDiagnostic>();

        if (report.Groups.Count == 0)
        {
            Error("TID1500.GROUP_REQUIRED", "$.groups", "At least one measurement group is required.");
        }

        ValidateCode(report.ProcedureReported, "$.procedureReported");

        for (var groupIndex = 0; groupIndex < report.Groups.Count; groupIndex++)
        {
            var group = report.Groups[groupIndex];
            var path = $"$.groups[{groupIndex}]";
            ValidateCode(group.Finding, $"{path}.finding");
            ValidateCode(group.FindingSite, $"{path}.findingSite");

            if (group.Measurements.Count == 0)
            {
                Error("TID1500.MEASUREMENT_REQUIRED", $"{path}.measurements", "A measurement group must contain at least one measurement.");
            }

            if (report.Groups.Take(groupIndex).Any(existing => existing.Tracking.Uid == group.Tracking.Uid))
            {
                Error("TID1500.TRACKING_UID_DUPLICATE", $"{path}.tracking.uid", "Tracking UIDs must be unique within a report.");
            }

            for (var measurementIndex = 0; measurementIndex < group.Measurements.Count; measurementIndex++)
            {
                var measurement = group.Measurements[measurementIndex];
                var measurementPath = $"{path}.measurements[{measurementIndex}]";
                ValidateCode(measurement.Type, $"{measurementPath}.type");
                ValidateCode(measurement.Method, $"{measurementPath}.method");
                ValidateCode(measurement.Derivation, $"{measurementPath}.derivation");

                if (measurement.SpatialCoordinate is not null &&
                    !group.SourceImages.Any(source => source.SopInstanceUid == measurement.SpatialCoordinate.SourceImage.SopInstanceUid))
                {
                    Error(
                        "TID1500.SCOORD_SOURCE_UNLISTED",
                        $"{measurementPath}.spatialCoordinate.sourceImage",
                        "A spatial coordinate source image must also appear in its measurement group's source images.");
                }
            }

            foreach (var segmentation in group.Segmentations)
            {
                if (segmentation.Segmentation.SopClassUid != "1.2.840.10008.5.1.4.1.1.66.4")
                {
                    Error(
                        "DICOM.SEG_SOP_CLASS",
                        $"{path}.segmentations",
                        "A segmentation reference must use the Segmentation Storage SOP Class UID.");
                }
            }
        }

        return diagnostics;

        void Error(string code, string path, string message) =>
            diagnostics.Add(new ValidationDiagnostic(code, DiagnosticSeverity.Error, path, message, "ImagingResults.Core"));

        void ValidateCode(CodedConcept? concept, string path)
        {
            if (concept is null)
            {
                return;
            }

            var diagnostic = _terminology.Validate(concept, path);
            if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
            }
        }
    }
}
