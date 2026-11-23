namespace ImagingResults.Dicom.Azure;

public sealed record AhdsDicomInstanceResult(
    string? SopClassUid,
    string? SopInstanceUid,
    ushort? FailureReason = null,
    ushort? WarningReason = null,
    string? ErrorComment = null);
