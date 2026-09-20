using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free caravan helpers: meeting-point centroid from pawn positions. The engine call
    /// (StartFormingCaravan) stays in CaravanRpc; the math is unit-tested in RimBridge.Tests.
    /// </summary>
    public static class CaravanLogic
    {
        public static (int x, int z) Centroid(IEnumerable<(int x, int z)> cells)
        {
            var list = cells.ToList();
            if (list.Count == 0) throw new ArgumentException("no cells");
            return ((int)Math.Round(list.Average(c => c.x)), (int)Math.Round(list.Average(c => c.z)));
        }
    }
}
