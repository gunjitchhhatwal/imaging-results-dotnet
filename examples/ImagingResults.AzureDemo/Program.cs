using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FellowOakDicom;
using Hl7.Fhir.Serialization;
using ImagingResults;
using ImagingResults.Dicom;
using ImagingResults.Dicom.Azure;
using ImagingResults.Fhir.Azure;
using ImagingResults.Fhir.R4;
using ImagingResults.Validation;
using FhirBundle = Hl7.Fhir.Model.Bundle;
using FhirDiagnosticReport = Hl7.Fhir.Model.DiagnosticReport;
using FhirExtension = Hl7.Fhir.Model.Extension;
using FhirImagingStudy = Hl7.Fhir.Model.ImagingStudy;
using FhirPatient = Hl7.Fhir.Model.Patient;
using FhirUri = Hl7.Fhir.Model.FhirUri;

var options = DemoOptions.Parse(args);

if (options.DicomDirectory is not null)
{
    await UploadDicomDirectoryAsync(
        options.DicomDirectory,
        options.DicomServiceUrl!);
    return;
}

if (options.FhirFromDicomSrPath is not null)
{
    await ExportFhirFromDicomSrAsync(
        options.FhirFromDicomSrPath,
        options.OutputDirectory,
        options.FhirServiceUrl);
    return;
}

var inputPath = Path.Combine(AppContext.BaseDirectory, "synthetic-viewer-case.json");
var outputDirectory = options.OutputDirectory is not null
    ? Path.GetFullPath(options.OutputDirectory)
    : Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "azure-demo");
Directory.CreateDirectory(outputDirectory);

var input = JsonSerializer.Deserialize<SyntheticAiResult>(
    File.ReadAllText(inputPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Synthetic AI input could not be read.");

var sourceImage = new SopInstanceReference(
    input.Source.SopClassUid,
    input.Source.SopInstanceUid,
    input.Source.StudyInstanceUid,
    input.Source.SeriesInstanceUid);
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
                    new CodedConcept("410668003", "SCT", "Length"),
                    new UcumQuantity(input.Finding.LongAxisMillimeters, "mm"),
                    spatialCoordinate: new SpatialCoordinate2D(
                        GraphicType2D.Polyline,
                        input.Finding.LongAxisPixels
                            .Select(point => new Point2D(point.X, point.Y))
                            .ToArray(),
                        sourceImage))
            },
            new CodedConcept("39607008", "SCT", "Lung structure"),
            new AlgorithmIdentification(input.Algorithm.Name, input.Algorithm.Version),
            new[] { sourceImage })
    },
    new ObserverContext(ObserverKind.Device, input.Algorithm.Name, input.Algorithm.Identifier),
    new CodedConcept("363679005", "SCT", "Imaging procedure"),
    input.PatientId);

var errors = new MeasurementReportValidator()
    .Validate(report)
    .Where(item => item.Severity == DiagnosticSeverity.Error)
    .ToArray();
if (errors.Length > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
}

var sourceCt = SyntheticCtFactory.Create(input);
var sourceCtPath = Path.Combine(outputDirectory, "synthetic-lung-ct.dcm");
sourceCt.Save(sourceCtPath);

var dicomFile = new Tid1500Writer().Write(report);
var dicomPath = Path.Combine(outputDirectory, "lung-nodule-measurement-report.dcm");
dicomFile.Save(dicomPath);

var fhirMapping = new FhirR4Mapper().Map(report);
var fhirPath = Path.Combine(outputDirectory, "lung-nodule-fhir-r4-bundle.json");
File.WriteAllText(
    fhirPath,
    JsonSerializer.Serialize(
        fhirMapping.Bundle,
        new JsonSerializerOptions().ForFhir().Pretty()));

Console.WriteLine($"DICOM CT: {sourceCtPath}");
Console.WriteLine($"DICOM SR: {dicomPath}");
Console.WriteLine($"FHIR R4:  {fhirPath}");

