# CI Workflow Design

## Incremental validation

`validate.yml` runs for pull requests and `main` pushes.

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
- when shared build infrastructure changes.

It builds, tests, packs and verifies every registered module and uploads temporary validation packages.

## Release

`release-modules.yml` is manual and version-driven.

It compares `module-registry.json` versions with the latest released catalog. Modules with no version change are not rebuilt or reuploaded.

The release job rejects non-dry-run execution unless repository variable `SIGNING_ENABLED` is `true`.