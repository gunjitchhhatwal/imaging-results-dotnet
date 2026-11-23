using Hl7.Fhir.Model;
using ImagingResults.Validation;
using FhirQuantity = Hl7.Fhir.Model.Quantity;

namespace ImagingResults.Fhir;

public sealed record FhirMappingResult(Bundle Bundle, IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed class FhirR5Mapper : FhirMapperBase<FhirMappingResult>
{
    private const string DicomUidSystem = "urn:dicom:uid";
    private const string TrackingUidSystem = "urn:dicom:tracking-uid";

    public override FhirMappingResult Map(MeasurementReport report)
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
            Study = new List<ResourceReference> { Reference(imagingStudy) }
        };
        Add(bundle, diagnosticReport);

        var provenanceTargets = new List<ResourceReference> { Reference(diagnosticReport) };
        for (var groupIndex = 0; groupIndex < report.Groups.Count; groupIndex++)
        {
            var group = report.Groups[groupIndex];
            var selectionReferences = BuildSelections(report, group, groupIndex, patientReference, imagingStudy, bundle, diagnostics);

            for (var measurementIndex = 0; measurementIndex < group.Measurements.Count; measurementIndex++)
            {
                var measurement = group.Measurements[measurementIndex];
                var observation = new Observation
                {
                    Id = StableId("observation", group.Tracking.Uid, measurementIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
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
                    DerivedFrom = selectionReferences.Count > 0
                        ? selectionReferences
                        : new List<ResourceReference> { Reference(imagingStudy) }
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
                    Type = ToCodeableConcept(report.Observer.Kind == ObserverKind.Device ? DeviceObserverCode : PersonObserverCode),
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
        return new FhirMappingResult(bundle, diagnostics);
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
            var modality = ResolveModality(seriesGroup.References.Select(reference => reference.SopClassUid));
            var series = new ImagingStudy.SeriesComponent
            {
                Uid = seriesGroup.SeriesInstanceUid,
                Modality = ToModalityConcept(modality)
            };
            if (!modality.IsKnown)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    "FHIR.IMAGING_STUDY_MODALITY_UNKNOWN",
                    DiagnosticSeverity.Warning,
                    $"$.imagingStudy.series[{seriesIndex}].modality",
                    "Series modality could not be derived unambiguously from the referenced SOP classes and is represented with data-absent-reason 'unknown'.",
                    "ImagingResults.Fhir"));
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

    private static List<ResourceReference> BuildSelections(
        MeasurementReport report,
        MeasurementGroup group,
        int groupIndex,
        ResourceReference? patient,
        ImagingStudy study,
        Bundle bundle,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var selections = new List<ResourceReference>();
        foreach (var seriesGroup in GetGroupSeries(report, group))
        {
            var selection = new ImagingSelection
            {
                Id = StableId("imaging-selection", group.Tracking.Uid, seriesGroup.SeriesInstanceUid),
                Status = ImagingSelection.ImagingSelectionStatus.Available,
                Subject = patient,
                Issued = report.CreatedAt,
                StudyUid = report.StudyInstanceUid,
                SeriesUid = seriesGroup.SeriesInstanceUid,
                Code = ToCodeableConcept(SelectionCode),
                Identifier = new List<Identifier> { new(TrackingUidSystem, group.Tracking.Uid) },
                DerivedFrom = new List<ResourceReference> { Reference(study) }
            };

            foreach (var entry in seriesGroup.Evidence)
            {
                var instance = new ImagingSelection.InstanceComponent
                {
                    Uid = entry.Reference.SopInstanceUid,
                    SopClass = new Coding("urn:ietf:rfc:3986", $"urn:oid:{entry.Reference.SopClassUid}")
                };
                var subsets = entry.Reference.FrameNumbers
                    .Select(frame => frame.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Cast<string?>()
                    .ToList();
                if (entry.SegmentNumber.HasValue)
                {
                    subsets.Add($"segment:{entry.SegmentNumber.Value}");
                    diagnostics.Add(new ValidationDiagnostic(
                        "FHIR.IMAGING_SELECTION_SEGMENT_SUBSET",
                        DiagnosticSeverity.Warning,
                        $"$.groups[{groupIndex}].segmentations",
                        "Segment selection is encoded in ImagingSelection.instance.subset using a documented local token because the base R5 resource has no dedicated segment-number element.",
                        "ImagingResults.Fhir"));
                }

                instance.Subset = subsets;
                selection.Instance.Add(instance);
            }

            Add(bundle, selection);
            selections.Add(Reference(selection));
        }

        return selections;
    }

    private static CodeableConcept ToCodeableConcept(CodedConcept concept) =>
        new(ToSystem(concept.SchemeDesignator), concept.Value, concept.Meaning);

    private static void Add(Bundle bundle, Resource resource) =>
        bundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = ToUuidUrn(resource.Id),
            Resource = resource
        });

    private static ResourceReference Reference(Resource resource) => new(ToUuidUrn(resource.Id));

    private static CodeableConcept ToModalityConcept(ModalityResolution modality)
    {
        if (modality.IsKnown)
        {
            return new CodeableConcept(
                "http://dicom.nema.org/resources/ontology/DCM",
                modality.Code);
        }

        var unknown = new CodeableConcept();
        unknown.Extension.Add(new Extension(
            "http://hl7.org/fhir/StructureDefinition/data-absent-reason",
            new Code("unknown")));
        return unknown;
    }
}
