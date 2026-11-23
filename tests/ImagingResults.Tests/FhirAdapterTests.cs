using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using ImagingResults.Fhir;

namespace ImagingResults.Tests;

public sealed class FhirAdapterTests
{
    [Fact]
    public void Mapper_requires_report()
    {
        var mapper = new FhirR5Mapper();

        Assert.IsAssignableFrom<FhirMapperBase<FhirMappingResult>>(mapper);
        Assert.Throws<ArgumentNullException>(() => mapper.Map(null!));
    }

    [Fact]
    public void Equivalent_reports_have_deterministic_resource_ids_and_links()
    {
        var first = new FhirR5Mapper().Map(TestReportFactory.Create()).Bundle;
        var second = new FhirR5Mapper().Map(TestReportFactory.Create()).Bundle;

        Assert.Equal(first.Entry.Select(entry => entry.FullUrl), second.Entry.Select(entry => entry.FullUrl));
        Assert.Equal(
            first.Entry.OfType<Bundle.EntryComponent>().Select(entry => entry.Resource!.Id),
            second.Entry.OfType<Bundle.EntryComponent>().Select(entry => entry.Resource!.Id));
    }

    [Fact]
    public void Observation_contains_ucum_value_finding_site_method_and_tracking()
    {
        var observation = GetSingle<Observation>(Map());
        var quantity = Assert.IsType<Quantity>(observation.Value);

        Assert.Equal(12.4m, quantity.Value);
        Assert.Equal("http://unitsofmeasure.org", quantity.System);
        Assert.Equal("mm", quantity.Code);
        Assert.Equal("Lung structure", observation.BodySite!.Text);
        Assert.Equal("Measurement Method", observation.Method!.Text);
        Assert.Contains(observation.Identifier, item => item.System == "urn:dicom:tracking-uid");
        Assert.Contains(observation.Identifier, item => item.System == "urn:dicom:tracking-identifier");
        Assert.Contains(observation.Component, item => Assert.IsType<CodeableConcept>(item.Value).Text == "Neoplasm");
    }

    [Fact]
    public void Patient_reference_is_sanitized_consistently()
    {
        var bundle = new FhirR5Mapper().Map(TestReportFactory.Create(patientId: " patient/ä 123 ")).Bundle;
        var study = GetSingle<ImagingStudy>(bundle);
        var report = GetSingle<DiagnosticReport>(bundle);
        var observation = GetSingle<Observation>(bundle);

        Assert.Matches("^Patient/[0-9a-f]{32}$", study.Subject.Reference);
        Assert.Equal(study.Subject.Reference, report.Subject!.Reference);
        Assert.Equal(study.Subject.Reference, observation.Subject!.Reference);
    }

    [Fact]
    public void Valid_patient_id_is_preserved_and_lossy_ids_do_not_collide()
    {
        var valid = GetSingle<ImagingStudy>(new FhirR5Mapper().Map(TestReportFactory.Create(patientId: "Patient-123")).Bundle);
        var space = GetSingle<ImagingStudy>(new FhirR5Mapper().Map(TestReportFactory.Create(patientId: "A B")).Bundle);
        var slash = GetSingle<ImagingStudy>(new FhirR5Mapper().Map(TestReportFactory.Create(patientId: "A/B")).Bundle);
        var dash = GetSingle<ImagingStudy>(new FhirR5Mapper().Map(TestReportFactory.Create(patientId: "A-B")).Bundle);

        Assert.Equal("Patient/Patient-123", valid.Subject.Reference);
        Assert.NotEqual(space.Subject.Reference, slash.Subject.Reference);
        Assert.NotEqual(space.Subject.Reference, dash.Subject.Reference);
        Assert.NotEqual(slash.Subject.Reference, dash.Subject.Reference);
    }

    [Fact]
    public void Missing_patient_does_not_create_subject_references()
    {
        var bundle = new FhirR5Mapper().Map(TestReportFactory.Create(patientId: null)).Bundle;

        Assert.Null(GetSingle<ImagingStudy>(bundle).Subject);
        Assert.Null(GetSingle<DiagnosticReport>(bundle).Subject);
        Assert.Null(GetSingle<Observation>(bundle).Subject);
        Assert.All(bundle.Entry.Select(entry => entry.Resource).OfType<ImagingSelection>(), selection => Assert.Null(selection.Subject));
    }

