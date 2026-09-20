using System;
using System.Collections.Generic;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free diplomacy rules: given a faction's standing, which levers actually exist.
    /// The RPC maps engine state to inputs; the lever list is unit-tested in RimBridge.Tests.
    /// </summary>
    public static class DiplomacyLogic
    {
        public struct Standing
        {
            public bool IsPlayer;
            public bool Hostile;
            public bool PermanentEnemy;
            public bool Defeated;
            public bool HasSettlements;
            public bool HasPrisoners;
        }

        /// <summary>Action ids the agent can actually take right now (each maps to a real RPC).</summary>
        public static List<string> AvailableLevers(Standing s)
        {
            var levers = new List<string>();
            if (s.IsPlayer || s.Defeated) return levers;
            if (s.Hostile)
            {
                if (!s.PermanentEnemy)
                {
                    // Peace talks arrive by caravan; release/defeat shape the road there.
                    levers.Add("peace_talks_via_caravan");
                    if (s.HasPrisoners) levers.Add("release_prisoners");
                }
                levers.Add("raid_settlement_via_caravan");
                return levers;
            }
            // Non-hostile: trade, gifts, and (if it sours) the same roads as above.
            if (s.HasSettlements)
            {
                levers.Add("trade_via_caravan");
                if (!s.PermanentEnemy) levers.Add("gift_via_transport_pods");
            }
            if (s.HasPrisoners) levers.Add("release_prisoners");
            return levers;
        }

        public static string Posture(bool hostile, bool permanentEnemy, bool defeated)
        {
            if (defeated) return "defeated";
            if (hostile) return permanentEnemy ? "war_permanent" : "war";
            return "peace";
        }
    }
}
