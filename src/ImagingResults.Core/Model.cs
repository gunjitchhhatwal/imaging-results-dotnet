using System.Collections.ObjectModel;

namespace ImagingResults;

public sealed record CodedConcept
{
    public CodedConcept(string value, string schemeDesignator, string meaning, string? schemeVersion = null)
    {
        Value = Required(value, nameof(value));
        SchemeDesignator = Required(schemeDesignator, nameof(schemeDesignator));
        Meaning = Required(meaning, nameof(meaning));
        SchemeVersion = schemeVersion;
    }

    public string Value { get; }
    public string SchemeDesignator { get; }
    public string Meaning { get; }
    public string? SchemeVersion { get; }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value;
}

public sealed record UcumQuantity
{
    public UcumQuantity(decimal value, string unit, string? display = null)
    {
        Value = value;
        Unit = string.IsNullOrWhiteSpace(unit) ? throw new ArgumentException("A UCUM unit is required.", nameof(unit)) : unit;
        Display = string.IsNullOrWhiteSpace(display) ? unit : display!;
    }

    public decimal Value { get; }
    public string Unit { get; }
    public string Display { get; }
}

public sealed record TrackingIdentifier
{
    public TrackingIdentifier(string identifier, string uid)
    {
        Identifier = string.IsNullOrWhiteSpace(identifier) ? throw new ArgumentException("Tracking identifier is required.", nameof(identifier)) : identifier;
        Uid = DicomUid.RequireValid(uid, nameof(uid));
    }

    public string Identifier { get; }
    public string Uid { get; }
}

public sealed record AlgorithmIdentification(
    string Name,
    string Version,
    string? Family = null,
    IReadOnlyDictionary<string, string>? Parameters = null)
{
    public IReadOnlyDictionary<string, string> Parameters { get; } =
        new ReadOnlyDictionary<string, string>(
            (Parameters ?? new Dictionary<string, string>())
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
}

public enum ObserverKind
{
    Person,
    Device
}

public sealed record ObserverContext(ObserverKind Kind, string Name, string? Identifier = null, string? Organization = null);

public sealed record SopInstanceReference
{
    public SopInstanceReference(string sopClassUid, string sopInstanceUid, string? studyInstanceUid = null, string? seriesInstanceUid = null, IReadOnlyList<int>? frameNumbers = null)
    {
        SopClassUid = DicomUid.RequireValid(sopClassUid, nameof(sopClassUid));
        SopInstanceUid = DicomUid.RequireValid(sopInstanceUid, nameof(sopInstanceUid));
        StudyInstanceUid = OptionalUid(studyInstanceUid, nameof(studyInstanceUid));
        SeriesInstanceUid = OptionalUid(seriesInstanceUid, nameof(seriesInstanceUid));
        FrameNumbers = Array.AsReadOnly((frameNumbers ?? Array.Empty<int>()).Distinct().OrderBy(value => value).ToArray());
        if (FrameNumbers.Any(value => value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumbers), "Frame numbers are one-based.");
        }
    }

    public string SopClassUid { get; }
    public string SopInstanceUid { get; }
    public string? StudyInstanceUid { get; }
    public string? SeriesInstanceUid { get; }
    public IReadOnlyList<int> FrameNumbers { get; }

    private static string? OptionalUid(string? value, string name) =>
        value is null ? null : DicomUid.RequireValid(value, name);
}

public sealed record SegmentationReference
{
    public SegmentationReference(SopInstanceReference segmentation, int segmentNumber)
    {
        Segmentation = segmentation ?? throw new ArgumentNullException(nameof(segmentation));
        SegmentNumber = segmentNumber > 0 ? segmentNumber : throw new ArgumentOutOfRangeException(nameof(segmentNumber));
    }

    public SopInstanceReference Segmentation { get; }
    public int SegmentNumber { get; }
}

public enum GraphicType2D
{
    Point,
    Polyline,
    Circle,
    Ellipse
}

public readonly record struct Point2D(decimal X, decimal Y);

