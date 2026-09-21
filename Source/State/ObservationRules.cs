// Written for RimBridge (2026). Verse-free rules for what the observation may hide, shared with the unit tests
// in mod/Tests. Kept free of Verse types on purpose: these decisions cannot be tested through a live Map, and
// they are exactly the decisions that fail silently.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// What the model is allowed to be told about hostiles.
    ///
    /// RimWorld's own <c>ThreatDisabled</c> was the filter here, and it answers a different question than the one
    /// the observation is asking. It is true for a dormant sleeper (which the player genuinely does not know about)
    /// AND for a pawn who is merely downed (who is lying in plain sight, bleeding, on a cell we might build on).
    /// Collapsing those two into "not reported" made a downed raider indistinguishable from one who fled, and in
    /// episode 1 the model wrote the wrong one into its permanent notebook.
    ///
    /// The rule now: <b>fog decides what is reported; everything else is a flag.</b> Fog is what actually keeps an
    /// unopened ancient danger secret, so the visibility invariant survives, while a downed raider and a visible
    /// dormant mech cluster -- both of which a player can plainly see -- are reported with their state attached.
    /// </summary>
    public static class ObservationRules
    {
        /// <summary>Whether a hostile belongs in the observation at all. Presence, not danger.</summary>
        public static bool VisibleHostile(bool spawned, bool fogged, bool dead)
            => spawned && !fogged && !dead;

        /// <summary>How to label one: "active", "downed" or "dormant". Downed wins, because a downed sleeper
        /// cannot wake up fighting and calling it dormant would imply it can.</summary>
        public static string HostileStatus(bool downed, bool dormant)
            => downed ? "downed" : dormant ? "dormant" : "active";

        /// <summary>Hostiles on the map, split by why they are or are not fighting. The split is the point: one
        /// number cannot distinguish "they left" from "they are lying on the ground bleeding".</summary>
        public sealed class HostileTally
        {
            public int Active;
            public int Downed;
            public int Dormant;

            public int Total { get { return Active + Downed + Dormant; } }

            public void Add(string status)
            {
                if (status == "downed") Downed++;
                else if (status == "dormant") Dormant++;
                else Active++;
            }
        }

        /// <summary>
        /// The danger-change line, which is what the model actually reads mid-step.
        ///
        /// The old text counted only what <c>ThreatDisabled</c> let through, so a raid that ended with a raider
        /// bleeding on the ground 18 cells away announced itself as "danger Low -&gt; None (0 hostile targets)".
        /// That sentence is why the notebook says she fled.
        /// </summary>
        public static string DangerText(string from, string to, int active, int downed, int dormant)
        {
            if (active == 0 && downed == 0 && dormant == 0)
                return "danger " + from + " -> " + to + " (no hostiles on the map)";
            // `active` is always spelled out, including when it is zero: "0 active, 1 downed" is precisely the
            // sentence the old text could not say, and the one that stops "danger gone" reading as "they left".
            var parts = new List<string> { active + " active" };
            if (downed > 0) parts.Add(downed + " downed");
            if (dormant > 0) parts.Add(dormant + " dormant");
            return "danger " + from + " -> " + to + " (" + string.Join(", ", parts.ToArray()) + ")";
        }
    }
}
