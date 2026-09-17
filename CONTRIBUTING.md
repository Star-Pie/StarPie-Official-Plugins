# Contributing

## Module changes

- Keep each module independently versioned.
- Update the module version whenever module source or package contents change.
- Do not copy `StarPie.Plugin.Abstractions.dll` into module output or `.spkg`.
- Do not reference the main `StarPie.dll` assembly.
- Keep module dependencies limited to the SDK and BCL unless an RFC explicitly approves a dependency.
- Add a test under `tests/` when behavior changes.

## Required local validation

```powershell
./build/Build-Modules.ps1 -ModuleIds <module-id> -Configuration Release -RunTests
./build/Pack-Module.ps1 -ModuleId <module-id> -Configuration Release -ReleaseTag local -OutputMetadataPath release-metadata/packages/<module-id>.json -Clean
./build/Verify-ModulePackage.ps1 -PackagePath <package-path> -ModuleId <module-id>
```

## Pull requests

A pull request that changes one module should build and test only that module. Changes to shared build logic, SDK compatibility, schemas, catalog logic or release scripts trigger full validation.

## Release policy

PRs do not create releases. Release creation is manual through `release-modules.yml` after merge and version updates.