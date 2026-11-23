using Hl7.Fhir.Model;
using ImagingResults.Validation;
using FhirQuantity = Hl7.Fhir.Model.Quantity;

namespace ImagingResults.Fhir.R4;

public sealed record FhirR4MappingResult(
    Bundle Bundle,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class FhirR4Mapper : FhirMapperBase<FhirR4MappingResult>
{
    private const string DicomUidSystem = "urn:dicom:uid";
    private const string TrackingUidSystem = "urn:dicom:tracking-uid";
    private const string SopInstanceSystem = "urn:imaging-results:dicom:sop-instance-uid";
    private const string FrameReferenceSystem = "urn:imaging-results:dicom:frame-reference";
    private const string SegmentReferenceSystem = "urn:imaging-results:dicom:segment-reference";

    public override FhirR4MappingResult Map(MeasurementReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var diagnostics = new List<ValidationDiagnostic>();
        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Collection,
            Timestamp = report.CreatedAt
        };
        var patientReference = string.IsNullOrWhiteSpace(report.PatientId)
            ? null
            : new ResourceReference($"Patient/{PatientResourceId(report.PatientId)}");

        var imagingStudy = BuildImagingStudy(report, patientReference, diagnostics);
        Add(bundle, imagingStudy);

        var diagnosticReport = new DiagnosticReport
        {
            Id = StableId("diagnostic-report", report.SopInstanceUid),
            Status = DiagnosticReport.DiagnosticReportStatus.Final,
            Code = ToCodeableConcept(Tid1500ReportCode),
            Subject = patientReference,
            Issued = report.CreatedAt,
            Identifier = new List<Identifier> { new(DicomUidSystem, report.SopInstanceUid) },
            ImagingStudy = new List<ResourceReference> { Reference(imagingStudy) }
        };
        Add(bundle, diagnosticReport);

        var provenanceTargets = new List<ResourceReference> { Reference(diagnosticReport) };
        for (var groupIndex = 0; groupIndex < report.Groups.Count; groupIndex++)
        {
            var group = report.Groups[groupIndex];
            var evidenceReferences = BuildEvidenceReferences(report, group, groupIndex, diagnostics);

            for (var measurementIndex = 0; measurementIndex < group.Measurements.Count; measurementIndex++)
            {
                var measurement = group.Measurements[measurementIndex];
                var observation = new Observation
                {
                    Id = StableId(
                        "observation",
                        group.Tracking.Uid,
                        measurementIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Status = ObservationStatus.Final,
                    Code = ToCodeableConcept(measurement.Type),
                    Subject = patientReference,
                    Issued = report.CreatedAt,
                    Value = new FhirQuantity
                    {
                        Value = measurement.Value.Value,
                        System = "http://unitsofmeasure.org",
                        Code = measurement.Value.Unit,
                        Unit = measurement.Value.Display
                    },
                    Identifier = new List<Identifier>
                    {
                        new(TrackingUidSystem, group.Tracking.Uid),
                        new("urn:dicom:tracking-identifier", group.Tracking.Identifier)
                    },
                    DerivedFrom = new List<ResourceReference> { Reference(imagingStudy) }
                        .Concat(evidenceReferences)
                        .ToList()
                };

                if (group.FindingSite is not null)
                {
                    observation.BodySite = ToCodeableConcept(group.FindingSite);
                }

                observation.Component.Add(new Observation.ComponentComponent
                {
                    Code = ToCodeableConcept(FindingCode),
                    Value = ToCodeableConcept(group.Finding)
                });
                if (measurement.Method is not null)
                {
                    observation.Method = ToCodeableConcept(measurement.Method);
                }

                Add(bundle, observation);
                diagnosticReport.Result.Add(Reference(observation));
                provenanceTargets.Add(Reference(observation));
            }
        }

        var provenance = new Provenance
        {
            Id = StableId("provenance", report.SopInstanceUid),
            Target = provenanceTargets,
            Recorded = report.CreatedAt,
            Agent = new List<Provenance.AgentComponent>
            {
                new()
                {
                    Type = ToCodeableConcept(
                        report.Observer.Kind == ObserverKind.Device
                            ? DeviceObserverCode
                            : PersonObserverCode),
                    Who = new ResourceReference
                    {
                        Display = report.Observer.Name,
                        Identifier = string.IsNullOrWhiteSpace(report.Observer.Identifier)
                            ? null
                            : new Identifier("urn:imaging-results:observer", report.Observer.Identifier)
                    }
                }
            },
            Entity = new List<Provenance.EntityComponent>
            {
                new()
                {
                    Role = Provenance.ProvenanceEntityRole.Source,
                    What = new ResourceReference
                    {
                        Identifier = new Identifier(DicomUidSystem, report.SopInstanceUid),
                        Display = "Source DICOM Structured Report"
                    }
                }
            }
        };
        Add(bundle, provenance);
        return new FhirR4MappingResult(bundle, diagnostics);
    }

    private static ImagingStudy BuildImagingStudy(
        MeasurementReport report,
        ResourceReference? patient,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var study = new ImagingStudy
        {
            Id = StableId("imaging-study", report.StudyInstanceUid),
            Status = ImagingStudy.ImagingStudyStatus.Available,
            Identifier = new List<Identifier> { new(DicomUidSystem, report.StudyInstanceUid) },
            Started = report.CreatedAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (patient is not null)
        {
            study.Subject = patient;
        }

        var seriesIndex = 0;
        foreach (var seriesGroup in GetStudySeries(report))
        {
            var modality = ResolveModality(
                seriesGroup.References.Select(reference => reference.SopClassUid));
            var modalityCoding = modality.IsKnown
                ? new Coding(
                    "http://dicom.nema.org/resources/ontology/DCM",
                    modality.Code)
                : UnknownModality();
            var series = new ImagingStudy.SeriesComponent
            {
                Uid = seriesGroup.SeriesInstanceUid,
                Modality = modalityCoding
            };
            if (!modality.IsKnown)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    "FHIR.R4.IMAGING_STUDY_MODALITY_UNKNOWN",
                    DiagnosticSeverity.Warning,
                    $"$.imagingStudy.series[{seriesIndex}].modality",
                    "Series modality could not be derived unambiguously from the referenced SOP classes and is represented with data-absent-reason 'unknown'.",
                    "ImagingResults.Fhir.R4"));
            }

            foreach (var reference in seriesGroup.References.DistinctBy(item => item.SopInstanceUid))
            {
                series.Instance.Add(new ImagingStudy.InstanceComponent
                {
                    Uid = reference.SopInstanceUid,
                    SopClass = new Coding("urn:ietf:rfc:3986", $"urn:oid:{reference.SopClassUid}")
                });
            }

            study.Series.Add(series);
            seriesIndex++;
        }

        study.NumberOfSeries = study.Series.Count;
        study.NumberOfInstances = study.Series.Sum(series => series.Instance.Count);
        return study;
    }

    private static IReadOnlyList<ResourceReference> BuildEvidenceReferences(
        MeasurementReport report,
        MeasurementGroup group,
        int groupIndex,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var references = new List<ResourceReference>();
        foreach (var series in GetGroupSeries(report, group))
        {
            foreach (var evidence in series.Evidence)
            {
                references.Add(IdentifierReference(
                    SopInstanceSystem,
                    evidence.Reference.SopInstanceUid,
                    $"DICOM SOP Instance {evidence.Reference.SopInstanceUid}"));

                references.AddRange(evidence.Reference.FrameNumbers.Select(frame =>
                    IdentifierReference(
                        FrameReferenceSystem,
                        $"{evidence.Reference.SopInstanceUid}#frame={frame}",
                        $"DICOM frame {frame}")));

                if (evidence.SegmentNumber.HasValue)
                {
                    references.Add(IdentifierReference(
                        SegmentReferenceSystem,
                        $"{evidence.Reference.SopInstanceUid}#segment={evidence.SegmentNumber.Value}",
                        $"DICOM segment {evidence.SegmentNumber.Value}"));
                }
            }
        }

        if (references.Count > 0)
        {
            diagnostics.Add(new ValidationDiagnostic(
                "FHIR.R4.IMAGING_EVIDENCE_IDENTIFIER_REFERENCE",
                DiagnosticSeverity.Warning,
                $"$.groups[{groupIndex}]",
                "FHIR R4 has no ImagingSelection resource; DICOM SOP instance, frame, and segment evidence is represented by documented identifier-only Observation.derivedFrom references.",
                "ImagingResults.Fhir.R4"));
        }

        return references;
    }

    private static ResourceReference IdentifierReference(
        string system,
        string value,
        string display) =>
        new()
        {
            Identifier = new Identifier(system, value),
            Display = display
        };

    private static CodeableConcept ToCodeableConcept(CodedConcept concept) =>
        new(ToSystem(concept.SchemeDesignator), concept.Value, concept.Meaning);

    private static Coding UnknownModality()
    {
        var coding = new Coding();
        coding.Extension.Add(new Extension(
            "http://hl7.org/fhir/StructureDefinition/data-absent-reason",
            new Code("unknown")));
        return coding;
    }

    private static void Add(Bundle bundle, Resource resource) =>
        bundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = ToUuidUrn(resource.Id),
            Resource = resource
        });

    private static ResourceReference Reference(Resource resource) =>
        new(ToUuidUrn(resource.Id));
}
