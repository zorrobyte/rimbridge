namespace RimBridge.World
{
    /// <summary>
    /// Verse-free ideology helpers: describe a ritual obligation's state from facts the RPC extracts.
    /// </summary>
    public static class IdeoLogic
    {
        public static string ObligationStatus(bool hasTarget, string? targetLabel, bool anytime)
        {
            if (hasTarget) return "ready at " + (string.IsNullOrEmpty(targetLabel) ? "target" : targetLabel);
            if (anytime) return "can begin anytime";
            return "waiting for trigger";
        }
    }
}
