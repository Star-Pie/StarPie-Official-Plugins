# CI Workflow Design

## Incremental validation

`validate.yml` runs for pull requests (excluding documentation-only and workflow-only changes) and manual dispatch, but not for `main` pushes.

```text
module-only change
-> build/test changed module

shared build/schema/catalog/workflow change
-> build/test all modules
```

## Full validation

`full-validation.yml` runs:

- manually;
- weekly;
- not on `main` pushes.

It builds, tests, packs and verifies every registered module and uploads temporary validation packages.

## Release

`release-modules.yml` is manual and version-driven.

It generates a temporary registry from each module's plugin.json, then compares those versions with the latest released catalog. Modules with no version change are not rebuilt or reuploaded.

The release job rejects non-dry-run execution unless repository variable `SIGNING_ENABLED` is `true`.

`catalog_base_channel` defaults to `same`, retaining the normal per-channel history policy. For an explicit stable promotion, select `release_channel=stable` and `catalog_base_channel=beta` to retain the tested beta catalog's immutable version records while publishing a stable catalog. `module_ids` still limits newly built packages; other modules keep their original package URLs and hashes.

DesktopPet 0.1.6 uses this official release workflow with ID `starpie.plugin.desktoppet`. The historical independent 0.1.5 release remains available; its old ID, installed data and action bindings are not automatically rewritten. See [installation and switching instructions](desktop-pet-release.md).
