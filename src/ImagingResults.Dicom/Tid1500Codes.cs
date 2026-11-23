namespace ImagingResults.Dicom;

internal static class Tid1500Codes
{
    public static readonly CodedConcept ImagingMeasurementReport = new("126000", "DCM", "Imaging Measurement Report");
    public static readonly CodedConcept ImagingMeasurements = new("126010", "DCM", "Imaging Measurements");
    public static readonly CodedConcept MeasurementGroup = new("125007", "DCM", "Measurement Group");
    public static readonly CodedConcept TrackingIdentifier = new("112039", "DCM", "Tracking Identifier");
    public static readonly CodedConcept TrackingUniqueIdentifier = new("112040", "DCM", "Tracking Unique Identifier");
    public static readonly CodedConcept Finding = new("121071", "DCM", "Finding");
    public static readonly CodedConcept FindingSite = new("363698007", "SCT", "Finding site");
    public static readonly CodedConcept LegacyFindingSite = new("G-C0E3", "SRT", "Finding Site");
    public static readonly CodedConcept MeasurementMethod = new("370129005", "SCT", "Measurement Method");
    public static readonly CodedConcept Derivation = new("121401", "DCM", "Derivation");
    public static readonly CodedConcept SourceImage = new("121112", "DCM", "Source of Measurement");
    public static readonly CodedConcept ReferencedSegment = new("121191", "DCM", "Referenced Segment");
    public static readonly CodedConcept ImageRegion = new("111030", "DCM", "Image Region");
    public static readonly CodedConcept AlgorithmName = new("111001", "DCM", "Algorithm Name");
    public static readonly CodedConcept AlgorithmVersion = new("111003", "DCM", "Algorithm Version");
    public static readonly CodedConcept ProcedureReported = new("121058", "DCM", "Procedure reported");
    public static readonly CodedConcept ObserverType = new("121005", "DCM", "Observer Type");
    public static readonly CodedConcept Person = new("121006", "DCM", "Person");
    public static readonly CodedConcept Device = new("121007", "DCM", "Device");
    public static readonly CodedConcept ObserverName = new("121008", "DCM", "Person Observer Name");
    public static readonly CodedConcept DeviceObserverName = new("121012", "DCM", "Device Observer Name");
}
