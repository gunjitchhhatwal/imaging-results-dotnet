#!/bin/sh
set -eu

until curl --fail --silent http://orthanc:8042/system >/dev/null; do
  sleep 2
done

find /demo-data/tcia-tcga-bp-4343 \
  -type f -name '*.dcm' -print |
  sort |
  while IFS= read -r file; do
    echo "Loading $file"
    curl \
      --fail \
      --silent \
      --show-error \
      --request POST \
      --header 'Content-Type: application/dicom' \
      --data-binary "@$file" \
      http://orthanc:8042/instances >/dev/null
  done

echo "DICOM studies loaded into Orthanc."
