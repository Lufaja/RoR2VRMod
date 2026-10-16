# Installeren en testen op Steam Machine

Deze route is experimenteel. [Valve Support](https://help.steampowered.com/en/faqs/view/2668-0B6E-79E0-A85B) vermeldt bij controle op 16 oktober 2026 dat volledige SteamVR/Frame-streamingondersteuning nog niet voor Steam Machine/Linux geldt. Valve adviseert Desktop Mode, SteamVR Beta en SteamVR handmatig starten. Voor de Frame-adapter noemt Valve SteamOS 3.9+ of Linux kernel 7.2+. Controleer deze voorwaarden opnieuw op de daadwerkelijke machine.

## Eerst de verbinding

1. Noteer SteamOS, Steam-client, SteamVR en Proton-versies. Stel de Steam Frame-adapter en headset in volgens Valve's handleiding.
2. Test eerst een bestaande VR-toepassing via dezelfde Steam Machine/Frame-verbinding. Als beeld/tracking daar al faalt, diagnoseer die verbinding vóór de mod.
3. Start de Steam-versie van RoR2 zonder mods via Proton en sluit netjes af. Bewaar een kopie van saves en bestaande launch options. Er is geen database-reset, game-downgrade of herinstallatie nodig voor dit project.

## Modprofiel

1. Installeer [r2modman uit de officiële releases](https://github.com/ebkr/r2modmanPlus/releases) en kies Risk of Rain 2, Steam en een nieuw profiel `RiskOfVR`.
2. Gebruik de Proton-launchmodus. Laat de manager de profielspecificieke Steam-launchopties genereren; behoud eventuele bestaande opties. Gebruik geen los `proton run`-script.
3. Installeer `bbepis-BepInExPack-5.4.2122` en `RiskofThunder-HookGenPatcher-1.2.9`, inclusief hun dependencies. Gebruik het profiel niet tegelijk met DrBibop VRMod of Resurrected VRMod: de plugin-ID is bewust hetzelfde.
4. Download de import-zip uit een geslaagde **Experimental reference build** onder GitHub Actions, of gebruik de lokaal gebouwde zip. In r2modman: Settings → Profile → Import local mod → selecteer `RiskOfVR-1.1.0-experimental.zip`. Naam `RiskOfVR`, auteur `Lufaja`, versie `1.1.0`. Een broncode-download van GitHub is geen installeerbare mod. Het importpakket is gecompileerd maar nog niet op de game/headset getest.
5. Start SteamVR en de Frame-stream eerst. Start RoR2 met de gemodificeerde launchopties vanuit r2modman/Steam. De Windows-game en Windows-XR-DLLs draaien onder Proton; plaats hier geen Linux `.so`-bestanden in.
6. Controleer `BepInEx/LogOutput.log` in het profiel. Lees de runtime-/controllerregels en installatiefouten. Bij zwart beeld niet eindeloos opnieuw starten: vergelijk eerst met de werkende baseline en controleer native bestanden/runtime.

De officiële Thunderstore Mod Manager is een Windows/Overwolf-app. Op SteamOS gebruiken we de afgesproken r2modman-route met hetzelfde Thunderstore-pakketformaat. Installatie en updaten gebeurt aanvankelijk in Desktop Mode. Starten vanuit Gaming Mode is een aparte, nog onbewezen test.

## Instellingen en herstel

De bestaande `com.DrBibop.VRMod` plugin-ID en configuratie blijven behouden. Gebruik de VR-tab of r2modman Config editor voor gameplaybindings en de eenmalige Steam Frame seated-preset. Menu-/systeemknoppen blijven beschikbaar. Configwijzigingen die een herstart vereisen worden als zodanig weergegeven.

Bindings, comfortinstellingen en de preset staan in `BepInEx/config/VRMod.cfg`;
de nieuwe camera-/diagnostiekopties in `BepInEx/config/com.DrBibop.VRMod.cfg`.

Een apart profiel is geen volledige isolatie: de patcher plaatst twee XR-DLLs en één subsystemmanifest in `Risk of Rain 2_Data`. De originele bestanden en hashregistratie staan onder `.vrmod-native` in die map. Bewaar die directory.

Sluit de game en schakel de mod/patcher in r2modman uit voordat je terugdraait.
Pak dezelfde import-zip uit in een aparte werkmap en open daar een terminal;
de onderstaande relatieve scriptnaam verwijst naar die uitgepakte map.

```sh
python3 tools/restore-native.py --game-data '/pad/naar/Risk of Rain 2/Risk of Rain 2_Data'
python3 tools/restore-native.py --game-data '/pad/naar/Risk of Rain 2/Risk of Rain 2_Data' --apply
```

De eerste opdracht toont alleen wat hersteld wordt. Herstel weigert bestanden die inmiddels door de game of een andere mod zijn gewijzigd. Verwijder geen backups om die controle te omzeilen. De herstelprocedure schakelt herinstallatie via deze patcher blijvend uit. Alleen het profiel aanzetten of de `disabled`-vlag wijzigen herstelt de installatie niet; een ondersteunde automatische herinschakelroute ontbreekt nog. Bewaar ledger en backups voor beoordeling vóór een latere herinstallatie.
