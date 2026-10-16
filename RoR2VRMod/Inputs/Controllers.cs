using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Rewired;
using RoR2;
using RoR2.GamepadVibration;
using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using VRMod.Inputs;

namespace VRMod
{
    public class Controllers
    {
        private static CustomController vrControllers;
        private static CustomControllerMap vrGameplayMap;
        private static CustomControllerMap vrUIMap;

        private static bool initializedMainPlayer;
        private static bool initializedLocalUser;

        private static List<SkillBindingOverride> skillBindingOverrides = new List<SkillBindingOverride>()
        {
            new SkillBindingOverride("LoaderBody", SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Special, SkillSlot.Utility),
            new SkillBindingOverride("RailgunnerBody", SkillSlot.Primary, SkillSlot.Utility, SkillSlot.Special, SkillSlot.Secondary)
        };

        private static BaseInput[] inputs;
        private static List<BaseInput> modInputs = new List<BaseInput>();

        internal static int leftJoystickID { get; private set; }
        internal static int rightJoystickID { get; private set; }

        internal static int ControllerID => vrControllers.id;

        internal static void Init()
        {
            // Stop the new Input System from claiming the XR controllers as its own native devices;
            // the mod reads them through the legacy XR InputDevices API instead. If the InputSystem
            // build ever renames this method, don't let the whole input path die silently with it.
            try
            {
                var onNativeDeviceDiscovered = typeof(UnityEngine.InputSystem.InputManager).GetMethod("OnNativeDeviceDiscovered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (onNativeDeviceDiscovered != null)
                    new Hook(onNativeDeviceDiscovered, (Action<Action<UnityEngine.InputSystem.InputManager, int, string>, UnityEngine.InputSystem.InputManager, int, string>)DisableInputManager);
                else
                    VRMod.StaticLogger.LogWarning("[VR input] InputManager.OnNativeDeviceDiscovered not found; skipping the native-device suppression hook.");
            }
            catch (Exception e)
            {
                VRMod.StaticLogger.LogWarning($"[VR input] Failed to hook InputManager.OnNativeDeviceDiscovered (continuing anyway): {e.Message}");
            }

            VRInputDiagnostics.LogInteractionProfiles("Controllers.Init");
            VRInputDiagnostics.DumpDevices("Controllers.Init");

            ReInput.InputSourceUpdateEvent += UpdateVRInputs;

            RoR2Application.onUpdate += Update;

            On.RoR2.UI.InputBindingControl.Awake += DisableControllerBinds;

            On.RoR2.GamepadVibration.GamepadVibrationManager.Update += VRHaptics;

            IL.RoR2.PlayerCharacterMasterController.Update += ControllerMovementDirection;

            On.RoR2.UI.MainMenu.ProfileMainMenuScreen.SetMainProfile += (orig, self, profile) => 
            {
                orig(self, profile);
                if (initializedLocalUser)
                {
                    initializedLocalUser = false;
                    RoR2Application.onUpdate += Update;
                }
            };

            SetupControllerInputs();
        }

        private static void DisableInputManager(Action<UnityEngine.InputSystem.InputManager, int, string> orig, UnityEngine.InputSystem.InputManager self, int deviceId, string deviceDescriptor)
        {
            return;
        }

        private static void VRHaptics(On.RoR2.GamepadVibration.GamepadVibrationManager.orig_Update orig)
        {
            orig();

            if (Utils.localUserProfile == null || Utils.localCameraRig == null || Utils.localInputPlayer == null || Utils.localInputPlayer.controllers.GetLastActiveController() == null || Utils.localInputPlayer.controllers.GetLastActiveController().type != ControllerType.Custom) return;

            VibrationContext context = new VibrationContext();
            context.localUser = LocalUserManager.GetFirstLocalUser();
            context.cameraRigController = Utils.localCameraRig;
            context.userVibrationScale = Utils.localUserProfile.gamepadVibrationScale;

            float motorValue = context.CalcCamDisplacementMagnitude() * context.userVibrationScale;

            InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            HapticCapabilities capabilities;

            if (leftHand.TryGetHapticCapabilities(out capabilities))
            {
                if (capabilities.supportsImpulse)
                {
                    leftHand.SendHapticImpulse(0, motorValue, Time.deltaTime);
                }
            }
            if (rightHand.TryGetHapticCapabilities(out capabilities))
            {
                if (capabilities.supportsImpulse)
                {
                    rightHand.SendHapticImpulse(0, motorValue, Time.deltaTime);
                }
            }
        }

        [Obsolete("Deprecated. Use AddSkillBindingOverride instead.")]
        public static void AddSkillRemap(string bodyName, SkillSlot skill1, SkillSlot skill2)
        {
            if (skillBindingOverrides.Exists(x => x.bodyName == bodyName))
            {
                throw new ArgumentException("VR Mod: Cannot add multiple skill binding overrides on the same body.");
            }

            if (skill1 == SkillSlot.None || skill2 == SkillSlot.None)
            {
                throw new ArgumentException("VR Mod: Cannot override a skill binding with None.");
            }

            SkillSlot[] skillSlots = new SkillSlot[] { SkillSlot.Primary, SkillSlot.Secondary, SkillSlot.Utility, SkillSlot.Special };

            int skill1Index = Array.IndexOf(skillSlots, skill1);
            int skill2Index = Array.IndexOf(skillSlots, skill2);

            skillSlots[skill1Index] = skill2;
            skillSlots[skill2Index] = skill1;

            AddSkillBindingOverride(bodyName, skillSlots[0], skillSlots[1], skillSlots[2], skillSlots[3]);
        }

        public static void AddSkillBindingOverride(string bodyName, SkillSlot dominantTrigger, SkillSlot nonDominantTrigger, SkillSlot nonDominantGrip, SkillSlot dominantGrip)
        {
            if (skillBindingOverrides.Exists(x => x.bodyName == bodyName))
            {
                throw new ArgumentException("VR Mod: Cannot add multiple skill binding overrides on the same body.");
            }

            if (dominantTrigger == SkillSlot.None || nonDominantTrigger == SkillSlot.None || nonDominantGrip == SkillSlot.None || dominantGrip == SkillSlot.None)
            {
                throw new ArgumentException("VR Mod: Cannot override a skill binding with None.");
            }

            skillBindingOverrides.Add(new SkillBindingOverride(bodyName, dominantTrigger, nonDominantTrigger, nonDominantGrip, dominantGrip));
        }

        private static string activeBindingBody;
        private static readonly Dictionary<int, int> gameplayElements = new Dictionary<int, int>();

        internal static void ApplyRemaps(string bodyName)
        {
            activeBindingBody = bodyName;
            RefreshGameplayBindings();
        }

        internal static void RevertRemap()
        {
            activeBindingBody = null;
            RefreshGameplayBindings();
        }

        internal static void ChangeDominanceDependantMaps()
        {
            RefreshGameplayBindings();
        }

        internal static int GameplayElement(int actionId)
        {
            if (gameplayElements.TryGetValue(actionId, out int resolved)) return resolved;
            int[] slots = null;
            SkillBindingOverride binding = skillBindingOverrides.FirstOrDefault(x => x.bodyName == activeBindingBody);
            if (!string.IsNullOrEmpty(activeBindingBody) && binding.bodyName == activeBindingBody)
                slots = new[] { (int)binding.dominantTrigger, (int)binding.nonDominantTrigger, (int)binding.nonDominantGrip, (int)binding.dominantGrip };
            int original = GameplayBindingCatalog.DefaultElement(actionId, ModConfig.LeftDominantHand.Value, slots);
            resolved = GameplayBindingConfig.Resolve(actionId, original);
            gameplayElements[actionId] = resolved;
            return resolved;
        }

        /// <summary>Update gameplay maps only; UI actions keep their native physical buttons.</summary>
        internal static void RefreshGameplayBindings()
        {
            gameplayElements.Clear();
            if (vrControllers == null || vrGameplayMap == null) return;
            var maps = new HashSet<ControllerMap> { vrGameplayMap };
            Player main = LocalUserManager.GetRewiredMainPlayer();
            Player local = Utils.localInputPlayer;
            if (main != null) maps.Add(main.controllers.maps.GetMap(vrControllers, vrGameplayMap.id));
            if (local != null) maps.Add(local.controllers.maps.GetMap(vrControllers, vrGameplayMap.id));
            foreach (ControllerMap map in maps)
            {
                if (map == null) continue;
                foreach (int actionId in GameplayBindingCatalog.ActionIds)
                {
                    int element = GameplayElement(actionId);
                    foreach (ActionElementMap binding in map.GetElementMapsWithAction(actionId).ToArray())
                    {
                        // Synthetic sprint has its own element and never moves to a physical button.
                        if (binding.elementIdentifierId == GameplayBindingCatalog.AutoSprintElement || binding.elementIdentifierId == element) continue;
                        if (!map.ReplaceElementMap(binding.id, binding.actionId, binding.axisContribution, element, binding.elementType, binding.axisRange, binding.invert))
                            VRMod.StaticLogger.LogWarning($"[VR input] Could not update gameplay binding for action {actionId}.");
                    }
                }
            }
            foreach (int actionId in GameplayBindingCatalog.ActionIds)
                ControllerGlyphs.RegisterFallbackBindings(vrGameplayMap.GetElementMapsWithAction(actionId));
        }

        private static void ControllerMovementDirection(ILContext il)
        {
            ILCursor c = new ILCursor(il);

            // Find the locals by type instead of fixed slots: the Oct 2026 game update added three locals at the
            // start of PlayerCharacterMasterController.Update, shifting the camera Transform from 6 to 9 and the
            // two move-input Vector2s from 7/8 to 10/11.
            int transformLocal = -1, moveLocal = -1, moveLocal2 = -1;
            foreach (var v in il.Body.Variables)
            {
                string tn = v.VariableType.FullName;
                if (transformLocal < 0 && tn == "UnityEngine.Transform") transformLocal = v.Index;
                else if (tn == "UnityEngine.Vector2") { if (moveLocal < 0) moveLocal = v.Index; else if (moveLocal2 < 0) moveLocal2 = v.Index; }
            }
            if (transformLocal < 0 || moveLocal < 0 || moveLocal2 < 0)
            {
                VRMod.StaticLogger.LogWarning($"[VR hooks] ControllerMovementDirection: locals not found (T={transformLocal}, V2={moveLocal}/{moveLocal2}) - movement follows the head");
                return;
            }
            var moveVar = il.Body.Variables[moveLocal];

            c.GotoNext(x => x.MatchStloc(transformLocal));
            c.EmitDelegate<Func<Transform, Transform>>((headTransform) =>
            {
                if (!ModConfig.ControllerMovementDirection.Value) return headTransform;

                if (MotionControls.HandsReady)
                {
                    // If controller movement tracking is enabled, replace the base camera transform with the controller transform.
                    return MotionControls.GetHandByDominance(false).muzzle.transform;
                }
                else
                {
                    // See below for handling of the case where hands are not ready.
                    return headTransform;
                }
            });

            c.GotoNext(x => x.MatchLdloca(moveLocal2));

            c.Emit(OpCodes.Ldloc, moveVar);
            c.EmitDelegate<Func<Vector2, Vector2>>((vector) =>
            {
                if (!ModConfig.ControllerMovementDirection.Value || MotionControls.HandsReady) return vector;

                // Special case only for if hands are not ready; in this case, we can't assume a hand transform exists, so we fall back to old logic.
                // Note: this will only provide y-axis rotation (yaw); z-axis rotation (pitch) will still be controlled by the head.
                
                Quaternion controllerRotation = InputTracking.GetLocalRotation(XRNode.LeftHand);
                Quaternion headRotation = Camera.main.transform.localRotation;

                float angleDifference = headRotation.eulerAngles.y - controllerRotation.eulerAngles.y;

                return Quaternion.Euler(new Vector3(0, 0, angleDifference)) * vector;
            });
            c.Emit(OpCodes.Stloc, moveVar);

            c.GotoNext(x => x.MatchCallvirt<Transform>("get_right"));
            c.Index += 1;
            c.EmitDelegate<Func<Vector3, Vector3>>((vector) =>
            {
                if (!ModConfig.ControllerMovementDirection.Value || !MotionControls.HandsReady) return vector;
                // In flight mode, clamp the controller's left-right vector to the xz plane (normal to Vector3.up) so that only pitch and yaw are affected, not roll.
                return Vector3.ProjectOnPlane(vector, Vector3.up).normalized * vector.magnitude;
            });
        }

        private static void DisableControllerBinds(On.RoR2.UI.InputBindingControl.orig_Awake orig, InputBindingControl self)
        {
            orig(self);

            if (ModConfig.InitialMotionControlsValue && self.inputSource == MPEventSystem.InputSource.Gamepad && self.button)
            {
                self.button.interactable = false;
                self.button = null;
            }
        }

        private static void SetupControllerInputs()
        {
            vrControllers = RewiredAddons.CreateRewiredController();
            vrUIMap = RewiredAddons.CreateUIMap(vrControllers.id);
            vrGameplayMap = RewiredAddons.CreateGameplayMap(vrControllers.id);

            inputs = new BaseInput[]
            {
                new VectorInput(XRNode.LeftHand, InputHelpers.Axis2D.PrimaryAxis2D, 0, 1),
                new VectorInput(XRNode.RightHand, InputHelpers.Axis2D.PrimaryAxis2D, 2, 3),
                new VectorInput(XRNode.LeftHand, InputHelpers.Axis2D.PrimaryAxis2D, 4, 5),
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.PrimaryButton, 6, ViveOverride.PadClickDown),
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.SecondaryButton, 7, ViveOverride.PadClickUp),
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.TriggerButton, 8),
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.TriggerButton, 9),
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.GripButton, 10),
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.GripButton, 11),
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.PrimaryButton, 12, ViveOverride.PadClickUp),
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.Primary2DAxisClick, 13, ViveOverride.PadClickDown),
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.Primary2DAxisClick, 14, ViveOverride.MenuButton),
                new HoldableButtonInput(XRNode.LeftHand, InputHelpers.Button.SecondaryButton, 15, ViveOverride.MenuButton),
                // These elements MUST match ControllerGlyphs.standardGlyphs, which is indexed by element
                // id and is what the game draws in every on-screen prompt. The OpenXR port wired them to
                // the wrong physical buttons: the glyph art says 17=A, 19=X, 20/21=triggers, 22/23=grips,
                // but 17 was the right trigger, 19 was A, and the triggers and grips were swapped. So the
                // footer prompts advertised buttons that did something else - "left/right trigger" was
                // drawn for tab left/right while the triggers actually fired submenu left/right. Bound
                // the way the glyphs describe, every prompt now names the button that really does it.
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.PrimaryButton, 17, ViveOverride.PadClickDown),      // Submit (A)
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.SecondaryButton, 18, ViveOverride.PadClickUp),    // Cancel (B)
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.PrimaryButton, 19, ViveOverride.PadClickUp),       // Ready / alt submit (X)
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.TriggerButton, 20),       // Tab left (left trigger)
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.TriggerButton, 21),      // Tab right (right trigger)
                new ButtonInput(XRNode.LeftHand, InputHelpers.Button.GripButton, 22),          // Submenu left (left grip)
                new ButtonInput(XRNode.RightHand, InputHelpers.Button.GripButton, 23),         // Submenu right (right grip)
                new TapButtonInput(XRNode.LeftHand, InputHelpers.Button.SecondaryButton, 24, ViveOverride.MenuButton)      // Pause menu - quick press-and-RELEASE on Y (hold = scoreboard, element 15)
            };
        }

        private static void Update()
        {
            if (!initializedMainPlayer)
            {
                if (AddVRController(LocalUserManager.GetRewiredMainPlayer()))
                    initializedMainPlayer = true;
            }

            LocalUser localUser = LocalUserManager.GetFirstLocalUser();

            if (localUser != null)
            {
                if (AddVRController(localUser.inputPlayer))
                {
                    initializedLocalUser = true;
                    RoR2Application.onUpdate -= Update;
                }
            }
        }

        internal static bool AddVRController(Player inputPlayer)
        {
            if (!inputPlayer.controllers.ContainsController(vrControllers))
            {
                inputPlayer.controllers.AddController(vrControllers, false);
                vrControllers.enabled = true;
                VRInputDiagnostics.OnControllerAddedToPlayer(inputPlayer.name);
            }

            if (inputPlayer.controllers.maps.GetAllMaps(ControllerType.Custom).ToList().Count < 2)
            {
                if (inputPlayer.controllers.maps.GetMap(ControllerType.Custom, vrControllers.id, 2, 0) == null)
                    inputPlayer.controllers.maps.AddMap(vrControllers, vrUIMap);
                if (inputPlayer.controllers.maps.GetMap(ControllerType.Custom, vrControllers.id, 0, 0) == null)
                    inputPlayer.controllers.maps.AddMap(vrControllers, vrGameplayMap);
                if (!vrGameplayMap.enabled)
                    vrGameplayMap.enabled = true;
                if (!vrUIMap.enabled)
                    vrUIMap.enabled = true;
            }

            return inputPlayer.controllers.ContainsController(vrControllers) && inputPlayer.controllers.maps.GetAllMaps(ControllerType.Custom).ToList().Count >= 2;
        }

        private static float suppressGameInputUntil;
        private static bool suppressUntilReleased;

        /// <summary>Mutes all VR->game input until the given unscaled time.</summary>
        internal static void SuppressUntil(float time)
        {
            if (time > suppressGameInputUntil) suppressGameInputUntil = time;
        }

        /// <summary>
        /// Mutes all VR->game input until the given time AND until every face button and stick
        /// click is physically released. A fixed delay alone was not enough: when the press that
        /// closed the keyboard outlasted it, the game saw the still-held button as a fresh press.
        /// </summary>
        internal static void SuppressUntilReleased(float time)
        {
            SuppressUntil(time);
            suppressUntilReleased = true;
        }

        private static bool AnyFaceButtonHeld()
        {
            UnityEngine.XR.InputDevice l = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            UnityEngine.XR.InputDevice r = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            bool v;
            if (l.isValid)
            {
                if (l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out v) && v) return true;
                if (l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out v) && v) return true;
                if (l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out v) && v) return true;
            }
            if (r.isValid)
            {
                if (r.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out v) && v) return true;
                if (r.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out v) && v) return true;
                if (r.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out v) && v) return true;
            }
            return false;
        }

        private static void UpdateVRInputs()
        {
            // NOTE: the old "Use Oculus mode" path used to live here. It scanned Unity's LEGACY
            // joystick names for ones containing "left"/"right" and bailed out entirely when it found
            // none - which under OpenXR is always, so switching that option on silently killed every
            // VR input. It is meaningless now: OpenXR talks to whichever runtime is active (SteamVR,
            // Oculus/Meta, VDXR, WMR) with no per-vendor code path, so the branch is gone.

            // In the lobby, the Y button should trigger Load (element 32) instead of
            // Pause (24) and Info/Scoreboard (15).  We must prevent those elements from
            // ever being set — clearing them after the fact doesn't work because Rewired
            // latches the button-down edge the instant SetButtonValueById is called, so
            // the pause menu flickers open even if we immediately clear element 24.
            bool lobbyActive = LobbyNavigation.IsInLobby;

            // The VR keyboard owns the controllers while it is open (and for a moment after it
            // closes, so the closing press does not leak into the menu underneath).
            if (suppressUntilReleased && Time.unscaledTime >= suppressGameInputUntil)
            {
                if (AnyFaceButtonHeld()) suppressGameInputUntil = Time.unscaledTime + 0.1f;
                else suppressUntilReleased = false;
            }
            if (VRKeyboard.IsOpen || Time.unscaledTime < suppressGameInputUntil)
            {
                for (int id = 0; id <= 5; id++) vrControllers.SetAxisValueById(id, 0f);
                for (int id = 6; id <= GameplayBindingCatalog.AutoSprintElement; id++) vrControllers.SetButtonValueById(id, false);
                Inputs.KeyItemCombo.Reset();
                autoSprint.Reset();
                foreach (BaseInput input in inputs) input.ResetState();
                VRKeyboard.Tick();
                return;
            }

            // Key item combo (left grip + right A / right stick) decides first - see KeyItemCombo.
            Inputs.KeyItemCombo.Poll();

            foreach (BaseInput input in inputs)
            {
                if (lobbyActive && input is Inputs.ButtonInput bi && (bi.ElementId == 24 || bi.ElementId == 15))
                {
                    // Suppress before polling: clearing after UpdateValues would latch a false press.
                    input.ResetState();
                    vrControllers.SetButtonValueById(bi.ElementId, false);
                    continue;
                }
                if (Inputs.KeyItemCombo.Owns(input))
                    continue;
                input.UpdateValues(vrControllers);
            }

            Inputs.KeyItemCombo.Apply(vrControllers);

            foreach (BaseInput input in modInputs)
            {
                input.UpdateValues(vrControllers);
            }

            // With elements 15 and 24 skipped above, poll Y directly for Load.
            if (lobbyActive)
            {
                InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                if (leftHand.isValid && leftHand.IsPressed(InputHelpers.Button.SecondaryButton, out bool yDown) && yDown)
                    vrControllers.SetButtonValueById(32, true);
                else
                    vrControllers.SetButtonValueById(32, false);
            }
            else vrControllers.SetButtonValueById(32, false);

            ApplyAutoSprint();

            LobbyNavigation.Poll();
            SettingsTabs.Poll();

            VRInputDiagnostics.Tick(inputs);
        }

        private static readonly AutoSprintPulse autoSprint = new AutoSprintPulse();

        /// <summary>
        /// Ground movement can request a fresh Sprint edge after an ability cancels sprinting.
        /// Flying bodies use Sprint for descent, so they only receive the physical binding.
        /// A separate virtual element prevents automatic sprint from pressing another action
        /// when a user assigns that action to the original sprint button.
        /// </summary>
        private static void ApplyAutoSprint()
        {
            if (vrControllers == null) return;
            CharacterBody body = Utils.localBody;
            InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            Vector2 stick = Vector2.zero;
            bool moving = leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick) && stick.sqrMagnitude > 0.04f;
            bool eligible = ModConfig.AutoSprint.Value && RoR2.Run.instance && body && moving;
            if (eligible && (IsAnySkillButtonHeld() || IsSkillStateBusy(body)))
                skillBusyUntil = Time.unscaledTime + 0.2f;
            eligible = eligible && Time.unscaledTime >= skillBusyUntil && !IsGameplayButtonHeld(18);
            bool pressed = autoSprint.Poll(Time.unscaledTime, eligible, IsFlier(), body && body.isSprinting);
            vrControllers.SetButtonValueById(GameplayBindingCatalog.AutoSprintElement, pressed);
        }

        private static float skillBusyUntil;
        private static RoR2.CharacterBody skillMachinesBody;
        private static readonly List<EntityStateMachine> skillMachines = new List<EntityStateMachine>();

        /// <summary>
        /// True while one of the character's skill state machines ("Weapon", "Weapon2") is running
        /// a state other than its idle/main state - i.e. an attack is winding up, firing or
        /// recovering. Machines such as "Body" (movement) or "Stance" (MUL-T, permanently
        /// non-idle) are deliberately not consulted.
        /// </summary>
        private static bool IsSkillStateBusy(RoR2.CharacterBody body)
        {
            if (!body) return false;
            if (skillMachinesBody != body)
            {
                skillMachinesBody = body;
                skillMachines.Clear();
                foreach (EntityStateMachine m in body.GetComponents<EntityStateMachine>())
                {
                    if (m && (m.customName == "Weapon" || m.customName == "Weapon2"))
                        skillMachines.Add(m);
                }
            }
            for (int i = 0; i < skillMachines.Count; i++)
            {
                EntityStateMachine m = skillMachines[i];
                if (!m || m.state == null) continue;
                Type main = m.mainStateType.stateType;
                if (main != null && m.state.GetType() != main) return true;
            }
            return false;
        }

        private static bool IsAnySkillButtonHeld()
        {
            for (int action = 7; action <= 10; action++)
                if (IsGameplayButtonHeld(action)) return true;
            return false;
        }

        private static bool IsGameplayButtonHeld(int actionId)
        {
            int element = GameplayElement(actionId);
            foreach (BaseInput input in inputs)
                if (input is ButtonInput button && button.ElementId == element)
                    return button.IsHeld;
            return false;
        }

        private static bool IsFlier()
        {
            try
            {
                RoR2.LocalUser user = RoR2.LocalUserManager.GetFirstLocalUser();
                RoR2.PlayerCharacterMasterController controller = user != null ? user.cachedMasterController : null;
                return controller && controller.bodyIsFlier;
            }
            catch
            {
                return false;
            }
        }

        public struct SkillBindingOverride
        {
            public string bodyName;
            public SkillSlot dominantTrigger;
            public SkillSlot nonDominantTrigger;
            public SkillSlot nonDominantGrip;
            public SkillSlot dominantGrip;

            public SkillBindingOverride(string bodyName, SkillSlot dominantTrigger, SkillSlot nonDominantTrigger, SkillSlot nonDominantGrip, SkillSlot dominantGrip)
            {
                this.bodyName = bodyName;
                this.dominantTrigger = dominantTrigger;
                this.nonDominantTrigger = nonDominantTrigger;
                this.nonDominantGrip = nonDominantGrip;
                this.dominantGrip = dominantGrip;
            }
        }
    }
}
