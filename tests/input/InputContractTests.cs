using System;
using System.Collections.Generic;
using VRMod.Inputs;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

// These stand-ins deliberately model only the XR readers / Rewired setters used by the input
// adapters. They verify the production adapter code, not compatibility with a game assembly.
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
    }
    public static class Time
    {
        public static float realtimeSinceStartup;
        public static float unscaledTime => realtimeSinceStartup;
    }
    public static class Mathf { public static float Abs(float value) => Math.Abs(value); }
}
namespace UnityEngine.XR
{
    public enum XRNode { LeftHand, RightHand }
    public sealed class FakeDevice
    {
        public bool valid = true;
        public bool pressed;
        public Vector2 axis;
    }
    public struct InputDevice
    {
        internal FakeDevice data;
        public bool isValid => data != null && data.valid;
        public bool TryGetFeatureValue(int usage, out Vector2 value)
        { value = isValid ? data.axis : Vector2.zero; return isValid; }
    }
    public static class CommonUsages { public const int primary2DAxis = 0; }
    public static class InputDevices
    {
        public static readonly FakeDevice Left = new FakeDevice(), Right = new FakeDevice();
        public static InputDevice GetDeviceAtXRNode(XRNode node) => new InputDevice { data = node == XRNode.LeftHand ? Left : Right };
    }
}
namespace UnityEngine.XR.Interaction.Toolkit
{
    public static class InputHelpers
    {
        public enum Button { PrimaryButton, SecondaryButton, MenuButton, TriggerButton, GripButton, Primary2DAxisClick }
        public enum Axis2D { PrimaryAxis2D }
        public static bool IsPressed(this InputDevice device, Button button, out bool value)
        { value = device.isValid && device.data.pressed; return device.isValid; }
        public static bool TryReadAxis2DValue(this InputDevice device, Axis2D axis, out Vector2 value)
        { value = device.isValid ? device.data.axis : Vector2.zero; return device.isValid; }
    }
}
namespace Rewired
{
    public sealed class CustomController
    {
        public readonly Dictionary<int, bool> Buttons = new Dictionary<int, bool>();
        public readonly Dictionary<int, float> Axes = new Dictionary<int, float>();
        public readonly HashSet<int> LatchedPresses = new HashSet<int>();
        public void SetButtonValueById(int id, bool value)
        {
            if (value && (!Buttons.TryGetValue(id, out bool previous) || !previous)) LatchedPresses.Add(id);
            Buttons[id] = value;
        }
        public void SetAxisValueById(int id, float value) => Axes[id] = value;
    }
}
namespace VRMod.Inputs
{
    internal static class KeyItemIndicator
    {
        internal static void Show(bool visible, RoR2.KeyItemSlot slot) { }
        internal static void Flash(RoR2.KeyItemSlot slot, string message) { }
    }
    internal enum ViveOverride { None, MenuButton, PadClickUp, PadClickDown }
    internal static class ViveControllers
    {
        internal static bool IsTrackpadWand => false;
        internal static bool PadClickUp(InputDevice device) => false;
        internal static bool PadClickDown(InputDevice device) => false;
    }
}

namespace RoR2
{
    public enum KeyItemIndex { None, TestItem }
    public sealed class KeyItemSlot
    {
        public KeyItemIndex keyItem;
        public static implicit operator bool(KeyItemSlot slot) => slot != null;
    }
    public sealed class CharacterBody
    {
        public KeyItemSlot keyItemSlot;
        public static implicit operator bool(CharacterBody body) => body != null;
    }
    public sealed class LocalUser { public CharacterBody cachedBody; }
    public static class LocalUserManager
    {
        public static LocalUser User;
        public static LocalUser GetFirstLocalUser() => User;
    }
}

public static class InputContractTests
{
    private static int assertions;
    private static void Check(bool passed, string message)
    {
        assertions++;
        if (!passed) throw new Exception(message);
    }
    private static void Equal(int actual, int expected, string message) => Check(actual == expected, message + $" ({actual} != {expected})");

