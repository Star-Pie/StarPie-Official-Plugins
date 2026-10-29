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

## Process modules (`runtime: "process"`)

A process module ships an executable instead of a managed assembly. It does not run inside the host, so the host cannot gate it at runtime — that changes what the trust chain is actually promising.

### Payload trust chain

```text
Selinga release asset (GitHub)
-> sha256 pinned in payload.json
-> fetched by Fetch-ModulePayload.ps1 during packaging
-> recorded again in module.manifest.json files[]
-> .spkg hash in the signed catalog
```

Two independent pins matter: `payload.json` pins **provenance** (which release, which asset), `module.manifest.json` pins **the artifact that was actually packed**. A mismatch at either step aborts the build rather than shipping a package built from something else.

The executable is never committed. Like managed assemblies, it is a build product obtained from a release, not repository content.

### Capability declarations are advisory

For assembly modules, `capabilities` is a real gate: calling a service without declaring it throws `PluginCapabilityDeniedException`.

For process modules it is **not**. The plugin is an ordinary process; it can call any Win32 API regardless of what the manifest says. There is no interception point short of OS-level isolation, which is not this project's direction. Therefore:

- the install confirmation page must state, before the user clicks OK, that a separate program will run, and that it runs **with the user's rights and does not inherit host elevation**;
- reviewers must not treat a process module's capability list as a containment guarantee;
- the honest boundary for a process module is the operating system's own permission model — nothing less, nothing more.