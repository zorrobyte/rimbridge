using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimBridge.State;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace RimBridge.World
{
    /// <summary>
    /// Transport pods: list groups with fuel and contents, load hauls, board pawns, launch with an
    /// arrival action (gift/trade/visit/attack). Launches go through vanilla TryLaunch and are verified
    /// by the pods leaving the map — a refusal surfaces as an error, never a silent no-op.
    /// </summary>
    public static class PodsRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static CompTransporter PodOrThrow(string id, Map map)
        {
            var t = Lookup.Thing(id);
            if (t.Map != map) throw new RpcError($"{id} is not on the current map");
            return t.TryGetComp<CompTransporter>() ?? throw new RpcError($"{id} is not a transport pod");
        }

        static JObject PodJson(Thing pod, CompTransporter tr)
        {
            int fuel = -1, fuelMax = -1;
            try { var lc = pod.TryGetComp<CompLaunchable>(); fuel = (int)(lc?.FuelLevel ?? -1); fuelMax = (int)(lc?.MaxFuelLevel ?? -1); } catch { }
            var o = new JObject
            {
                ["id"] = pod.ThingID, ["pos"] = new JArray(pod.Position.x, pod.Position.z),
                ["group"] = tr.groupID, ["fuel"] = fuel, ["fuel_max"] = fuelMax,
                ["loading"] = tr.LoadingInProgressOrReadyToLaunch,
            };
            try
            {
                var rows = tr.innerContainer
                    .Where(x => x != null && !x.Destroyed)
                    .Select(x => new WorldLogic.GoodRow { Def = x.def?.defName ?? "?", Count = x.stackCount, Value = 0 });
                var s = WorldLogic.SummarizeGoods(rows, 8);
                o["contents"] = new JArray(s.Top.Select(r => new JObject { ["def"] = r.Def, ["count"] = r.Count }));
                o["contents_stacks"] = s.TotalStacks;
            }
            catch { }
            return o;
        }

        [Rpc("pods.list", "transport pods on the map: group, fuel, contents")]
        public static JToken List(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var t in map.listerThings.AllThings)
            {
                var tr = t.TryGetComp<CompTransporter>();
                if (tr == null || !t.Spawned) continue;
                arr.Add(PodJson(t, tr));
            }
            return arr;
        }

        [Rpc("pods.load", "{pod, thing, count?, pawn} haul an item to a pod (pawn carries it over)")]
        public static JToken Load(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var pod = Lookup.Thing(P.Str(p, "pod"));
            var tr = pod.TryGetComp<CompTransporter>() ?? throw new RpcError("not a transport pod");
            if (pod.Map != map) throw new RpcError("pod is not on the current map");
            var pawn = Lookup.Colonist(P.Str(p, "pawn"));
            if (pawn.Downed) throw new RpcError($"{pawn.LabelShortCap} is downed");
            var thing = Lookup.Thing(P.Str(p, "thing"));
            if (thing is Pawn) throw new RpcError("pawns board via pods.board, not pods.load");
            if (!thing.Spawned || thing.Map != map) throw new RpcError("thing is not on the current map");
            int count = p["count"] != null ? Math.Max(1, Math.Min(thing.stackCount, P.Int(p, "count"))) : thing.stackCount;
            // Pods only accept registered loads: mirror the Load dialog's AddToTheToLoadList step.
            var reg = new TransferableOneWay();
            reg.things.Add(thing);
            try { tr.AddToTheToLoadList(reg, count); }
            catch (Exception ex) { throw new RpcError("pod refused the load: " + ex.Message); }
            var job = JobMaker.MakeJob(JobDefOf.HaulToTransporter, thing, pod);
            job.count = count;
            job.playerForced = true;
            // HaulToContainer drivers read target queues, which ordered jobs don't fill in.
            job.targetQueueA = new System.Collections.Generic.List<LocalTargetInfo> { new LocalTargetInfo(thing) };
            job.targetQueueB = new System.Collections.Generic.List<LocalTargetInfo> { new LocalTargetInfo(pod) };
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                throw new RpcError("the game refused the haul (reserved, unreachable, or pod full)");
            Hooks.RaiseManualTouch(pawn, "pods.load");
            return new JObject { ["ok"] = true, ["pawn"] = pawn.LabelShortCap, ["job"] = State.Snapshot.JobText(pawn) };
        }

        [Rpc("pods.board", "{pod, pawn} send a pawn into a pod")]
        public static JToken Board(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var pod = Lookup.Thing(P.Str(p, "pod"));
            if (pod.TryGetComp<CompTransporter>() == null) throw new RpcError("not a transport pod");
            if (pod.Map != map) throw new RpcError("pod is not on the current map");
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            if (pawn.Dead || !pawn.Spawned || pawn.Map != map) throw new RpcError("pawn unavailable");
            var job = JobMaker.MakeJob(JobDefOf.EnterTransporter, pod);
            job.playerForced = true;
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                throw new RpcError("the game refused boarding (reserved or unreachable)");
            Hooks.RaiseManualTouch(pawn, "pods.board");
            return new JObject { ["ok"] = true, ["pawn"] = pawn.LabelShortCap, ["job"] = State.Snapshot.JobText(pawn) };
        }

        [Rpc("pods.launch", "{pod, tile, action: gift|trade|visit|attack, settlement: id} launch the pod group; verified by liftoff")]
        public static JToken Launch(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var pod = Lookup.Thing(P.Str(p, "pod"));
            var tr = pod.TryGetComp<CompTransporter>() ?? throw new RpcError("not a transport pod");
            if (!pod.Spawned || pod.Map != map) throw new RpcError("pod is not on the current map");
            var launchable = pod.TryGetComp<CompLaunchable>() ?? throw new RpcError("pod has no launcher");
            string action = P.Str(p, "action").ToLowerInvariant();

            var so = Find.WorldObjects.AllWorldObjects.OfType<Settlement>()
                .FirstOrDefault(s => s.ID == P.Int(p, "settlement", -1)) ?? throw new RpcError("action needs settlement=<world id>");
            Settlement settlement = so;
            int dest = p["tile"] != null ? P.Int(p, "tile") : settlement.Tile;
            if (dest < 0 || dest >= Find.WorldGrid.TilesCount) throw new RpcError($"tile {dest} out of range");
            TransportersArrivalAction arrival = action switch
            {
                "gift" => new TransportersArrivalAction_GiveGift(settlement!),
                "trade" => new TransportersArrivalAction_Trade(settlement!, "MessageShuttleArrived"),
                "visit" => new TransportersArrivalAction_VisitSettlement(settlement!, "MessageShuttleArrived"),
                "attack" => new TransportersArrivalAction_AttackSettlement(settlement!, PawnsArrivalModeDefOf.EdgeDrop),
                _ => throw new RpcError("action must be gift|trade|visit|attack"),
            };
            if (action == "gift")
            {
                var holders = new List<IThingHolder> { tr };
                FloatMenuAcceptanceReport rep;
                try { rep = TransportersArrivalAction_GiveGift.CanGiveGiftTo(holders, settlement!); }
                catch (Exception ex) { throw new RpcError("gift check failed: " + ex.Message); }
                if (!rep) throw new RpcError("gift refused: " + (rep.FailReason ?? "invalid target"));
            }

            float range = -1, dist = -1;
            try
            {
                range = launchable.MaxLaunchDistanceAtFuelLevel(launchable.FuelLevel);
                dist = Find.WorldGrid.TraversalDistanceBetween(map.Tile, dest, true, int.MaxValue, true);
            }
            catch { }
            bool ready = false;
            try { ready = tr.LoadingInProgressOrReadyToLaunch; } catch { }
            string? blocked = PodsLogic.LaunchBlocker(range, dist, ready, pod.Spawned);
            if (blocked != null) throw new RpcError(blocked);

            try { launchable.TryLaunch(dest, arrival); }
            catch (Exception ex) { throw new RpcError("launch threw: " + ex.Message); }
            if (pod.Spawned) throw new RpcError("launch refused by the game (roofed? cooldown? no fuel? empty?)");
            EventLedger.Add("pods_launched", $"pods launched at tile {dest} ({action})", new JObject { ["tile"] = dest, ["action"] = action });
            return new JObject { ["launched"] = true, ["tile"] = dest, ["action"] = action };
        }

        [Rpc("pods.cancel", "{pod} cancel loading jobs for a pod")]
        public static JToken Cancel(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var tr = PodOrThrow(P.Str(p, "pod"), map);
            bool ok;
            try { ok = tr.CancelLoad(); } catch (Exception ex) { throw new RpcError("cancel failed: " + ex.Message); }
            return new JObject { ["cancelled"] = ok };
        }
    }
}
