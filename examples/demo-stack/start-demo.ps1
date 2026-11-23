[CmdletBinding()]
param(
    [switch]$Stop,
    [switch]$Reset
)

$ErrorActionPreference = "Stop"
$composePath = Join-Path $PSScriptRoot "docker-compose.yml"
$projectName = "imagingresults-demo"

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop is not running."
}

$compose = @(
    "compose",
    "--project-name", $projectName,
    "--file", $composePath
)

if ($Stop -or $Reset) {
    $arguments = $compose + @("down")
    if ($Reset) {
        $arguments += "--volumes"
    }

    & docker @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to stop the demo stack."
    }

    if ($Stop) {
        return
    }
}

$arguments = $compose + @("up", "--detach", "--build")
& docker @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Failed to start the demo stack."
}

function Wait-ForUrl {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$Url
    )

    $deadline = (Get-Date).AddMinutes(10)
    do {
        try {
            $response = Invoke-WebRequest `
                -Uri $Url `
                -Method Get `
                -UseBasicParsing `
                -TimeoutSec 10
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "$Name is ready: $Url"
                return
            }
        }
        catch {
            Start-Sleep -Seconds 3
        }
    } while ((Get-Date) -lt $deadline)

    throw "$Name did not become ready at $Url."
}

Wait-ForUrl -Name "Orthanc" -Url "http://localhost:8042/system"
Wait-ForUrl -Name "OHIF" -Url "http://localhost:3000"
Wait-ForUrl -Name "Medplum" -Url "http://localhost:3100"
Wait-ForUrl -Name "Medplum Provider" -Url "http://localhost:3101"

Write-Host ""
Write-Host "TCIA study in OHIF:"
Write-Host "http://localhost:3000/viewer?StudyInstanceUIDs=1.3.6.1.4.1.14519.5.2.1.9203.4004.100545948082587796019777812429"
Write-Host ""
Write-Host "Medplum App:      http://localhost:3100"
Write-Host "Medplum Provider: http://localhost:3101"
Write-Host ""
Write-Host "After creating a local Medplum account and project, import:"
Write-Host "artifacts\real-sample\fhir\real-renal-measurement-fhir-r4-transaction.json"
