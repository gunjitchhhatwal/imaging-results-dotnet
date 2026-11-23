[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$composePath = Join-Path $PSScriptRoot "docker-compose.yml"

docker compose `
    --project-name imagingresults-medplum `
    --file $composePath `
    down