    [Fact]
    public void Source_frames_and_segment_are_encoded_in_separate_selections()
    {
        var result = new FhirR5Mapper().Map(TestReportFactory.Create());
        var selections = result.Bundle.Entry.Select(entry => entry.Resource).OfType<ImagingSelection>().ToArray();
        var source = Assert.Single(selections, selection => selection.SeriesUid == TestReportFactory.SourceSeriesUid);
        var segmentation = Assert.Single(selections, selection => selection.SeriesUid == TestReportFactory.SegSeriesUid);

        Assert.Equal(new[] { "7" }, source.Instance.Single().Subset);
        Assert.Equal(new[] { "segment:3" }, segmentation.Instance.Single().Subset);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("FHIR.IMAGING_SELECTION_SEGMENT_SUBSET", diagnostic.Code);
        Assert.Equal("$.groups[0].segmentations", diagnostic.Path);
    }

    [Fact]
    public void Imaging_study_counts_unique_instances_and_excludes_other_studies()
    {
        var repeatedSource = TestReportFactory.CreateSource();
        var externalSource = TestReportFactory.CreateSource(
            "1.2.826.0.1.3680043.10.511.90",
            "1.2.826.0.1.3680043.10.511.91",
            "1.2.826.0.1.3680043.10.511.92");
        var group = TestReportFactory.CreateGroup(
            sourceImages: new[] { repeatedSource, repeatedSource, externalSource });

        var study = GetSingle<ImagingStudy>(new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group })).Bundle);

        Assert.Equal(2, study.NumberOfSeries);
        Assert.Equal(2, study.NumberOfInstances);
        Assert.DoesNotContain(study.Series.SelectMany(series => series.Instance), item => item.Uid == externalSource.SopInstanceUid);
    }

    [Fact]
    public void Imaging_study_series_have_required_dicom_modalities()
    {
        var study = GetSingle<ImagingStudy>(Map());
        var source = Assert.Single(study.Series, series => series.Uid == TestReportFactory.SourceSeriesUid);
        var segmentation = Assert.Single(study.Series, series => series.Uid == TestReportFactory.SegSeriesUid);

        Assert.Equal("CT", source.Modality.Coding.Single().Code);
        Assert.Equal("SEG", segmentation.Modality.Coding.Single().Code);
    }

    [Fact]
    public void Unknown_series_modality_uses_data_absent_reason_and_diagnostic()
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

        var result = new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group }));
        var series = GetSingle<ImagingStudy>(result.Bundle).Series.Single();
        var extension = Assert.Single(series.Modality.Extension);

        Assert.Empty(series.Modality.Coding);
        Assert.Equal("http://hl7.org/fhir/StructureDefinition/data-absent-reason", extension.Url);
        Assert.Equal("unknown", Assert.IsType<Code>(extension.Value).Value);
        Assert.Contains(result.Diagnostics, item => item.Code == "FHIR.IMAGING_STUDY_MODALITY_UNKNOWN");
    }

    [Fact]
    public void Mixed_known_and_unknown_sop_classes_do_not_guess_series_modality()
    {
        var known = TestReportFactory.CreateSource();
        var unknown = new SopInstanceReference(
            "1.2.840.10008.5.1.4.1.1.999",
            "1.2.826.0.1.3680043.10.511.93",
            TestReportFactory.StudyUid,
            TestReportFactory.SourceSeriesUid);
        var group = TestReportFactory.CreateGroup(sourceImages: new[] { known, unknown });

        var result = new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group }));
        var series = GetSingle<ImagingStudy>(result.Bundle).Series.Single(item => item.Uid == TestReportFactory.SourceSeriesUid);

        Assert.Empty(series.Modality.Coding);
        Assert.Contains(result.Diagnostics, item => item.Code == "FHIR.IMAGING_STUDY_MODALITY_UNKNOWN");
    }

    [Fact]
    public void Multiple_measurements_create_distinct_observations_and_report_links()
    {
        var group = TestReportFactory.CreateGroup(
            measurements: new[]
            {
                TestReportFactory.CreateMeasurement(1),
                TestReportFactory.CreateMeasurement(2)
            });
        var bundle = new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group })).Bundle;
        var observations = bundle.Entry.Select(entry => entry.Resource).OfType<Observation>().ToArray();
        var report = GetSingle<DiagnosticReport>(bundle);
        var provenance = GetSingle<Provenance>(bundle);

        Assert.Equal(2, observations.Length);
        Assert.Equal(2, observations.Select(item => item.Id).Distinct().Count());
        Assert.All(observations, item =>
        {
            var observationUrl = bundle.Entry.Single(entry => entry.Resource == item).FullUrl;
            Assert.Contains(report.Result, reference => reference.Reference == observationUrl);
        });
        Assert.Equal(3, provenance.Target.Count);
    }

    [Fact]
    public void Observation_without_imaging_evidence_derives_from_study()
    {
        var measurement = new Measurement(
            new CodedConcept("G-D7FE", "SRT", "Length"),
            new UcumQuantity(1, "mm"));
        var group = TestReportFactory.CreateGroup(
            measurements: new[] { measurement },
            sourceImages: Array.Empty<SopInstanceReference>(),
            segmentations: Array.Empty<SegmentationReference>());
        var bundle = new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group })).Bundle;
        var observation = GetSingle<Observation>(bundle);
        var study = GetSingle<ImagingStudy>(bundle);

        var studyUrl = bundle.Entry.Single(entry => entry.Resource == study).FullUrl;
        Assert.Equal(studyUrl, Assert.Single(observation.DerivedFrom).Reference);
    }

    [Theory]
    [InlineData(ObserverKind.Device, "121007")]
    [InlineData(ObserverKind.Person, "121006")]
    public void Provenance_records_observer_kind_and_identifier(ObserverKind kind, string expectedCode)
    {
        var observer = new ObserverContext(kind, "Observer", "observer-id");
        var provenance = GetSingle<Provenance>(new FhirR5Mapper().Map(TestReportFactory.Create(observer: observer)).Bundle);
        var agent = Assert.Single(provenance.Agent);

        Assert.Equal(expectedCode, agent.Type!.Coding.Single().Code);
        Assert.Equal("Observer", agent.Who.Display);
        Assert.Equal("observer-id", agent.Who.Identifier!.Value);
    }

    [Fact]
    public void Unknown_coding_scheme_maps_to_stable_dicom_scheme_uri()
    {
        var measurement = new Measurement(
            new CodedConcept("X1", "99 LOCAL", "Local metric"),
            new UcumQuantity(1, "1"));
        var group = TestReportFactory.CreateGroup(measurements: new[] { measurement });
        var observation = GetSingle<Observation>(new FhirR5Mapper().Map(TestReportFactory.Create(new[] { group })).Bundle);

        Assert.Equal("urn:dicom:coding-scheme:99%20LOCAL", observation.Code.Coding.Single().System);
    }

    [Fact]
    public void Bundle_serializes_and_parses_with_firely_json_converter()
    {
        var options = new JsonSerializerOptions().ForFhir().Pretty();
        var expected = Map();
        var json = JsonSerializer.Serialize(expected, options);
        var actual = JsonSerializer.Deserialize<Bundle>(json, options);

        Assert.Contains("\"resourceType\": \"Bundle\"", json, StringComparison.Ordinal);
        Assert.NotNull(actual);
        Assert.Equal(expected.Entry.Count, actual.Entry.Count);
        Assert.Equal(expected.Entry.Select(item => item.FullUrl), actual.Entry.Select(item => item.FullUrl));
        Assert.All(actual.Entry, entry =>
        {
            var fullUrl = Assert.IsType<string>(entry.FullUrl);
            Assert.StartsWith("urn:uuid:", fullUrl, StringComparison.Ordinal);
            var uuid = Guid.Parse(fullUrl["urn:uuid:".Length..]);
            Assert.Equal(8, uuid.Version);
            Assert.Equal(0x80, uuid.ToByteArray(bigEndian: true)[8] & 0xc0);
        });
    }

    private static Bundle Map() => new FhirR5Mapper().Map(TestReportFactory.Create()).Bundle;

    private static T GetSingle<T>(Bundle bundle)
        where T : Resource =>
        Assert.Single(bundle.Entry.Select(entry => entry.Resource).OfType<T>());
}
