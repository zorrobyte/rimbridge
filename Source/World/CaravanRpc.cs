using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimBridge.World
{
    /// <summary>
    /// Caravan agency: form a real forming-caravan (gather, pack, exit) through vanilla's own
    /// StartFormingCaravan, and stop one. Validation mirrors the dialog: sendable pawns, real items,
    /// reachable edge exit spot, valid destination tile.
    /// </summary>
    public static class CaravanRpc
    {
        [Rpc("caravan.form", "{pawns: [ids], items?: [{thing, count}], destination: tile} start a forming caravan: pawns gather, load, and march. Downed pawns are carried like the dialog does.")]
        public static JToken Form(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var map = Find.CurrentMap;
            var ids = P.Arr(p, "pawns") ?? throw new RpcError("missing pawns: [ids]");
            if (ids.Count == 0) throw new RpcError("no pawns given");
            var pawns = new List<Pawn>();
            foreach (var id in ids)
            {
                var pawn = Lookup.Pawn(id.ToString());
                if (pawn.Dead) throw new RpcError($"{pawn.LabelShortCap} is dead");
                if (!pawn.Spawned || pawn.Map != map) throw new RpcError($"{pawn.LabelShortCap} is not on the current map");
                if (!(pawn.IsColonist || (pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer)))
                    throw new RpcError($"{pawn.LabelShortCap} cannot join a caravan");
                if (CaravanFormingUtility.IsFormingCaravan(pawn)) throw new RpcError($"{pawn.LabelShortCap} is already forming a caravan");
                pawns.Add(pawn);
            }
            var ups = pawns.Where(x => !x.Downed).ToList();
            var downs = pawns.Where(x => x.Downed).ToList();
            if (ups.Count == 0) throw new RpcError("at least one walking pawn is required");
            int dest = P.Int(p, "destination");
            if (dest < 0 || dest >= Find.WorldGrid.TilesCount) throw new RpcError($"destination tile {dest} out of range");

            var transferables = new List<TransferableOneWay>();
            var items = P.Arr(p, "items");
            if (items != null)
                foreach (var it in items.OfType<JObject>())
                {
                    string tid = it["thing"]?.ToString() ?? throw new RpcError("items entries need {thing, count}");
                    var t = Lookup.ThingOrNull(tid) ?? throw new RpcError($"no thing '{tid}'");
                    if (t is Pawn) throw new RpcError("pawns ride along via pawns:, not items:");
                    if (!t.Spawned || t.Map != map) throw new RpcError($"{t.ThingID} is not on the current map");
                    int count = Math.Max(1, Math.Min(t.stackCount, it["count"] != null ? (int)it["count"]! : t.stackCount));
                    var tr = new TransferableOneWay();
                    tr.things.Add(t);
                    tr.ForceTo(count);
                    transferables.Add(tr);
                }

            var (mx, mz) = CaravanLogic.Centroid(pawns.Select(x => (x.Position.x, x.Position.z)));
            var meeting = new IntVec3(mx, 0, mz);
            if (!meeting.InBounds(map) || !meeting.Standable(map)) meeting = ups[0].Position;
            if (!RCellFinder.TryFindClosestEdgeCellTo(meeting, map, out var exit))
                throw new RpcError("no exit spot (map has no reachable edge near the pawns?)");

            try
            {
                CaravanFormingUtility.StartFormingCaravan(ups, downs, Faction.OfPlayer, transferables, meeting, exit, map.Tile, dest);
            }
            catch (Exception ex) { throw new RpcError("the game refused to form: " + ex.Message); }
            foreach (var pawn in pawns) Hooks.RaiseManualTouch(pawn, "caravan.form");
            EventLedger.Add("caravan_forming", $"{ups.Count} pawn(s) forming caravan to tile {dest}",
                new JObject { ["pawns"] = new JArray(ups.Select(x => x.ThingID)), ["items"] = transferables.Count, ["destination"] = dest });
            return new JObject
            {
                ["forming"] = ups.Count, ["downed_carried"] = downs.Count, ["items"] = transferables.Count,
                ["meeting"] = new JArray(meeting.x, meeting.z), ["exit"] = new JArray(exit.x, exit.z), ["destination"] = dest,
            };
        }

        static Caravan PlayerCaravan(int id)
        {
            var c = Find.WorldObjects.AllWorldObjects.OfType<Caravan>().FirstOrDefault(x => x.ID == id)
                ?? throw new RpcError($"no caravan with id {id}");
            if (!c.IsPlayerControlled) throw new RpcError("refusing to order someone else's caravan");
            return c;
        }

        [Rpc("caravan.reroute", "{id, tile} send a moving caravan somewhere else (keeps its arrival action)")]
        public static JToken Reroute(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var c = PlayerCaravan(P.Int(p, "id"));
            int dest = P.Int(p, "tile");
            if (dest < 0 || dest >= Find.WorldGrid.TilesCount) throw new RpcError($"tile {dest} out of range");
            bool ok;
            try { ok = c.pather.StartPath(dest, c.pather.ArrivalAction, repathImmediately: true, resetPauseStatus: true); }
            catch (Exception ex) { throw new RpcError("reroute failed: " + ex.Message); }
            if (!ok) throw new RpcError("the game refused the route (unreachable?)");
            EventLedger.Add("caravan_rerouted", $"caravan rerouted to tile {dest}", new JObject { ["id"] = c.ID, ["tile"] = dest });
            return new JObject { ["rerouted"] = true, ["id"] = c.ID, ["destination"] = dest };
        }

        [Rpc("caravan.pause", "{id, paused: bool} hold a caravan in place / resume it")]
        public static JToken Pause(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var c = PlayerCaravan(P.Int(p, "id"));
            bool paused = P.Bool(p, "paused", true);
            try { c.pather.Paused = paused; }
            catch (Exception ex) { throw new RpcError("pause failed: " + ex.Message); }
            return new JObject { ["id"] = c.ID, ["paused"] = c.pather.Paused };
        }

        [Rpc("caravan.stop", "{pawn} stop a forming caravan one of its pawns belongs to (pawns unpack, lord dissolves)")]
        public static JToken Stop(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            if (!pawn.TryGetFormingCaravanLord(out var lord) || lord == null)
                throw new RpcError($"{pawn.LabelShortCap} is not forming a caravan");
            try { CaravanFormingUtility.StopFormingCaravan(lord); }
            catch (Exception ex) { throw new RpcError("the game refused: " + ex.Message); }
            Hooks.RaiseManualTouch(pawn, "caravan.stop");
            return new JObject { ["stopped"] = true, ["pawn"] = pawn.LabelShortCap };
        }
    }
}
