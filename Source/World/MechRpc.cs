using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Mechanitor control: list overseers' mechs with work groups, set a mech's work mode
    /// (Work/Escort/Recharge...) optionally onto a target cell, via the vanilla control groups.
    /// </summary>
    public static class MechRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static List<string> AvailableModes()
        {
            try { return DefDatabase<MechWorkModeDef>.AllDefs.Select(d => d.defName).OrderBy(n => n).ToList(); }
            catch { return new List<string>(); }
        }

        static (Pawn overseer, Pawn_MechanitorTracker tracker, MechanitorControlGroup group) FindMech(Pawn mech)
        {
            var map = Find.CurrentMap;
            foreach (var c in map.mapPawns.FreeColonists)
            {
                Pawn_MechanitorTracker? tr = null;
                try { tr = c.mechanitor; } catch { }
                if (tr == null) continue;
                bool has = false;
                try { has = tr.ControlledPawns.Contains(mech); } catch { }
                if (!has) continue;
                MechanitorControlGroup? g = null;
                try { g = tr.GetControlGroup(mech); } catch { }
                if (g == null) throw new RpcError($"{mech.LabelShortCap} is overseen but in no control group");
                return (c, tr, g);
            }
            throw new RpcError($"{mech.LabelShortCap} has no mechanitor overseer");
        }

        [Rpc("mech.list", "mechanitors with bandwidth, control groups, mechs and their work modes")]
        public static JToken List(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var c in map.mapPawns.FreeColonists)
            {
                Pawn_MechanitorTracker? tr = null;
                try { tr = c.mechanitor; } catch { }
                if (tr == null) continue;
                int used = 0, total = 0;
                try { used = tr.UsedBandwidth; total = tr.TotalBandwidth; } catch { }
                var groups = new JArray();
                try
                {
                    foreach (var g in tr.controlGroups)
                    {
                        var mechs = new JArray();
                        try { foreach (var m in g.MechsForReading) mechs.Add(Render.PawnHandle(m)); } catch { }
                        string mode = "?";
                        try { mode = g.WorkMode?.defName ?? "?"; } catch { }
                        groups.Add(new JObject { ["mode"] = mode, ["mechs"] = mechs });
                    }
                }
                catch { }
                arr.Add(new JObject
                {
                    ["mechanitor"] = c.LabelShortCap, ["bandwidth"] = used, ["bandwidth_max"] = total, ["groups"] = groups,
                });
            }
            return arr;
        }

        [Rpc("mech.setmode", "{mech: id, mode: defName, cell?: [x,z]} set a mech's work mode, optionally onto a target")]
        public static JToken SetMode(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            var mech = Lookup.Pawn(P.Str(p, "mech"));
            if (mech.Dead) throw new RpcError($"{mech.LabelShortCap} is dead");
            var (_, _, group) = FindMech(mech);
            var available = AvailableModes();
            var (matched, error) = MechLogic.ResolveMode(P.Str(p, "mode"), available);
            if (matched == null) throw new RpcError(error ?? "unknown mode");
            var modeDef = DefDatabase<MechWorkModeDef>.GetNamed(matched, false) ?? throw new RpcError($"mode def '{matched}' missing");
            try
            {
                if (p["cell"] != null)
                {
                    var cell = Lookup.Cell(p["cell"]);
                    group.SetWorkMode(modeDef, new GlobalTargetInfo(cell, map));
                }
                else group.SetWorkMode(modeDef);
            }
            catch (Exception ex) { throw new RpcError("SetWorkMode threw: " + ex.Message); }
            string now = "?";
            try { now = group.WorkMode?.defName ?? "?"; } catch { }
            if (!string.Equals(now, matched, StringComparison.OrdinalIgnoreCase))
                throw new RpcError($"mode did not stick (now {now})");
            Hooks.RaiseManualTouch(mech, "mech.setmode");
            return new JObject { ["ok"] = true, ["mech"] = mech.LabelShortCap, ["mode"] = now };
        }
    }
}
