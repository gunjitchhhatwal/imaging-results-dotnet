# Third-party components

This optional demonstration runs Medplum Server, Medplum App, and Medplum
Provider. Medplum is available under the Apache License 2.0:

- <https://github.com/medplum/medplum>
- <https://github.com/medplum/medplum-provider>

The Docker Compose configuration is adapted from Medplum's official
full-stack self-hosting example.

The optional Provider container is built directly from Medplum Provider commit
`c16dc278caf21b41fefa3db0a3447e1a44e9aee7`. Third-party JavaScript, CSS, and
brand assets are not stored in this repository. The container build uses the
checked-in reproducible dependency lock and copies the upstream Apache-2.0
license into the image. `provider-sbom.spdx.json` records the resolved npm
dependency inventory and declared package licenses.
Names and logos rendered by the upstream application remain the property of
their respective owners and do not imply endorsement of ImagingResults.NET.
