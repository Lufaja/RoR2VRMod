using BepInEx.Configuration;
using System.Collections.Generic;
using VRMod.Inputs;

namespace VRMod
{
    internal static class GameplayBindingConfig
    {
        private static readonly Dictionary<int, ConfigEntry<string>> entries = new Dictionary<int, ConfigEntry<string>>();
        private static ConfigEntry<bool> applyFramePreset;

        internal static void Init(ConfigFile file)
        {
            for (int i = 0; i < GameplayBindingCatalog.ActionIds.Length; i++)
            {
                int action = GameplayBindingCatalog.ActionIds[i];
                string name = GameplayBindingCatalog.ActionNames[i];
                ConfigEntry<string> entry = file.Bind("Gameplay bindings", name, "Default",
                    new ConfigDescription("Gameplay only. Default preserves the survivor and handedness mappings. Menu buttons and key-item shortcuts stay fixed. Sharing a button triggers all actions assigned to it.",
                        new AcceptableValueList<string>(GameplayBindingCatalog.Choices)));
                entries.Add(action, entry);
                entry.SettingChanged += (sender, args) => Controllers.RefreshGameplayBindings();
                ModConfig.settings.Add("vr_binding_" + action, new ModConfig.ConfigSetting(entry, ModConfig.ConfigSetting.SettingUpdate.Instant)
                {
                    choiceValues = GameplayBindingCatalog.Choices,
                    choiceLabels = GameplayBindingCatalog.Labels
                });
            }

            applyFramePreset = file.Bind("Controls", "Apply Steam Frame seated preset", false,
                "Set true to apply seated mode, right-handed controls, 30 degree snap turn, locked pitch and comfort vignette once. Automatically returns to false. Restart for seated mode. Other settings and gameplay bindings are preserved; this does not select an OpenXR runtime.");
            applyFramePreset.SettingChanged += (sender, args) => ApplyFramePreset();
            ModConfig.settings.Add("vr_frame_seated_preset", new ModConfig.ConfigSetting(applyFramePreset, ModConfig.ConfigSetting.SettingUpdate.AfterRestart));
            ApplyFramePreset();
        }

        internal static int Resolve(int actionId, int defaultElement)
        {
            return entries.TryGetValue(actionId, out ConfigEntry<string> entry)
                ? GameplayBindingCatalog.Resolve(entry.Value, defaultElement) : defaultElement;
        }

        private static void ApplyFramePreset()
        {
            if (!applyFramePreset.Value) return;
            ModConfig.SeatedMode.Value = true;
            ModConfig.LeftDominantHand.Value = false;
            ModConfig.SnapTurn.Value = true;
            ModConfig.SnapTurnAngle.Value = 30;
            ModConfig.LockedCameraPitch.Value = true;
            ModConfig.UseConfortVignette.Value = true;
            applyFramePreset.Value = false;
            VRMod.StaticLogger.LogInfo("[VR config] Applied Steam Frame seated comfort preset. Restart if seated mode changed; gameplay bindings were preserved.");
        }
    }
}
