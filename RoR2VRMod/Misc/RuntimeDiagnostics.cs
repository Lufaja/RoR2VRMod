using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace VRMod
{
    /// <summary>Bounded diagnostics, never a substitute for an in-headset stereo test.</summary>
    internal static class RuntimeDiagnostics
    {
        private static ConfigEntry<bool> summary;
        private static ConfigEntry<bool> pickerEvents;
        private const int MaxPickerSamples = 120;
        private static int pickerSamples;
        private static readonly Dictionary<int, float> pickerOpenedAt = new Dictionary<int, float>();

        internal static void Init(ConfigFile config)
        {
            summary = config.Bind("Diagnostics", "Runtime summary", true,
                "Log game/Unity/graphics/OpenXR versions once at startup. Does not enable streaming or foveated rendering.");
            pickerEvents = config.Bind("Diagnostics", "Picker events", false,
                "Log at most 120 world item-picker open/close events per process to investigate upstream issues #1 and #4.");
        }

        internal static void LogRuntime()
        {
            if (summary == null || !summary.Value) return;
            try
            {
                bool protonEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH"));
                VRMod.StaticLogger.LogInfo($"[Risk of VR] game={Application.version}; game assembly={typeof(RoR2Application).Assembly.GetName().Version}; Unity={Application.unityVersion}; OS reported by game={SystemInfo.operatingSystem}; Proton environment detected={protonEnvironment}.");
                VRMod.StaticLogger.LogInfo($"[Risk of VR] graphics={SystemInfo.graphicsDeviceType}; GPU={SystemInfo.graphicsDeviceName}; XR enabled={XRSettings.enabled}; device active={XRSettings.isDeviceActive}; stereo={XRSettings.stereoRenderingMode}; per-eye texture={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}.");
                VRMod.StaticLogger.LogInfo($"[Risk of VR] OpenXR runtime={OpenXRRuntime.name} {OpenXRRuntime.version}; Frame extension enabled={OpenXRRuntime.IsExtensionEnabled("XR_VALVE_frame_controller_interaction")}; eye-gaze extension enabled={OpenXRRuntime.IsExtensionEnabled("XR_EXT_eye_gaze_interaction")}.");
                VRMod.StaticLogger.LogInfo("[Risk of VR] Streaming/foveated-streaming state is managed by Steam and is not verified by this mod. Foveated rendering is not implemented. Correct stereo and Steam Machine compatibility require an in-headset test.");
            }
            catch (Exception e)
            {
                VRMod.StaticLogger.LogWarning("[Risk of VR] Runtime diagnostic unavailable: " + e.Message);
            }
        }

        internal static void PickerEvent(bool opened, PickupPickerController picker, LocalUser user)
        {
            if (pickerEvents == null || !pickerEvents.Value || !picker || pickerSamples >= MaxPickerSamples) return;
            // Count attempts, including failures, so a broken lifecycle cannot flood the log.
            pickerSamples++;
            try
            {
                int id = picker.GetInstanceID();
                float duration = 0;
                if (opened)
                {
                    // A broken lifecycle must not grow the diagnostic bookkeeping indefinitely.
                    if (pickerOpenedAt.Count >= 32) pickerOpenedAt.Clear();
                    pickerOpenedAt[id] = Time.unscaledTime;
                }
                else
                {
                    if (pickerOpenedAt.TryGetValue(id, out float start)) duration = Time.unscaledTime - start;
                    pickerOpenedAt.Remove(id);
                }
                var eventSystem = user?.eventSystem;
                string selected = eventSystem && eventSystem.currentSelectedGameObject ? eventSystem.currentSelectedGameObject.name : "none";
                VRMod.StaticLogger.LogInfo($"[Risk of VR picker] {(opened ? "open" : "close")} id={id}; object={picker.name}; duration={duration:F2}s; selected={selected}; paused={PauseManager.isPaused}. Close cause is not supplied by this hook.");
            }
            catch (Exception e)
            {
                VRMod.StaticLogger.LogWarning("[Risk of VR picker] Diagnostic unavailable: " + e.Message);
            }
            finally
            {
                if (pickerSamples >= MaxPickerSamples) pickerOpenedAt.Clear();
            }
        }
    }
}
