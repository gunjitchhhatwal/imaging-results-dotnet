# ImagingResults.NET

ImagingResults.NET is an Apache-2.0 .NET SDK for moving quantitative medical-imaging and AI results between an implementation-independent domain model, DICOM SR measurement reports, and FHIR R4/R5.

## Packages

| Package | Targets | Purpose |
| --- | --- | --- |
| `ImagingResults.Dicom` | .NET 11 | A deterministic, tested TID 1500 subset on fo-dicom 5.2.6 |
| `ImagingResults.Fhir.R4` | .NET 11 | Best-effort FHIR R4 mapping on Firely 6.8.0 |
| `ImagingResults.Fhir.R5` | .NET 11 | Best-effort FHIR R5 mapping on Firely 6.8.0 |
| `ImagingResults.Fhir.Azure` | .NET 11 | Optional authenticated transaction upload to Azure Health Data Services FHIR service |
| `ImagingResults.Dicom.Azure` | .NET 11 | Optional authenticated DICOMweb STOW-RS upload to Azure Health Data Services DICOM service |

These are the consumer-facing entry packages. `ImagingResults.Core` and
`ImagingResults.Fhir.Common` are restored transitively as implementation
dependencies and should not normally be installed directly. All SDK packages,
executable examples, and tests target .NET 11.

## Install and use

Choose packages by scenario; applications do not need to reference every
package.

| Scenario | Install | What it provides |
| --- | --- | --- |
| Author or parse DICOM SR | `ImagingResults.Dicom` | Domain model, validation, and the supported TID 1500 reader/writer |
| Map results to FHIR R4 | `ImagingResults.Fhir.R4` | Domain model and R4 `DiagnosticReport`, `Observation`, `ImagingStudy`, and provenance mapping |
| Map results to FHIR R5 | `ImagingResults.Fhir.R5` | Domain model and R5 mapping, including `ImagingSelection` |
| Upload DICOM to Azure | `ImagingResults.Dicom.Azure` | DICOM authoring plus authenticated AHDS DICOMweb transport |
| Map and upload FHIR to Azure | `ImagingResults.Fhir.Azure` | R4 mapping plus authenticated AHDS FHIR transaction transport |

For a local DICOM-to-FHIR R4 workflow:

```powershell
dotnet add package ImagingResults.Dicom
dotnet add package ImagingResults.Fhir.R4
```

