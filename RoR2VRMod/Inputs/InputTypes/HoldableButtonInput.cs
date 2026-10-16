using Rewired;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRMod.Inputs
{
    internal class HoldableButtonInput : ButtonInput
    {
        private readonly ButtonGestureState gesture = new ButtonGestureState();

        internal HoldableButtonInput(XRNode deviceNode, InputHelpers.Button button, int buttonID, ViveOverride viveOverride = ViveOverride.None) : base(deviceNode, button, buttonID, viveOverride) { }

        internal override void UpdateValues(CustomController vrController)
        {
            bool available = CheckDevice();
            vrController.SetButtonValueById(buttonID, gesture.PollHold(available, available && PollButtonState(), Time.realtimeSinceStartup));
        }

        internal override void ResetState() => gesture.Reset();
    }
}
