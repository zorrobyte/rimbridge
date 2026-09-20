using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridge.Ledger
{
    /// <summary>
    /// Main-thread-only throttle + watch state for decision-trigger events. Patch postfixes and the
    /// DecisionWatchComponent below are the only writers, and all run on the game thread.
    /// </summary>
    static class DecisionThrottle
    {
        static readonly Dictionary<string, int> LastFired = new Dictionary<string, int>();

        public static bool Check(string key, int intervalTicks)
        {
            int now = 0;
            try { now = Find.TickManager.TicksGame; } catch { return false; }
            if (LastFired.TryGetValue(key, out int last) && !Decision.DecisionLogic.ShouldFire(last, now, intervalTicks))
                return false;
            LastFired[key] = now;
            if (LastFired.Count > 500)
                foreach (var k in LastFired.Where(kv => now - kv.Value > 60000).Select(kv => kv.Key).Take(100).ToList())
                    LastFired.Remove(k);
            return true;
        }
    }

    static class DecisionPawn
    {
        public static bool IsColony(Pawn? p) => p != null && !p.Dead && (p.IsColonist || p.IsPrisonerOfColony);
    }

    // Pawn_JobTracker.EndCurrentJob is the single funnel for job endings (success, error, interrupts).
    // The prefix snapshots the ending job; the postfix sees the already-started replacement, which is
    // exactly what pawn_idle needs.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    static class Patch_JobEnd
    {
        static void Prefix(Pawn_JobTracker __instance, out Job? __state)
        {
            Job? job = null;
            try { job = __instance.curJob; } catch { }
            __state = job;
        }

        static void Postfix(Pawn ___pawn, JobCondition condition, Job? __state)
        {
            try
            {
                var pawn = ___pawn;
                if (pawn == null || pawn.Dead || !pawn.IsColonist || !pawn.Spawned) return;
                string def = __state?.def?.defName ?? "?";
                if (condition == JobCondition.Succeeded && __state?.def != null)
                {
                    var d = new JObject { ["job"] = def };
                    if (__state.targetA.HasThing) d["target"] = __state.targetA.Thing.ThingID;
                    EventLedger.Add("job_finished", $"{pawn.LabelShortCap} finished {def}", d, pawn.PositionHeld, pawn.ThingID);
                }
                else if (condition == JobCondition.Errored && __state?.def != null)
                {
                    EventLedger.Add("job_failed", $"{pawn.LabelShortCap} failed {def}", new JObject { ["job"] = def }, pawn.PositionHeld, pawn.ThingID);
                }
                // Idle: the replacement job is already current in the postfix.
                string? cur = null;
                try { cur = pawn.CurJob?.def?.defName; } catch { }
                if (cur == null || cur == "Wait_Wander" || cur == "GotoWander" || cur == "Wait")
                {
                    if (DecisionThrottle.Check("idle:" + pawn.ThingID, 2500))
                        EventLedger.Add("pawn_idle", $"{pawn.LabelShortCap} is idle", null, pawn.PositionHeld, pawn.ThingID);
                }
            }
            catch (Exception ex) { BridgeLog.Warning("ledger jobend: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    static class Patch_DamageTaken
    {
        static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            try
            {
                if (!DecisionPawn.IsColony(__instance) || totalDamageDealt < 1f) return;
                if (!__instance.Spawned) return;
                if (!DecisionThrottle.Check("hurt:" + __instance.ThingID, 1500)) return;
                var d = new JObject
                {
                    ["damage"] = Math.Round(totalDamageDealt, 1),
                    ["def"] = dinfo.Def?.defName,
                    ["by"] = (dinfo.Instigator as Thing)?.ThingID,
                    ["health"] = Math.Round(__instance.health.summaryHealth.SummaryHealthPercent * 100),
                };
                EventLedger.Add("pawn_injured", $"{__instance.LabelShortCap} took {d["damage"]} ({dinfo.Def?.label ?? "damage"})", d, __instance.PositionHeld, __instance.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger damage: " + ex.Message); }
        }
    }

    // A hostile dying is combat feedback (my raider went down), distinct from the colony-centric pawn_died.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class Patch_TargetDied
    {
        static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance.Faction == Faction.OfPlayer || !__instance.HostileTo(Faction.OfPlayer)) return;
                var d = new JObject { ["faction"] = __instance.Faction?.Name, ["kind"] = __instance.kindDef?.defName };
                EventLedger.Add("target_died", $"{__instance.LabelShortCap} ({__instance.kindDef?.label}) died", d, __instance.PositionHeld, __instance.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger targetdied: " + ex.Message); }
        }
    }

    /// <summary>
    /// Poll-based decision triggers that have no clean patch point: fires starting, patients waiting,
    /// mental-break risk crossing, enemies entering colonist range, critical resources. Instantiated
    /// automatically like every other GameComponent subclass.
    /// </summary>
    public class DecisionWatchComponent : GameComponent
    {
        bool _init;
        readonly HashSet<string> _fires = new HashSet<string>();
        readonly HashSet<string> _tendWarned = new HashSet<string>();
        readonly HashSet<string> _breakWarned = new HashSet<string>();
        readonly Dictionary<string, bool> _nearThreat = new Dictionary<string, bool>();
        readonly Dictionary<string, bool> _resourceCritical = new Dictionary<string, bool>();

        public DecisionWatchComponent(Game game) { }

        public override void GameComponentTick()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            var map = Find.CurrentMap;
            if (map == null) return;
            int tick = Find.TickManager.TicksGame;
            try
            {
                if (tick % 120 == 0) WatchFires(map);
                if (tick % 300 == 0) WatchEnemyRange(map);
                if (tick % 600 == 0) { WatchPatients(map); WatchBreakRisk(map); }
                if (tick % 2500 == 0) WatchResources(map);
            }
            catch (Exception ex) { BridgeLog.Warning("decision watch: " + ex.Message); }
        }

        void WatchFires(Map map)
        {
            var now = new HashSet<string>();
            var fresh = new List<IntVec3>();
            var home = map.areaManager.Home;
            foreach (var f in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
            {
                if (!f.Spawned) continue;
                now.Add(f.ThingID);
                if (!_fires.Contains(f.ThingID) && (home == null || home[f.Position])) fresh.Add(f.Position);
            }
            _fires.IntersectWith(now);
            if (!_init) { foreach (var id in now) _fires.Add(id); _init = true; return; }
            if (fresh.Count > 0)
            {
                var cells = new JArray(fresh.Take(3).Select(c => new JArray(c.x, c.z)));
                EventLedger.Add("fire_started", $"fire started ({fresh.Count})", new JObject { ["count"] = fresh.Count, ["cells"] = cells }, fresh[0]);
            }
            foreach (var id in now) _fires.Add(id);
        }

        void WatchPatients(Map map)
        {
            var now = new HashSet<string>();
            var fresh = new List<JObject>();
            foreach (var q in map.mapPawns.FreeColonists.Concat(map.mapPawns.PrisonersOfColony))
            {
                if (q.Dead || !q.Spawned) continue;
                bool needs;
                try { needs = HealthAIUtility.ShouldBeTendedNowByPlayer(q); } catch { continue; }
                if (!needs) continue;
                now.Add(q.ThingID);
                if (!_tendWarned.Contains(q.ThingID) && fresh.Count < 3)
                    fresh.Add(new JObject { ["id"] = q.ThingID, ["name"] = q.LabelShortCap });
            }
            _tendWarned.IntersectWith(now);
            if (fresh.Count > 0)
                EventLedger.Add("patient_needs_tending", $"{fresh.Count} pawn(s) need tending", new JObject { ["patients"] = new JArray(fresh) });
            foreach (var id in now) _tendWarned.Add(id);
        }

        void WatchBreakRisk(Map map)
        {
            var now = new HashSet<string>();
            foreach (var q in map.mapPawns.FreeColonists)
            {
                if (q.Dead || !q.Spawned || q.needs?.mood == null || q.mindState?.mentalBreaker == null) continue;
                double mood, minor;
                try
                {
                    mood = q.needs.mood.CurLevelPercentage * 100;
                    minor = q.mindState.mentalBreaker.BreakThresholdMinor * 100;
                }
                catch { continue; }
                if (mood <= minor)
                {
                    now.Add(q.ThingID);
                    if (!_breakWarned.Contains(q.ThingID))
                        EventLedger.Add("colonist_mental_break_risk", $"{q.LabelShortCap} mood {Math.Round(mood)} (minor break at {Math.Round(minor)})",
                            new JObject { ["mood"] = Math.Round(mood), ["minor"] = Math.Round(minor) }, q.PositionHeld, q.ThingID);
                }
                else if (mood > minor + 5) { /* recovered: stays out of `now`, drops from warned below */ }
                else now.Add(q.ThingID); // still low: don't re-fire, don't clear
            }
            _breakWarned.IntersectWith(now);
            foreach (var id in now) _breakWarned.Add(id);
        }

        void WatchEnemyRange(Map map)
        {
            var foes = new List<Pawn>();
            try
            {
                foreach (var t in map.attackTargetsCache.TargetsHostileToColony)
                {
                    if (t.Thing is Pawn e && e.Spawned && !e.Dead) foes.Add(e);
                    if (foes.Count >= 50) break;
                }
            }
            catch { return; }
            var alive = new HashSet<string>();
            foreach (var q in map.mapPawns.FreeColonistsSpawned)
            {
                alive.Add(q.ThingID);
                Pawn? nearest = null; float best = float.MaxValue;
                foreach (var f in foes)
                {
                    float d = q.Position.DistanceTo(f.Position);
                    if (d < best) { best = d; nearest = f; }
                    if (best <= 5f) break;
                }
                bool near = nearest != null && best <= 30f;
                if (near && (!_nearThreat.TryGetValue(q.ThingID, out bool was) || !was))
                    EventLedger.Add("enemy_entered_range", $"{nearest!.LabelShortCap} within {Math.Round(best)} of {q.LabelShortCap}",
                        new JObject { ["enemy"] = nearest.ThingID, ["pawn"] = q.ThingID, ["dist"] = Math.Round(best) }, q.PositionHeld, q.ThingID);
                _nearThreat[q.ThingID] = near;
            }
            foreach (var dead in _nearThreat.Keys.Where(k => !alive.Contains(k)).ToList()) _nearThreat.Remove(dead);
        }

        void WatchResources(Map map)
        {
            var cols = map.mapPawns.FreeColonists;
            float foodDays = cols.Count > 0 ? map.resourceCounter.TotalHumanEdibleNutrition / (cols.Count * 1.6f) : 99f;
            SetCritical("food", foodDays < 2f, new JObject { ["food_days"] = Math.Round(foodDays, 1) }, $"food critical: {Math.Round(foodDays, 1)} days");
            int meds = 0, needing = 0;
            try
            {
                meds = map.resourceCounter.GetCount(ThingDefOf.MedicineIndustrial) + map.resourceCounter.GetCount(ThingDefOf.MedicineHerbal);
                needing = cols.Concat(map.mapPawns.PrisonersOfColony).Count(q => !q.Dead && HealthAIUtility.ShouldBeTendedNowByPlayer(q));
            }
            catch { }
            SetCritical("medicine", meds == 0 && needing > 0, new JObject { ["patients"] = needing }, "no medicine with patients waiting");
        }

        void SetCritical(string kind, bool critical, JObject data, string text)
        {
            _resourceCritical.TryGetValue(kind, out bool was);
            if (critical && !was) EventLedger.Add("resource_critical", text, data);
            _resourceCritical[kind] = critical;
        }
    }
}
