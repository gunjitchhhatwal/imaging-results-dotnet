param(
    [string]$Image = "ohif/app:v3.10.4",
    [string]$ContainerName = "imaging-results-ohif",
    [string]$StudyInstanceUid =
        "1.3.6.1.4.1.14519.5.2.1.9203.4004.100545948082587796019777812429"
)

$ErrorActionPreference = "Stop"
$configPath = (Resolve-Path "$PSScriptRoot\app-config.js").Path

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop is not running."
}

$existing = docker ps -a --filter "name=^/${ContainerName}$" --format "{{.ID}}"
if ($existing) {
    docker rm -f $ContainerName | Out-Null
}

docker run --detach `
    --name $ContainerName `
    --publish 127.0.0.1:3000:80 `
    --volume "${configPath}:/usr/share/nginx/html/app-config.js:ro" `
    $Image

Write-Host "OHIF: http://localhost:3000/viewer?StudyInstanceUIDs=$StudyInstanceUid"
