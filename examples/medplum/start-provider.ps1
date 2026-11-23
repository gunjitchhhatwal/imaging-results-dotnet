[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$image = "imagingresults-medplum-provider:5.2.17"

docker build `
    --file (Join-Path $root "examples\demo-stack\provider.Dockerfile") `
    --tag $image `
    $root
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build the Medplum Provider image."
}

Write-Host "Medplum Provider will be available at http://localhost:3101"
docker run --rm --publish 127.0.0.1:3101:80 $image
