using FellowOakDicom;
using ImagingResults.Dicom;
using ImagingResults.Validation;

namespace ImagingResults.Tests;

public sealed class DicomAdapterTests
{
    [Fact]
    public void Writer_and_reader_require_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new Tid1500Writer().Write(null!));
        Assert.Throws<ArgumentNullException>(() => new Tid1500Reader().Read(null!));
    }

    [Fact]
    public void Writer_sets_sr_identity_and_completion_metadata()
    {
        var dataset = new Tid1500Writer().Write(TestReportFactory.Create()).Dataset;

        Assert.Equal(DicomUID.ComprehensiveSRStorage.UID, dataset.GetSingleValue<string>(DicomTag.SOPClassUID));
        Assert.Equal("SR", dataset.GetSingleValue<string>(DicomTag.Modality));
        Assert.Equal("COMPLETE", dataset.GetSingleValue<string>(DicomTag.CompletionFlag));
        Assert.Equal("UNVERIFIED", dataset.GetSingleValue<string>(DicomTag.VerificationFlag));
        Assert.Equal("synthetic-patient", dataset.GetSingleValue<string>(DicomTag.PatientID));
        var template = Assert.Single(dataset.GetSequence(DicomTag.ContentTemplateSequence).Items);
        Assert.Equal("DCMR", template.GetSingleValue<string>(DicomTag.MappingResource));
        Assert.Equal("1500", template.GetSingleValue<string>(DicomTag.TemplateIdentifier));
        var imagingMeasurements = FindContentItem(dataset, "126010");
        Assert.Single(
            imagingMeasurements.GetSequence(DicomTag.ContentSequence).Items,
            item => HasConcept(item, "125007"));
    }

    [Fact]
    public void Patient_id_is_omitted_when_absent()
    {
        var dataset = new Tid1500Writer().Write(TestReportFactory.Create(patientId: null)).Dataset;

        Assert.False(dataset.Contains(DicomTag.PatientID));
    }

    [Fact]
    public void Person_observer_and_optional_measurement_content_round_trip()
    {
        var derivation = new CodedConcept("121401", "DCM", "Derivation");
        var measurement = TestReportFactory.CreateMeasurement(derivation: derivation);
        var group = TestReportFactory.CreateGroup(measurements: new[] { measurement });
        var expected = TestReportFactory.Create(
            new[] { group },
            new ObserverContext(ObserverKind.Person, "Dr Example", "doctor-1"));

        var actual = RoundTrip(expected);

        Assert.Equal(ObserverKind.Person, actual.Observer.Kind);
        Assert.Equal("Dr Example", actual.Observer.Name);
        Assert.Equal(expected.ProcedureReported, actual.ProcedureReported);
        Assert.Equal(measurement.Method, actual.Groups[0].Measurements[0].Method);
        Assert.Equal(derivation, actual.Groups[0].Measurements[0].Derivation);
    }

    [Fact]
    public void Multiple_groups_measurements_frames_and_segments_round_trip()
    {
        var firstSource = TestReportFactory.CreateSource(frames: new[] { 2, 7 });
        var first = TestReportFactory.CreateGroup(
            measurements: new[]
            {
                TestReportFactory.CreateMeasurement(12.4m, coordinate: new SpatialCoordinate2D(
                    GraphicType2D.Polyline,
                    new[] { new Point2D(10, 20), new Point2D(30, 40) },
                    firstSource)),
                TestReportFactory.CreateMeasurement(5.5m, "cm", coordinate: new SpatialCoordinate2D(
                    GraphicType2D.Circle,
                    new[] { new Point2D(15, 25), new Point2D(18, 25) },
                    firstSource))
            },
            sourceImages: new[] { firstSource });
        var second = TestReportFactory.CreateGroup(
            "lesion-2",
            "1.2.826.0.1.3680043.10.511.31",
            measurements: new[] { TestReportFactory.CreateMeasurement(8.25m) },
            segmentations: new[] { TestReportFactory.CreateSegmentation(9) });

        var actual = RoundTrip(TestReportFactory.Create(new[] { first, second }));

        Assert.Equal(2, actual.Groups.Count);
        Assert.Equal(2, actual.Groups[0].Measurements.Count);
        Assert.Equal(new[] { 2, 7 }, actual.Groups[0].SourceImages[0].FrameNumbers);
        Assert.Equal(GraphicType2D.Circle, actual.Groups[0].Measurements[1].SpatialCoordinate!.GraphicType);
        Assert.Equal(8.25m, actual.Groups[1].Measurements[0].Value.Value);
        Assert.Equal(9, actual.Groups[1].Segmentations[0].SegmentNumber);
    }

    [Fact]
    public void Evidence_sequence_deduplicates_repeated_instances()
    {
        var source = TestReportFactory.CreateSource();
        var group = TestReportFactory.CreateGroup(sourceImages: new[] { source, source });
        var dataset = new Tid1500Writer().Write(TestReportFactory.Create(new[] { group })).Dataset;

        var studies = dataset.GetSequence(DicomTag.CurrentRequestedProcedureEvidenceSequence).Items;
        var series = studies.Single().GetSequence(DicomTag.ReferencedSeriesSequence).Items;
        var sourceSeries = Assert.Single(series, item => item.GetSingleValue<string>(DicomTag.SeriesInstanceUID) == TestReportFactory.SourceSeriesUid);

        Assert.Single(sourceSeries.GetSequence(DicomTag.ReferencedSOPSequence).Items);
    }

    [Fact]
    public void Evidence_sequence_is_omitted_without_study_and_series_context()
    {
        var source = TestReportFactory.CreateSource(studyUid: null, seriesUid: null);
        var measurement = TestReportFactory.CreateMeasurement(
            coordinate: new SpatialCoordinate2D(GraphicType2D.Point, new[] { new Point2D(1, 2) }, source));
        var group = TestReportFactory.CreateGroup(
            measurements: new[] { measurement },
            sourceImages: new[] { source },
            segmentations: Array.Empty<SegmentationReference>());
        var dataset = new Tid1500Writer().Write(TestReportFactory.Create(new[] { group })).Dataset;

        Assert.False(dataset.Contains(DicomTag.CurrentRequestedProcedureEvidenceSequence));
    }

    [Fact]
    public void Wrong_root_concept_returns_source_aware_error()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        file.Dataset.Remove(DicomTag.ConceptNameCodeSequence);

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Equal("TID1500.ROOT_CONCEPT", diagnostic.Code);
        Assert.Equal("$.ConceptNameCodeSequence", diagnostic.Path);
        Assert.Equal("ImagingResults.Dicom", diagnostic.Source);
    }

    [Theory]
    [InlineData("StudyInstanceUID", "$.StudyInstanceUID")]
    [InlineData("SeriesInstanceUID", "$.SeriesInstanceUID")]
    [InlineData("SOPInstanceUID", "$.SOPInstanceUID")]
    public void Missing_required_header_uid_returns_diagnostic(string tagName, string expectedPath)
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var tag = tagName switch
        {
            "StudyInstanceUID" => DicomTag.StudyInstanceUID,
            "SeriesInstanceUID" => DicomTag.SeriesInstanceUID,
            _ => DicomTag.SOPInstanceUID
        };
        file.Dataset.Remove(tag);

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "DICOM.HEADER_REQUIRED");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Equal(expectedPath, diagnostic.Path);
    }

    [Fact]
    public void Report_without_measurement_groups_is_rejected_after_parsing()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var imagingMeasurements = FindContentItem(file.Dataset, "126010");
        var nonGroupItems = imagingMeasurements.GetSequence(DicomTag.ContentSequence).Items
            .Where(item => !HasConcept(item, "125007"))
            .ToArray();
        imagingMeasurements.AddOrUpdate(
            new DicomSequence(DicomTag.ContentSequence, nonGroupItems));

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "TID1500.GROUP_REQUIRED");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Equal("$.groups", diagnostic.Path);
    }

    [Fact]
    public void Group_without_measurements_is_rejected_after_parsing()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var group = FindMeasurementGroup(file.Dataset);
        var nonMeasurements = group.GetSequence(DicomTag.ContentSequence).Items
            .Where(item => item.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) != "NUM")
            .ToArray();
        group.AddOrUpdate(new DicomSequence(DicomTag.ContentSequence, nonMeasurements));

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "TID1500.MEASUREMENT_REQUIRED");

        Assert.False(result.IsSuccess);
        Assert.Equal("$.groups[0].measurements", diagnostic.Path);
    }

    [Fact]
    public void Missing_group_context_returns_group_path()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var group = FindMeasurementGroup(file.Dataset);
        var tracking = FindContentItem(group, "112039");
        tracking.Remove(DicomTag.TextValue);

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "TID1500.GROUP_CONTEXT");

        Assert.False(result.IsSuccess);
        Assert.Equal("$.groups[0]", diagnostic.Path);
    }

    [Fact]
    public void Finding_site_is_used_when_optional_finding_is_absent()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var group = FindMeasurementGroup(file.Dataset);
        var findingSite = FindContentItem(group, "363698007")
            .GetSequence(DicomTag.ConceptNameCodeSequence)
            .Items
            .Single();
        findingSite.AddOrUpdate(DicomTag.CodeValue, "G-C0E3");
        findingSite.AddOrUpdate(DicomTag.CodingSchemeDesignator, "SRT");
        findingSite.AddOrUpdate(DicomTag.CodeMeaning, "Finding Site");
        var content = group.GetSequence(DicomTag.ContentSequence).Items
            .Where(item => !HasConcept(item, "121071"))
            .ToArray();
        group.AddOrUpdate(new DicomSequence(DicomTag.ContentSequence, content));

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Code == "TID1500.FINDING_INFERRED_FROM_SITE");

        Assert.True(result.IsSuccess);
        Assert.Equal("$.groups[0].finding", diagnostic.Path);
        Assert.Equal(
            result.Report!.Groups[0].FindingSite,
            result.Report.Groups[0].Finding);
    }

    [Fact]
    public void Legacy_unnamed_image_and_scoord_content_is_parsed()
    {
        var source = TestReportFactory.CreateSource();
        var measurement = TestReportFactory.CreateMeasurement(
            coordinate: new SpatialCoordinate2D(
                GraphicType2D.Polyline,
                new[] { new Point2D(10, 20), new Point2D(30, 40) },
                source));
        var group = TestReportFactory.CreateGroup(
            measurements: new[] { measurement },
            sourceImages: new[] { source });
        var file = new Tid1500Writer().Write(TestReportFactory.Create(new[] { group }));
        var groupItem = FindMeasurementGroup(file.Dataset);
        var groupContent = groupItem.GetSequence(DicomTag.ContentSequence).Items
            .Where(item => !HasConcept(item, "121112"))
            .ToArray();
        groupItem.AddOrUpdate(new DicomSequence(DicomTag.ContentSequence, groupContent));
        var numeric = groupItem.GetSequence(DicomTag.ContentSequence).Items.Single(
            item => item.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "NUM");
        var coordinate = numeric.GetSequence(DicomTag.ContentSequence).Items.Single(
            item => item.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "SCOORD");
        coordinate.Remove(DicomTag.ConceptNameCodeSequence);

        var result = new Tid1500Reader().Read(file);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var parsedGroup = Assert.Single(result.Report!.Groups);
        Assert.Equal(source.SopInstanceUid, Assert.Single(parsedGroup.SourceImages).SopInstanceUid);
        Assert.Equal(
            source.SopInstanceUid,
            Assert.Single(parsedGroup.Measurements).SpatialCoordinate!.SourceImage.SopInstanceUid);
    }

    [Fact]
    public void Invalid_tracking_uid_returns_stable_group_diagnostic()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var group = FindMeasurementGroup(file.Dataset);
        var trackingUid = FindContentItem(group, "112040");
        trackingUid.Remove(DicomTag.UID);

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "TID1500.GROUP_CONTEXT");

        Assert.False(result.IsSuccess);
        Assert.Equal("$.groups[0]", diagnostic.Path);
    }

    [Fact]
    public void Invalid_numeric_value_returns_measurement_path()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        var group = FindMeasurementGroup(file.Dataset);
        var numeric = group.GetSequence(DicomTag.ContentSequence).Items.Single(
            item => item.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "NUM");
        var measuredValue = numeric.GetSequence(DicomTag.MeasuredValueSequence).Items.Single();
        measuredValue.Remove(DicomTag.MeasurementUnitsCodeSequence);

        var result = new Tid1500Reader().Read(file);
        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "TID1500.NUM_INVALID");

        Assert.False(result.IsSuccess);
        Assert.Equal("$.groups[0].measurements[0]", diagnostic.Path);
    }

    [Fact]
    public void Invalid_content_datetime_uses_documented_epoch_fallback()
    {
        var file = new Tid1500Writer().Write(TestReportFactory.Create());
        file.Dataset.Remove(DicomTag.ContentDate);
        file.Dataset.Remove(DicomTag.ContentTime);

        var result = new Tid1500Reader().Read(file);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Report!.CreatedAt);
    }

    [Fact]
    public void Segment_number_above_dicom_us_range_fails_explicitly()
    {
        var group = TestReportFactory.CreateGroup(
            segmentations: new[] { TestReportFactory.CreateSegmentation(ushort.MaxValue + 1) });

        Assert.Throws<OverflowException>(() => new Tid1500Writer().Write(TestReportFactory.Create(new[] { group })));
    }

    [Fact]
    public void Equivalent_reports_produce_identical_dicom_bytes()
    {
        static byte[] Write(MeasurementReport report)
        {
            using var stream = new MemoryStream();
            new Tid1500Writer().Write(report).Save(stream);
            return stream.ToArray();
        }

        Assert.Equal(Write(TestReportFactory.Create()), Write(TestReportFactory.Create()));
    }

    private static MeasurementReport RoundTrip(MeasurementReport report)
    {
        using var stream = new MemoryStream();
        new Tid1500Writer().Write(report).Save(stream);
        stream.Position = 0;
        var result = new Tid1500Reader().Read(DicomFile.Open(stream));
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<MeasurementReport>(result.Report);
    }

    private static DicomDataset FindContentItem(DicomDataset owner, string conceptCode) =>
        owner.GetSequence(DicomTag.ContentSequence).Items.Single(item => HasConcept(item, conceptCode));

    private static DicomDataset FindMeasurementGroup(DicomDataset dataset) =>
        FindContentItem(FindContentItem(dataset, "126010"), "125007");

    private static bool HasConcept(DicomDataset item, string conceptCode) =>
        item.TryGetSequence(DicomTag.ConceptNameCodeSequence, out var sequence)
        && sequence.Items.Single().GetSingleValueOrDefault(DicomTag.CodeValue, string.Empty) == conceptCode;
}
