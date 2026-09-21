// Written for RimBridge (2026). Verse-free rules for whether a stockpile is keeping anything safe, shared with
// the unit tests in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// Whether a stockpile is doing its job.
    ///
    /// An open-air stockpile was structurally invisible. <c>OutsideStorage</c> begins by skipping anything that
    /// <c>IsInAnyStorage()</c>, so the roof check further down never ran on stored things; and the obvious home
    /// for the check -- BaseGraph's room "problems" channel -- rejects rooms that are PsychologicallyOutdoors,
    /// which an open-air stockpile always is. The zone fell between the two views and appeared in neither, so the
    /// payload said unroofed_deteriorating: 0 while crows ate the food and the rest rotted.
    ///
    /// Phrased as problem strings on purpose: the model already reads that channel for rooms, so this needs no
    /// new habit from it.
    /// </summary>
    public static class StorageRules
    {
        public static List<string> Problems(int cells, int unroofed, int deteriorating, int rotting)
        {
            var p = new List<string>();
            if (cells > 0 && unroofed > 0)
                p.Add(unroofed >= cells ? "entirely unroofed" : unroofed + " of " + cells + " cells unroofed");
            // The roof is the cause; this is the damage, and it is what should actually prompt a decision.
            if (deteriorating > 0) p.Add(deteriorating + " stack(s) deteriorating in the open");
            // Kept separate because roofing does not stop rot -- that needs cold, and saying so in one breath
            // would suggest a roof fixes it.
            if (rotting > 0) p.Add(rotting + " stack(s) rotting");
            return p;
        }
    }
}
