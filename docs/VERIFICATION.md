# Verificatiestatus

Versie 1.1.0, ontwikkelcontrole op 16 oktober 2026. De volledige broncompilatie
is geslaagd tegen publieke referentiepackages. Er is nog geen game-launch,
Steam Machine-test of Steam Frame-headsettest. De laptop is alleen voor
ontwikkeling en compilatie gebruikt.

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
| Volledige broncompilatie | Alle zeven runtime-DLLs compileren zonder fouten tegen de vastgelegde referenties | Werking in de daadwerkelijk geïnstalleerde game/Proton/headset |
| Preflight zonder referenties | Ontbrekende echte dependencies worden afgewezen | Werkende gameversie |

Lokaal geslaagd: **89 C#-bronbestanden**, **134 inputasserties**, **11 C#-
installatietests**, **11 Python-hersteltests** en **28 bouw-/pakkettests**.
Preflight zonder referenties weigert de ontbrekende dependencies. Pakketvalidatie
weigert een ontbrekende of verouderde buildregistratie.

De GitHub-workflow **Source and isolated logic** voert deze losse checks uit.
Een groene workflow betekent nadrukkelijk geen speelbare VR-release. De aparte
handmatige workflow **Experimental reference build** voert de volledige
referentiecompilatie en pakketbouw uit en levert de experimentele import-zip.

## Volledige lokale broncompilatie

De canonieke bouwketen heeft Unity.InputSystem, XR.Management, XR.CoreUtils,
XR.OpenXR, VRPatcher, VRMod en VRAPI met nul compilerfouten gebouwd. Basis:
`RiskOfRain2.GameLibs 1.5.0-r.1`, UnityEngine.Modules 2021.3.33, de vier locked
XR-pakketten, legacyinputhelpers 2.1.7, BepInEx/MonoMod en originele haptics-
referenties uit DrBibops modpakket. De bouwregistratie markeert dit als
`reference-feed`, `game_runtime_verified: false` en `hardware_tested: false`.

Een lokale kopie van de publieke UnityEngine.UI-referentie kreeg elf herstelde
methodetoegangsrechten: publieke moddingreferenties botsten met de beschermde
overrides in officiële Unity-broncode. Er zijn geen types, methoden of bodies
toegevoegd. Deze referenties worden niet verpakt. Bronversies, transformaties
en hashes zijn vastgelegd; zie de [bouwdocumentatie](../build-tools/README.md).

De patcher bevat exact de drie verwachte native resources; VRMod bevat de
verwachte `vrmodassets`-resource. Lengtes en SHA-256-hashes komen met de
bronbestanden overeen. Beide native DLLs zijn ook
byte-identiek aan het vastgelegde OpenXR 1.9.1-pakket. Dit is metadata-/byte-
controle zonder uitvoering van mod- of gamecode.

## Voor gebruik op de eigen host

1. Leg de exacte Steam-gamebuild/Unity-versie vast en kopieer de eigen Managed-
   referenties. Haal de passende BepInEx/MonoMod- en hapticsreferenties op.
2. Haal de vier vastgelegde XR-sourcepakketten op met `fetch-xr`. Bewaar de
   pakketlicenties. Beide Windows-native DLLs zijn lokaal byte-voor-byte aan
   OpenXR 1.9.1 gekoppeld; de Git-blob-ID's staan in THIRD-PARTY-NOTICES.md.
   Controleer bij vervangen/updaten opnieuw de overeenkomst en licenties.
3. Voer `preflight`, `build` en `validate-package` uit met dezelfde paden.
   Bewaar compileruitvoer en de lokale buildregistratie. Gebruik geen
   synthetische testreferenties als vervanging voor gamebestanden.
4. Maak na een geslaagde volledige build een lokale import-zip via `package`,
   of gebruik het met deze bouwketen gemaakte experimentele importpakket.
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
