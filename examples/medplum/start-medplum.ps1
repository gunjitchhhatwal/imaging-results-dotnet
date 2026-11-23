[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$composePath = Join-Path $PSScriptRoot "docker-compose.yml"

docker compose `
    --project-name imagingresults-medplum `
    --file $composePath `
    up --detach --wait

Write-Host ""
Write-Host "Medplum admin and FHIR resource viewer: http://localhost:3100"
Write-Host "Medplum FHIR R4 endpoint:             http://localhost:8103/fhir/R4"
Write-Host ""
Write-Host "Create a local account and project, then use the Batch tool to import:"
Write-Host "artifacts\real-sample\fhir\real-renal-measurement-fhir-r4-transaction.json"
