using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimWorld;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Gene extraction: list extractors, insert a pawn for xenogerm extraction, cancel.
    /// Extraction itself runs on the vanilla building ticker once the pawn is inside.
    /// </summary>
    public static class GeneRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static Pawn? ContainedPawn(Building_GeneExtractor ex)
        {
            try { return ex.innerContainer.OfType<Pawn>().FirstOrDefault(); } catch { return null; }
        }

        static JObject ExtractorJson(Building_GeneExtractor ex)
        {
            bool power = false;
            try { power = ex.PowerOn; } catch { }
            string? contained = ContainedPawn(ex)?.ThingID;
            return new JObject
            {
                ["id"] = ex.ThingID, ["pos"] = new JArray(ex.Position.x, ex.Position.z),
                ["powered"] = power, ["contained"] = contained,
            };
        }

        static Building_GeneExtractor FindExtractor(string? id, Map map)
        {
            if (id != null)
            {
                var t = Lookup.Thing(id);
                if (t is Building_GeneExtractor ex && ex.Spawned && ex.Map == map) return ex;
                throw new RpcError($"{id} is not a gene extractor on the current map");
            }
            return map.listerThings.AllThings.OfType<Building_GeneExtractor>()
                .FirstOrDefault(e => e.Spawned) ?? throw new RpcError("no gene extractor on the current map");
        }

        [Rpc("gene.status", "gene extractors: power and contained pawn")]
        public static JToken Status(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var ex in map.listerThings.AllThings.OfType<Building_GeneExtractor>().Where(e => e.Spawned))
                arr.Add(ExtractorJson(ex));
            return arr;
        }

        [Rpc("gene.extract", "{pawn, extractor?} insert a pawn into a gene extractor (vanilla carry + ticker take over)")]
        public static JToken Extract(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            if (pawn.Dead || !pawn.Spawned || pawn.Map != map) throw new RpcError("pawn unavailable");
            var ex = FindExtractor(p["extractor"] != null ? P.Str(p, "extractor") : null, map);

            bool power = false;
            try { power = ex.PowerOn; } catch { }
            bool occupied = ContainedPawn(ex) != null;
            bool canAccept = false;
            string acceptReason = "";
            try { var rep = ex.CanAcceptPawn(pawn); canAccept = rep.Accepted; acceptReason = rep.Reason ?? ""; }
            catch (Exception e) { acceptReason = e.Message; }
            string? blocked = GeneLogic.ExtractBlocker(power, occupied, canAccept, acceptReason);
            if (blocked != null) throw new RpcError(blocked);

            try { ex.TryAcceptPawn(pawn); }
            catch (Exception e) { throw new RpcError("extractor threw: " + e.Message); }
            string? contained = ContainedPawn(ex)?.ThingID;
            Hooks.RaiseManualTouch(pawn, "gene.extract");
            EventLedger.Add("gene_extract", $"{pawn.LabelShortCap} -> extractor", new JObject { ["pawn"] = pawn.ThingID });
            return new JObject { ["ok"] = true, ["pawn"] = pawn.LabelShortCap, ["contained_now"] = contained == pawn.ThingID };
        }

        [Rpc("gene.cancel", "{extractor} eject/cancel the extraction")]
        public static JToken Cancel(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            var ex = FindExtractor(P.Str(p, "extractor"), map);
            try { ex.innerContainer.TryDropAll(ex.Position, map, ThingPlaceMode.Near); }
            catch (Exception e) { throw new RpcError("eject threw: " + e.Message); }
            return new JObject { ["cancelled"] = true, ["contained"] = ContainedPawn(ex)?.ThingID };
        }
    }
}
