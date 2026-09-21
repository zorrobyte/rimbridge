// Written for RimBridge (2026). Verse-free rules for whether a room's chairs and tables let a pawn eat seated,
// shared with the unit tests in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// Whether a room's chairs and tables can be used together.
    ///
    /// Game rule (1.6): <c>Toils_Ingest.TryFindChairOrSpot</c> takes a chair only when a table is the edifice of
    /// one of the 4 cardinal neighbours of the sitting cell. Diagonals do not count and rotation does not matter.
    /// A diagonal chair therefore looks placed and is never used.
    ///
    /// This models chair selection only. The <c>AteWithoutTable</c> mood penalty uses a different test.
    /// </summary>
    public static class DiningRules
    {
        /// <param name="tables">Eat-surface edifices in the room.</param>
        /// <param name="chairs">Sittable buildings in the room.</param>
        /// <param name="chairsAtTable">Of those chairs, the ones cardinally adjacent to an eat surface.</param>
        /// <param name="tablesWithChair">Of those tables, the ones with a cardinally adjacent chair.</param>
        public static List<string> Problems(int tables, int chairs, int chairsAtTable, int tablesWithChair)
        {
            var p = new List<string>();
            if (tables <= 0) return p;
            if (chairs > 0 && chairsAtTable == 0)
                p.Add(chairs + " chair(s) but none cardinally adjacent to a table");
            int bare = tables - tablesWithChair;
            if (bare > 0) p.Add(bare + " table(s) with no cardinally adjacent chair");
            return p;
        }
    }
}
