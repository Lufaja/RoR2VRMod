# Portable source build

This is the canonical build route for the maintained fork. It runs on Windows,
macOS and Linux with **Python 3.9+, Git and PowerShell 7**. PowerShell supplies the
Roslyn compiler, so Visual Studio, Unity Editor and the .NET SDK are not required.
The output targets the **Windows x64** game, even when compiled on another OS.
The legacy `.csproj` files describe the projects; they are not the complete XR build.

A plan or passing tool test is not a compiled mod. A successful source build is not
proof that the current game or a headset works. Game-launch and hardware checks
are separate release gates; see the root documentation.

## Local inputs

Nothing here downloads or redistributes Risk of Rain 2. Copy references from your
own legally installed Windows game. Keep the originals unchanged. By default:

```text
refs/
  game/                    complete Risk of Rain 2_Data/Managed directory
  optional/
    Bhaptics.Tact.dll       compile reference for the optional haptics integration
    ShockwaveManager.dll   compile reference for the optional haptics integration
  publicized/              GENERATED copies; never the installed game directory
build/
  tools/
    bepinex/BepInEx/core/   BepInEx 5.4.x core assemblies (5.4.21 API)
    monomod/               compatible MonoMod legacy HookGen tools (see below)
    packages/              locked Unity source checkouts (fetch-xr)
  mmhook/                  GENERATED MMHOOK_RoR2.dll
  out/                     GENERATED runtime assemblies and compiler response files
  build-receipt.json       written only after the entire source build succeeds
```

The MonoMod tool directory must contain `Mono.Cecil.dll`, `MonoMod.Utils.dll`,
`MonoMod.RuntimeDetour.dll`, `MonoMod.exe` and
`MonoMod.RuntimeDetour.HookGen.exe` from the same compatible legacy release.
Use the versions matching your BepInEx/HookGenPatcher installation. Newer MonoMod
releases with different APIs are not interchangeable. These external tool/reference
binaries are hashed into the build receipt; this repository does not claim to have
validated every possible MonoMod release.

The complete game Managed directory also supplies Unity modules, Rewired,
postprocessing, Addressables and other game dependencies. Preflight lists every
missing reference instead of substituting stubs. If a current game no longer
provides a named reference, review the source/build configuration against that
actual game build; do not download arbitrary replacement game DLLs.

The optional haptics libraries are still required to **compile** the existing
integrations. Their absence at runtime is supported by the mod. They are never
silently added to the Thunderstore ZIP.

## Locked XR sources

