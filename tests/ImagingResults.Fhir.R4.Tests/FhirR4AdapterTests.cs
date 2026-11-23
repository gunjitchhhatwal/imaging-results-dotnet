using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using ImagingResults.Fhir;
using ImagingResults.Fhir.R4;
using ImagingResults.Tests;

namespace ImagingResults.Fhir.R4.Tests;

public sealed class FhirR4AdapterTests
{
    [Fact]
    public void Mapper_inherits_common_mapper_contract_and_requires_report()
    {
        var mapper = new FhirR4Mapper();

        Assert.IsAssignableFrom<FhirMapperBase<FhirR4MappingResult>>(mapper);
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public void Mapper_creates_linked_report_observation_study_and_provenance()
    {
        var bundle = Map().Bundle;
        var study = GetSingle<ImagingStudy>(bundle);
        var report = GetSingle<DiagnosticReport>(bundle);
        var observation = GetSingle<Observation>(bundle);
        var provenance = GetSingle<Provenance>(bundle);
        var studyUrl = GetUrl(bundle, study);
        var observationUrl = GetUrl(bundle, observation);

        Assert.Contains(report.ImagingStudy, reference => reference.Reference == studyUrl);
        Assert.Contains(report.Result, reference => reference.Reference == observationUrl);
        Assert.Contains(observation.DerivedFrom, reference => reference.Reference == studyUrl);
        Assert.Contains(provenance.Target, reference => reference.Reference == observationUrl);
    }

    [Fact]
    public void Observation_contains_ucum_measurement_and_identifier_evidence()
    {
        var result = Map();
        var observation = GetSingle<Observation>(result.Bundle);
        var quantity = Assert.IsType<Quantity>(observation.Value);
        var identifiers = observation.DerivedFrom
            .Where(reference => reference.Identifier is not null)
            .Select(reference => (reference.Identifier!.System, reference.Identifier.Value))
            .ToArray();

        Assert.Equal(12.4m, quantity.Value);
        Assert.Equal("http://unitsofmeasure.org", quantity.System);
        Assert.Equal("mm", quantity.Code);
        Assert.Contains(identifiers, item =>
            item.System == "urn:imaging-results:dicom:frame-reference"
            && item.Value == $"{TestReportFactory.SourceInstanceUid}#frame=7");
        Assert.Contains(identifiers, item =>
            item.System == "urn:imaging-results:dicom:segment-reference"
            && item.Value == $"{TestReportFactory.SegInstanceUid}#segment=3");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "FHIR.R4.IMAGING_EVIDENCE_IDENTIFIER_REFERENCE");
    }

    [Fact]
    public void Imaging_study_preserves_series_instances_and_modalities()
    {
        var study = GetSingle<ImagingStudy>(Map().Bundle);
        var source = Assert.Single(
            study.Series,
            series => series.Uid == TestReportFactory.SourceSeriesUid);
        var segmentation = Assert.Single(
            study.Series,
            series => series.Uid == TestReportFactory.SegSeriesUid);

        Assert.Equal("CT", source.Modality.Code);
        Assert.Equal(TestReportFactory.SourceInstanceUid, source.Instance.Single().Uid);
        Assert.Equal("SEG", segmentation.Modality.Code);
        Assert.Equal(TestReportFactory.SegInstanceUid, segmentation.Instance.Single().Uid);
    }

    [Fact]
    public void Spatial_coordinate_source_is_mapped_without_duplicate_group_reference()
    {
        var source = TestReportFactory.CreateSource();
        var group = TestReportFactory.CreateGroup(
            measurements: new[]
            {
                TestReportFactory.CreateMeasurement(
                    coordinate: new SpatialCoordinate2D(
                        GraphicType2D.Polyline,
                        new[] { new Point2D(10, 20), new Point2D(30, 40) },
                        source))
            },
            sourceImages: Array.Empty<SopInstanceReference>(),
            segmentations: Array.Empty<SegmentationReference>());

        var result = new FhirR4Mapper().Map(TestReportFactory.Create(new[] { group }));
        var study = GetSingle<ImagingStudy>(result.Bundle);
        var observation = GetSingle<Observation>(result.Bundle);

        Assert.Equal(1, study.NumberOfSeries);
        Assert.Equal(1, study.NumberOfInstances);
        Assert.Contains(
            observation.DerivedFrom,
            reference =>
                reference.Identifier?.System
                    == "urn:imaging-results:dicom:sop-instance-uid"
                && reference.Identifier.Value == TestReportFactory.SourceInstanceUid);
    }

