using System.Security.Cryptography;
using System.Text;

namespace ImagingResults.Fhir;

public abstract class FhirMapperBase<TMappingResult>
{
    public abstract TMappingResult Map(MeasurementReport report);

    protected static IReadOnlyList<StudySeries> GetStudySeries(MeasurementReport report) =>
        report.Groups
            .SelectMany(GetGroupReferences)
            .Where(reference =>
                reference.StudyInstanceUid is null
                || reference.StudyInstanceUid == report.StudyInstanceUid)
            .Where(reference => reference.SeriesInstanceUid is not null)
            .GroupBy(reference => reference.SeriesInstanceUid!, StringComparer.Ordinal)
            .Select(group => new StudySeries(group.Key, group.ToArray()))
            .ToArray();

    protected static IReadOnlyList<GroupSeries> GetGroupSeries(
        MeasurementReport report,
        MeasurementGroup group) =>
        group.SourceImages
            .Select(reference => new EvidenceReference(reference, null))
            .Concat(group.Measurements
                .Where(measurement => measurement.SpatialCoordinate is not null)
                .Select(measurement => new EvidenceReference(
                    measurement.SpatialCoordinate!.SourceImage,
                    null)))
            .Concat(group.Segmentations.Select(segmentation =>
                new EvidenceReference(segmentation.Segmentation, segmentation.SegmentNumber)))
            .DistinctBy(item => (
                item.Reference.SopInstanceUid,
                item.SegmentNumber))
            .GroupBy(
                item => item.Reference.SeriesInstanceUid ?? report.SeriesInstanceUid,
                StringComparer.Ordinal)
            .Select(series => new GroupSeries(series.Key, series.ToArray()))
            .ToArray();

    protected static ModalityResolution ResolveModality(IEnumerable<string> sopClassUids)
    {
        var modalities = sopClassUids.Select(ToModalityCode).Distinct(StringComparer.Ordinal).ToArray();
        return modalities.Length == 1 && modalities[0] is not null
            ? new ModalityResolution(modalities[0], true)
            : new ModalityResolution(null, false);
    }

    protected static string ToSystem(string scheme) => scheme switch
    {
        "DCM" => "http://dicom.nema.org/resources/ontology/DCM",
        "SCT" => "http://snomed.info/sct",
        "SRT" => "urn:dicom:coding-scheme:SRT",
        "LN" or "LOINC" => "http://loinc.org",
        "UCUM" => "http://unitsofmeasure.org",
        _ => $"urn:dicom:coding-scheme:{Uri.EscapeDataString(scheme)}"
    };

    protected static string StableId(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes, bigEndian: true).ToString("N", System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static string PatientResourceId(string value)
    {
        var isValid = value.Length is > 0 and <= 64
            && value.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-'
                    or '.');
        return isValid ? value : StableId("patient", value);
    }

    protected static string ToUuidUrn(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException("Mapped FHIR resources must have deterministic IDs.");
        }

        return $"urn:uuid:{Guid.ParseExact(id, "N").ToString("D", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    protected sealed record StudySeries(
        string SeriesInstanceUid,
        IReadOnlyList<SopInstanceReference> References);

    protected sealed record GroupSeries(
        string SeriesInstanceUid,
        IReadOnlyList<EvidenceReference> Evidence);

    protected sealed record EvidenceReference(
        SopInstanceReference Reference,
        int? SegmentNumber);

    protected sealed record ModalityResolution(string? Code, bool IsKnown);

    protected static readonly CodedConcept Tid1500ReportCode =
        new("126000", "DCM", "Imaging Measurement Report");

    protected static readonly CodedConcept FindingCode =
        new("121071", "DCM", "Finding");

    protected static readonly CodedConcept SelectionCode =
        new("121112", "DCM", "Source of Measurement");

    protected static readonly CodedConcept PersonObserverCode =
        new("121006", "DCM", "Person");

    protected static readonly CodedConcept DeviceObserverCode =
        new("121007", "DCM", "Device");

    private static string? ToModalityCode(string sopClassUid) => sopClassUid switch
    {
        "1.2.840.10008.5.1.4.1.1.2" or "1.2.840.10008.5.1.4.1.1.2.1" => "CT",
        "1.2.840.10008.5.1.4.1.1.4" or "1.2.840.10008.5.1.4.1.1.4.1" => "MR",
        "1.2.840.10008.5.1.4.1.1.128" or "1.2.840.10008.5.1.4.1.1.130" => "PT",
        "1.2.840.10008.5.1.4.1.1.66.4" => "SEG",
        _ => null
    };

    private static IEnumerable<SopInstanceReference> GetGroupReferences(
        MeasurementGroup group) =>
        group.SourceImages
            .Concat(group.Measurements
                .Where(measurement => measurement.SpatialCoordinate is not null)
                .Select(measurement => measurement.SpatialCoordinate!.SourceImage))
            .Concat(group.Segmentations.Select(
                segmentation => segmentation.Segmentation))
            .DistinctBy(reference => reference.SopInstanceUid);
}
