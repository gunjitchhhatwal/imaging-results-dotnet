namespace ImagingResults.Tests;

internal static class TestReportFactory
{
    public const string StudyUid = "1.2.826.0.1.3680043.10.511.1";
    public const string SourceSeriesUid = "1.2.826.0.1.3680043.10.511.2";
    public const string SourceInstanceUid = "1.2.826.0.1.3680043.10.511.3.1";
    public const string SegSeriesUid = "1.2.826.0.1.3680043.10.511.4";
    public const string SegInstanceUid = "1.2.826.0.1.3680043.10.511.4.1";

    public static MeasurementReport Create(
        IReadOnlyList<MeasurementGroup>? groups = null,
        ObserverContext? observer = null,
        string? patientId = "synthetic-patient",
        CodedConcept? procedure = null) =>
        new(
            StudyUid,
            "1.2.826.0.1.3680043.10.511.20",
            "1.2.826.0.1.3680043.10.511.21",
            new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero),
            groups ?? new[] { CreateGroup() },
            observer ?? new ObserverContext(ObserverKind.Device, "Synthetic AI", "synthetic-ai"),
            procedure ?? new CodedConcept("363679005", "SCT", "Imaging procedure"),
            patientId);

    public static MeasurementGroup CreateGroup(
        string trackingIdentifier = "lesion-1",
        string trackingUid = "1.2.826.0.1.3680043.10.511.30",
        IReadOnlyList<Measurement>? measurements = null,
        IReadOnlyList<SopInstanceReference>? sourceImages = null,
        IReadOnlyList<SegmentationReference>? segmentations = null,
        CodedConcept? findingSite = null,
        AlgorithmIdentification? algorithm = null) =>
        new(
            new TrackingIdentifier(trackingIdentifier, trackingUid),
            new CodedConcept("108369006", "SCT", "Neoplasm"),
            measurements ?? new[] { CreateMeasurement() },
            findingSite ?? new CodedConcept("39607008", "SCT", "Lung structure"),
            algorithm ?? new AlgorithmIdentification("Synthetic AI", "1.0"),
            sourceImages ?? new[] { CreateSource() },
            segmentations ?? new[] { CreateSegmentation() });

    public static Measurement CreateMeasurement(
        decimal value = 12.4m,
        string unit = "mm",
        SpatialCoordinate2D? coordinate = null,
        CodedConcept? method = null,
        CodedConcept? derivation = null) =>
        new(
            new CodedConcept("G-D7FE", "SRT", "Length"),
            new UcumQuantity(value, unit),
            method ?? new CodedConcept("370129005", "SCT", "Measurement Method"),
            derivation,
            coordinate ?? new SpatialCoordinate2D(
                GraphicType2D.Polyline,
                new[] { new Point2D(10, 20), new Point2D(30, 40) },
                CreateSource()));

    public static SopInstanceReference CreateSource(
        string instanceUid = SourceInstanceUid,
        string? studyUid = StudyUid,
        string? seriesUid = SourceSeriesUid,
        IReadOnlyList<int>? frames = null) =>
        new(
            "1.2.840.10008.5.1.4.1.1.2",
            instanceUid,
            studyUid,
            seriesUid,
            frames ?? new[] { 7 });

    public static SegmentationReference CreateSegmentation(
        int segmentNumber = 3,
        string sopClassUid = "1.2.840.10008.5.1.4.1.1.66.4",
        string instanceUid = SegInstanceUid,
        string? studyUid = StudyUid,
        string? seriesUid = SegSeriesUid) =>
        new(
            new SopInstanceReference(sopClassUid, instanceUid, studyUid, seriesUid),
            segmentNumber);
}
