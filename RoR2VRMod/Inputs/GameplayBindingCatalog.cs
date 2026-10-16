using System;

namespace VRMod.Inputs
{
    // No Unity/Rewired dependency: the selection rules can be exercised without loading the game.
    internal static class GameplayBindingCatalog
    {
        internal const int AutoSprintElement = 36;
        internal static readonly int[] ActionIds = { 5, 4, 6, 7, 8, 9, 10, 18, 28 };
        internal static readonly string[] ActionNames = { "Interact", "Jump", "Equipment", "Primary skill", "Secondary skill", "Utility skill", "Special skill", "Sprint", "Ping" };
        internal static readonly string[] Choices = { "Default", "RightPrimary", "RightSecondary", "RightTrigger", "LeftTrigger", "LeftGrip", "RightGrip", "LeftPrimary", "LeftStickClick", "RightStickClick" };
        internal static readonly string[] Labels = { "Default (survivor / handedness)", "Right primary (A)", "Right secondary (B / Frame Y)", "Right trigger", "Left trigger", "Left grip", "Right grip", "Left primary (X / Frame D-pad down)", "Left stick click", "Right stick click" };
        private static readonly int[] Elements = { -1, 6, 7, 8, 9, 10, 11, 12, 13, 14 };

        internal static int Resolve(string choice, int defaultElement)
        {
            int index = Array.IndexOf(Choices, choice);
            return index > 0 ? Elements[index] : defaultElement;
        }

        internal static int DefaultElement(int actionId, bool leftHanded, int[] skillSlots = null)
        {
            if (actionId >= 7 && actionId <= 10)
            {
                int slot = actionId - 7;
                int physicalIndex = skillSlots == null ? slot : Array.IndexOf(skillSlots, slot);
                if (physicalIndex < 0 || physicalIndex > 3) physicalIndex = slot;
                int[] physical = leftHanded ? new[] { 9, 8, 11, 10 } : new[] { 8, 9, 10, 11 };
                return physical[physicalIndex];
            }
            switch (actionId)
            {
                case 5: return 6;
                case 4: return 7;
                case 6: return 12;
                case 18: return 13;
                case 28: return 14;
                default: return -1;
            }
        }
    }
}
