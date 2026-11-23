namespace ImagingResults.Tests;

public sealed class CoreModelTests
{
    [Theory]
    [InlineData("", "SCT", "Meaning")]
    [InlineData("123", "", "Meaning")]
    [InlineData("123", "SCT", "")]
    [InlineData(" ", "SCT", "Meaning")]
    public void Coded_concept_requires_all_core_fields(string value, string scheme, string meaning)
    {
        Assert.Throws<ArgumentException>(() => new CodedConcept(value, scheme, meaning));
    }

    [Fact]
    public void Ucum_quantity_requires_unit_and_defaults_display()
    {
        Assert.Throws<ArgumentException>(() => new UcumQuantity(1, " "));
        Assert.Equal("mm", new UcumQuantity(1, "mm").Display);
        Assert.Equal("millimetre", new UcumQuantity(1, "mm", "millimetre").Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".1.2")]
    [InlineData("1.2.")]
    [InlineData("1..2")]
    [InlineData("1.2a")]
    [InlineData("1 2")]
    public void Tracking_identifier_rejects_invalid_uids(string uid)
    {
        Assert.Throws<ArgumentException>(() => new TrackingIdentifier("lesion", uid));
    }

    [Fact]
    public void Tracking_identifier_requires_human_identifier()
    {
        Assert.Throws<ArgumentException>(() => new TrackingIdentifier(" ", "1.2.3"));
    }

    [Fact]
    public void Dicom_uid_cannot_exceed_64_characters()
    {
        Assert.Throws<ArgumentException>(() => new TrackingIdentifier("lesion", $"1.{new string('2', 63)}"));
    }

    [Fact]
    public void Source_reference_sorts_and_deduplicates_frames()
    {
        var source = TestReportFactory.CreateSource(frames: new[] { 4, 2, 4, 1 });

        Assert.Equal(new[] { 1, 2, 4 }, source.FrameNumbers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Source_reference_rejects_non_positive_frames(int frame)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestReportFactory.CreateSource(frames: new[] { frame }));
    }

    [Fact]
    public void Source_reference_validates_optional_study_and_series_uids()
    {
        Assert.Throws<ArgumentException>(() => TestReportFactory.CreateSource(studyUid: "bad"));
        Assert.Throws<ArgumentException>(() => TestReportFactory.CreateSource(seriesUid: "bad"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Segmentation_reference_rejects_non_positive_segment_number(int segment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestReportFactory.CreateSegmentation(segment));
    }

    [Fact]
    public void Segmentation_reference_requires_instance()
    {
        Assert.Throws<ArgumentNullException>(() => new SegmentationReference(null!, 1));
    }

    [Fact]
    public void Spatial_coordinate_requires_points_and_source()
    {
        Assert.Throws<ArgumentNullException>(() => new SpatialCoordinate2D(GraphicType2D.Point, null!, TestReportFactory.CreateSource()));
        Assert.Throws<ArgumentException>(() => new SpatialCoordinate2D(GraphicType2D.Point, Array.Empty<Point2D>(), TestReportFactory.CreateSource()));
        Assert.Throws<ArgumentNullException>(() => new SpatialCoordinate2D(GraphicType2D.Point, new[] { new Point2D(1, 2) }, null!));
    }

    [Fact]
    public void Measurement_requires_type_and_value()
    {
        Assert.Throws<ArgumentNullException>(() => new Measurement(null!, new UcumQuantity(1, "mm")));
        Assert.Throws<ArgumentNullException>(() => new Measurement(new CodedConcept("1", "99TEST", "Test"), null!));
    }

    [Fact]
    public void Measurement_group_requires_tracking_finding_and_measurement_collection()
    {
        var finding = new CodedConcept("1", "99TEST", "Finding");
        var tracking = new TrackingIdentifier("one", "1.2.3");

        Assert.Throws<ArgumentNullException>(() => new MeasurementGroup(null!, finding, Array.Empty<Measurement>()));
        Assert.Throws<ArgumentNullException>(() => new MeasurementGroup(tracking, null!, Array.Empty<Measurement>()));
        Assert.Throws<ArgumentNullException>(() => new MeasurementGroup(tracking, finding, null!));
    }

    [Fact]
    public void Report_requires_groups_and_observer()
    {
        var observer = new ObserverContext(ObserverKind.Person, "Dr Example");

        Assert.Throws<ArgumentNullException>(() => new MeasurementReport("1.2.3", "1.2.4", "1.2.5", DateTimeOffset.UtcNow, null!, observer));
        Assert.Throws<ArgumentNullException>(() => new MeasurementReport("1.2.3", "1.2.4", "1.2.5", DateTimeOffset.UtcNow, Array.Empty<MeasurementGroup>(), null!));
    }

    [Fact]
    public void Constructor_inputs_are_copied()
    {
        var frames = new List<int> { 1 };
        var source = TestReportFactory.CreateSource(frames: frames);
        frames.Add(2);

        var measurements = new List<Measurement> { TestReportFactory.CreateMeasurement() };
        var group = TestReportFactory.CreateGroup(measurements: measurements);
        measurements.Clear();

        Assert.Equal(new[] { 1 }, source.FrameNumbers);
        Assert.Single(group.Measurements);
    }

    [Fact]
    public void Algorithm_parameters_are_copied_with_ordinal_keys()
    {
        var parameters = new Dictionary<string, string> { ["Threshold"] = "0.7" };
        var algorithm = new AlgorithmIdentification("AI", "1", Parameters: parameters);
        parameters["Threshold"] = "0.8";

        Assert.Equal("0.7", algorithm.Parameters["Threshold"]);
        Assert.False(algorithm.Parameters.ContainsKey("threshold"));
    }
}