`xr-dependencies.lock.json` pins these versions to immutable commits in the
[needle Unity package mirrors](https://github.com/needle-mirror):

| Runtime assembly | Package version |
| --- | --- |
| Unity.InputSystem | 1.6.3 |
| Unity.XR.Management | 4.4.0 |
| Unity.XR.CoreUtils | 2.1.1 |
| Unity.XR.OpenXR | 1.9.1 |

The OpenXR managed version matches the native binaries already tracked under
`VREnabler/Plugins`. Engine, legacy input helpers and UI references come from the
game Managed directory. Source package licenses are required and included in the
release archive, including OpenXR's `Third Party Notices.md`. Preflight compares
both tracked native DLLs and their Windows x64 counterparts in the locked OpenXR
source tree to fixed SHA-256 hashes, preventing managed/native version mismatch.
It also verifies package names, versions, exact Git commits
and absence of local/untracked modifications.

Fetch is an explicit network action. It preserves existing directories and fails
if a fetched tag does not match its locked commit:

```sh
python3 build-tools/build.py fetch-xr
```

On Windows use `python` instead of `python3` if that is your Python command.

## Commands

Run from the repository root. All commands accept the same location flags.
Defaults above work after copying references. `--game-managed` can also point
straight at the installed game's Managed directory: generation uses separate
copies, and the build does not write to that directory.

```sh
python3 build-tools/build.py preflight --game-managed "/path/to/Risk of Rain 2_Data/Managed"
python3 build-tools/build.py plan --game-managed "/path/to/Risk of Rain 2_Data/Managed"
python3 build-tools/build.py build --game-managed "/path/to/Risk of Rain 2_Data/Managed"
python3 build-tools/build.py validate-package --game-managed "/path/to/Risk of Rain 2_Data/Managed"
python3 build-tools/build.py package --game-managed "/path/to/Risk of Rain 2_Data/Managed"
```

PowerShell example with paths containing spaces:

```powershell
python build-tools/build.py preflight --game-managed 'D:\SteamLibrary\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed' --pwsh 'C:\Program Files\PowerShell\7\pwsh.exe'
```

Options: `--refs`, `--tools`, `--bepinex-core`, `--monomod`, `--packages`,
`--build-dir`, `--pwsh`, and `--zip-path` (packaging). `ROR2_MANAGED` and `PWSH`
can set the game and PowerShell defaults. Keep the same path options for build,
validation and packaging. `plan` prints a JSON array of exact argument lists
without compiling, downloading or needing game files. Paths are not shell code.

Build order: InputSystem → XR.Management → XR.CoreUtils → XR.OpenXR → private
HookGen hooks → publicized reference copies → VRPatcher → VRMod → VRAPI.
Generated response files show every source, define, reference and resource.
`ubuild.py --plan` writes one response file without compiling; unlike the full
plan, it requires that assembly's source/reference files to exist.

`gen.ps1`, `publicize.ps1` and `dumpil.ps1` accept explicit tool and search paths.
`ubuild.py` resolves `csc.ps1` relative to itself and accepts `--pwsh`, `--driver`
and repeatable `--var NAME=PATH`. The checked-in reference lists use these
variables rather than machine paths. Explicit `--ref` arguments take priority;
otherwise the first same-named reference wins.

## Resources and package safety

The raw manifest resource names are preserved exactly:

- `VRMod.Properties.vrmodassets`
- `VRPatcher.Plugins.UnityOpenXR.dll`
- `VRPatcher.Plugins.openxr_loader.dll`
- `VRPatcher.Dependencies.UnitySubsystemsManifest.json`

Only a successful full build writes the SHA-256 receipt. A new build attempt
invalidates an older receipt before preflight; any build failure therefore blocks
packaging. Changes to sources, resources, references, dependency sources,
compiler binaries, metadata or tool scripts require a new build. Missing or changed
output DLLs also block packaging.

Packaging copies an explicit allowlist: the seven built runtime assemblies,
root manifest/README/changelog/icon/license/notices, the linked build/install/
verification/issue/foveation documentation, `tools/restore-native.py` and four Unity
package licenses plus the OpenXR third-party notice.
VRPatcher goes in `patchers/`; VRMod, VRAPI and the four XR assemblies go in
`plugins/`. Publicized DLLs, game/reference assemblies and MMHOOK are excluded.
Generated directories and existing output-file links are resolved before checking
that they cannot overlap the original game references. No existing release ZIP is
overwritten: packaging uses an exclusive random temporary file and atomic
no-overwrite hard-link promotion. A filesystem that cannot create hard links fails
safely; choose a local filesystem for `--zip-path`. ZIP entry timestamps and ordering are
fixed; receipts retain local paths and stay outside the ZIP. A receipt verifies
build freshness, not authorship or runtime compatibility.

## Local verification

```sh
python3 -m unittest discover -s tests/build_tools -v
```

These stdlib tests cover source selection, paths with spaces, raw resource
arguments, missing dependencies, invalidating failed builds, stale sources,
missing/tampered outputs, version mismatch, symlink protection, concurrent archive
publication and package allowlisting. With `PWSH` set (or `pwsh` on PATH), a real
Roslyn smoke test also checks embedded resources and removal of stale compiler output. They create
fixtures only in disposable temporary directories. They do not reset any database,
create a real mod release or simulate a successful game build.
