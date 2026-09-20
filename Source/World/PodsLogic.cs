namespace RimBridge.World
{
    /// <summary>
    /// Verse-free launch validation: given fuel range, distance and readiness, why a launch may not
    /// go. Returns null when clear. The engine mapping stays in PodsRpc.
    /// </summary>
    public static class PodsLogic
    {
        public static string? LaunchBlocker(float fuelRangeTiles, float distTiles, bool loadingDone, bool spawned)
        {
            if (!spawned) return "pod is gone";
            if (!loadingDone) return "loading still in progress";
            if (distTiles > fuelRangeTiles) return $"out of range ({distTiles} tiles, fuel reaches {fuelRangeTiles})";
            return null;
        }
    }
}
