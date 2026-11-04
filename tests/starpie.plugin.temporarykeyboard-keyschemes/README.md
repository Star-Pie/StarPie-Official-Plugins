# Temporary keyboard key-scheme tests

Pure console regression tests for `KeySchemes`, `KeyEditingState`, and `KeySchemeStorage`. The project links only the real keyboard model/layout sources and does not reference WPF, the SDK, a plugin host, or input simulation.

Run from the worktree root with an explicit evidence root:

```cmd
dotnet build tests\starpie.plugin.temporarykeyboard-keyschemes\KeySchemeTests.csproj -c Release --nologo -p:OutDir=H:\starpie-task-evidence\keyboard-key-layout-20261103\deepseek-model\
set KEYBOARD_TEST_ROOT=H:\starpie-task-evidence\keyboard-key-layout-20261103\deepseek-model
dotnet H:\starpie-task-evidence\keyboard-key-layout-20261103\deepseek-model\test_temporary_keyboard_keyschemes.dll
```

Coverage includes the full-layout key whitelist, JSON camelCase/case-insensitive round trips, dimension and count limits, deep cloning, editing and undo/redo, safe resize behavior, 32-entry/4 MiB history limits, strict UTF-8 and JSON-size handling, recovery backups, concurrent-file rejection, injected write failures, and preservation of the old `keyboard-schemes.json` bytes.

The test refuses to run unless `KEYBOARD_TEST_ROOT` is set. Storage tests create only disposable subdirectories under that explicit evidence root.
