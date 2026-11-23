using System.Globalization;
using FellowOakDicom;
using ImagingResults.Validation;

namespace ImagingResults.Dicom;

public sealed class Tid1500ReadResult
{
    public Tid1500ReadResult(MeasurementReport? report, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        Report = report;
        Diagnostics = diagnostics;
    }

    public MeasurementReport? Report { get; }
    public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }
    public bool IsSuccess => Report is not null && Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public sealed class Tid1500Reader
{
    public Tid1500ReadResult Read(DicomFile file)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var dataset = file.Dataset;
        var rootName = ReadCode(dataset, DicomTag.ConceptNameCodeSequence);
        if (rootName?.Value != Tid1500Codes.ImagingMeasurementReport.Value)
        {
            Error("TID1500.ROOT_CONCEPT", "$.ConceptNameCodeSequence", "The SR root is not an Imaging Measurement Report.");
            return new Tid1500ReadResult(null, diagnostics);
        }

        var content = SequenceOrEmpty(dataset, DicomTag.ContentSequence);
        var observerKind = FindCode(content, Tid1500Codes.ObserverType.Value)?.Value == Tid1500Codes.Device.Value
            ? ObserverKind.Device
            : ObserverKind.Person;
        var observerNameCode = observerKind == ObserverKind.Person ? Tid1500Codes.ObserverName.Value : Tid1500Codes.DeviceObserverName.Value;
        var observerName = Find(content, observerNameCode)?.GetSingleValueOrDefault(DicomTag.TextValue, "Unknown") ?? "Unknown";
        var procedure = ReadCode(Find(content, Tid1500Codes.ProcedureReported.Value), DicomTag.ConceptCodeSequence);
        var evidence = ReadEvidence(dataset);

        var groups = new List<MeasurementGroup>();
        var imagingMeasurements = content.FirstOrDefault(
            item => ConceptValue(item) == Tid1500Codes.ImagingMeasurements.Value);
        var measurementContent = imagingMeasurements is null
            ? content
            : SequenceOrEmpty(imagingMeasurements, DicomTag.ContentSequence);
        var groupItems = measurementContent
            .Where(item => ConceptValue(item) == Tid1500Codes.MeasurementGroup.Value)
            .ToArray();
        for (var index = 0; index < groupItems.Length; index++)
        {
            var group = ReadGroup(groupItems[index], index, diagnostics, evidence);
            if (group is not null)
            {
                groups.Add(group);
            }
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new Tid1500ReadResult(null, diagnostics);
        }

        var date = dataset.GetSingleValueOrDefault(DicomTag.ContentDate, "19700101");
        var time = dataset.GetSingleValueOrDefault(DicomTag.ContentTime, "000000");
        DateTimeOffset createdAt;
        if (!DateTimeOffset.TryParseExact(
                date + time.PadRight(6, '0').Substring(0, 6),
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out createdAt))
        {
            createdAt = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        var studyUid = RequiredUid(DicomTag.StudyInstanceUID, "$.StudyInstanceUID");
        var seriesUid = RequiredUid(DicomTag.SeriesInstanceUID, "$.SeriesInstanceUID");
        var sopInstanceUid = RequiredUid(DicomTag.SOPInstanceUID, "$.SOPInstanceUID");
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new Tid1500ReadResult(null, diagnostics);
        }

        MeasurementReport report;
        try
        {
            report = new MeasurementReport(
                studyUid!,
                seriesUid!,
                sopInstanceUid!,
                createdAt,
                groups,
                new ObserverContext(observerKind, observerName),
                procedure,
                dataset.GetSingleValueOrDefault<string?>(DicomTag.PatientID, null));
        }
        catch (ArgumentException exception) when (exception is not ArgumentNullException)
        {
            Error("DICOM.REPORT_CONTEXT_INVALID", "$", "The SR report context contains an invalid DICOM UID.");
            return new Tid1500ReadResult(null, diagnostics);
        }

        diagnostics.AddRange(new MeasurementReportValidator().Validate(report));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new Tid1500ReadResult(null, diagnostics);
        }

        return new Tid1500ReadResult(report, diagnostics);

        void Error(string code, string path, string message) =>
            diagnostics.Add(new ValidationDiagnostic(code, DiagnosticSeverity.Error, path, message, "ImagingResults.Dicom"));

        string? RequiredUid(DicomTag tag, string path)
        {
            var value = dataset.GetSingleValueOrDefault<string?>(tag, null);
            if (string.IsNullOrWhiteSpace(value))
            {
                Error("DICOM.HEADER_REQUIRED", path, $"{tag.DictionaryEntry.Name} is required.");
            }

            return value;
        }
    }

    private static MeasurementGroup? ReadGroup(
        DicomDataset item,
        int groupIndex,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> evidence)
    {
        var path = $"$.groups[{groupIndex}]";
        var content = SequenceOrEmpty(item, DicomTag.ContentSequence);
        var trackingIdentifier = Find(content, Tid1500Codes.TrackingIdentifier.Value)?.GetSingleValueOrDefault(DicomTag.TextValue, string.Empty);
        var trackingUid = Find(content, Tid1500Codes.TrackingUniqueIdentifier.Value)?.GetSingleValueOrDefault(DicomTag.UID, string.Empty);
        var findingSite = ReadCode(
            Find(
                content,
                Tid1500Codes.FindingSite.Value,
                Tid1500Codes.LegacyFindingSite.Value),
            DicomTag.ConceptCodeSequence);
        var finding = ReadCode(Find(content, Tid1500Codes.Finding.Value), DicomTag.ConceptCodeSequence);
        if (finding is null && findingSite is not null)
        {
            finding = findingSite;
            diagnostics.Add(new ValidationDiagnostic(
                "TID1500.FINDING_INFERRED_FROM_SITE",
                DiagnosticSeverity.Warning,
                $"{path}.finding",
                "Finding is absent; the finding-site concept is used as the domain-model finding.",
                "ImagingResults.Dicom"));
        }

        if (string.IsNullOrWhiteSpace(trackingIdentifier) || string.IsNullOrWhiteSpace(trackingUid) || finding is null)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(trackingIdentifier))
            {
                missing.Add("tracking identifier");
            }

            if (string.IsNullOrWhiteSpace(trackingUid))
            {
                missing.Add("tracking UID");
            }

            if (finding is null)
            {
                missing.Add("finding or finding site");
            }

            diagnostics.Add(new ValidationDiagnostic(
                "TID1500.GROUP_CONTEXT",
                DiagnosticSeverity.Error,
                path,
                $"Measurement group is missing: {string.Join(", ", missing)}.",
                "ImagingResults.Dicom"));
            return null;
        }

        var algorithmName = Find(content, Tid1500Codes.AlgorithmName.Value)?.GetSingleValueOrDefault<string?>(DicomTag.TextValue, null);
        var algorithmVersion = Find(content, Tid1500Codes.AlgorithmVersion.Value)?.GetSingleValueOrDefault<string?>(DicomTag.TextValue, null);
        var algorithm = algorithmName is null || algorithmVersion is null ? null : new AlgorithmIdentification(algorithmName, algorithmVersion);

        var explicitImages = content
            .Where(candidate =>
                ConceptValue(candidate) == Tid1500Codes.SourceImage.Value
                || (candidate.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "IMAGE"
                    && ConceptValue(candidate) is null))
            .Select(candidate => ReadImageReference(candidate, evidence))
            .Where(reference => reference is not null)
            .Cast<SopInstanceReference>()
            .ToArray();
        var segmentations = content
            .Where(candidate => ConceptValue(candidate) == Tid1500Codes.ReferencedSegment.Value)
            .Select(candidate => ReadSegmentationReference(candidate, evidence))
            .Where(reference => reference is not null)
            .Cast<SegmentationReference>()
            .ToArray();

        var measurements = content
            .Where(candidate => candidate.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "NUM")
            .Select((candidate, index) => ReadMeasurement(candidate, $"{path}.measurements[{index}]", diagnostics, evidence))
            .Where(measurement => measurement is not null)
            .Cast<Measurement>()
            .ToArray();
        var images = explicitImages
            .Concat(measurements
                .Where(measurement => measurement.SpatialCoordinate is not null)
                .Select(measurement => measurement.SpatialCoordinate!.SourceImage))
            .GroupBy(reference => reference.SopInstanceUid, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        TrackingIdentifier tracking;
        try
        {
            tracking = new TrackingIdentifier(trackingIdentifier!, trackingUid!);
        }
        catch (ArgumentException exception) when (exception is not ArgumentNullException)
        {
            diagnostics.Add(new ValidationDiagnostic(
                "TID1500.GROUP_INVALID",
                DiagnosticSeverity.Error,
                $"{path}.tracking",
                "Measurement group tracking content is invalid.",
                "ImagingResults.Dicom"));
            return null;
        }

        return new MeasurementGroup(
            tracking,
            finding,
            measurements,
            findingSite,
            algorithm,
            images,
            segmentations);
    }

    private static Measurement? ReadMeasurement(
        DicomDataset item,
        string path,
        ICollection<ValidationDiagnostic> diagnostics,
        IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> evidence)
    {
        var type = ReadCode(item, DicomTag.ConceptNameCodeSequence);
        var measuredValues = SequenceOrEmpty(item, DicomTag.MeasuredValueSequence);
        if (type is null || measuredValues.Count != 1)
        {
            Invalid("A measurement must have a concept name and one measured value.");
            return null;
        }

        var measuredValue = measuredValues[0];
        var numericText = measuredValue.GetSingleValueOrDefault(DicomTag.NumericValue, string.Empty);
        var unit = ReadCode(measuredValue, DicomTag.MeasurementUnitsCodeSequence);
        if (!decimal.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericValue) || unit is null)
        {
            Invalid("A measurement must have a decimal numeric value and UCUM unit.");
            return null;
        }

        var children = SequenceOrEmpty(item, DicomTag.ContentSequence);
        var method = ReadCode(Find(children, Tid1500Codes.MeasurementMethod.Value), DicomTag.ConceptCodeSequence);
        var derivation = ReadCode(Find(children, Tid1500Codes.Derivation.Value), DicomTag.ConceptCodeSequence);
        var spatialItem = Find(children, Tid1500Codes.ImageRegion.Value)
            ?? children.FirstOrDefault(candidate =>
                candidate.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) == "SCOORD"
                && ConceptValue(candidate) is null);
        var spatial = ReadSpatialCoordinate(spatialItem, evidence);
        return new Measurement(type, new UcumQuantity(numericValue, unit.Value, unit.Meaning), method, derivation, spatial);

        void Invalid(string message) =>
            diagnostics.Add(new ValidationDiagnostic("TID1500.NUM_INVALID", DiagnosticSeverity.Error, path, message, "ImagingResults.Dicom"));
    }

    private static SpatialCoordinate2D? ReadSpatialCoordinate(
        DicomDataset? item,
        IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> evidence)
    {
        if (item is null || item.GetSingleValueOrDefault(DicomTag.ValueType, string.Empty) != "SCOORD")
        {
            return null;
        }

        var source = SequenceOrEmpty(item, DicomTag.ContentSequence)
            .Select(candidate => ReadImageReference(candidate, evidence))
            .FirstOrDefault(reference => reference is not null);
        if (source is null)
        {
            return null;
        }

        var graphicTypeText = item.GetSingleValueOrDefault(DicomTag.GraphicType, "POINT");
        if (!Enum.TryParse<GraphicType2D>(graphicTypeText, true, out var graphicType))
        {
            graphicType = GraphicType2D.Point;
        }

        var values = item.GetValues<float>(DicomTag.GraphicData);
        var points = values
            .Select((value, index) => new { value, index })
            .GroupBy(entry => entry.index / 2)
            .Where(group => group.Count() == 2)
            .Select(group => new Point2D((decimal)group.ElementAt(0).value, (decimal)group.ElementAt(1).value))
            .ToArray();
        return points.Length == 0 ? null : new SpatialCoordinate2D(graphicType, points, source);
    }

    private static SegmentationReference? ReadSegmentationReference(
        DicomDataset item,
        IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> evidence)
    {
        var sop = SequenceOrEmpty(item, DicomTag.ReferencedSOPSequence).FirstOrDefault();
        var reference = ReadImageReference(item, evidence);
        if (sop is null || reference is null)
        {
            return null;
        }

        var segmentNumber = sop.GetSingleValueOrDefault(DicomTag.ReferencedSegmentNumber, (ushort)0);
        return segmentNumber > 0 ? new SegmentationReference(reference, segmentNumber) : null;
    }

    private static SopInstanceReference? ReadImageReference(
        DicomDataset item,
        IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> evidence)
    {
        var sop = SequenceOrEmpty(item, DicomTag.ReferencedSOPSequence).FirstOrDefault();
        if (sop is null)
        {
            return null;
        }

        var sopClass = sop.GetSingleValueOrDefault<string?>(DicomTag.ReferencedSOPClassUID, null);
        var sopInstance = sop.GetSingleValueOrDefault<string?>(DicomTag.ReferencedSOPInstanceUID, null);
        if (sopClass is null || sopInstance is null)
        {
            return null;
        }

        var frames = sop.Contains(DicomTag.ReferencedFrameNumber)
            ? sop.GetValues<int>(DicomTag.ReferencedFrameNumber)
            : Array.Empty<int>();
        evidence.TryGetValue(sopInstance, out var context);
        return new SopInstanceReference(
            sopClass,
            sopInstance,
            string.IsNullOrEmpty(context.StudyUid) ? null : context.StudyUid,
            string.IsNullOrEmpty(context.SeriesUid) ? null : context.SeriesUid,
            frames);
    }

    private static IReadOnlyDictionary<string, (string StudyUid, string SeriesUid)> ReadEvidence(DicomDataset dataset)
    {
        var evidence = new Dictionary<string, (string StudyUid, string SeriesUid)>(StringComparer.Ordinal);
        foreach (var study in SequenceOrEmpty(dataset, DicomTag.CurrentRequestedProcedureEvidenceSequence))
        {
            var studyUid = study.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty);
            foreach (var series in SequenceOrEmpty(study, DicomTag.ReferencedSeriesSequence))
            {
                var seriesUid = series.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty);
                foreach (var instance in SequenceOrEmpty(series, DicomTag.ReferencedSOPSequence))
                {
                    var instanceUid = instance.GetSingleValueOrDefault(DicomTag.ReferencedSOPInstanceUID, string.Empty);
                    if (!string.IsNullOrEmpty(instanceUid))
                    {
                        evidence[instanceUid] = (studyUid, seriesUid);
                    }
                }
            }
        }

        return evidence;
    }

    private static CodedConcept? FindCode(IReadOnlyList<DicomDataset> content, string conceptValue) =>
        ReadCode(Find(content, conceptValue), DicomTag.ConceptCodeSequence);

    private static DicomDataset? Find(
        IEnumerable<DicomDataset> content,
        params string[] conceptValues) =>
        content.FirstOrDefault(
            item => conceptValues.Contains(ConceptValue(item), StringComparer.Ordinal));

    private static string? ConceptValue(DicomDataset item) =>
        ReadCode(item, DicomTag.ConceptNameCodeSequence)?.Value;

    private static CodedConcept? ReadCode(DicomDataset? owner, DicomTag tag)
    {
        if (owner is null || !owner.TryGetSequence(tag, out var sequence) || sequence.Items.Count != 1)
        {
            return null;
        }

        var item = sequence.Items[0];
        var value = item.GetSingleValueOrDefault<string?>(DicomTag.CodeValue, null);
        var scheme = item.GetSingleValueOrDefault<string?>(DicomTag.CodingSchemeDesignator, null);
        var meaning = item.GetSingleValueOrDefault<string?>(DicomTag.CodeMeaning, null);
        return value is null || scheme is null || meaning is null
            ? null
            : new CodedConcept(value, scheme, meaning, item.GetSingleValueOrDefault<string?>(DicomTag.CodingSchemeVersion, null));
    }

    private static IReadOnlyList<DicomDataset> SequenceOrEmpty(DicomDataset dataset, DicomTag tag) =>
        dataset.TryGetSequence(tag, out var sequence) ? sequence.Items.ToArray() : Array.Empty<DicomDataset>();
}
