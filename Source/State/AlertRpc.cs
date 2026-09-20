using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimBridge.State
{
    /// <summary>
    /// alerts.detail: every active alert with its culprit targets and a suggested resolution.
    /// state.alerts stays untouched; this is the actionable version.
    /// </summary>
    public static class AlertRpc
    {
        [Rpc("alerts.detail", "active alerts with culprit targets (thing/pawn/tile) and a suggested fix for each")]
        public static JToken Detail(JObject p)
        {
            GameCtl.GameControl.RequirePlaying();
            var arr = new JArray();
            try
            {
                if (!(Find.UIRoot is UIRoot_Play play)) return arr;
                var field = AccessTools.Field(typeof(AlertsReadout), "AllAlerts");
                if (field?.GetValue(play.alerts) is List<Alert> all)
                    foreach (var a in all)
                    {
                        JObject jo;
                        try
                        {
                            if (!a.Active) continue;
                            string label;
                            try { label = a.GetLabel().StripTags(); } catch { continue; }
                            jo = new JObject
                            {
                                ["class"] = a.GetType().Name, ["label"] = label,
                                ["priority"] = a.Priority.ToString(),
                            };
                            try { jo["explanation"] = Snapshot.Trunc(a.GetExplanation().ToString().StripTags(), 300); } catch { }
                            try
                            {
                                var culprits = new JArray(a.GetReport().AllCulprits.Take(8).Select(Culprit).Where(x => x.Count > 0));
                                if (culprits.Count > 0) jo["culprits"] = culprits;
                            }
                            catch { }
                            var s = AlertLogic.Suggest(a.GetType().Name);
                            jo["suggested"] = s.Action;
                            jo["via"] = s.Via;
                        }
                        catch { continue; }
                        arr.Add(jo);
                    }
            }
            catch (Exception ex) { arr.Add(new JObject { ["error"] = ex.Message }); }
            return arr;
        }

        static JObject Culprit(GlobalTargetInfo t)
        {
            var o = new JObject();
            try
            {
                if (!t.IsValid) return o;
                if (t.HasThing && t.Thing != null)
                {
                    o["thing"] = t.Thing.ThingID;
                    try { o["label"] = (t.Thing as Pawn)?.LabelShortCap ?? t.Thing.LabelCap.ToString().StripTags(); } catch { }
                    return o;
                }
                if (t.WorldObject != null) { o["world_id"] = t.WorldObject.ID; o["label"] = t.WorldObject.Label; return o; }
                if (t.Tile.Valid) o["tile"] = (int)t.Tile;
                else if (t.Map != null) o["map"] = t.Map.Index;
                else if (t.Cell.IsValid) o["cell"] = new JArray(t.Cell.x, t.Cell.z);
            }
            catch { }
            return o;
        }
    }
}
