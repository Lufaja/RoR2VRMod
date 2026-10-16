namespace VRMod.Inputs
{
    internal sealed class ButtonGestureState
    {
        internal const float HoldThreshold = 0.4f;
        private const float TapPulseLength = 0.05f;
        private bool previous;
        private float pressTime;
        private float pulseUntil;

        internal bool PollTap(bool available, bool pressed, float now)
        {
            if (!available) { Reset(); return false; }
            if (pressed && !previous) pressTime = now;
            if (!pressed && previous && now - pressTime < HoldThreshold) pulseUntil = now + TapPulseLength;
            previous = pressed;
            return now < pulseUntil;
        }

        internal bool PollHold(bool available, bool pressed, float now)
        {
            if (!available) { Reset(); return false; }
            if (pressed && !previous) pressTime = now;
            previous = pressed;
            return pressed && now - pressTime >= HoldThreshold;
        }

        internal void Reset()
        {
            previous = false;
            pressTime = pulseUntil = 0f;
        }
    }
}
