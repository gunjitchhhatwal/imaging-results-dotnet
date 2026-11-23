using FellowOakDicom;
using Hl7.Fhir.Model;
using ImagingResults.Dicom;
using ImagingResults.Fhir;
using ImagingResults.Validation;

namespace ImagingResults.Tests;

public sealed class SdkBaselineTests
{
    [Fact]
    public void Invalid_dicom_uid_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new TrackingIdentifier("lesion", "not-a-uid"));
    }

    [Fact]
    public void Validator_reports_precise_duplicate_tracking_path()
    {
        var report = CreateReport();
        var duplicate = new MeasurementReport(
            report.StudyInstanceUid,
            report.SeriesInstanceUid,
            report.SopInstanceUid,
            report.CreatedAt,
            new[] { report.Groups[0], report.Groups[0] },
            report.Observer);

        var diagnostic = Assert.Single(
            new MeasurementReportValidator().Validate(duplicate),
            item => item.Code == "TID1500.TRACKING_UID_DUPLICATE");

        Assert.Equal("$.groups[1].tracking.uid", diagnostic.Path);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Tid1500_subset_round_trips_through_dicom_bytes()
    {
        var expected = CreateReport();
        using var stream = new MemoryStream();
        new Tid1500Writer().Write(expected).Save(stream);
        stream.Position = 0;

        var actual = new Tid1500Reader().Read(DicomFile.Open(stream));

        Assert.True(actual.IsSuccess, string.Join(Environment.NewLine, actual.Diagnostics));
        Assert.NotNull(actual.Report);
        Assert.Equal(expected.StudyInstanceUid, actual.Report.StudyInstanceUid);
        Assert.Equal(expected.Groups[0].Tracking, actual.Report.Groups[0].Tracking);
        Assert.Equal(expected.Groups[0].Finding, actual.Report.Groups[0].Finding);
        Assert.Equal(expected.Groups[0].Measurements[0].Value, actual.Report.Groups[0].Measurements[0].Value);
        var expectedSource = expected.Groups[0].SourceImages[0];
        var actualSource = actual.Report.Groups[0].SourceImages[0];
        Assert.Equal(expectedSource.SopClassUid, actualSource.SopClassUid);
        Assert.Equal(expectedSource.SopInstanceUid, actualSource.SopInstanceUid);
        Assert.Equal(expectedSource.StudyInstanceUid, actualSource.StudyInstanceUid);
        Assert.Equal(expectedSource.SeriesInstanceUid, actualSource.SeriesInstanceUid);
        Assert.Equal(expectedSource.FrameNumbers, actualSource.FrameNumbers);

        var expectedSegmentation = expected.Groups[0].Segmentations[0];
        var actualSegmentation = actual.Report.Groups[0].Segmentations[0];
        Assert.Equal(expectedSegmentation.SegmentNumber, actualSegmentation.SegmentNumber);
        Assert.Equal(expectedSegmentation.Segmentation.SopInstanceUid, actualSegmentation.Segmentation.SopInstanceUid);
        Assert.Equal(expectedSegmentation.Segmentation.SeriesInstanceUid, actualSegmentation.Segmentation.SeriesInstanceUid);

        var expectedCoordinate = expected.Groups[0].Measurements[0].SpatialCoordinate!;
        var actualCoordinate = actual.Report.Groups[0].Measurements[0].SpatialCoordinate!;
        Assert.Equal(expectedCoordinate.GraphicType, actualCoordinate.GraphicType);
        Assert.Equal(expectedCoordinate.Points, actualCoordinate.Points);
        Assert.Equal(expectedCoordinate.SourceImage.SopInstanceUid, actualCoordinate.SourceImage.SopInstanceUid);
    }

    [Fact]
    public void Fhir_mapping_links_report_observation_study_selection_and_provenance()
    {
        var result = new FhirR5Mapper().Map(CreateReport());
        var resources = result.Bundle.Entry.Select(entry => entry.Resource).ToArray();

        var diagnosticReport = Assert.Single(resources.OfType<DiagnosticReport>());
        var observation = Assert.Single(resources.OfType<Observation>());
        Assert.Single(resources.OfType<ImagingStudy>());
        Assert.Equal(2, resources.OfType<ImagingSelection>().Count());
        var provenance = Assert.Single(resources.OfType<Provenance>());
        var observationUrl = result.Bundle.Entry.Single(entry => entry.Resource == observation).FullUrl;

        Assert.Contains(diagnosticReport.Result, reference => reference.Reference == observationUrl);
        Assert.NotEmpty(observation.DerivedFrom);
        Assert.Contains(provenance.Target, reference => reference.Reference == observationUrl);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "FHIR.IMAGING_SELECTION_SEGMENT_SUBSET");
    }

    private static MeasurementReport CreateReport()
    {
        var source = new SopInstanceReference(
            "1.2.840.10008.5.1.4.1.1.2",
            "1.2.826.0.1.3680043.10.511.3.1",
            "1.2.826.0.1.3680043.10.511.1",
            "1.2.826.0.1.3680043.10.511.2",
            new[] { 7 });
        var segmentation = new SopInstanceReference(
            "1.2.840.10008.5.1.4.1.1.66.4",
            "1.2.826.0.1.3680043.10.511.4.1",
            "1.2.826.0.1.3680043.10.511.1",
            "1.2.826.0.1.3680043.10.511.4");

        return new MeasurementReport(
            "1.2.826.0.1.3680043.10.511.1",
            "1.2.826.0.1.3680043.10.511.20",
            "1.2.826.0.1.3680043.10.511.21",
            new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero),
            new[]
            {
                new MeasurementGroup(
                    new TrackingIdentifier("lesion-1", "1.2.826.0.1.3680043.10.511.30"),
                    new CodedConcept("108369006", "SCT", "Neoplasm"),
                    new[]
                    {
                        new Measurement(
                            new CodedConcept("G-D7FE", "SRT", "Length"),
                            new UcumQuantity(12.4m, "mm"),
                            new CodedConcept("370129005", "SCT", "Measurement Method"),
                            spatialCoordinate: new SpatialCoordinate2D(
                                GraphicType2D.Polyline,
                                new[] { new Point2D(10, 20), new Point2D(30, 40) },
                                source))
                    },
                    new CodedConcept("39607008", "SCT", "Lung structure"),
                    new AlgorithmIdentification("Synthetic AI", "1.0"),
                    new[] { source },
                    new[] { new SegmentationReference(segmentation, 3) })
            },
            new ObserverContext(ObserverKind.Device, "Synthetic AI", "synthetic-ai"),
            new CodedConcept("363679005", "SCT", "Imaging procedure"),
            "synthetic-patient");
    }
}
