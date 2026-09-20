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
    /// Ideology state: precepts, role holders, rituals with obligations and begin-targets, development.
    /// Starting a ritual itself is player parity through ui.gizmos/ui.press on the obligation target
    /// ("Begin ritual" button) — this endpoint tells the agent WHICH rituals are pending and WHERE.
    /// </summary>
    public static class IdeoRpc
    {
        [Rpc("ideo.detail", "player ideology: precepts, role holders, rituals with obligations + begin targets, development points")]
        public static JToken Detail(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var o = new JObject();
            bool active = false;
            try { active = ModsConfig.IdeologyActive; } catch { }
            o["active"] = active;
            if (!active) return o;
            var ideo = Find.CurrentMap?.mapPawns?.FreeColonists?.FirstOrDefault()?.Ideo;
            if (ideo == null) { o["note"] = "no colonist ideology found"; return o; }
            o["name"] = ideo.name;
            o["member"] = ideo.memberName;
            try { o["fluid"] = ideo.Fluid; } catch { }
            try { o["development"] = ideo.development?.Points ?? 0; o["can_reform"] = ideo.development?.CanReformNow ?? false; } catch { }

            var precepts = new JArray();
            try
            {
                foreach (var pr in ideo.PreceptsListForReading.Take(60))
                {
                    string kind = pr is Precept_Role ? "role" : pr is Precept_Ritual ? "ritual" : "rule";
                    string label = pr.def?.label ?? pr.GetType().Name;
                    precepts.Add(new JObject { ["kind"] = kind, ["label"] = label, ["def"] = pr.def?.defName });
                }
            }
            catch { }
            o["precepts"] = precepts;

            var roles = new JArray();
            try
            {
                var cols = Find.CurrentMap.mapPawns.FreeColonists.ToList();
                foreach (var role in ideo.RolesListForReading)
                {
                    string label = role.def?.label ?? role.GetType().Name;
                    var holders = new JArray();
                    foreach (var c in cols)
                    {
                        try { if (ideo.GetRole(c) == role) holders.Add(c.LabelShortCap); } catch { }
                    }
                    roles.Add(new JObject { ["role"] = label, ["holders"] = holders });
                }
            }
            catch { }
            o["roles"] = roles;

            var rituals = new JArray();
            try
            {
                foreach (var r in ideo.PreceptsListForReading.OfType<Precept_Ritual>())
                {
                    string label = r.def?.label ?? "ritual";
                    var ro = new JObject { ["ritual"] = label, ["anytime"] = r.isAnytime };
                    var obs = new JArray();
                    try
                    {
                        foreach (var ob in r.activeObligations.Take(8))
                        {
                            string? targetId = null; string? targetLabel = null; string? cell = null;
                            try
                            {
                                var t = ob.targetA;
                                if (t.HasThing && t.Thing != null) { targetId = t.Thing.ThingID; targetLabel = t.Thing.LabelCap.ToString().StripTags(); }
                                else if (t.IsValid) cell = t.Cell.x + "," + t.Cell.z;
                            }
                            catch { }
                            obs.Add(new JObject
                            {
                                ["status"] = IdeoLogic.ObligationStatus(targetId != null || cell != null, targetLabel ?? cell, r.isAnytime),
                                ["target"] = targetId, ["target_label"] = targetLabel, ["cell"] = cell,
                            });
                        }
                    }
                    catch { }
                    ro["obligations"] = obs;
                    rituals.Add(ro);
                }
            }
            catch { }
            o["rituals"] = rituals;
            return o;
        }
    }
}
