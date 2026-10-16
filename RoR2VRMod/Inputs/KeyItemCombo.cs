using Rewired;
using RoR2;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRMod.Inputs
{
    /// <summary>
    /// Key items (Hallowed Concepts update, Oct 2026): "Use Key Item" (Rewired action 44) and
    /// "Key Item Change Element" (action 47, axis: + next / - previous) had no VR binding.
    ///
    /// Combo, only while the local body actually holds a key item:
    ///   hold LEFT GRIP + press RIGHT A (Vive: right pad down)   -> use key item
    ///   hold LEFT GRIP + flick RIGHT STICK left/right (Vive: pad) -> previous / next element
    /// While armed, the left grip's own skill (Utility, or Special when left-handed) fires on
    /// RELEASE instead of on press, and only if no combo was used during the hold - the same
    /// tap-vs-hold idea as Y. Right A does not Interact, and the right stick does not turn, while
    /// the grip is held. With no key item the grip, A and stick behave exactly as before.
    ///
    /// Rewired latches a button-down edge the instant an element is set true, so the combo must
    /// decide BEFORE the normal inputs run: Poll() first, then the loop asks Owns(...) to skip the
    /// elements this class drives, then Apply() writes them.
    /// </summary>
    internal static class KeyItemCombo
    {
        internal const int UseElement = 33, NextElement = 34, PrevElement = 35;
        private const int GripElement = 10, InteractElement = 6, LookXAxis = 2, LookYAxis = 3;
        private const float Pulse = 0.12f;          // long enough for the game's buffered poll to see it
        private const float IndicatorDelay = 0.15f; // quick grip taps for the skill don't flash the panel
        private const float FlickOn = 0.7f, FlickOff = 0.35f;

        private static bool armed, gripPrev, aPrev, comboUsed, flickLatched, interactBlocked;
        private static bool cancelGripUntilRelease, cancelInteractUntilRelease;
        private static float gripDownTime, skillPulseUntil, usePulseUntil, nextPulseUntil, prevPulseUntil;
        private static Vector2 rightStick;

        internal static bool OwnsGrip => cancelGripUntilRelease || armed || Time.unscaledTime < skillPulseUntil;
        internal static bool OwnsInteract => cancelInteractUntilRelease || interactBlocked;
        internal static bool OwnsLookX => armed && gripPrev;

        internal static bool Owns(BaseInput input)
        {
            if (input is ButtonInput b)
                return (b.ElementId == GripElement && OwnsGrip) || (b.ElementId == InteractElement && OwnsInteract);
            if (input is VectorInput v && v.XAxisId == LookXAxis)
                return OwnsLookX;
            return false;
        }

        internal static KeyItemSlot LocalSlot()
        {
            CharacterBody body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            KeyItemSlot slot = body ? body.keyItemSlot : null;
            return slot && slot.keyItem != KeyItemIndex.None ? slot : null;
        }

        internal static void Poll()
        {
            float now = Time.unscaledTime;
            InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            bool grip = false, a = false;
            bool gripAvailable = left.isValid && left.IsPressed(InputHelpers.Button.GripButton, out grip);
            bool aAvailable = right.isValid;
            if (ViveControllers.IsTrackpadWand) a = aAvailable && ViveControllers.PadClickDown(right);
            else aAvailable = right.isValid && right.IsPressed(InputHelpers.Button.PrimaryButton, out a);

            // A sleeping/disconnected controller is not a deliberate grip release. Otherwise
            // the pending modifier would manufacture a skill press when tracking disappears.
            // Keep ownership of its consumed buttons until each physical release is observed:
            // simply dropping ownership lets the other, still-held controller fire normally.
            if (!gripAvailable || !aAvailable)
            {
                Reset();
                ObserveCancellationReleases(gripAvailable, aAvailable, grip, a);
                return;
            }

            if (cancelGripUntilRelease || cancelInteractUntilRelease)
            {
                ObserveCancellationReleases(true, true, grip, a);
                gripPrev = grip;
                aPrev = a;
                return;
            }
            if (!right.isValid || !right.TryGetFeatureValue(CommonUsages.primary2DAxis, out rightStick)) rightStick = Vector2.zero;

            KeyItemSlot slot = null;
            if (grip && !gripPrev)
            {
                slot = LocalSlot();
                armed = slot != null;
                comboUsed = false;
                gripDownTime = now;
                aPrev = a;                                            // A already held doesn't count as a press
                flickLatched = Mathf.Abs(rightStick.x) > FlickOff;    // nor does a stick already pushed
            }

            if (armed && grip)
            {
                if (slot == null) slot = LocalSlot();

                if (a && !aPrev)
                {
                    usePulseUntil = now + Pulse;
                    comboUsed = true;
                    KeyItemIndicator.Flash(slot, "Used");
                }

                if (!flickLatched && Mathf.Abs(rightStick.x) > FlickOn)
                {
                    if (rightStick.x > 0) nextPulseUntil = now + Pulse; else prevPulseUntil = now + Pulse;
                    comboUsed = true;
                    flickLatched = true;
                    KeyItemIndicator.Flash(slot, rightStick.x > 0 ? "Element  ►" : "◄  Element");
                }
                else if (flickLatched && Mathf.Abs(rightStick.x) < FlickOff)
                {
                    flickLatched = false;
                }
            }

            if (!grip && gripPrev && armed)
            {
                if (!comboUsed) skillPulseUntil = now + Pulse; // a plain tap: fire the grip's skill now
                armed = false;
            }

            // Right A stays away from Interact for the whole hold, and until it is let go after a combo.
            interactBlocked = (armed && grip) || (interactBlocked && a);

            KeyItemIndicator.Show(armed && grip && now - gripDownTime > IndicatorDelay, armed ? (slot ?? LocalSlot()) : null);

            gripPrev = grip;
            aPrev = a;
        }

        internal static void Apply(CustomController vr)
        {
            float now = Time.unscaledTime;
            if (OwnsGrip) vr.SetButtonValueById(GripElement, now < skillPulseUntil);
            if (OwnsInteract) vr.SetButtonValueById(InteractElement, false);
            if (OwnsLookX)
            {
                vr.SetAxisValueById(LookXAxis, 0f);
                vr.SetAxisValueById(LookYAxis, rightStick.y);
            }
            vr.SetButtonValueById(UseElement, now < usePulseUntil);
            vr.SetButtonValueById(NextElement, now < nextPulseUntil);
            vr.SetButtonValueById(PrevElement, now < prevPulseUntil);
        }

        /// <summary>Called when the VR keyboard / menus suppress input: drop any half-finished combo.</summary>
        internal static void Reset()
        {
            cancelGripUntilRelease |= armed || Time.unscaledTime < skillPulseUntil;
            cancelInteractUntilRelease |= armed || interactBlocked;
            armed = comboUsed = flickLatched = interactBlocked = false;
            gripPrev = aPrev = false;
            skillPulseUntil = usePulseUntil = nextPulseUntil = prevPulseUntil = 0f;
            KeyItemIndicator.Show(false, null);
        }

        private static void ObserveCancellationReleases(bool gripAvailable, bool aAvailable, bool grip, bool a)
        {
            // An unavailable button reports no value, which must not count as a physical release.
            if (gripAvailable && !grip) cancelGripUntilRelease = false;
            if (aAvailable && !a) cancelInteractUntilRelease = false;
        }
    }

}