public sealed record SpatialCoordinate2D
{
    public SpatialCoordinate2D(GraphicType2D graphicType, IReadOnlyList<Point2D> points, SopInstanceReference sourceImage)
    {
        GraphicType = graphicType;
        Points = Array.AsReadOnly((points ?? throw new ArgumentNullException(nameof(points))).ToArray());
        SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
        if (Points.Count == 0)
        {
            throw new ArgumentException("At least one coordinate is required.", nameof(points));
        }
    }

    public GraphicType2D GraphicType { get; }
    public IReadOnlyList<Point2D> Points { get; }
    public SopInstanceReference SourceImage { get; }
}

public sealed record Measurement
{
    public Measurement(
        CodedConcept type,
        UcumQuantity value,
        CodedConcept? method = null,
        CodedConcept? derivation = null,
        SpatialCoordinate2D? spatialCoordinate = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Method = method;
        Derivation = derivation;
        SpatialCoordinate = spatialCoordinate;
        Properties = new ReadOnlyDictionary<string, string>(
            (properties ?? new Dictionary<string, string>())
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public CodedConcept Type { get; }
    public UcumQuantity Value { get; }
    public CodedConcept? Method { get; }
    public CodedConcept? Derivation { get; }
    public SpatialCoordinate2D? SpatialCoordinate { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }
}

public sealed record MeasurementGroup
{
    public MeasurementGroup(
        TrackingIdentifier tracking,
        CodedConcept finding,
        IReadOnlyList<Measurement> measurements,
        CodedConcept? findingSite = null,
        AlgorithmIdentification? algorithm = null,
        IReadOnlyList<SopInstanceReference>? sourceImages = null,
        IReadOnlyList<SegmentationReference>? segmentations = null)
    {
        Tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
        Measurements = Array.AsReadOnly((measurements ?? throw new ArgumentNullException(nameof(measurements))).ToArray());
        FindingSite = findingSite;
        Algorithm = algorithm;
        SourceImages = Array.AsReadOnly((sourceImages ?? Array.Empty<SopInstanceReference>()).ToArray());
        Segmentations = Array.AsReadOnly((segmentations ?? Array.Empty<SegmentationReference>()).ToArray());
    }

    public TrackingIdentifier Tracking { get; }
    public CodedConcept Finding { get; }
    public CodedConcept? FindingSite { get; }
    public AlgorithmIdentification? Algorithm { get; }
    public IReadOnlyList<Measurement> Measurements { get; }
    public IReadOnlyList<SopInstanceReference> SourceImages { get; }
    public IReadOnlyList<SegmentationReference> Segmentations { get; }
}

public sealed record MeasurementReport
{
    public MeasurementReport(
        string studyInstanceUid,
        string seriesInstanceUid,
        string sopInstanceUid,
        DateTimeOffset createdAt,
        IReadOnlyList<MeasurementGroup> groups,
        ObserverContext observer,
        CodedConcept? procedureReported = null,
        string? patientId = null)
    {
        StudyInstanceUid = DicomUid.RequireValid(studyInstanceUid, nameof(studyInstanceUid));
        SeriesInstanceUid = DicomUid.RequireValid(seriesInstanceUid, nameof(seriesInstanceUid));
        SopInstanceUid = DicomUid.RequireValid(sopInstanceUid, nameof(sopInstanceUid));
        CreatedAt = createdAt;
        Groups = Array.AsReadOnly((groups ?? throw new ArgumentNullException(nameof(groups))).ToArray());
        Observer = observer ?? throw new ArgumentNullException(nameof(observer));
        ProcedureReported = procedureReported;
        PatientId = patientId;
    }

    public string StudyInstanceUid { get; }
    public string SeriesInstanceUid { get; }
    public string SopInstanceUid { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<MeasurementGroup> Groups { get; }
    public ObserverContext Observer { get; }
    public CodedConcept? ProcedureReported { get; }
    public string? PatientId { get; }
}

internal static class DicomUid
{
    public static string RequireValid(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value[0] == '.' || value[value.Length - 1] == '.' ||
            value.Any(character => character != '.' && (character < '0' || character > '9')) ||
            value.Split('.').Any(part => part.Length == 0))
        {
            throw new ArgumentException("A valid DICOM UID is required.", name);
        }

        return value;
    }
}