if (options.DicomServiceUrl is not null)
{
    using var client = new AhdsDicomClient(
        new AhdsDicomOptions(options.DicomServiceUrl));
    var result = await client.StoreAsync(new[] { sourceCt, dicomFile });
    if (result.FailedInstances.Count > 0)
    {
        throw new InvalidOperationException(
            $"AHDS DICOM accepted the request but reported {result.FailedInstances.Count} failed instance(s).");
    }

    Console.WriteLine(
        $"AHDS DICOM: {(int)result.StatusCode} {result.StatusCode}; "
        + $"{result.StoredInstances.Count} stored instance(s).");
}
else
{
    Console.WriteLine("AHDS DICOM: skipped; --dicom-service-url was not supplied.");
}

if (options.FhirServiceUrl is not null)
{
    using var client = new AhdsFhirClient(
        new AhdsFhirOptions(options.FhirServiceUrl));
    var result = await client.UploadAsync(fhirMapping);
    Console.WriteLine(
        $"AHDS FHIR:  {(int)result.StatusCode} {result.StatusCode}; "
        + $"{result.TransactionBundle.Entry.Count} submitted resources.");
}
else
{
    Console.WriteLine("AHDS FHIR:  skipped; --fhir-service-url was not supplied.");
}

Console.WriteLine("The generated SR references the uploaded synthetic CT instance.");

