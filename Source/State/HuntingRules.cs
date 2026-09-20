// Written for RimBridge (2026). Verse-free rules for whether a hunt designation can be actioned, shared with the
// unit tests in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// Whether a hunt designation can ever be actioned.
    ///
    /// Game rule (1.6): <c>WorkGiver_HunterHunt.ShouldSkip</c> returns true when the pawn has no ranged hunting
    /// weapon, or wears a shield belt with a ranged weapon. It ignores the forced flag, so melee hunting has no
    /// override. The game states this once, as a transient message at designation time, and never again.
    /// </summary>
    public static class HuntingRules
    {
        /// <param name="designations">Hunt designations on the map.</param>
        /// <param name="assigned">Colonists who are up and have the Hunting work type active.</param>
        /// <param name="armed">Of those, the ones with a valid ranged hunting weapon.</param>
        /// <param name="shielded">Of those armed, the ones also wearing a shield belt.</param>
        public static List<string> Problems(int designations, int assigned, int armed, int shielded)
        {
            var p = new List<string>();
            if (designations <= 0) return p;
            if (assigned <= 0)
            {
                p.Add(designations + " hunt designation(s) but no colonist is assigned to Hunting");
                return p;
            }
            if (armed <= 0)
            {
                // The part a player may not know: there is no melee fallback and no way to force one.
                p.Add(designations + " hunt designation(s) but no hunter has a ranged weapon; melee hunting is not possible");
                return p;
            }
            if (shielded > 0)
            {
                p.Add(shielded + " hunter(s) using a ranged weapon and wearing a shield belt, which prevents shooting at range");
                if (shielded >= armed)
                    p.Add(designations + " hunt designation(s) with no hunter able to shoot");
            }
            return p;
        }
    }
}
