using ImagingResults.Dicom;
using ImagingResults.Fhir;

namespace ImagingResults.Consumer.Net11;

public static class ConsumerApi
{
    public static (object Dicom, object Fhir) Convert(MeasurementReport report) =>
        (new Tid1500Writer().Write(report), new FhirR5Mapper().Map(report).Bundle);
}
