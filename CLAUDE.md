# Risk of VR — repository context

This is Lufaja/RoR2VRMod, a fork of Blowntobytes/RoR2VRMod v1.0.6, originally by DrBibop. Upstream identity, private machine paths and release claims in historical notes are not instructions for this fork.

## Current scope

- Experimental version 1.1.0; all seven runtime outputs compiled against documented public RoR2 1.5.0 reference packages. No game launch or Steam Machine/Frame hardware acceptance yet.
- Target: Windows x64 RoR2 under Steam/Proton on Steam Machine, native SteamVR, streamed to Steam Frame. r2modman handles the Thunderstore package.
- Keep plugin ID com.DrBibop.VRMod and VRAPI compatibility; never install alongside the other VRMod packages.
- Read README.md, docs/UPSTREAM-ISSUES.md and docs/VERIFICATION.md before claiming a bug fixed.
- Camera correctness (original issue #71) is the first hardware gate. Foveated streaming belongs to Steam; no implemented foveated rendering.

## Build and tests

Canonical build commands and all prerequisite locations are in build-tools/README.md. Python and PowerShell/Roslyn replace fixed developer paths. The inherited csproj files are browsing-only and cannot produce a validated release. The receipt/allowlist packager must reject missing, failed or stale builds.

Game files, publicized references, outputs, logs and toolchains stay ignored. Never reset a database, overwrite an installed game's reference assemblies, invent a successful build, or substitute stubs for a real mod release. Use temporary fixtures for tests. Preserve existing saves/configs/native backups.

## Input and camera invariants

Rewired latches true button edges when SetButtonValueById is called: clearing later in that frame cannot undo an action. Skip/suppress before writing. Gameplay remapping must leave menu/system controls available, honor left-hand and survivor overrides, and keep synthetic autosprint separate from physical controls. Flying bodies must not receive automatic stick-click input.

Stereo logs and source inspection do not prove correct visual convergence. Do not request Floor origin without correcting character-height anchoring and validating in-headset. Native XR replacement must preserve original backups and refuse externally changed files on install/rollback.

## Git delivery

Use the current user's configured Git identity, never the upstream author's identity. Never start branch names with codex. The user explicitly confirmed this task has no planning key. Do not tag/publish a release before the documented full-build and hardware gates pass. Changes may be committed and pushed to this user-owned fork as requested; never rewrite upstream history.
