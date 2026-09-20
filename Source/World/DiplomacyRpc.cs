using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimBridge.State;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Quest detail (parts, look targets, expiry) and faction diplomacy detail (standing, leader,
    /// prisoners held, nearest settlement, available levers). Quest acceptance itself flows through
    /// ui.letter choices and ui.dialog answers, which is where the game puts it.
    /// </summary>
    public static class DiplomacyRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static JObject LookTarget(GlobalTargetInfo t)
        {
            var o = new JObject();
            try
            {
                if (!t.IsValid) return o;
                if (t.HasThing && t.Thing != null) { o["thing"] = t.Thing.ThingID; o["label"] = t.Thing.LabelCap.ToString(); return o; }
                if (t.WorldObject != null) { o["world_id"] = t.WorldObject.ID; o["label"] = t.WorldObject.Label; return o; }
                if (t.Tile.Valid) o["tile"] = (int)t.Tile;
                else if (t.Map != null) o["map"] = t.Map.Index;
                else if (t.Cell.IsValid) o["cell"] = new JArray(t.Cell.x, t.Cell.z);
                try { o["label"] = t.Label; } catch { }
            }
            catch { }
            return o;
        }

        [Rpc("quest.detail", "{id} full quest: state, expiry, challenge, parts, look targets, linked letters")]
        public static JToken QuestDetail(JObject p)
        {
            RequirePlaying();
            int id = P.Int(p, "id");
            var q = Find.QuestManager.QuestsListForReading.FirstOrDefault(x => x.id == id)
                ?? throw new RpcError($"no quest with id {id}");
            var o = new JObject
            {
                ["id"] = q.id, ["name"] = q.name, ["state"] = q.State.ToString(),
                ["challenge"] = q.challengeRating, ["ever_accepted"] = q.EverAccepted,
                ["expires_in_ticks"] = q.State == QuestState.NotYetAccepted ? q.TicksUntilExpiry : -1,
            };
            try { o["description"] = Snapshot.Trunc(q.description.ToString().StripTags(), 800); } catch { }
            try { o["root"] = q.root?.defName; } catch { }
            var parts = new JArray();
            try
            {
                foreach (var part in q.PartsListForReading.Take(12))
                {
                    var po = new JObject { ["type"] = part.GetType().Name };
                    try
                    {
                        var targets = new JArray(part.QuestLookTargets.Take(6).Select(LookTarget));
                        if (targets.Count > 0) po["targets"] = targets;
                    }
                    catch { }
                    parts.Add(po);
                }
            }
            catch { }
            o["parts"] = parts;
            try
            {
                var look = new JArray(q.QuestLookTargets.Take(8).Select(LookTarget));
                if (look.Count > 0) o["look"] = look;
            }
            catch { }
            try
            {
                var letters = Find.LetterStack.LettersListForReading
                    .Where(l => l is ChoiceLetter cl && cl.quest != null && cl.quest.id == id)
                    .Select(l => new JObject { ["letter_id"] = l.ID, ["label"] = l.Label.ToString().StripTags() });
                o["letters"] = new JArray(letters);
            }
            catch { }
            return o;
        }

        [Rpc("diplomacy.detail", "{faction: name} standing, leader, prisoners held, nearest settlement, what you can actually do")]
        public static JToken FactionDetail(JObject p)
        {
            RequirePlaying();
            var f = Lookup.FactionOrNull(P.Str(p, "faction")) ?? throw new RpcError($"no faction '{P.Str(p, "faction")}'");
            var o = new JObject
            {
                ["name"] = f.Name, ["def"] = f.def?.defName,
                ["goodwill"] = f.PlayerGoodwill, ["relation"] = f.PlayerRelationKind.ToString(),
                ["hostile"] = f.HostileTo(Faction.OfPlayer),
                ["posture"] = DiplomacyLogic.Posture(f.HostileTo(Faction.OfPlayer), f.def?.permanentEnemy ?? false, f.defeated),
                ["permanent_enemy"] = f.def?.permanentEnemy ?? false,
                ["defeated"] = f.defeated,
                ["tech"] = f.def?.techLevel.ToString(),
            };
            try
            {
                if (f.leader != null && !f.leader.Dead)
                    o["leader"] = new JObject { ["id"] = f.leader.ThingID, ["name"] = f.leader.LabelShortCap, ["alive"] = true };
            }
            catch { }
            int home = -1;
            try { if (Find.CurrentMap != null) home = Find.CurrentMap.Tile; } catch { }
            try
            {
                Settlement? best = null; float bestDist = float.MaxValue;
                foreach (var s in Find.WorldObjects.AllWorldObjects.OfType<Settlement>().Where(s => s.Faction == f))
                {
                    float d = home >= 0 ? Find.WorldGrid.ApproxDistanceInTiles(home, (int)s.Tile) : 0;
                    if (d < bestDist) { bestDist = d; best = s; }
                }
                o["settlements"] = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().Count(s => s.Faction == f);
                if (best != null)
                {
                    string name = "?";
                    try { name = best.Name; } catch { }
                    o["nearest_settlement"] = new JObject { ["id"] = best.ID, ["name"] = name, ["tiles"] = Math.Round(bestDist), ["can_trade"] = best.CanTradeNow };
                }
            }
            catch { }
            try
            {
                var map = Find.CurrentMap;
                int held = map == null ? 0 : map.mapPawns.PrisonersOfColony.Count(q => q.HomeFaction == f);
                o["prisoners_held"] = held;
            }
            catch { }
            bool hasSettlements = false;
            try { hasSettlements = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().Any(s => s.Faction == f); } catch { }
            int heldForLevers = 0;
            try { heldForLevers = (int?)o["prisoners_held"] ?? 0; } catch { }
            o["levers"] = new JArray(DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing
            {
                IsPlayer = f.IsPlayer,
                Hostile = f.HostileTo(Faction.OfPlayer),
                PermanentEnemy = f.def?.permanentEnemy ?? false,
                Defeated = f.defeated,
                HasSettlements = hasSettlements,
                HasPrisoners = heldForLevers > 0,
            }));
            return o;
        }
    }
}
