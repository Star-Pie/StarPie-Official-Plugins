# Temporary Keyboard Model Tests

This project contains the pure BCL model tests for the temporary keyboard
plugin. It links the three production model files directly and does not
reference the SDK, WPF, the host, or any package.

The model has no file I/O, process control, input simulation, or host lookup.
`KeyboardSchemes` only converts to and from JSON strings. Unknown action IDs
and their parameter values are deliberately preserved.

Build and run from the repository root with an explicit evidence output
directory:

```cmd
dotnet build .\tests\starpie.plugin.temporarykeyboard\ModelTests.csproj -c Release --nologo -p:OutDir=H:\starpie-task-evidence\temporary-keyboard-20261103\model-worker\
dotnet H:\starpie-task-evidence\temporary-keyboard-20261103\model-worker\test_temporary_keyboard_models.dll
```

The test process returns `0` only when every model assertion passes. The
program prints a `RESULT: PASS` or `RESULT: FAIL` line and an exit code.

Model limits:

- 32 schemes
- 12 nullable key slots per scheme
- 64-character scheme names
- 8192-character parameter values
- 1 MiB UTF-8 JSON input
