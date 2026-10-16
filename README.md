# Risk of VR

Eigen Steam Frame-fork van [Resurrected VRMod](https://github.com/Blowntobytes/RoR2VRMod), voortbouwend op [DrBibops RoR2VRMod](https://github.com/DrBibop/RoR2VRMod). Bron: [Lufaja/RoR2VRMod](https://github.com/Lufaja/RoR2VRMod).

**Status: experimentele versie 1.1.0.** Alle zeven mod-/XR-DLLs zijn succesvol gecompileerd tegen publieke RoR2 1.5.0-referenties. De mod is nog niet gestart in de game of getest op Steam Machine/Steam Frame. De oorspronkelijke auteurs' compatibiliteitsclaims gelden niet automatisch als bewijs voor deze fork. Er is geen gepubliceerde, als speelbaar geverifieerde release van deze versie.

## Bedoelde opstelling

Steam Machine met SteamOS → Windows-versie van RoR2 onder Proton → SteamVR/OpenXR → Steam Frame. Installeren via **r2modman**, met het standaard Thunderstore-zipformaat. Deze laptop is alleen de ontwikkelmachine.

Valve noemt Steam Machine/Linux-streaming momenteel nog niet volledig ondersteund. Begin met Desktop Mode/SteamVR Beta volgens [Valve Support](https://help.steampowered.com/en/faqs/view/2668-0B6E-79E0-A85B). Volg [de installatie- en herstelhandleiding](docs/STEAM-MACHINE.md); een broncode-zip is geen modpakket.

## Wijzigingen

- Instelbare gameplaybindings in de VR-tab/configuratie, met behoud van menu- en systeemknoppen en oorspronkelijke standaardbindings.
- Eenmalige Steam Frame seated-preset: rechtshandig, zittend en 30° snap turn. Bestaande instellingen worden niet bij elke start overschreven. Vloeiend draaien en linkshandigheid blijven instelbaar.
- Native Steam Frame-controllerprofiel alleen wanneer de OpenXR-extensie actief is; Touch-compatibiliteit blijft beschikbaar.
- Autosprint gebruikt een aparte virtuele input en drukt bij vliegende bodies geen stickklik meer in. Dit richt zich op [de Operator-drone-melding](https://github.com/Blowntobytes/RoR2VRMod/issues/3); een hardwarehertest blijft nodig.
- Verbeterd camerabeheer, herstel van projectiematrices en optionele stereodiagnostiek. Dit bewijst niet dat [DrBibops camerablokkade #71](https://github.com/DrBibop/RoR2VRMod/issues/71) is opgelost.
- Native XR-installatie met oorspronkelijke backups, hashes, hersteljournal en een herstelhulpmiddel.
- Portable bouwketen met vastgelegde XR-bronnen, referentiecontrole en pakketcontrole die onvolledige of verouderde builds weigert.

De zittende Device-origin blijft de aanbevolen route. De bestaande standing-instelling is experimenteel; echte floor-gekalibreerde roomscale is niet geïmplementeerd/gevalideerd.

Foveated **streaming** wordt door Steam verzorgd. Foveated **rendering** is niet geïmplementeerd; zie [de haalbaarheidsnotitie](docs/FOVEATION.md). De mod toont geen fictieve foveatie- of streamingstatus.

## Bouwen en controleren

Zie [build-tools/README.md](build-tools/README.md) voor Python 3.9+, PowerShell 7, locked XR-bronnen en lokale game-/toolreferenties. De bestaande `.csproj`-bestanden zijn alleen voor broncodebrowsing en blokkeren een verouderde MSBuild-build; gebruik de gedocumenteerde Python-bouwroute.

```sh
python3 build-tools/build.py preflight
python3 build-tools/build.py plan
python3 -m unittest discover -s tests/build_tools -v
pwsh -NoProfile -File tests/check-csharp-syntax.ps1
pwsh -NoProfile -File tests/input/run.ps1
pwsh -NoProfile -File tests/runtime_safety/run.ps1
RISKOFVR_PWSH=pwsh python3 -m unittest discover -s tests/runtime_safety -v
```

Preflight vereist echte, vastgelegde referenties. De bouwketen onderscheidt lokale gamebestanden en publieke referentiepackages. Pure-logica- en syntaxtests vervangen geen volledige compilatie of in-game test. [Verificatiestatus en hardwarechecklist](docs/VERIFICATION.md) onderscheiden die bewijzen.

De game-/referentieassemblies blijven lokaal en worden niet gecommit of verpakt. Een geslaagde volledige build is vereist voordat `build.py package` een zip maakt. Er wordt niet automatisch naar Thunderstore gepubliceerd.

De handmatig startbare GitHub Actions-workflow **Experimental reference build**
bouwt dezelfde code met vastgelegde publieke referenties en levert een
experimentele import-zip als downloadartefact. De game hoeft daarvoor niet op
de laptop te staan. Een geslaagde workflow bevestigt compilatie en verpakking;
de Steam Machine/Frame-hardwarecheck blijft nodig.

## Issues, configuratie en compatibiliteit

[Het issueoverzicht](docs/UPSTREAM-ISSUES.md) bevat de vier recente forkmeldingen en relevante originele issues, met reproduceerstappen en bewijsstatus. Itemopname, scrappermenu's en Bandit-handrotatie blijven expliciet onbevestigd.

De interne plugin-ID blijft `com.DrBibop.VRMod`; VRAPI blijft behouden. Installeer daarom nooit tegelijk deze fork, Resurrected VRMod en de originele VRMod. Gebruik een apart profiel en bewaar je bestaande configuratie. Gameplaybindings, comfortinstellingen en de seated-preset staan in `BepInEx/config/VRMod.cfg`. De nieuwe cameraworkaround en diagnostiek staan apart in `BepInEx/config/com.DrBibop.VRMod.cfg`.

`[Diagnostics] Runtime summary` logt versies/runtime bij opstarten. `Picker events` logt optioneel het openen/sluiten van wereldkeuzemenu's. `StereoCamera` logt optioneel begrensde informatie per oog. Stuur geen log met persoonlijke paden publiek zonder dit eerst na te kijken.

## Credits en licenties

DrBibop en oorspronkelijke contributors maakten de VR-mod. Blowntobytes verzorgde de OpenXR-voortzetting. Deze fork voegt Steam Frame-gerichte aanpassingen en ontwikkel-/herstelgereedschap toe. De oorspronkelijke MIT-notices blijven staan; zie [LICENSE.md](LICENSE.md) en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). De upstream-releasegeschiedenis blijft onder onze nieuwe vermelding in [CHANGELOG.md](CHANGELOG.md).