    public static string Run()
    {
        assertions = 0;
        MappingPreservesDefaultsAndOverrides();
        AutoSprintDoesNotManufactureDroneDescent();
        InterruptedGesturesDoNotProduceMenuPresses();
        DisconnectReleasesProductionAdapters();
        CancelledComboKeepsOwnershipUntilRelease(true);
        CancelledComboKeepsOwnershipUntilRelease(false);
        return $"PASS: {assertions} input assertions (pure rules, production adapters and key-item pipeline with XR/Rewired test doubles). No game or hardware validation.";
    }

    private static void MappingPreservesDefaultsAndOverrides()
    {
        int[] expected = { 6, 7, 12, 8, 9, 10, 11, 13, 14 };
        for (int i = 0; i < GameplayBindingCatalog.ActionIds.Length; i++)
        {
            int original = GameplayBindingCatalog.DefaultElement(GameplayBindingCatalog.ActionIds[i], false);
            Equal(GameplayBindingCatalog.Resolve("Default", original), expected[i], "Existing default action mapping");
            Equal(GameplayBindingCatalog.Resolve("invalid", original), expected[i], "Invalid config falls back safely");
        }
        int[] left = { 9, 8, 11, 10 };
        for (int i = 0; i < 4; i++) Equal(GameplayBindingCatalog.DefaultElement(7 + i, true), left[i], "Left-handed skill mapping");
        // Loader uses dominant grip for Utility and non-dominant grip for Special.
        int[] loaderSlots = { 0, 1, 3, 2 };
        Equal(GameplayBindingCatalog.DefaultElement(9, false, loaderSlots), 11, "Loader Utility on right grip");
        Equal(GameplayBindingCatalog.DefaultElement(10, true, loaderSlots), 11, "Left-handed Loader Special on right grip");
        // Railgunner puts Secondary on dominant grip and Utility on non-dominant trigger.
        int[] railgunnerSlots = { 0, 2, 3, 1 };
        Equal(GameplayBindingCatalog.DefaultElement(8, false, railgunnerSlots), 11, "Railgunner Secondary on right grip");
        Equal(GameplayBindingCatalog.DefaultElement(9, false, railgunnerSlots), 9, "Railgunner Utility on left trigger");
        Equal(GameplayBindingCatalog.Resolve("LeftPrimary", GameplayBindingCatalog.DefaultElement(8, true, railgunnerSlots)), 12, "Explicit binding survives survivor and handedness changes");
        Equal(GameplayBindingCatalog.DefaultElement(8, false), 9, "Leaving a survivor restores default skill mapping");
        for (int i = 1; i < GameplayBindingCatalog.Choices.Length; i++)
        {
            int element = GameplayBindingCatalog.Resolve(GameplayBindingCatalog.Choices[i], -1);
            Check(element >= 6 && element <= 14, "Config cannot select UI, pause, combo or synthetic elements");
            Check(element != GameplayBindingCatalog.AutoSprintElement, "Synthetic sprint cannot be rebound as a physical action");
        }
    }

    private static void AutoSprintDoesNotManufactureDroneDescent()
    {
        var pulse = new AutoSprintPulse();
        Check(pulse.Poll(1f, true, false, false), "Ground movement starts sprint");
        Check(!pulse.Poll(1.02f, true, false, false), "Pulse releases before another edge");
        Check(!pulse.Poll(1.05f, true, false, false), "No pulse before cooldown");
        Check(pulse.Poll(1.2f, true, false, false), "Ground sprint can resume after a cancellation");
        Check(!pulse.Poll(1.4f, true, false, true), "Do not toggle an active sprint off");
        Check(!pulse.Poll(1.6f, false, false, false), "Skills / idle / disabled setting suppress auto sprint");
        for (int i = 0; i < 20; i++) Check(!pulse.Poll(2f + i, true, true, false), "Flying movement never synthesizes descent");
        Check(pulse.Poll(24f, true, false, false), "Ground sprint works after leaving the drone");
        pulse.Reset();
        Check(!pulse.Poll(0.01f, true, false, false), "Reset clears old timing state");
    }

