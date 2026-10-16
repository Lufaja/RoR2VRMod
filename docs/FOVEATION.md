# Foveatie: wat deze mod wel en niet doet

## Foveated streaming

Steam Frame/Steam verzorgen eye-tracked beeldcodering en verzending. Deze mod levert stereobeeld via OpenXR en implementeert geen encoder. Er is geen modinstelling die beweert foveated streaming in te schakelen of te bewijzen dat deze actief is. Stel streamingkwaliteit en refreshrate in Steam in en controleer de werkelijke verbinding op de host/headset.

Bron: [Valve Steam Frame](https://store.steampowered.com/hardware/steamframe). Dit is geen extra GPU-renderoptimalisatie in RoR2.

## Foveated rendering

Niet geïmplementeerd in Risk of VR 1.1.0. De upstream-mod gebruikt OpenXR MultiPass en de bestaande RoR2-renderpipeline. Valve's Unity-helper vereist onder andere Unity 2022.3+, URP en Vulkan; DXVK onder Proton maakt de game niet automatisch een Unity-Vulkan/URP-app. De exacte engine- en graphicsversies moeten uit de eigen installatie/logs worden vastgesteld; oude projectbestanden zijn daarvoor geen betrouwbare bron.

Bronnen: [Valve Unity-integratie](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity), [OpenXR-extensies](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom).

## Begrensde vervolgproef

Pas nadat de stereoacceptatie voor issue #71 slaagt: meet geldige eye-gaze, beschikbare extensies en de feitelijke GPU/backend op de Steam Machine. Kies dan een aantoonbaar ondersteunde renderintegratie en vergelijk GPU-frametijd, beeldkwaliteit en gaze-uitval met dezelfde scène zonder optimalisatie. Het loggen van een aanwezige gaze-extensie alleen bewijst geen functionele foveatie. Zonder werkende backend geen render-foveatieknop en geen performanceclaim.
