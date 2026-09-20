using System.Collections.Generic;

namespace RimBridge.World
{
    /// <summary>Verse-free mech / shuttle / gene validators. Engine mapping stays in the RPCs.</summary>
    public static class MechLogic
    {
        public static (string? matched, string? error) ResolveMode(string requested, IList<string> available)
        {
            foreach (var a in available)
                if (string.Equals(a, requested, System.StringComparison.OrdinalIgnoreCase))
                    return (a, null);
            return (null, $"unknown mode '{requested}' (available: {string.Join(", ", available)})");
        }
    }

    public static class ShuttleLogic
    {
        public static string? LaunchBlocker(bool canLaunch, string? reason, int tile, int tileCount, float distTiles, float rangeTiles)
        {
            if (!canLaunch) return "shuttle refuses launch: " + (string.IsNullOrEmpty(reason) ? "unknown" : reason);
            if (tile < 0 || tile >= tileCount) return $"tile {tile} out of range";
            if (distTiles > rangeTiles) return $"out of range ({distTiles} tiles, shuttle reaches {rangeTiles})";
            return null;
        }
    }

    public static class GeneLogic
    {
        public static string? ExtractBlocker(bool powered, bool occupied, bool canAccept, string? reason)
        {
            if (!powered) return "extractor has no power";
            if (occupied) return "extractor already holds a pawn";
            if (!canAccept) return "extractor refuses pawn: " + (string.IsNullOrEmpty(reason) ? "unknown" : reason);
            return null;
        }
    }
}
