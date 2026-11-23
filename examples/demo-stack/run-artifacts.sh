#!/usr/bin/env bash
set -euo pipefail

mkdir -p /artifacts/real-sample/fhir

dotnet run \
  --project /workspace/examples/ImagingResults.AzureDemo \
  --configuration Release \
  --no-build \
  -- \
  --fhir-from-dicom-sr \
  /demo-data/tcia-tcga-bp-4343/measurement-report.dcm \
  --output-directory \
  /artifacts/real-sample/fhir
