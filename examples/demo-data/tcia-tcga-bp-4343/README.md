# TCIA TCGA-BP-4343 demonstration data

This directory contains 101 de-identified CT instances and one Enhanced SR
TID 1500 measurement report used by the local OHIF and FHIR demonstrations.

Before redistribution, the source TCIA objects were re-pseudonymized for this
repository. Direct identifiers, acquisition dates and times, device identifiers,
and private DICOM elements were removed or replaced. Study, series, instance,
and frame-of-reference UIDs were retained so the Enhanced SR continues to
reference the CT images correctly. Automated tests enforce the publication
profile.

The underlying research images remain derived from a real de-identified
clinical case. They are distributed under CC BY 3.0. See
[`examples/ohif/TCIA-ATTRIBUTION.md`](../../ohif/TCIA-ATTRIBUTION.md) for the
required citations, source collection details, modifications, and intended
research-demonstration scope.