    [Fact]
    public void Legacy_srt_codes_are_not_labeled_as_snomed_ct()
    {
        var observation = GetSingle<Observation>(Map().Bundle);

        Assert.Equal(
            "urn:dicom:coding-scheme:SRT",
            observation.Code.Coding.Single().System);
    }

    [Fact]
    public void Unknown_modality_uses_data_absent_reason()
    {
        var unknown = new SopInstanceReference(
            "1.2.840.10008.5.1.4.1.1.999",
            "1.2.826.0.1.3680043.10.511.93",
            TestReportFactory.StudyUid,
            "1.2.826.0.1.3680043.10.511.94");
        var group = TestReportFactory.CreateGroup(
            measurements: new[]
            {
                TestReportFactory.CreateMeasurement(
                    coordinate: new SpatialCoordinate2D(
                        GraphicType2D.Point,
                        new[] { new Point2D(1, 2) },
                        unknown))
            },
            sourceImages: new[] { unknown },
            segmentations: Array.Empty<SegmentationReference>());

        var result = new FhirR4Mapper().Map(TestReportFactory.Create(new[] { group }));
        var modality = GetSingle<ImagingStudy>(result.Bundle).Series.Single().Modality;
        var extension = Assert.Single(modality.Extension);

        Assert.Null(modality.Code);
        Assert.Equal(
            "http://hl7.org/fhir/StructureDefinition/data-absent-reason",
            extension.Url);
        Assert.Equal("unknown", Assert.IsType<Code>(extension.Value).Value);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "FHIR.R4.IMAGING_STUDY_MODALITY_UNKNOWN");
    }

    [Fact]
    public void Mapping_is_deterministic_and_round_trips_through_firely_json()
    {
        var first = Map().Bundle;
        var second = Map().Bundle;
        var options = new JsonSerializerOptions().ForFhir().Pretty();
        var json = JsonSerializer.Serialize(first, options);
        var parsed = JsonSerializer.Deserialize<Bundle>(json, options);

        Assert.Equal(
            first.Entry.Select(entry => entry.FullUrl),
            second.Entry.Select(entry => entry.FullUrl));
        Assert.NotNull(parsed);
        Assert.Equal(first.Entry.Count, parsed.Entry.Count);
        Assert.All(parsed.Entry, entry =>
        {
            var fullUrl = Assert.IsType<string>(entry.FullUrl);
            Assert.Matches(
                "^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-8[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
                fullUrl);
        });
    }

    [Fact]
    public void Patient_references_are_consistent_and_collision_resistant()
    {
        var first = new FhirR4Mapper().Map(
            TestReportFactory.Create(patientId: "A B")).Bundle;
        var second = new FhirR4Mapper().Map(
            TestReportFactory.Create(patientId: "A/B")).Bundle;
        var firstStudy = GetSingle<ImagingStudy>(first);
        var firstReport = GetSingle<DiagnosticReport>(first);
        var firstObservation = GetSingle<Observation>(first);

        Assert.Equal(firstStudy.Subject.Reference, firstReport.Subject!.Reference);
        Assert.Equal(firstStudy.Subject.Reference, firstObservation.Subject!.Reference);
        Assert.NotEqual(
            firstStudy.Subject.Reference,
            GetSingle<ImagingStudy>(second).Subject.Reference);
    }

    private static FhirR4MappingResult Map() =>
        new FhirR4Mapper().Map(TestReportFactory.Create());

    private static string? GetUrl(Bundle bundle, Resource resource) =>
        bundle.Entry.Single(entry => entry.Resource == resource).FullUrl;

    private static T GetSingle<T>(Bundle bundle)
        where T : Resource =>
        Assert.Single(bundle.Entry.Select(entry => entry.Resource).OfType<T>());
}