    private static void InterruptedGesturesDoNotProduceMenuPresses()
    {
        var tap = new ButtonGestureState();
        var hold = new ButtonGestureState();
        Check(!tap.PollTap(true, true, 1f), "Press does not immediately pause");
        Check(!hold.PollHold(true, true, 1f), "Press does not immediately show scoreboard");
        Check(tap.PollTap(true, false, 1.1f), "A short release pauses");
        Check(!tap.PollTap(true, false, 1.2f), "Pause pulse expires");
        Check(!tap.PollTap(true, true, 2f), "New long press starts without pause");
        Check(!hold.PollHold(true, false, 1.2f), "Released scoreboard stays off");
        Check(!hold.PollHold(true, true, 2f), "New hold starts its own timer");
        Check(hold.PollHold(true, true, 2.5f), "Long hold shows scoreboard");
        Check(!tap.PollTap(true, false, 2.5f), "Long hold release does not pause");
        tap.PollTap(true, true, 3f);
        Check(!tap.PollTap(false, false, 3.1f), "Disconnect is not a short release");
        Check(!tap.PollTap(true, false, 3.2f), "Reconnect does not replay an interrupted tap");
        tap.PollTap(true, true, 4f);
        tap.Reset();
        Check(!tap.PollTap(true, false, 4.1f), "Lobby / keyboard suppression cancels a pending tap");
        Check(!hold.PollHold(false, false, 4.1f), "Disconnect releases scoreboard");
        Check(!hold.PollHold(true, true, 4.2f), "Reconnect must begin a fresh hold");
    }

    private static void DisconnectReleasesProductionAdapters()
    {
        var controller = new Rewired.CustomController();
        var button = new ButtonInput(XRNode.RightHand, InputHelpers.Button.PrimaryButton, 6);
        var vector = new VectorInput(XRNode.LeftHand, InputHelpers.Axis2D.PrimaryAxis2D, 0, 1);
        var tap = new TapButtonInput(XRNode.LeftHand, InputHelpers.Button.SecondaryButton, 24);
        var hold = new HoldableButtonInput(XRNode.LeftHand, InputHelpers.Button.SecondaryButton, 15);
        InputDevices.Right.valid = true;
        InputDevices.Right.pressed = true;
        button.UpdateValues(controller);
        Check(controller.Buttons[6], "Production Interact adapter forwards a press");
        InputDevices.Right.valid = false;
        button.UpdateValues(controller);
        Check(!controller.Buttons[6], "Disconnected Interact must release");
        InputDevices.Left.valid = true;
        InputDevices.Left.axis = new Vector2(.8f, -.5f);
        vector.UpdateValues(controller);
        Check(controller.Axes[0] == .8f && controller.Axes[1] == -.5f, "Production axes forward movement");
        InputDevices.Left.valid = false;
        vector.UpdateValues(controller);
        Check(controller.Axes[0] == 0 && controller.Axes[1] == 0, "Disconnected sticks return to neutral");
        InputDevices.Left.valid = true;
        InputDevices.Left.pressed = true;
        Time.realtimeSinceStartup = 10f;
        tap.UpdateValues(controller);
        hold.UpdateValues(controller);
        InputDevices.Left.valid = false;
        Time.realtimeSinceStartup = 10.1f;
        controller.LatchedPresses.Clear();
        tap.UpdateValues(controller);
        hold.UpdateValues(controller);
        Check(!controller.Buttons[24] && !controller.LatchedPresses.Contains(24), "No latched pause edge on disconnect");
        Check(!controller.Buttons[15], "No held scoreboard on disconnect");
    }

