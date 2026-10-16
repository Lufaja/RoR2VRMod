using Rewired;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRMod.Inputs
{
    /// <summary>
    /// 2.23.25: fires a short pulse on RELEASE, and only when the button was held for less than
    /// HoldThreshold. Paired with HoldableButtonInput on the same physical button (Y): a quick
    /// press-and-release opens the pause menu, holding past the threshold shows the scoreboard
    /// instead - the menu no longer pops open on the way to a hold.
    /// </summary>
    internal class TapButtonInput : ButtonInput
    {
        internal const float HoldThreshold = ButtonGestureState.HoldThreshold;
        private readonly ButtonGestureState gesture = new ButtonGestureState();

        internal TapButtonInput(XRNode deviceNode, InputHelpers.Button button, int buttonID, ViveOverride viveOverride = ViveOverride.None) : base(deviceNode, button, buttonID, viveOverride) { }

        internal override void UpdateValues(CustomController vrController)
        {
            bool available = CheckDevice();
            vrController.SetButtonValueById(buttonID, gesture.PollTap(available, available && PollButtonState(), Time.realtimeSinceStartup));
        }

        internal override void ResetState() => gesture.Reset();
    }
}
