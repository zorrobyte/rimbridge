using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Royalty shuttles: list loaded shuttles and launch them at a settlement (visit), going
    /// through vanilla CompLaunchable.TryLaunch. Verified by liftoff, like pods.launch.
    /// </summary>
    public static class ShuttleRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        [Rpc("shuttle.list", "player shuttles on the map: launch readiness, contained pawns")]
        public static JToken List(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var t in map.listerThings.AllThings)
            {
                var comp = t.TryGetComp<CompShuttle>();
                if (comp == null || !t.Spawned) continue;
                string reason = "";
                bool ok = false;
                try { var rep = comp.CanLaunch; ok = rep.Accepted; reason = rep.Reason ?? ""; } catch { }
                int contained = -1;
                try { contained = comp.Transporter?.innerContainer.OfType<Pawn>().Count() ?? -1; } catch { }
                arr.Add(new JObject
                {
                    ["id"] = t.ThingID, ["label"] = t.LabelCap.ToString().StripTags(),
                    ["can_launch"] = ok, ["reason"] = reason, ["contained"] = contained,
                });
            }
            return arr;
        }

        [Rpc("shuttle.launch", "{shuttle: id, tile} fly a loaded shuttle to a settlement (visit)")]
        public static JToken Launch(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            var shuttle = Lookup.Thing(P.Str(p, "shuttle"));
            if (!shuttle.Spawned || shuttle.Map != map) throw new RpcError("shuttle is not on the current map");
            var comp = shuttle.TryGetComp<CompShuttle>() ?? throw new RpcError("not a shuttle");
            var launchable = comp.Transporter?.Launchable ?? throw new RpcError("shuttle has no launcher");
            int dest = P.Int(p, "tile");
            var settlement = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().FirstOrDefault(s => s.Tile == dest)
                ?? throw new RpcError($"tile {dest} is not a settlement (shuttle.launch visits settlements)");

            string reason = "";
            bool ok = false;
            try { var rep = comp.CanLaunch; ok = rep.Accepted; reason = rep.Reason ?? ""; } catch (Exception ex) { reason = ex.Message; }
            float range = -1, dist = -1;
            try
            {
                range = launchable.MaxLaunchDistanceAtFuelLevel(launchable.FuelLevel);
                dist = Find.WorldGrid.TraversalDistanceBetween(map.Tile, dest, true, int.MaxValue, true);
            }
            catch { }
            bool ready = false;
            try { ready = comp.Transporter.LoadingInProgressOrReadyToLaunch; } catch { }
            string? blocked = ShuttleLogic.LaunchBlocker(ok, reason, dest, Find.WorldGrid.TilesCount, dist, range);
            if (blocked == null) blocked = PodsLogic.LaunchBlocker(range, dist, ready, shuttle.Spawned);
            if (blocked != null) throw new RpcError(blocked);

            var arrival = new TransportersArrivalAction_VisitSettlement(settlement, "MessageShuttleArrived");
            try { launchable.TryLaunch(dest, arrival); }
            catch (Exception ex) { throw new RpcError("launch threw: " + ex.Message); }
            if (shuttle.Spawned) throw new RpcError("launch refused by the game (roofed? cooldown? no pilot?)");
            EventLedger.Add("shuttle_launched", $"shuttle to {settlement.LabelCap}", new JObject { ["tile"] = dest });
            return new JObject { ["launched"] = true, ["tile"] = dest, ["settlement"] = settlement.LabelCap.ToString() };
        }
    }
}