    private static void CancelledComboKeepsOwnershipUntilRelease(bool disconnectLeft)
    {
        string side = disconnectLeft ? "left disconnect" : "right disconnect";
        var controller = new Rewired.CustomController();
        var grip = new ButtonInput(XRNode.LeftHand, InputHelpers.Button.GripButton, 10);
        var interact = new ButtonInput(XRNode.RightHand, InputHelpers.Button.PrimaryButton, 6);
        RoR2.LocalUserManager.User = new RoR2.LocalUser
        {
            cachedBody = new RoR2.CharacterBody { keyItemSlot = new RoR2.KeyItemSlot { keyItem = RoR2.KeyItemIndex.TestItem } }
        };
        InputDevices.Left.valid = InputDevices.Right.valid = true;
        InputDevices.Left.pressed = InputDevices.Right.pressed = false;
        InputDevices.Left.axis = InputDevices.Right.axis = Vector2.zero;
        Time.realtimeSinceStartup = 30f;
        KeyItemCombo.Reset();
        TickComboPipeline(controller, grip, interact);

        InputDevices.Left.pressed = true;
        TickComboPipeline(controller, grip, interact);
        Check(KeyItemCombo.Owns(grip) && KeyItemCombo.Owns(interact), side + ": modifier owns grip and interact before normal polling");
        InputDevices.Right.pressed = true;
        TickComboPipeline(controller, grip, interact);
        Check(controller.LatchedPresses.Contains(KeyItemCombo.UseElement), side + ": combo actually uses key item");
        Check(!controller.LatchedPresses.Contains(6) && !controller.LatchedPresses.Contains(10), side + ": combo does not fire regular actions");

        if (disconnectLeft) InputDevices.Left.valid = false;
        else InputDevices.Right.valid = false;
        for (int i = 0; i < 3; i++)
        {
            TickComboPipeline(controller, grip, interact);
            Check(KeyItemCombo.Owns(grip) && KeyItemCombo.Owns(interact), side + ": cancellation retains ownership, including the valid held controller");
            Check(!controller.Buttons[6] && !controller.Buttons[10], side + ": normal actions remain released");
            Check(!controller.LatchedPresses.Contains(6) && !controller.LatchedPresses.Contains(10), side + ": no Rewired edge can leak before Apply");
            Check(!controller.Buttons[KeyItemCombo.UseElement], side + ": interrupted key-item pulse is cancelled");
        }

        InputDevices.Left.valid = InputDevices.Right.valid = true;
        TickComboPipeline(controller, grip, interact);
        Check(!controller.LatchedPresses.Contains(6) && !controller.LatchedPresses.Contains(10), side + ": reconnect while held remains suppressed");
        InputDevices.Left.pressed = false;
        TickComboPipeline(controller, grip, interact);
        Check(!controller.LatchedPresses.Contains(10), side + ": cancelled grip release cannot fire the deferred skill");
        Check(KeyItemCombo.Owns(interact) && !controller.LatchedPresses.Contains(6), side + ": A remains consumed until its own release");
        InputDevices.Right.pressed = false;
        TickComboPipeline(controller, grip, interact);
        Check(!KeyItemCombo.Owns(grip) && !KeyItemCombo.Owns(interact), side + ": physical releases end cancellation ownership");
        InputDevices.Right.pressed = true;
        TickComboPipeline(controller, grip, interact);
        Check(controller.LatchedPresses.Contains(6), side + ": a new deliberate A press interacts normally");
        InputDevices.Right.pressed = false;
        TickComboPipeline(controller, grip, interact);
    }

    private static void TickComboPipeline(Rewired.CustomController controller, ButtonInput grip, ButtonInput interact)
    {
        Time.realtimeSinceStartup += .02f;
        controller.LatchedPresses.Clear();
        // Same order as Controllers.UpdateVRInputs, using the real combo and button adapters.
        KeyItemCombo.Poll();
        if (!KeyItemCombo.Owns(grip)) grip.UpdateValues(controller);
        if (!KeyItemCombo.Owns(interact)) interact.UpdateValues(controller);
        KeyItemCombo.Apply(controller);
    }
}
