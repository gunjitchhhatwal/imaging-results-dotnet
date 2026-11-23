using System.Globalization;
using FellowOakDicom;

namespace ImagingResults.Dicom;

public sealed class Tid1500Writer
{
    public DicomFile Write(MeasurementReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SOPClassUID, DicomUID.ComprehensiveSRStorage },
            { DicomTag.SOPInstanceUID, report.SopInstanceUid },
            { DicomTag.StudyInstanceUID, report.StudyInstanceUid },
            { DicomTag.SeriesInstanceUID, report.SeriesInstanceUid },
            { DicomTag.Modality, "SR" },
            { DicomTag.SeriesNumber, "1" },
            { DicomTag.InstanceNumber, "1" },
            { DicomTag.ContentDate, report.CreatedAt.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) },
            { DicomTag.ContentTime, report.CreatedAt.UtcDateTime.ToString("HHmmss.ffffff", CultureInfo.InvariantCulture) },
            { DicomTag.ValueType, "CONTAINER" },
            { DicomTag.ContinuityOfContent, "SEPARATE" },
            { DicomTag.CompletionFlag, "COMPLETE" },
            { DicomTag.VerificationFlag, "UNVERIFIED" },
            new DicomSequence(
                DicomTag.ContentTemplateSequence,
                new DicomDataset
                {
                    { DicomTag.MappingResource, "DCMR" },
                    { DicomTag.TemplateIdentifier, "1500" }
                })
        };

        if (!string.IsNullOrWhiteSpace(report.PatientId))
        {
            dataset.Add(DicomTag.PatientID, report.PatientId);
        }

        dataset.Add(CodeSequence(DicomTag.ConceptNameCodeSequence, Tid1500Codes.ImagingMeasurementReport));

        var rootItems = new List<DicomDataset>
        {
            CodeItem("HAS OBS CONTEXT", Tid1500Codes.ObserverType, report.Observer.Kind == ObserverKind.Person ? Tid1500Codes.Person : Tid1500Codes.Device),
            TextItem(
                "HAS OBS CONTEXT",
                report.Observer.Kind == ObserverKind.Person ? Tid1500Codes.ObserverName : Tid1500Codes.DeviceObserverName,
                report.Observer.Name)
        };

        if (report.ProcedureReported is not null)
        {
            rootItems.Add(CodeItem("CONTAINS", Tid1500Codes.ProcedureReported, report.ProcedureReported));
        }

        rootItems.Add(ContainerItem(
            "CONTAINS",
            Tid1500Codes.ImagingMeasurements,
            report.Groups.Select(WriteGroup).ToArray()));
        dataset.Add(new DicomSequence(DicomTag.ContentSequence, rootItems.ToArray()));
        AddEvidenceSequence(dataset, report);
        return new DicomFile(dataset);
    }

    private static void AddEvidenceSequence(DicomDataset dataset, MeasurementReport report)
    {
        var references = report.Groups
            .SelectMany(group => group.SourceImages.Concat(group.Segmentations.Select(segmentation => segmentation.Segmentation)))
            .Where(reference => reference.StudyInstanceUid is not null && reference.SeriesInstanceUid is not null)
            .GroupBy(reference => reference.StudyInstanceUid!, StringComparer.Ordinal)
            .Select(studyGroup =>
            {
                var study = new DicomDataset { { DicomTag.StudyInstanceUID, studyGroup.Key } };
                var seriesItems = studyGroup
                    .GroupBy(reference => reference.SeriesInstanceUid!, StringComparer.Ordinal)
                    .Select(seriesGroup =>
                    {
                        var series = new DicomDataset { { DicomTag.SeriesInstanceUID, seriesGroup.Key } };
                        var instances = seriesGroup
                            .GroupBy(reference => reference.SopInstanceUid, StringComparer.Ordinal)
                            .Select(group => group.First())
                            .Select(reference => new DicomDataset
                            {
                                { DicomTag.ReferencedSOPClassUID, reference.SopClassUid },
                                { DicomTag.ReferencedSOPInstanceUID, reference.SopInstanceUid }
                            })
                            .ToArray();
                        series.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, instances));
                        return series;
                    })
                    .ToArray();
                study.Add(new DicomSequence(DicomTag.ReferencedSeriesSequence, seriesItems));
                return study;
            })
            .ToArray();

        if (references.Length > 0)
        {
            dataset.Add(new DicomSequence(DicomTag.CurrentRequestedProcedureEvidenceSequence, references));
        }
    }

    private static DicomDataset WriteGroup(MeasurementGroup group)
    {
        var items = new List<DicomDataset>
        {
            TextItem("HAS OBS CONTEXT", Tid1500Codes.TrackingIdentifier, group.Tracking.Identifier),
            UidRefItem("HAS OBS CONTEXT", Tid1500Codes.TrackingUniqueIdentifier, group.Tracking.Uid),
            CodeItem("CONTAINS", Tid1500Codes.Finding, group.Finding)
        };

        if (group.FindingSite is not null)
        {
            items.Add(CodeItem("HAS CONCEPT MOD", Tid1500Codes.FindingSite, group.FindingSite));
        }

        if (group.Algorithm is not null)
        {
            items.Add(TextItem("HAS OBS CONTEXT", Tid1500Codes.AlgorithmName, group.Algorithm.Name));
            items.Add(TextItem("HAS OBS CONTEXT", Tid1500Codes.AlgorithmVersion, group.Algorithm.Version));
        }

        items.AddRange(group.SourceImages.Select(reference => ImageItem("CONTAINS", Tid1500Codes.SourceImage, reference, null)));
        items.AddRange(group.Segmentations.Select(reference =>
            ImageItem("CONTAINS", Tid1500Codes.ReferencedSegment, reference.Segmentation, reference.SegmentNumber)));
        items.AddRange(group.Measurements.Select(WriteMeasurement));

        return ContainerItem("CONTAINS", Tid1500Codes.MeasurementGroup, items);
    }

    private static DicomDataset WriteMeasurement(Measurement measurement)
    {
        var measuredValue = new DicomDataset
        {
            { DicomTag.NumericValue, measurement.Value.Value.ToString(CultureInfo.InvariantCulture) },
            CodeSequence(DicomTag.MeasurementUnitsCodeSequence, new CodedConcept(measurement.Value.Unit, "UCUM", measurement.Value.Display))
        };

        var item = BaseItem("CONTAINS", "NUM", measurement.Type);
        item.Add(new DicomSequence(DicomTag.MeasuredValueSequence, measuredValue));
        var children = new List<DicomDataset>();

        if (measurement.Method is not null)
        {
            children.Add(CodeItem("HAS CONCEPT MOD", Tid1500Codes.MeasurementMethod, measurement.Method));
        }

        if (measurement.Derivation is not null)
        {
            children.Add(CodeItem("HAS CONCEPT MOD", Tid1500Codes.Derivation, measurement.Derivation));
        }

        if (measurement.SpatialCoordinate is not null)
        {
            children.Add(SpatialCoordinateItem(measurement.SpatialCoordinate));
        }

        if (children.Count > 0)
        {
            item.Add(new DicomSequence(DicomTag.ContentSequence, children.ToArray()));
        }

        return item;
    }

    private static DicomDataset SpatialCoordinateItem(SpatialCoordinate2D coordinate)
    {
        var item = BaseItem("INFERRED FROM", "SCOORD", Tid1500Codes.ImageRegion);
        item.Add(DicomTag.GraphicType, coordinate.GraphicType.ToString().ToUpperInvariant());
        item.Add(
            DicomTag.GraphicData,
            coordinate.Points.SelectMany(point => new[] { (double)point.X, (double)point.Y }).ToArray());
        item.Add(new DicomSequence(
            DicomTag.ContentSequence,
            ImageItem("SELECTED FROM", Tid1500Codes.SourceImage, coordinate.SourceImage, null)));
        return item;
    }

    private static DicomDataset ContainerItem(string relationship, CodedConcept name, IReadOnlyList<DicomDataset> children)
    {
        var item = BaseItem(relationship, "CONTAINER", name);
        item.Add(DicomTag.ContinuityOfContent, "SEPARATE");
        item.Add(new DicomSequence(DicomTag.ContentSequence, children.ToArray()));
        return item;
    }

    private static DicomDataset TextItem(string relationship, CodedConcept name, string value)
    {
        var item = BaseItem(relationship, "TEXT", name);
        item.Add(DicomTag.TextValue, value);
        return item;
    }

    private static DicomDataset UidRefItem(string relationship, CodedConcept name, string value)
    {
        var item = BaseItem(relationship, "UIDREF", name);
        item.Add(DicomTag.UID, value);
        return item;
    }

    private static DicomDataset CodeItem(string relationship, CodedConcept name, CodedConcept value)
    {
        var item = BaseItem(relationship, "CODE", name);
        item.Add(CodeSequence(DicomTag.ConceptCodeSequence, value));
        return item;
    }

    private static DicomDataset ImageItem(string relationship, CodedConcept name, SopInstanceReference reference, int? segmentNumber)
    {
        var referencedSop = new DicomDataset
        {
            { DicomTag.ReferencedSOPClassUID, reference.SopClassUid },
            { DicomTag.ReferencedSOPInstanceUID, reference.SopInstanceUid }
        };
        if (reference.FrameNumbers.Count > 0)
        {
            referencedSop.Add(DicomTag.ReferencedFrameNumber, reference.FrameNumbers.ToArray());
        }

        if (segmentNumber.HasValue)
        {
            referencedSop.Add(DicomTag.ReferencedSegmentNumber, checked((ushort)segmentNumber.Value));
        }

        var item = BaseItem(relationship, "IMAGE", name);
        item.Add(new DicomSequence(DicomTag.ReferencedSOPSequence, referencedSop));
        return item;
    }

    private static DicomDataset BaseItem(string relationship, string valueType, CodedConcept name) =>
        new()
        {
            { DicomTag.RelationshipType, relationship },
            { DicomTag.ValueType, valueType },
            CodeSequence(DicomTag.ConceptNameCodeSequence, name)
        };

    private static DicomSequence CodeSequence(DicomTag tag, CodedConcept concept)
    {
        var code = new DicomDataset
        {
            { DicomTag.CodeValue, concept.Value },
            { DicomTag.CodingSchemeDesignator, concept.SchemeDesignator },
            { DicomTag.CodeMeaning, concept.Meaning }
        };
        if (!string.IsNullOrWhiteSpace(concept.SchemeVersion))
        {
            code.Add(DicomTag.CodingSchemeVersion, concept.SchemeVersion);
        }

        return new DicomSequence(tag, code);
    }
}
