using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;

internal static class SyntheticCtFactory
{
    private const ushort ImageSize = 512;

    public static DicomFile Create(SyntheticAiResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var createdAt = input.Report.CreatedAt.UtcDateTime;
        var dataset = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SpecificCharacterSet, "ISO_IR 100" },
            { DicomTag.ImageType, new[] { "ORIGINAL", "PRIMARY", "AXIAL" } },
            { DicomTag.SOPClassUID, input.Source.SopClassUid },
            { DicomTag.SOPInstanceUID, input.Source.SopInstanceUid },
            { DicomTag.StudyDate, createdAt.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.SeriesDate, createdAt.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.AcquisitionDate, createdAt.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.ContentDate, createdAt.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.StudyTime, createdAt.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.SeriesTime, createdAt.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.AcquisitionTime, createdAt.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.ContentTime, createdAt.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture) },
            { DicomTag.AccessionNumber, "SYNVIEW001" },
            { DicomTag.Modality, "CT" },
            { DicomTag.Manufacturer, "ImagingResults.NET" },
            { DicomTag.InstitutionName, "Synthetic Demonstration" },
            { DicomTag.ReferringPhysicianName, "Demo^ImagingResults" },
            { DicomTag.StudyDescription, "Synthetic CT with AI measurement" },
            { DicomTag.SeriesDescription, "Synthetic axial CT" },
            { DicomTag.PatientName, "Synthetic^Viewer" },
            { DicomTag.PatientID, input.PatientId },
            { DicomTag.PatientBirthDate, "19700101" },
            { DicomTag.PatientSex, "O" },
            { DicomTag.SliceThickness, "1.0" },
            { DicomTag.KVP, "120" },
            { DicomTag.StudyInstanceUID, input.Source.StudyInstanceUid },
            { DicomTag.SeriesInstanceUID, input.Source.SeriesInstanceUid },
            { DicomTag.StudyID, "VIEWER-001" },
            { DicomTag.SeriesNumber, "1" },
            { DicomTag.InstanceNumber, "1" },
            { DicomTag.FrameOfReferenceUID, "1.2.826.0.1.3680043.10.511.540" },
            { DicomTag.ImagePositionPatient, new[] { "0", "0", "0" } },
            { DicomTag.ImageOrientationPatient, new[] { "1", "0", "0", "0", "1", "0" } },
            { DicomTag.PositionReferenceIndicator, string.Empty },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.Rows, ImageSize },
            { DicomTag.Columns, ImageSize },
            { DicomTag.PixelSpacing, new[] { "0.5", "0.5" } },
            { DicomTag.BitsAllocated, (ushort)16 },
            { DicomTag.BitsStored, (ushort)16 },
            { DicomTag.HighBit, (ushort)15 },
            { DicomTag.PixelRepresentation, (ushort)1 },
            { DicomTag.WindowCenter, "-400" },
            { DicomTag.WindowWidth, "1500" },
            { DicomTag.RescaleIntercept, "0" },
            { DicomTag.RescaleSlope, "1" },
            { DicomTag.RescaleType, "HU" }
        };

        var pixelData = DicomPixelData.Create(dataset, true);
        pixelData.AddFrame(new MemoryByteBuffer(CreatePixels()));
        return new DicomFile(dataset);
    }

    private static byte[] CreatePixels()
    {
        var pixels = new short[ImageSize * ImageSize];
        for (var y = 0; y < ImageSize; y++)
        {
            for (var x = 0; x < ImageSize; x++)
            {
                var dx = x - ImageSize / 2.0;
                var dy = y - ImageSize / 2.0;
                var body = (dx * dx / (220.0 * 220.0)) + (dy * dy / (245.0 * 245.0)) <= 1.0;
                var leftLung = Ellipse(x, y, 185, 255, 74, 142);
                var rightLung = Ellipse(x, y, 327, 255, 74, 142);
                var nodule = Ellipse(x, y, 198, 220, 15, 13);

                pixels[(y * ImageSize) + x] = nodule
                    ? (short)120
                    : leftLung || rightLung
                        ? (short)-760
                        : body
                            ? (short)35
                            : (short)-1000;
            }
        }

        var bytes = new byte[pixels.Length * sizeof(short)];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static bool Ellipse(
        int x,
        int y,
        double centerX,
        double centerY,
        double radiusX,
        double radiusY)
    {
        var dx = (x - centerX) / radiusX;
        var dy = (y - centerY) / radiusY;
        return (dx * dx) + (dy * dy) <= 1.0;
    }
}
