# Official Module Contract

## Module registry entry

Every official module is declared in `module-registry.json`.

Required fields:

- `id`: stable module ID; historical migrated actions use `starpie.builtin.*`, while new official plugins use `starpie.plugin.*`;
- `name`: display name used by build and release output;
- `project`: relative path to the module `.csproj`;
- `assembly`: expected output DLL name;
- `targetFramework`: expected output target framework;
- `version`: module package version;
- `pluginId`: runtime plugin ID written to `plugin.json`;
- `contributionId`: action contribution ID implemented by the module;
- `typeClaims`: legacy top-level action types claimed by the module; new `starpie.plugin.*` modules should leave this empty, while historical migrated `starpie.builtin.*` modules may claim their original action types;
- `capabilities`: declared host capabilities;
- `releaseDirectory`: logical release grouping for diagnostics.

## Package layout

An `.spkg` package is a deterministic ZIP archive containing:

```text
plugin.json
module.manifest.json
<module assembly>.dll
optional module dependencies
optional assets/
```

`plugin.json` is consumed by StarPie's existing plugin scanner and loader.

`module.manifest.json` is consumed by the module installer and package-verification pipeline. It records the module version, assembly hash, SDK version, host compatibility range, Type Claims and capabilities.

## Runtime contract

The package installer extracts `.spkg` into the writable plugin host directory. StarPie then uses the existing scanner and runtime:

```text
.spkg
-> verify catalog hash
-> extract to plugin-data/<pluginId>/
-> PluginScanner
-> PluginManifestReader
-> PluginInstance
-> PluginLoadContext
-> PluginRuntime
```

The runtime does not load `.spkg` directly.