static async Task UploadDicomDirectoryAsync(
    string directory,
    Uri dicomServiceUrl)
{
    var fullPath = Path.GetFullPath(directory);
    var paths = Directory
        .EnumerateFiles(fullPath, "*.dcm", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToArray();
    if (paths.Length == 0)
    {
        throw new InvalidOperationException(
            $"No DICOM files were found in {fullPath}.");
    }

    var files = new List<DicomFile>(paths.Length);
    foreach (var path in paths)
    {
        files.Add(await DicomFile.OpenAsync(path));
    }

    var studies = files
        .Select(file => file.Dataset.GetSingleValue<string>(
            DicomTag.StudyInstanceUID))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (studies.Length != 1)
    {
        throw new InvalidOperationException(
            $"The directory must contain exactly one DICOM study; found {studies.Length}.");
    }

    using var client = new AhdsDicomClient(
        new AhdsDicomOptions(dicomServiceUrl));
    var result = await client.StoreAsync(files);
    if (result.FailedInstances.Count > 0)
    {
        throw new InvalidOperationException(
            $"AHDS DICOM reported {result.FailedInstances.Count} failed instance(s).");
    }

    Console.WriteLine(
        $"AHDS DICOM: {(int)result.StatusCode} {result.StatusCode}; "
        + $"{result.StoredInstances.Count} stored instance(s).");
    Console.WriteLine($"Study Instance UID: {studies[0]}");
}

static async Task ExportFhirFromDicomSrAsync(
    string srPath,
    string? outputDirectory,
    Uri? fhirServiceUrl)
{
    var fullSrPath = Path.GetFullPath(srPath);
    var dicomFile = await DicomFile.OpenAsync(fullSrPath);
    var readResult = new Tid1500Reader().Read(dicomFile);
    if (!readResult.IsSuccess || readResult.Report is null)
    {
        throw new InvalidOperationException(
            "The DICOM SR could not be parsed as the supported TID 1500 subset:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, readResult.Diagnostics));
    }

    var report = readResult.Report;
    var mapping = new FhirR4Mapper().Map(report);
    AddDeidentifiedPatient(mapping.Bundle, report.PatientId);
    AddDemoPresentation(mapping.Bundle, report);

    var destination = outputDirectory is null
        ? Path.GetDirectoryName(fullSrPath)!
        : Path.GetFullPath(outputDirectory);
    Directory.CreateDirectory(destination);

    var jsonOptions = new JsonSerializerOptions().ForFhir().Pretty();
    var collectionPath = Path.Combine(
        destination,
        "real-renal-measurement-fhir-r4-collection.json");
    File.WriteAllText(
        collectionPath,
        JsonSerializer.Serialize(mapping.Bundle, jsonOptions));

    var transaction = AhdsFhirClient.CreateTransactionBundle(mapping.Bundle);
    PrepareMedplumTransaction(transaction);
    var transactionPath = Path.Combine(
        destination,
        "real-renal-measurement-fhir-r4-transaction.json");
    File.WriteAllText(
        transactionPath,
        JsonSerializer.Serialize(transaction, jsonOptions));

    Console.WriteLine($"FHIR R4 collection:  {collectionPath}");
    Console.WriteLine($"FHIR R4 transaction: {transactionPath}");
    Console.WriteLine(
        $"Mapped {report.Groups.Count} measurement group(s) and "
        + $"{report.Groups.Sum(group => group.Measurements.Count)} measurement(s).");
    foreach (var diagnostic in readResult.Diagnostics.Concat(mapping.Diagnostics))
    {
        Console.WriteLine(diagnostic);
    }

    if (fhirServiceUrl is not null)
    {
        using var client = new AhdsFhirClient(
            new AhdsFhirOptions(fhirServiceUrl));
        var upload = await client.UploadAsync(mapping.Bundle);
        Console.WriteLine(
            $"AHDS FHIR: {(int)upload.StatusCode} {upload.StatusCode}; "
            + $"{upload.TransactionBundle.Entry.Count} submitted resources.");
    }
    else
    {
        Console.WriteLine(
            "AHDS FHIR: skipped; --fhir-service-url was not supplied.");
    }
}

static void PrepareMedplumTransaction(FhirBundle transaction)
{
    var patientEntry = transaction.Entry.Single(
        entry => entry.Resource is FhirPatient);
    var patient = patientEntry.Resource as FhirPatient
        ?? throw new InvalidOperationException(
            "The Medplum transaction requires a Patient resource.");
    var previousPatientReference = $"Patient/{patient.Id}";
    var patientUuid = StableUuid("patient", patient.Id!);
    patient.Id = patientUuid;
    patientEntry.FullUrl = $"urn:uuid:{patientUuid}";

    foreach (var entry in transaction.Entry)
    {
        var resource = entry.Resource
            ?? throw new InvalidOperationException(
                "Every transaction entry must contain a resource.");
        var uuid = entry.FullUrl?.StartsWith(
            "urn:uuid:",
            StringComparison.Ordinal) == true
            ? entry.FullUrl["urn:uuid:".Length..]
            : StableUuid(resource.TypeName, resource.Id!);
        entry.FullUrl = $"urn:uuid:{uuid}";
        resource.Id = null;
        entry.Request!.Method = FhirBundle.HTTPVerb.POST;
        entry.Request.Url = resource.TypeName;
    }

    var patientFullUrl = patientEntry.FullUrl;
    foreach (var subject in transaction.Entry
        .Select(entry => entry.Resource)
        .Select(resource => resource switch
        {
            FhirImagingStudy study => study.Subject,
            FhirDiagnosticReport report => report.Subject,
            Hl7.Fhir.Model.Observation observation => observation.Subject,
            _ => null
        })
        .Where(reference =>
            string.Equals(
                reference?.Reference,
                previousPatientReference,
                StringComparison.Ordinal)))
    {
        subject!.Reference = patientFullUrl;
    }
}

static string StableUuid(params string[] parts)
{
    var bytes = SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", parts)))[..16];
    bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
    bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
    return new Guid(bytes, bigEndian: true).ToString(
        "D",
        System.Globalization.CultureInfo.InvariantCulture);
}

static void AddDeidentifiedPatient(FhirBundle bundle, string? patientId)
{
    if (string.IsNullOrWhiteSpace(patientId))
    {
        return;
    }

    var patientReference = bundle.Entry
        .Select(entry => entry.Resource)
        .OfType<FhirImagingStudy>()
        .Select(study => study.Subject?.Reference)
        .FirstOrDefault(reference => !string.IsNullOrWhiteSpace(reference));
    var resourceId = patientReference?.StartsWith(
        "Patient/",
        StringComparison.Ordinal) == true
        ? patientReference["Patient/".Length..]
        : patientId;

    bundle.Entry.Insert(
        0,
        new FhirBundle.EntryComponent
        {
            Resource = new FhirPatient
            {
                Id = resourceId,
                Active = true,
                Identifier = new List<Hl7.Fhir.Model.Identifier>
                {
                    new("urn:imaging-results:deidentified-subject", patientId)
                },
                Name = new List<Hl7.Fhir.Model.HumanName>
                {
                    new() { Text = patientId }
                }
            }
        });
}

static void AddDemoPresentation(FhirBundle bundle, MeasurementReport report)
{
    var reportResource = bundle.Entry
        .Select(entry => entry.Resource)
        .OfType<FhirDiagnosticReport>()
        .Single();
    var measurements = report.Groups
        .SelectMany(group => group.Measurements.Select(measurement =>
            $"{measurement.Value.Value:0.##} {measurement.Value.Unit} "
            + $"{group.Finding.Meaning}"))
        .ToArray();
    reportResource.Conclusion =
        $"Quantitative research annotation: {string.Join(", ", measurements)}. "
        + "Source DICOM SR is complete but unverified.";
    reportResource.Extension.Add(new FhirExtension(
        "https://imagingresults.net/fhir/StructureDefinition/ohif-viewer-url",
        new FhirUri(
            "http://localhost:3000/viewer?StudyInstanceUIDs="
            + Uri.EscapeDataString(report.StudyInstanceUid))));
}

internal sealed record SyntheticAiResult(
    string PatientId,
    SyntheticSource Source,
    SyntheticFinding Finding,
    SyntheticAlgorithm Algorithm,
    SyntheticReport Report);

internal sealed record SyntheticSource(
    string StudyInstanceUid,
    string SeriesInstanceUid,
    string SopClassUid,
    string SopInstanceUid);

internal sealed record SyntheticFinding(
    string Label,
    string TrackingIdentifier,
    string TrackingUid,
    decimal LongAxisMillimeters,
    IReadOnlyList<SyntheticPoint> LongAxisPixels);

internal sealed record SyntheticPoint(decimal X, decimal Y);

internal sealed record SyntheticAlgorithm(string Identifier, string Name, string Version);

internal sealed record SyntheticReport(
    string SeriesInstanceUid,
    string SopInstanceUid,
    DateTimeOffset CreatedAt);

internal sealed record DemoOptions(
    string? OutputDirectory,
    string? DicomDirectory,
    string? FhirFromDicomSrPath,
    Uri? DicomServiceUrl,
    Uri? FhirServiceUrl)
{
    public static DemoOptions Parse(string[] arguments)
    {
        string? outputDirectory = null;
        string? dicomDirectory = null;
        string? fhirFromDicomSrPath = null;
        Uri? dicomServiceUrl = null;
        Uri? fhirServiceUrl = null;

        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            switch (option.ToLowerInvariant())
            {
                case "--output-directory":
                    outputDirectory = ReadValue(arguments, ref index, option);
                    break;
                case "--dicom-directory":
                    dicomDirectory = ReadValue(arguments, ref index, option);
                    break;
                case "--fhir-from-dicom-sr":
                    fhirFromDicomSrPath = ReadValue(arguments, ref index, option);
                    break;
                case "--dicom-service-url":
                    dicomServiceUrl = ReadServiceUrl(arguments, ref index, option);
                    break;
                case "--fhir-service-url":
                    fhirServiceUrl = ReadServiceUrl(arguments, ref index, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (dicomDirectory is not null && fhirFromDicomSrPath is not null)
        {
            throw new ArgumentException(
                "--dicom-directory and --fhir-from-dicom-sr cannot be combined.");
        }

        if (dicomDirectory is not null && dicomServiceUrl is null)
        {
            throw new ArgumentException(
                "--dicom-directory requires --dicom-service-url.");
        }

        return new DemoOptions(
            outputDirectory,
            dicomDirectory,
            fhirFromDicomSrPath,
            dicomServiceUrl,
            fhirServiceUrl);
    }

    private static string ReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string option)
    {
        if (++index >= arguments.Count
            || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return arguments[index];
    }

    private static Uri ReadServiceUrl(
        IReadOnlyList<string> arguments,
        ref int index,
        string option)
    {
        var value = ReadValue(arguments, ref index, option);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"{option} must be an absolute HTTPS URL.");
        }

        return url;
    }
}
