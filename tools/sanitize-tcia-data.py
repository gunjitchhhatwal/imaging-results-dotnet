"""Apply the repository publication profile to the bundled TCIA DICOM case."""

from __future__ import annotations

import argparse
from pathlib import Path

import pydicom
from pydicom.dataset import Dataset


PSEUDONYM = "IMAGINGRESULTS-DEMO-001"
PERSON_NAME = "IMAGINGRESULTS^DEMO"

EMPTY_KEYWORDS = {
    "AccessionNumber",
    "ClinicalTrialProtocolID",
    "ClinicalTrialSiteID",
    "ClinicalTrialSiteName",
    "ClinicalTrialSponsorName",
    "ClinicalTrialSubjectID",
    "ClinicalTrialSubjectReadingID",
    "DeviceSerialNumber",
    "InstitutionAddress",
    "InstitutionName",
    "InstitutionalDepartmentName",
    "IssuerOfPatientID",
    "MedicalRecordLocator",
    "OtherPatientIDs",
    "OtherPatientNames",
    "PatientAddress",
    "PatientAge",
    "PatientBirthDate",
    "PatientBirthName",
    "PatientMotherBirthName",
    "PatientSex",
    "PatientTelephoneNumbers",
    "PerformingPhysicianName",
    "PhysiciansOfRecord",
    "ReferringPhysicianName",
    "RequestingPhysician",
    "StationName",
}


def sanitize_dataset(dataset: Dataset) -> None:
    dataset.remove_private_tags()

    for element in list(dataset):
        if element.VR == "SQ":
            for item in element.value:
                sanitize_dataset(item)
            continue

        keyword = element.keyword
        if keyword == "PatientName":
            element.value = PERSON_NAME
        elif keyword == "PatientID":
            element.value = PSEUDONYM
        elif keyword in EMPTY_KEYWORDS or element.VR == "PN":
            element.value = ""
        elif element.VR == "DA":
            element.value = "20000101"
        elif element.VR == "DT":
            element.value = "20000101120000"
        elif element.VR == "TM":
            element.value = "120000"

    dataset.PatientIdentityRemoved = "YES"
    dataset.DeidentificationMethod = "TCIA de-id; ImagingResults.NET publication profile"
    if "BurnedInAnnotation" not in dataset:
        dataset.BurnedInAnnotation = "NO"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()

    paths = sorted(args.directory.rglob("*.dcm"))
    if not paths:
        raise SystemExit(f"No DICOM files found under {args.directory}")

    for path in paths:
        dicom_file = pydicom.dcmread(path, force=True)
        sanitize_dataset(dicom_file)
        dicom_file.save_as(path, enforce_file_format=True)

    print(f"Sanitized {len(paths)} DICOM files under {args.directory}")


if __name__ == "__main__":
    main()
