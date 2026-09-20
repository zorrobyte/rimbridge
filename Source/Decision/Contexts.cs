using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimBridge.State;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridge.Decision
{
    /// <summary>
    /// Compact AI-ready snapshots: state.pawn_context (one pawn, everything a decision needs) and
    /// state.colony_context (the strategic layer: food, medicine, power, beds, threats, backlogs).
    /// Both reuse Snapshot helpers so the numbers match state.summary.
    /// </summary>
    public static class Contexts
    {
        static Map Map() { GameCtl.GameControl.RequirePlaying(); return Find.CurrentMap; }

        [Rpc("state.pawn_context", "{pawn: id|name} one compact AI-ready snapshot: health, needs, mood + break risk, skills, job, drafted, weapon + range, position, nearby threats, nearby injured, fire, colony emergencies")]
        public static JToken PawnContext(JObject p)
        {
            var map = Map();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            var o = Render.PawnHandle(pawn);
            o["tick"] = Find.TickManager.TicksGame;
            o["pos"] = Snapshot.Cell(pawn.Spawned ? pawn.Position : pawn.PositionHeld);
            o["job"] = Snapshot.JobText(pawn);
            if (pawn.CurJob?.def != null) o["job_def"] = pawn.CurJob.def.defName;

            // Health
            o["health"] = Math.Round(pawn.health.summaryHealth.SummaryHealthPercent * 100);
            float bleed = pawn.health.hediffSet.BleedRateTotal;
            if (bleed > 0.01f) o["bleeding"] = Math.Round(bleed, 2);
            o["pain"] = Math.Round(pawn.health.hediffSet.PainTotal * 100);
            o["needs_tending"] = pawn.health.HasHediffsNeedingTend();
            o["ticks_to_bleed_out"] = HealthUtility.TicksUntilDeathDueToBloodLoss(pawn);
            var worstCaps = DefDatabase<PawnCapacityDef>.AllDefs
                .Where(c => c.showOnHumanlikes)
                .Select(c => (name: c.defName, level: pawn.health.capacities.GetLevel(c)))
                .OrderBy(x => x.level).Take(3)
                .Select(x => $"{x.name} {Math.Round(x.level * 100)}");
            o["worst_capacities"] = string.Join(", ", worstCaps);

            // Needs + mood + break risk
            if (pawn.needs != null)
            {
                var needs = new JObject();
                foreach (var n in pawn.needs.AllNeeds) needs[n.def.defName] = Math.Round(n.CurLevelPercentage * 100);
                o["needs"] = needs;
                if (pawn.needs.mood != null)
                {
                    double mood = Math.Round(pawn.needs.mood.CurLevelPercentage * 100);
                    o["mood"] = mood;
                    var br = pawn.mindState?.mentalBreaker;
                    if (br != null)
                    {
                        double minor = Math.Round(br.BreakThresholdMinor * 100), major = Math.Round(br.BreakThresholdMajor * 100), extreme = Math.Round(br.BreakThresholdExtreme * 100);
                        o["break_thresholds"] = new JArray(minor, major, extreme);
                        o["break_risk"] = DecisionLogic.BreakRisk(mood, minor, major, extreme);
                    }
                }
            }
            if (pawn.InMentalState) o["mental_state"] = pawn.MentalStateDef?.defName;

            // Skills with passions
            if (pawn.skills != null)
                o["skills"] = new JObject(pawn.skills.skills.Where(s => !s.TotallyDisabled)
                    .Select(s => new JProperty(s.def.defName, $"{s.Level}{(s.passion == Passion.Major ? "!!" : s.passion == Passion.Minor ? "!" : "")}")));

            // Weapon + range
            var primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                float range = primary.def.Verbs?.FirstOrDefault()?.range ?? (primary.def.IsMeleeWeapon ? 1.5f : 0f);
                o["weapon"] = new JObject { ["label"] = primary.LabelCap.ToString(), ["range"] = Math.Round(range, 1), ["melee"] = primary.def.IsMeleeWeapon };
            }

            // Nearby threats (nearest 5 hostiles with distance)
            var foes = new List<JObject>();
            int foesInRange = 0;
            float sightRange = primary?.def.Verbs?.FirstOrDefault(v => v.range > 4f)?.range ?? 25f;
            foreach (var t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (t.Thing is not Pawn e || !e.Spawned || e.Dead) continue;
                try { if (t.ThreatDisabled(null)) continue; } catch { }
                float d = pawn.Spawned ? pawn.Position.DistanceTo(e.Position) : 999f;
                if (d <= sightRange) foesInRange++;
                foes.Add(new JObject { ["id"] = e.ThingID, ["label"] = e.LabelShortCap, ["kind"] = e.kindDef?.defName, ["dist"] = Math.Round(d), ["downed"] = e.Downed });
            }
            o["threats_near"] = new JArray(foes.OrderBy(x => (float)x["dist"]!).Take(5));
            o["threats_in_sight_range"] = foesInRange;

            // Nearby injured colonists (triage radius 30)
            int injuredNear = 0;
            if (pawn.Spawned)
                foreach (var q in map.mapPawns.FreeColonistsSpawned.Concat(map.mapPawns.PrisonersOfColony.Where(x => x.Spawned)))
                {
                    if (q == pawn || q.Dead || !q.health.HasHediffsNeedingTend()) continue;
                    if (q.Position.DistanceTo(pawn.Position) <= 30f) injuredNear++;
                }
            o["injured_near"] = injuredNear;

            // Fire + colony emergencies
            var home = map.areaManager.Home;
            int fires = 0;
            try { fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count(f => f.Spawned && (home == null || home[f.Position])); } catch { }
            o["fires_home"] = fires;
            o["emergencies"] = new JObject
            {
                ["danger"] = map.dangerWatcher.DangerRating.ToString(),
                ["downed_colonists"] = map.mapPawns.FreeColonists.Count(q => q.Downed),
                ["needing_tend"] = map.mapPawns.FreeColonists.Concat(map.mapPawns.PrisonersOfColony).Count(q => !q.Dead && HealthAIUtility.ShouldBeTendedNowByPlayer(q)),
                ["enemy_near_25"] = pawn.Spawned && GenAI.EnemyIsNear(pawn, 25f),
            };
            return o;
        }

        [Rpc("state.colony_context", "compact colony snapshot for the strategic layer: food days, medicine, power, beds, injuries, threats, construction backlog, shortages, animals, research, bills, idle workers")]
        public static JToken ColonyContext(JObject p)
        {
            var map = Map();
            var cols = map.mapPawns.FreeColonists;
            var o = new JObject { ["tick"] = Find.TickManager.TicksGame, ["day"] = GenDate.DaysPassed, ["hour"] = GenLocalDate.HourInteger(map) };

            // Food + medicine
            float nutrition = map.resourceCounter.TotalHumanEdibleNutrition;
            float foodDays = cols.Count > 0 ? nutrition / (cols.Count * 1.6f) : 0;
            o["food_days"] = Math.Round(foodDays, 1);
            o["nutrition"] = Math.Round(nutrition, 1);
            int medInd = map.resourceCounter.GetCount(ThingDefOf.MedicineIndustrial);
            int medHerb = map.resourceCounter.GetCount(ThingDefOf.MedicineHerbal);
            o["medicine"] = new JObject { ["industrial"] = medInd, ["herbal"] = medHerb };

            // Power
            var power = Snapshot.PowerSummary(map);
            o["power_net_gain_w"] = power["net_gain_w"];
            o["power_stored_wd"] = power["stored_wd"];

            // Beds: total / medical / occupied
            int beds = 0, medBeds = 0, occupied = 0, occupiedMed = 0;
            try
            {
                foreach (var b in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>())
                {
                    beds++;
                    bool med = b.Medical;
                    if (med) medBeds++;
                    bool occ = b.CurOccupants.Any();
                    if (occ) { occupied++; if (med) occupiedMed++; }
                }
            }
            catch { }
            o["beds"] = new JObject { ["total"] = beds, ["medical"] = medBeds, ["occupied"] = occupied, ["medical_free"] = Math.Max(0, medBeds - occupiedMed) };

            // Injuries
            int needingTend = 0, downed = 0, bleeding = 0;
            foreach (var q in cols.Concat(map.mapPawns.PrisonersOfColony))
            {
                if (q.Dead) continue;
                if (q.Downed) downed++;
                if (q.health.hediffSet.BleedRateTotal > 0.01f) bleeding++;
                try { if (HealthAIUtility.ShouldBeTendedNowByPlayer(q)) needingTend++; } catch { }
            }
            o["injuries"] = new JObject { ["needing_tend"] = needingTend, ["downed"] = downed, ["bleeding"] = bleeding };

            // Threats
            int hostiles = 0;
            try { hostiles = map.attackTargetsCache.TargetsHostileToColony.Count(t => t.Thing.Spawned && !t.ThreatDisabled(null)); } catch { }
            o["threats"] = new JObject { ["hostiles"] = hostiles, ["danger"] = map.dangerWatcher.DangerRating.ToString() };

            // Backlogs
            int blueprints = 0, frames = 0;
            try
            {
                blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count(b => b.Faction == Faction.OfPlayer);
                frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count(b => b.Faction == Faction.OfPlayer);
            }
            catch { }
            o["construction"] = new JObject { ["blueprints"] = blueprints, ["frames"] = frames, ["designations"] = map.designationManager.AllDesignations.Count };

            // Shortages (flagged only when they bite)
            var shortages = new JArray();
            if (foodDays < 3f) shortages.Add($"food {Math.Round(foodDays, 1)} days");
            if (medInd + medHerb == 0 && needingTend > 0) shortages.Add("no medicine with patients waiting");
            if (map.resourceCounter.GetCount(ThingDefOf.ComponentIndustrial) == 0 && (blueprints > 0 || frames > 0)) shortages.Add("no components with construction pending");
            o["shortages"] = shortages;

            // Animals, research, bills, idle
            o["animals"] = map.mapPawns.SpawnedColonyAnimals.Count;
            o["research"] = new JObject { ["current"] = Find.ResearchManager.GetProject()?.defName, ["progress"] = Find.ResearchManager.GetProject() is { } rp ? Math.Round(rp.ProgressPercent * 100) : 0 };
            int bills = 0, suspended = 0;
            try
            {
                foreach (var t in map.listerThings.AllThings)
                    if (t is IBillGiver bg)
                        foreach (var b in bg.BillStack.Bills) { bills++; if (b.suspended) suspended++; }
            }
            catch { }
            o["bills"] = new JObject { ["active"] = bills - suspended, ["suspended"] = suspended };
            int idle = 0;
            foreach (var q in cols)
            {
                if (q.Dead || q.Downed || !q.Spawned) continue;
                var d = q.CurJob?.def?.defName;
                if (d == null || d == "Wait_Wander" || d == "GotoWander" || d == "Wait") idle++;
            }
            o["colonists"] = cols.Count;
            o["idle_colonists"] = idle;
            return o;
        }
    }
}
