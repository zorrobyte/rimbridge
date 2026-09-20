using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimWorld;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Life-state reads the colony overview skips: children growth, genes, mechanoid charge,
    /// entity holding platforms. Present only when relevant to the pawn.
    /// </summary>
    public static class LifeRpc
    {
        [Rpc("life.detail", "{pawn} child growth, genes, mechanoid charge — only the blocks that apply")]
        public static JToken Detail(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            bool isChild = false, hasGenes = false, isMech = false;
            try { isChild = !pawn.ageTracker.Adult && pawn.ageTracker.AgeBiologicalYears < 18; } catch { }
            try { hasGenes = pawn.genes != null && pawn.genes.GenesListForReading.Count > 0; } catch { }
            try { isMech = pawn.RaceProps.IsMechanoid; } catch { }
            var o = new JObject
            {
                ["id"] = pawn.ThingID, ["name"] = pawn.LabelShortCap,
                ["blocks"] = new JArray(LifeLogic.Blocks(isChild, hasGenes, isMech)),
            };
            if (isChild)
            {
                try
                {
                    o["child"] = new JObject
                    {
                        ["age"] = pawn.ageTracker.AgeBiologicalYears,
                        ["tier"] = pawn.ageTracker.GrowthTier,
                        ["growth"] = Math.Round(pawn.ageTracker.Growth * 100),
                        ["points_per_day"] = Math.Round(pawn.ageTracker.GrowthPointsPerDay, 1),
                    };
                }
                catch { }
            }
            if (hasGenes)
            {
                try
                {
                    var genes = pawn.genes.GenesListForReading;
                    o["genes"] = new JObject
                    {
                        ["xenotype"] = pawn.genes.Xenotype?.defName,
                        ["count"] = genes.Count,
                        ["list"] = new JArray(genes.Take(30).Select(g => (JToken)g.def?.defName)),
                    };
                }
                catch { }
            }
            if (isMech)
            {
                try
                {
                    var need = pawn.needs?.TryGetNeed<Need_MechEnergy>();
                    o["mech"] = new JObject { ["energy"] = need != null ? Math.Round(need.CurLevelPercentage * 100) : (double?)null };
                }
                catch { }
            }
            return o;
        }

        [Rpc("life.children", "colony children: age, growth tier, progress")]
        public static JToken Children(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var q in map.mapPawns.FreeColonists)
            {
                bool child = false;
                try { child = !q.ageTracker.Adult && q.ageTracker.AgeBiologicalYears < 18; } catch { continue; }
                if (!child) continue;
                var o = new JObject { ["id"] = q.ThingID, ["name"] = q.LabelShortCap, ["age"] = q.ageTracker.AgeBiologicalYears };
                try { o["tier"] = q.ageTracker.GrowthTier; o["growth"] = Math.Round(q.ageTracker.Growth * 100); } catch { }
                arr.Add(o);
            }
            return arr;
        }

        [Rpc("life.platforms", "entity holding platforms and their occupants")]
        public static JToken Platforms(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var b in map.listerBuildings.allBuildingsColonist.OfType<Building_HoldingPlatform>())
            {
                var o = new JObject { ["id"] = b.ThingID, ["pos"] = new JArray(b.Position.x, b.Position.z) };
                try
                {
                    var held = b.HeldPawn;
                    o["held"] = held != null ? Render.PawnHandle(held) : null;
                }
                catch { }
                arr.Add(o);
            }
            return arr;
        }
    }
}
