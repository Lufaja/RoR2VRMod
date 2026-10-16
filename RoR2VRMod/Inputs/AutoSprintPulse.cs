namespace VRMod.Inputs
{
    internal sealed class AutoSprintPulse
    {
        private bool high;
        private float lastPulse;

        internal bool Poll(float now, bool eligible, bool flying, bool sprinting)
        {
            // Remote Operation (and other flying bodies) uses Sprint for descent. Moving the
            // stick must never manufacture that press; physical Sprint is a separate element.
            if (!eligible || flying || sprinting)
            {
                high = false;
                return false;
            }
            if (high)
            {
                high = false;
                return false;
            }
            if (now - lastPulse < 0.15f) return false;
            lastPulse = now;
            high = true;
            return true;
        }

        internal void Reset()
        {
            high = false;
            lastPulse = 0f;
        }
    }
}
