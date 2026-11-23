using System.Text.Json;
using Hl7.Fhir.Serialization;
using ImagingResults;
using ImagingResults.Dicom;
using ImagingResults.Fhir;
using ImagingResults.Validation;

var inputPath = Path.Combine(AppContext.BaseDirectory, "synthetic-lung-nodule-case.json");
var outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "demo");
Directory.CreateDirectory(outputDirectory);

var input = JsonSerializer.Deserialize<SyntheticAiResult>(
    File.ReadAllText(inputPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Synthetic AI input could not be read.");

var sourceImage = new SopInstanceReference(
    input.Source.SopClassUid,
    input.Source.SopInstanceUid,
    input.Source.StudyInstanceUid,
    input.Source.SeriesInstanceUid,
    new[] { input.Source.FrameNumber });
var segmentation = new SopInstanceReference(
    "1.2.840.10008.5.1.4.1.1.66.4",
    input.Segmentation.SopInstanceUid,
    input.Source.StudyInstanceUid,
    input.Segmentation.SeriesInstanceUid);

var report = new MeasurementReport(
    input.Source.StudyInstanceUid,
    input.Report.SeriesInstanceUid,
    input.Report.SopInstanceUid,
    input.Report.CreatedAt,
    new[]
    {
        new MeasurementGroup(
            new TrackingIdentifier(input.Finding.TrackingIdentifier, input.Finding.TrackingUid),
            new CodedConcept("39607008", "SCT", "Lung structure"),
            new[]
            {
                new Measurement(
                    new CodedConcept("G-D7FE", "SRT", "Length"),
                    new UcumQuantity(input.Finding.LongAxisMillimeters, "mm"),
                    new CodedConcept("370129005", "SCT", "Measurement Method"),
                    spatialCoordinate: new SpatialCoordinate2D(
                        GraphicType2D.Polyline,
                        input.Finding.LongAxisPixels
                            .Select(point => new Point2D(point.X, point.Y))
                            .ToArray(),
                        sourceImage))
            },
            new CodedConcept("39607008", "SCT", "Lung structure"),
            new AlgorithmIdentification(input.Algorithm.Name, input.Algorithm.Version),
            new[] { sourceImage },
            new[] { new SegmentationReference(segmentation, input.Segmentation.SegmentNumber) })
    },
    new ObserverContext(ObserverKind.Device, input.Algorithm.Name, input.Algorithm.Identifier),
    new CodedConcept("363679005", "SCT", "Imaging procedure"),
    input.PatientId);

var diagnostics = new MeasurementReportValidator().Validate(report);
var errors = diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
if (errors.Length > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
}

var dicomPath = Path.Combine(outputDirectory, "lung-nodule-measurement-report.dcm");
new Tid1500Writer().Write(report).Save(dicomPath);

var parsed = new Tid1500Reader().Read(FellowOakDicom.DicomFile.Open(dicomPath));
if (!parsed.IsSuccess || parsed.Report is null)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, parsed.Diagnostics));
}

var parsedMeasurement = parsed.Report.Groups[0].Measurements[0];
var parsedGroup = parsed.Report.Groups[0];
if (parsedMeasurement.Value != new UcumQuantity(input.Finding.LongAxisMillimeters, "mm")
    || parsedGroup.SourceImages[0].FrameNumbers.Single() != input.Source.FrameNumber
    || parsedGroup.Segmentations[0].SegmentNumber != input.Segmentation.SegmentNumber)
{
    throw new InvalidOperationException("DICOM SR round trip changed the measurement evidence.");
}

var fhirResult = new FhirR5Mapper().Map(parsed.Report);
var fhirJsonOptions = new JsonSerializerOptions().ForFhir().Pretty();
var fhirPath = Path.Combine(outputDirectory, "lung-nodule-fhir-bundle.json");
File.WriteAllText(fhirPath, JsonSerializer.Serialize(fhirResult.Bundle, fhirJsonOptions));

var summary = new
{
    input.PatientId,
    Finding = input.Finding.Label,
    Measurement = $"{parsedMeasurement.Value.Value} {parsedMeasurement.Value.Unit}",
    SourceFrame = parsedGroup.SourceImages[0].FrameNumbers.Single(),
    Segment = parsedGroup.Segmentations[0].SegmentNumber,
    DicomSr = Path.GetFileName(dicomPath),
    FhirBundle = Path.GetFileName(fhirPath),
    FhirResources = fhirResult.Bundle.Entry
        .Select(entry => entry.Resource?.TypeName ?? entry.FullUrl ?? "Unknown")
        .ToArray(),
    MappingDiagnostics = fhirResult.Diagnostics
};
var summaryPath = Path.Combine(outputDirectory, "round-trip-summary.json");
File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"Input:   {inputPath}");
Console.WriteLine($"DICOM:   {dicomPath}");
Console.WriteLine($"FHIR:    {fhirPath}");
Console.WriteLine($"Summary: {summaryPath}");
Console.WriteLine($"Verified {summary.Measurement} on CT frame {summary.SourceFrame}, SEG segment {summary.Segment}.");

internal sealed record SyntheticAiResult(
    string PatientId,
    SyntheticSource Source,
    SyntheticSegmentation Segmentation,
    SyntheticFinding Finding,
    SyntheticAlgorithm Algorithm,
    SyntheticReport Report);

internal sealed record SyntheticSource(
    string StudyInstanceUid,
    string SeriesInstanceUid,
    string SopClassUid,
    string SopInstanceUid,
    int FrameNumber);

internal sealed record SyntheticSegmentation(string SeriesInstanceUid, string SopInstanceUid, int SegmentNumber);

internal sealed record SyntheticFinding(
    string Label,
    string TrackingIdentifier,
    string TrackingUid,
    decimal LongAxisMillimeters,
    IReadOnlyList<SyntheticPoint> LongAxisPixels);

internal sealed record SyntheticPoint(decimal X, decimal Y);

internal sealed record SyntheticAlgorithm(string Identifier, string Name, string Version);

internal sealed record SyntheticReport(string SeriesInstanceUid, string SopInstanceUid, DateTimeOffset CreatedAt);
