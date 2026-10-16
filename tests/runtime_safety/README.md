# Native installation safety checks

Run from the repository root with PowerShell 7 and Python 3:

```sh
pwsh -NoProfile -File tests/runtime_safety/run.ps1
RISKOFVR_PWSH="$(command -v pwsh)" python3 -m unittest discover -s tests/runtime_safety -p 'test_*.py' -v
```

The PowerShell harness compiles the actual `NativeFileInstaller.cs` with .NET's `Add-Type`, without Unity, game assemblies or NuGet. It writes only randomly named temporary fixtures and removes them. The Python suite covers restore dry-run, preservation, external changes, corrupt/missing backups, interrupted journal writes, interrupted restore, path validation and symlinks. `RISKOFVR_PWSH` enables an additional test that restores a ledger and backups produced by the actual C# installer. Without it, that cross-language test is explicitly skipped.

These checks prove file-content safety in local fixtures. They do not prove Unity loading, Proton's file replacement behavior, native DLL compatibility, stereo output, Steam Frame streaming, or game/headset operation. Camera behavior still requires a full build with target game references and the hardware acceptance run.

The restore tool is preview-only unless `--apply` is supplied:

```sh
python3 tools/restore-native.py --game-data "/path/to/Risk of Rain 2/Risk of Rain 2_Data"
python3 tools/restore-native.py --game-data "/path/to/Risk of Rain 2/Risk of Rain 2_Data" --apply
```

Close the game and mod manager before applying. Originals are the bytes present before this installer first ran, which may already have come from another mod or an older patcher; they are not asserted to be pristine Steam files. Keep `.vrmod-native` after restoration. Its `disabled` marker prevents automatic reinstallation by a still-present patcher. Remove/disable the VR patcher and plugin in the mod profile when returning to flat mode. The tool refuses unknown later changes and does not overwrite them to force a rollback.
