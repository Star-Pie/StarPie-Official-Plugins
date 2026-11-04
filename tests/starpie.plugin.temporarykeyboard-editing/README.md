# Temporary keyboard editing model tests

This console regression test exercises the pure `KeyboardEditingState` model without WPF, host services, file IO, input simulation, or plugin execution.

The project links the real model sources:

- `KeyboardModels.cs`
- `KeyboardLayouts.cs`
- `KeyboardSchemes.cs`
- `KeyboardEditingState.cs`

Build and run from the worktree root with an explicit evidence output directory:

```cmd
dotnet build tests\starpie.plugin.temporarykeyboard-editing\EditingTests.csproj -c Release --nologo -p:OutDir=H:\starpie-task-evidence\keyboard-repair-20261103\editing-worker\
dotnet H:\starpie-task-evidence\keyboard-repair-20261103\editing-worker\test_temporary_keyboard_editing.dll
```

Coverage includes deep cloning, selection and revision neutrality, scheme validation and limits, binding cloning, ordinal parameter comparison, label and swap operations, undo/redo restoration, redo clearing, unknown action preservation, parameter and JSON limits, and history count/byte caps.

This is model-level evidence only. WPF editing, drag/drop behavior, visual layout, keyboard navigation, focus behavior, and real input acceptance remain unverified and require a separate UI/manual candidate gate.
