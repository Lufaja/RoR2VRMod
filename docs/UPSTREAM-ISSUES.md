# Upstream-issues en bewijsgrenzen

Bronbasis: [Resurrected VRMod v1.0.6](https://github.com/Blowntobytes/RoR2VRMod/releases/tag/v1.0.6), commit `deb7ef84b961c02e8386c1fc873229a63214c17c`.
Issues gelezen op 16 oktober 2026. Geen van de onderstaande meldingen is op de Steam Machine/Frame gereproduceerd. Een codewijziging of geslaagde losse test is geen bewijs van een opgeloste gameplaybug.

| Issue | Bevinding / status in deze fork | Reproduceerstappen en acceptatie |
| --- | --- | --- |
| [DrBibop #71: camerablokkade](https://github.com/DrBibop/RoR2VRMod/issues/71) | Hoogste prioriteit. De oorspronkelijke maker beschrijft geen technische oorzaak. De fork bevat een stereoprojectieworkaround; de relatie met #71 is niet bewezen. Camera-lifecycle en diagnostiek zijn aangescherpt. | Controleer beide ogen: natuurlijke diepte/schaal, geen uitrekking/dubbelbeeld; hoofdrotatie/translatie, sprint/FOV, recenter, menu/run/stagewissel en XR reconnect. Bewaar stereo-opname én headsetwaarneming. |
| [Fork #4: itemopname](https://github.com/Blowntobytes/RoR2VRMod/issues/4) | Open/onbevestigd. Melder noemt 1.0.4 als werkende uitgangssituatie, niet welke latere versie faalt. Geen veronderstelde pickup-fix toegevoegd. | Versies/modlijst vastleggen; gronditem, equipment, itemkeuzemenu en multiplayer afzonderlijk testen, ook na herstart. Onderscheid geen pickup, ontbrekende input en netwerk/catalogusverschil. |
| [Fork #1: menu sluit](https://github.com/Blowntobytes/RoR2VRMod/issues/1) | Open/onbevestigd. Optionele picker-eventlogging helpt de levensduur volgen. Geen gemanipuleerde interactieknop die de oorzaak verbergt. | Tap interact bij scrapper/keuzemenu, loslaten, navigeren, selecteren, annuleren. Menu blijft tot voltooien/annuleren open zonder vasthouden. |
| [Fork #3: drone daalt](https://github.com/Blowntobytes/RoR2VRMod/issues/3) | Codepad gevonden: autosprint synthetiseert een stickklik voor vliegende bodies. Die synthese wordt voor vliegers gestopt; handmatige invoer blijft. Runtimehertest nodig. | Remote Operation: stick X/Y zonder klik mag geen daling veroorzaken; expliciete klik wel. Test autosprint aan/uit, remaps en terugkeer naar normale body. |
| [Fork #2: Bandit-handen](https://github.com/Blowntobytes/RoR2VRMod/issues/2) | Open/onbevestigd. Geen ongekalibreerde vaste rotatiecorrectie toegevoegd. | Vergelijk wapenmodel, aim-ray en inslagen bij beide handen/dominantie. Leg pose en hoek vast voordat offsets wijzigen. |
| [DrBibop #47: equipment-hand](https://github.com/DrBibop/RoR2VRMod/issues/47) | Historische regressietest; huidige bug niet vastgesteld. | Gerichte equipment als host/client, linker/rechter dominante hand en gewone medespeler. |
| [DrBibop #43: spectating](https://github.com/DrBibop/RoR2VRMod/issues/43) | Historische regressietest; huidige bug niet vastgesteld. | Waar ondersteund: join-in-progress, sterven, spectaten en herleven. Controleer juiste lokale camera. |
| [DrBibop #51: bewegingsvignette](https://github.com/DrBibop/RoR2VRMod/issues/51) | Featureverzoek, geen opgeloste bug. Bestaande vignette geldt voor specifieke mobility-abilities. Een nieuwe loop/sprintvignette valt buiten deze versie. | Bestaande vignette regressietesten; niet verwarren met echte foveated rendering. |

De vier recente forkissues hadden bij controle geen reacties, gekoppelde fixes of PR's. De originele GitHub-backlog is onvolledig: [#58 verwijst naar Trello](https://github.com/DrBibop/RoR2VRMod/issues/58), dat niet kon worden uitgelezen. [#70](https://github.com/DrBibop/RoR2VRMod/issues/70) en #71 documenteren de stilgevallen oorspronkelijke port. [PR #65](https://github.com/DrBibop/RoR2VRMod/pull/65) is een historische wijziging aan vliegbewegingen, geen oplossing die opnieuw blind toegepast moet worden.

## Een bevinding bijwerken

Noteer de exacte game/mod/Unity/Proton/SteamVR-versies, modprofiel, stappen, verwacht/werkelijk gedrag, log en eventuele stereo-opname. Gebruik status **onbevestigd**, **gereproduceerd**, **code aangepast, hertest nodig** of **op hardware geverifieerd**. Plaats geen laatste status op basis van alleen broninspectie of unit tests.