The commands above use a configured NuGet feed after the packages are
published. To consume packages built from this repository before publication,
run `dotnet pack` as shown under [Build](#build), then add
`--source <repository>\artifacts` to each command.

```csharp
using FellowOakDicom;
using ImagingResults.Dicom;
using ImagingResults.Fhir.R4;
using ImagingResults.Validation;

var dicomFile = DicomFile.Open("measurement-report.dcm");
var readResult = new Tid1500Reader().Read(dicomFile);
var report = readResult.Report
    ?? throw new InvalidOperationException("The DICOM SR could not be parsed.");
var validation = new MeasurementReportValidator(
        NullTerminologyValidator.Instance)
    .Validate(report);
var fhir = new FhirR4Mapper().Map(report);
```

Choose `ImagingResults.Fhir.R5` instead when the receiving system supports
FHIR R5 and the richer `ImagingSelection` representation is required. Do not
install both FHIR versions unless the application intentionally handles both
sets of Firely model types.

For Azure Health Data Services, install only the transport package for the
required endpoint:

```powershell
dotnet add package ImagingResults.Dicom.Azure
dotnet add package ImagingResults.Fhir.Azure
```

Each Azure package brings its underlying adapter and support dependencies
transitively. Installing both is necessary only when the application uploads
both DICOM objects and FHIR resources.

## Architecture

`ImagingResults.Core` defines an implementation-independent, immutable domain model for coded concepts, UCUM quantities, tracking identity, observer and algorithm provenance, source images and frames, spatial coordinates, and SEG references. It has no fo-dicom, Firely, or Azure dependency.

Adapters depend inward on Core:

- `ImagingResults.Dicom` authors and parses the supported DICOM SR subset.
- A transitive FHIR support assembly contains deterministic identity and evidence helpers shared by the versioned FHIR adapters.
- `ImagingResults.Fhir.R4` and `ImagingResults.Fhir.R5` build release-specific Firely resources without coupling the domain model to FHIR.
- The optional Azure packages add authentication and transport without changing the report model or adapters.

Structural validation is separate from serialization and returns stable diagnostic codes, object paths, severities, and sources. Applications can implement `ITerminologyValidator` for local value sets, terminology services, or organization-specific policy. FHIR adapters can derive from `FhirMapperBase<TMappingResult>`, while Azure clients accept injected `TokenCredential` and `HttpClient` instances.

## Supported scope and conformance

The DICOM adapter implements a deterministic, useful **subset inspired by TID 1500**. It supports measurement groups, tracking identifiers, coded findings and sites, UCUM numeric measurements, algorithm and observer context, image/frame evidence, SEG references, and 2-D spatial coordinates. It is not a general SR parser or a complete implementation of every TID 1500 condition, included template, context group, modality rule, or evidence requirement. SEG support is reference-only; the SDK does not generate segmentation pixels or validate segmentation geometry.

FHIR output is a **best-effort mapping**, not a declaration of conformance to a published implementation guide. R4 is the broad production target and is used by the Azure transport. Because R4 has no `ImagingSelection`, it uses documented identifier-based evidence references and emits a warning. R5 adds `ImagingSelection`, but segment selection still requires a documented local representation. R4 and R5 remain separate packages because their generated Firely model types are not safely interchangeable.

Azure integrations provide authenticated transport to existing AHDS endpoints. They do not provision services, assign RBAC, create referenced Patient resources, upload missing image evidence automatically, validate receiver profiles, or establish clinical conformance. Successful HTTP storage or transaction processing proves transport acceptance only.

Automated tests validate domain invariants, diagnostics, deterministic DICOM and FHIR output, supported-subset round trips, Azure request and retry behavior, response parsing, and .NET 11 package consumption. They do not replace independent DICOM template validation, external implementation fixtures, vendor interoperability testing, terminology validation, or clinical and regulatory review.

## Quick start

Run the complete local demonstration with Docker Desktop:

```powershell
.\examples\demo-stack\start-demo.ps1
```

This one command maps the bundled de-identified TCIA SR to FHIR, loads the TCIA CT and SR into a local DICOMweb server, and starts OHIF, Medplum, and the sample EHR UI. The [workflow and demonstration document](docs/quantitative-imaging-ai-workflow.md) explains the complete path, commands, expected results, validation boundaries, and troubleshooting in one place.

```csharp
var report = new MeasurementReport(
    studyInstanceUid,
    seriesInstanceUid,
    srSopInstanceUid,
    DateTimeOffset.UtcNow,
    groups,
    new ObserverContext(ObserverKind.Device, "Example AI"));

var diagnostics = new MeasurementReportValidator(terminologyValidator).Validate(report);
var dicom = new Tid1500Writer().Write(report);
var fhirR5 = new FhirR5Mapper().Map(report);
```

Applications targeting FHIR R4 use the separate `ImagingResults.Fhir.R4`
package and `FhirR4Mapper`. R4 and R5 packages are intentionally separate
because Firely's generated model types for different FHIR releases cannot be
referenced safely as one model assembly.

Azure Health Data Services integrations can map and upload R4 in one operation:

```csharp
using var client = new AhdsFhirClient(
    new AhdsFhirOptions(new Uri(fhirServiceUrl)));

var upload = await client.UploadAsync(report);
```

The Azure package uses `DefaultAzureCredential`, deterministic conditional PUTs
inside a FHIR transaction, bounded transient retries, and structured
`OperationOutcome` errors.

Generated SR instances can be stored through DICOMweb:

```csharp
using var client = new AhdsDicomClient(
    new AhdsDicomOptions(new Uri(dicomServiceUrl)));

var stored = await client.StoreAsync(report);
```

The DICOM client uses `DefaultAzureCredential`, POST create semantics by
default, explicit PUT upsert mode, single-part and multipart STOW-RS,
idempotent-upsert retries, and parsed DICOM JSON results. See the opt-in
[`examples/ImagingResults.AzureDemo`](examples/ImagingResults.AzureDemo).

The [workflow and demonstration document](docs/quantitative-imaging-ai-workflow.md) is the single detailed guide for the TCIA, local DICOMweb, OHIF, Medplum, optional Azure scenarios, validation boundaries, references, and glossary.

## Build

```powershell
dotnet restore ImagingResults.sln
dotnet build ImagingResults.sln --configuration Release --no-restore
dotnet test ImagingResults.sln --configuration Release --no-build
dotnet pack ImagingResults.sln --configuration Release --no-build --output artifacts
```

`global.json` pins .NET SDK 11.0.100. The repository includes one
de-identified, re-pseudonymized TCIA research imaging case under CC BY 3.0 for
the runnable demonstration. It is not clinical data for treatment or diagnosis.
The repository does not include controlled terminology tables.
