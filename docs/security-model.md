# Security Model

## Trust chain

The planned trust chain is:

```text
GitHub release identity
-> signed module-catalog.json
-> SHA-256 for each module package
-> verified .spkg content
-> static plugin scan
-> runtime load
```

The client must verify the catalog signature before trusting any package URL or hash from the catalog.

## Required checks

- Verify catalog signature.
- Verify catalog schema and release channel policy.
- Verify package SHA-256 against the signed catalog.
- Verify module manifest ID and version.
- Verify assembly SHA-256 from `module.manifest.json`.
- Verify `PluginApi` and host compatibility.
- Verify no private `StarPie.Plugin.Abstractions.dll` is packaged.
- Reject unsigned `stable` and `beta` releases; dry-run artifacts are never publishable releases.

## Non-goals

A hash embedded in an unsigned catalog is not a security boundary. If both the catalog and package can be replaced by an attacker, the hash provides integrity only against accidental corruption.