# Security Policy

Official StarPie modules are process-internal .NET assemblies. A malicious module can execute with the same privileges as StarPie.

## Release requirements

- Production releases must be signed.
- `module-catalog.json` is the root of trust for package resolution.
- Catalog signatures must be verified by the client before trusting package hashes.
- Module packages must be checked against the signed catalog SHA-256.
- Modules must not carry a private copy of `StarPie.Plugin.Abstractions.dll`.
- Modules must pass host SDK and manifest compatibility checks before loading.

See [`docs/security-model.md`](docs/security-model.md) for the planned trust chain.

## Reporting

Do not disclose a vulnerability in a public issue before a fix is available. Contact the StarPie maintainers through the security reporting channel configured for the main repository.