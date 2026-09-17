# Release Model

## Core rule

Each module-set release represents the complete official module state, but only changed module packages are attached to that release.

## Example

If OCR changes from `1.0.0` to `1.0.1`:

```text
Release: modules-2026.09.18.1
Assets:
- module-catalog.json
- module-catalog.sigstore.json
- StarPie.Plugin.Ocr-1.0.1.spkg
```

Unchanged modules are not uploaded again. Their catalog entries keep the package URL and SHA-256 from the release where they were last published.

The release notes and catalog still list the complete current set:

```text
Launch          1.0.0
WebUrl          1.0.0
Folder          1.0.0
Command         1.0.0
ShellTool       1.0.0
Tile            1.0.0
ToggleTopmost   1.0.0
MoveMonitor     1.0.0
WindowOpacity   1.0.0
SwitchWindow    1.0.0
Ocr             1.0.1
System          1.0.0
```

## Release sequence

```text
1. Merge a module version change.
2. Run release-modules.yml manually.
3. Compare registry versions with the latest released catalog.
4. Build/test only changed modules.
5. Pack and verify changed modules.
6. Generate catalog referencing new and historical package assets.
7. Sign the catalog.
8. Create a draft GitHub release.
9. Upload only changed .spkg assets and the signed catalog.
10. Publish the release.
```

## Catalog rules

- The catalog is complete for every enabled module.
- New package entries use the current release tag.
- Unchanged entries preserve their historical release tag and URL.
- A catalog is invalid if any enabled module is missing.
- A module version change without a new package is invalid.
- A package version that does not match the registry is invalid.
- Historical release assets must not be deleted.

## Client behavior

The StarPie client should:

- fetch the latest signed catalog;
- verify the catalog signature;
- compare catalog versions with locally installed versions;
- download only missing or outdated modules;
- verify package SHA-256 before extraction;
- retain a verified previous module when an update fails;
- never download modules on the mouse-gesture hot path.