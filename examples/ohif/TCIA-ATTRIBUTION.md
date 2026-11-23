# TCIA real-world viewer case

The optional real-world OHIF demo uses one de-identified case from **Crowds
Cure Cancer: Crowdsourced data collected at the RSNA 2017 annual meeting** and
its corresponding source CT from **TCGA-KIRC**.

- Subject: `TCGA-BP-4343`
- Study Instance UID:
  `1.3.6.1.4.1.14519.5.2.1.9203.4004.100545948082587796019777812429`
- CT Series Instance UID:
  `1.3.6.1.4.1.14519.5.2.1.9203.4004.178060436087844836988805217813`
- TID 1500 report: `dicomsrfiles/1.dcm` from the TCIA SR archive
- Measurement: radiologist-created renal lesion long-axis length, 66.44 mm
- License: [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/)

Required data citation:

> Kalpathy-Cramer, J., Beers, A., Mamonov, A., Ziegler, E., Lewis, R.,
> Almeida, A. B., Harris, G., Pieper, S., Sharma, A., Tarbox, L., Tobler, J.,
> Prior, F., Flanders, A., Dulkowski, J., Fevrier-Sullivan, B., Jaffe, C.,
> Freymann, J., & Kirby, J. (2019). Crowds Cure Cancer: Crowdsourced data
> collected at the RSNA 2017 annual meeting [Data set]. The Cancer Imaging
> Archive. https://doi.org/10.7937/K9/TCIA.2018.OW73VLO2

Source CT citation:

> Akin, O., Elnajjar, P., Heller, M., Jarosz, R., Erickson, B. J., Kirk, S.,
> Lee, Y., Linehan, M. W., Gautam, R., Vikram, R., Garcia, K. M., Roche, C.,
> Bonaccio, E., & Filippini, J. (2016). The Cancer Genome Atlas Kidney Renal
> Clear Cell Carcinoma Collection (TCGA-KIRC) (Version 3) [Data set]. The
> Cancer Imaging Archive. https://doi.org/10.7937/K9/TCIA.2016.V6PBVTDR

The 101 CT instances and Enhanced SR are redistributed in
`examples/demo-data/tcia-tcga-bp-4343` under the cited CC BY 3.0 terms. They
are de-identified research data, not a substitute for clinical care or a
clinically verified diagnostic report.

## Repository modifications

The redistributed DICOM objects were modified for this repository by replacing
the TCIA subject identifier with `IMAGINGRESULTS-DEMO-001`, clearing demographic,
accession, institution, observer, acquisition date/time, and device identifiers,
and removing private DICOM elements. DICOM study, series, instance, and
frame-of-reference UIDs were retained to preserve the Enhanced SR evidence
references. The modifications are not endorsed by TCIA or the original data
contributors.
