using ImagingResults.Validation;

namespace ImagingResults.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Validator_requires_report()
    {
        Assert.Throws<ArgumentNullException>(() => new MeasurementReportValidator().Validate(null!));
    }

    [Fact]
    public void Empty_report_has_precise_group_diagnostic()
    {
        var diagnostic = Assert.Single(new MeasurementReportValidator().Validate(TestReportFactory.Create(Array.Empty<MeasurementGroup>())));

        Assert.Equal("TID1500.GROUP_REQUIRED", diagnostic.Code);
        Assert.Equal("$.groups", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Empty_group_has_precise_measurement_diagnostic()
    {
        var group = TestReportFactory.CreateGroup(measurements: Array.Empty<Measurement>());
        var diagnostic = Assert.Single(
            new MeasurementReportValidator().Validate(TestReportFactory.Create(new[] { group })),
            item => item.Code == "TID1500.MEASUREMENT_REQUIRED");

        Assert.Equal("$.groups[0].measurements", diagnostic.Path);
    }

    [Fact]
    public void Duplicate_tracking_uid_points_to_later_group()
    {
        var first = TestReportFactory.CreateGroup();
        var second = TestReportFactory.CreateGroup("another-name");
        var diagnostic = Assert.Single(
            new MeasurementReportValidator().Validate(TestReportFactory.Create(new[] { first, second })),
            item => item.Code == "TID1500.TRACKING_UID_DUPLICATE");

        Assert.Equal("$.groups[1].tracking.uid", diagnostic.Path);
    }

    [Fact]
    public void Spatial_coordinate_source_must_be_listed_in_group()
    {
        var coordinateSource = TestReportFactory.CreateSource("1.2.826.0.1.3680043.10.511.99");
        var measurement = TestReportFactory.CreateMeasurement(
            coordinate: new SpatialCoordinate2D(
                GraphicType2D.Point,
                new[] { new Point2D(1, 2) },
                coordinateSource));
        var group = TestReportFactory.CreateGroup(measurements: new[] { measurement });

        var diagnostic = Assert.Single(
            new MeasurementReportValidator().Validate(TestReportFactory.Create(new[] { group })),
            item => item.Code == "TID1500.SCOORD_SOURCE_UNLISTED");

        Assert.Equal("$.groups[0].measurements[0].spatialCoordinate.sourceImage", diagnostic.Path);
    }

    [Fact]
    public void Spatial_coordinate_source_match_uses_sop_instance_uid()
    {
        var listed = TestReportFactory.CreateSource(frames: new[] { 1 });
        var coordinateSource = TestReportFactory.CreateSource(frames: new[] { 2 });
        var measurement = TestReportFactory.CreateMeasurement(
            coordinate: new SpatialCoordinate2D(GraphicType2D.Point, new[] { new Point2D(1, 2) }, coordinateSource));
        var group = TestReportFactory.CreateGroup(measurements: new[] { measurement }, sourceImages: new[] { listed });

        Assert.DoesNotContain(
            new MeasurementReportValidator().Validate(TestReportFactory.Create(new[] { group })),
            item => item.Code == "TID1500.SCOORD_SOURCE_UNLISTED");
    }

    [Fact]
    public void Segmentation_requires_seg_storage_sop_class()
    {
        var group = TestReportFactory.CreateGroup(
            segmentations: new[] { TestReportFactory.CreateSegmentation(sopClassUid: "1.2.840.10008.5.1.4.1.1.2") });
        var diagnostic = Assert.Single(
            new MeasurementReportValidator().Validate(TestReportFactory.Create(new[] { group })),
            item => item.Code == "DICOM.SEG_SOP_CLASS");

        Assert.Equal("$.groups[0].segmentations", diagnostic.Path);
    }

    [Fact]
    public void Terminology_validator_receives_every_supported_code_path()
    {
        var terminology = new CapturingTerminologyValidator();
        var derivation = new CodedConcept("121401", "DCM", "Derivation");
        var group = TestReportFactory.CreateGroup(
            measurements: new[] { TestReportFactory.CreateMeasurement(derivation: derivation) });

        var diagnostics = new MeasurementReportValidator(terminology).Validate(TestReportFactory.Create(new[] { group }));

        Assert.Empty(diagnostics);
        Assert.Equal(
            new[]
            {
                "$.procedureReported",
                "$.groups[0].finding",
                "$.groups[0].findingSite",
                "$.groups[0].measurements[0].type",
                "$.groups[0].measurements[0].method",
                "$.groups[0].measurements[0].derivation"
            },
            terminology.Paths);
    }

    [Fact]
    public void Terminology_diagnostic_is_preserved()
    {
        var expected = new ValidationDiagnostic(
            "TERM.UNKNOWN",
            DiagnosticSeverity.Warning,
            "$.groups[0].finding",
            "Unknown code.",
            "TestTerminology");
        var validator = new MeasurementReportValidator(new ReturningTerminologyValidator(expected));

        Assert.Contains(expected, validator.Validate(TestReportFactory.Create()));
    }

    private sealed class CapturingTerminologyValidator : ITerminologyValidator
    {
        public List<string> Paths { get; } = new();

        public ValidationDiagnostic? Validate(CodedConcept concept, string path)
        {
            Paths.Add(path);
            return null;
        }
    }

    private sealed class ReturningTerminologyValidator(ValidationDiagnostic diagnostic) : ITerminologyValidator
    {
        public ValidationDiagnostic? Validate(CodedConcept concept, string path) =>
            path == diagnostic.Path ? diagnostic : null;
    }
}
