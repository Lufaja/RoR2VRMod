# Verificatiestatus

Bronversie 1.1.0, ontwikkelcontrole op 16 oktober 2026. Deze status geldt voor
de aangepaste broncode; er is nog geen volledige modbuild, Steam Machine-test
of Steam Frame-headsettest. De laptop wordt alleen voor ontwikkeling gebruikt.

## Lokaal uitvoerbaar zonder game

De opdrachten staan in [README.md](../README.md) en draaien met Python en
PowerShell 7. Ze gebruiken tijdelijke testmappen en veranderen geen game,
Steam-profiel, saves of database.

| Controle | Wat dit bewijst | Wat dit niet bewijst |
| --- | --- | --- |
| C# Roslyn-syntaxcontrole | Alle bronbestanden zijn syntactisch geldig onder de gekozen Unity-defines | Types/API's passen bij de huidige game |
| Inputcontracten | Bindingkeuzes, sprintbeleid, button/axis/tap/hold-adapters en disconnectgedrag werken in geïsoleerde scenario's | Rewired/XR/game-integratie of echte Frame-knoppen |
| Native installatietests | Backups, hashes, crashjournal, herhaalde installatie en geweigerde onveilige writes | Windows/Proton-filesystemgedrag op de host |
| Hersteltests | Dry-run, herstel, externe wijzigingen, onderbrekingen en echte C#-ledger naar Python | Dat een bestaande gameinstallatie al correct is hersteld |
| Bouw- en pakkettests | Bronselectie, resource-inbedding, compilatiefoutafhandeling, verse buildregistratie en allowlist | Een succesvolle volledige VRMod-build |
| Preflight zonder referenties | Ontbrekende echte dependencies worden afgewezen | Werkende gameversie |

Lokaal geslaagd: **89 C#-bronbestanden**, **134 inputasserties**, **11 C#-
installatietests**, **11 Python-hersteltests** en **20 bouw-/pakkettests**.
Het plan genereert 31 opdrachten. Preflight weigert de ontbrekende game-/tool-
referenties en XR-bronnen; pakketvalidatie weigert de ontbrekende volledige build.

De GitHub-workflow **Source and isolated logic** voert deze losse checks uit.
Een groene workflow betekent nadrukkelijk geen speelbare VR-release.

## Volledige build: nog nodig

1. Leg de exacte Steam-gamebuild/Unity-versie vast en kopieer de eigen Managed-
   referenties. Haal de passende BepInEx/MonoMod- en hapticsreferenties op.
2. Haal de vier vastgelegde XR-sourcepakketten op met `fetch-xr`. Bewaar de
   pakketlicenties. Beide Windows-native DLLs zijn lokaal byte-voor-byte aan
   OpenXR 1.9.1 gekoppeld; de Git-blob-ID's staan in THIRD-PARTY-NOTICES.md.
   Controleer bij vervangen/updaten opnieuw de overeenkomst en licenties.
3. Voer `preflight`, `build` en `validate-package` uit met dezelfde paden.
   Bewaar compileruitvoer en de lokale buildregistratie. Gebruik geen
   synthetische testreferenties als vervanging voor gamebestanden.
4. Maak pas na een geslaagde volledige build een lokale import-zip via `package`.
   Bevestig het zipformaat/dependencies door import in een leeg r2modman-profiel.
   Publiceer geen release als de onderstaande hardwarepoort faalt.

## Hardwarepoort: camera eerst

Gebruik [de hosthandleiding](STEAM-MACHINE.md) en noteer game, modcommit, SteamOS,
Proton, GPU/driver, SteamVR, OpenXR-runtime, controllerprofiel en resolutie.

- Vergelijk een werkende gewone SteamVR-app via dezelfde verbinding met RoR2.
- Kijk in **beide ogen** naar natuurlijke diepte/schaal, uitrekking, dubbelbeeld,
  scheefstand, headtracking en clipping. Een desktop-mirror alleen is onvoldoende.
- Test hoofdrotatie/translatie, seated recenter, camera/sprint-FOV, pauze/menu,
  nieuwe run, meerdere stagewissels, sterven/spectaten/herleven en XR reconnect.
- Maak indien nodig een A/B-opname met `StereoProjectionWorkaround` aan/uit en
  `StereoCamera` aan. Bewaar headsetwaarneming naast begrensde diagnostiek.
- Test draaien 30°/smooth, beide dominante handen, alle gameplayacties,
  remaps/config-herstart en tijdelijk controllerverlies zonder spookinputs.
- Test scrapper/keuzemenu's (tap/loslaten/selecteren/annuleren), itemopname,
  Bandit-aim en Operator Remote Operation met autosprint aan/uit.
- Voltooi een solo-run en test host/client met een gewone niet-VR-speler;
  controleer equipment-hand en spectaten volgens het [issueoverzicht](UPSTREAM-ISSUES.md).
- Test terugdraaien op een kopie van de hostinstallatie met de echte Proton-
  installatiebestanden en controleer daarna de gewone game.

Leg per scenario verwacht/werkelijk gedrag en het resultaat vast. Issue #71 is
pas op deze opstelling geverifieerd na zichtbaar correcte stereo; een geldige
projectiematrix of geslaagde bronbuild is daarvoor onvoldoende. Foveated
rendering blijft buiten deze versie, ongeacht de streaminginstellingen van Steam.
