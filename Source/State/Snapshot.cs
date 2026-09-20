using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridge.State
{
    /// <summary>Shared summary builders used by state.* RPCs and the daily ledger snapshot.</summary>
    public static class Snapshot
    {
        public static JObject Daily(Map map)
        {
            var cols = map.mapPawns.FreeColonists;
            // food_days used to come straight from resourceCounter, which iterates the haul-destination groups --
            // storage. That answers "what can a bill consume?", and it was being used to answer "will we starve?".
            // Turn 1 of episode 1 opened with "food_days 0.0 - this is an emergency" beside outside_storage's own
            // food_stacks: 32, and the model spent the whole step on a famine that was not happening.
            double stored = map.resourceCounter.TotalHumanEdibleNutrition;
            var loose = LooseFood(map);
            return new JObject
            {
                ["colonists"] = cols.Count,
                ["downed"] = cols.Count(p => p.Downed),
                ["prisoners"] = map.mapPawns.PrisonersOfColonyCount,
                ["animals"] = map.mapPawns.SpawnedColonyAnimals.Count,
                ["wealth"] = Math.Round(map.wealthWatcher.WealthTotal),
                ["wealth_items"] = Math.Round(map.wealthWatcher.WealthItems),
                ["wealth_buildings"] = Math.Round(map.wealthWatcher.WealthBuildings),
                ["mood_avg"] = cols.Count > 0 ? Math.Round(cols.Where(p => p.needs?.mood != null).Select(p => p.needs.mood.CurLevelPercentage).DefaultIfEmpty(0).Average() * 100) : 0,
                ["nutrition"] = Math.Round(stored, 1),
                ["nutrition_loose"] = Math.Round(loose.Nutrition, 1),
                ["nutrition_forbidden"] = Math.Round(loose.Forbidden, 1),
                ["food_days"] = FoodRules.FoodDays(stored + loose.Nutrition, cols.Count),
                ["food_days_stored"] = FoodRules.FoodDays(stored, cols.Count),
                ["threat_points"] = Math.Round(StorytellerUtility.DefaultThreatPointsNow(map)),
                ["research_done"] = DefDatabase<ResearchProjectDef>.AllDefs.Count(r => r.IsFinished),
                ["temp_outdoor"] = Math.Round(map.mapTemperature.OutdoorTemp),
                ["danger"] = map.dangerWatcher.DangerRating.ToString(),
                ["assisted"] = Ledger.EventLedger.Assisted,
            };
        }

        public static JObject ColonySummary(Map map)
        {
            var o = Daily(map);
            var cols = map.mapPawns.FreeColonists;
            o["date"] = GenDate.DateFullStringAt(Find.TickManager.TicksAbs, Find.WorldGrid.LongLatOf(map.Tile));
            o["day"] = GenDate.DaysPassed;
            o["hour"] = GenLocalDate.HourInteger(map);
            o["season"] = GenLocalDate.Season(map).ToString();
            o["weather"] = map.weatherManager.curWeather?.defName;
            o["temp_seasonal"] = Math.Round(map.mapTemperature.SeasonalTemp);
            o["biome"] = map.Biome?.defName;
            o["growing_now"] = map.mapTemperature.SeasonalTemp > 0; // rough; plants need >0C (>10C for most crops)
            o["home_center"] = Cell(HomeCenter(map));
            o["colonist_list"] = new JArray(cols.Select(p => PawnBrief(p)));
            var hostiles = VisibleHostiles(map);
            o["hostiles"] = Engine.Render.Truncated(new JArray(hostiles.Take(60).Select(t => (JToken)HostileHandle(map, t))), hostiles.Count, 60);
            o["alerts"] = Alerts();
            var problems = HuntingProblems(map);
            if (problems.Count > 0) o["problems"] = new JArray(problems);
            o["research_current"] = Find.ResearchManager.GetProject()?.defName;
            o["research_progress"] = Find.ResearchManager.GetProject() is { } rp ? Math.Round(rp.ProgressPercent * 100) : 0;
            o["pending_letters"] = Find.LetterStack.LettersListForReading.Count;
            o["zones"] = new JArray(map.zoneManager.AllZones.Select(z => ZoneBrief(z, map)));
            o["blueprints"] = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count(b => b.Faction == Faction.OfPlayer);
            o["frames"] = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count(b => b.Faction == Faction.OfPlayer);
            o["designations"] = map.designationManager.AllDesignations.Count;
            o["power"] = PowerSummary(map);
            o["key_stocks"] = KeyStocks(map);
            o["outside_storage"] = OutsideStorage(map);
            o["room_digest"] = RoomDigest(map);
            o["speed"] = (int)Find.TickManager.CurTimeSpeed;
            o["paused"] = Find.TickManager.Paused;
            return o;
        }

        /// <summary>
        /// Everything hostile the player can see. Presence, not danger.
        ///
        /// The old filter was RimWorld's own <c>!t.ThreatDisabled(null)</c>, which is true for a dormant sleeper
        /// AND for a pawn who is merely downed. In episode 1 that erased a raider who was lying 18 cells out with
        /// a cracked radius and a 5.82 bleed rate: the list went non-empty to empty, which from the model's side
        /// is indistinguishable from fleeing. It wrote "FLED. No engagement." into its notebook, stood down, and
        /// cancelled a trap blueprint on the cell her body was blocking.
        ///
        /// Now fog decides -- that is what really keeps an unopened ancient danger secret -- and downed/dormant
        /// ride along as labels. The pawn sweep is deliberate: a downed pawn is threat-disabled and RimWorld is
        /// free to drop her from attackTargetsCache, so the cache alone cannot be trusted to still hold her.
        /// </summary>
        public static List<Thing> VisibleHostiles(Map map)
        {
            var seen = new HashSet<Thing>();
            var list = new List<Thing>();
            foreach (var t in map.attackTargetsCache.TargetsHostileToColony)
            {
                var th = t.Thing;
                if (th == null) continue;
                bool dead = th is Pawn dp ? dp.Dead : th.Destroyed;
                if (!ObservationRules.VisibleHostile(th.Spawned, th.Position.Fogged(map), dead)) continue;
                if (seen.Add(th)) list.Add(th);
            }
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p.Dead || p.Faction == Faction.OfPlayer || !p.HostileTo(Faction.OfPlayer)) continue;
                if (!ObservationRules.VisibleHostile(p.Spawned, p.Position.Fogged(map), p.Dead)) continue;
                if (seen.Add(p)) list.Add(p);
            }
            return list;
        }

        /// <summary>True when the thing is asleep in the game's own sense (mech cluster, sleeping insects).</summary>
        public static bool IsDormant(Thing t)
        {
            try { var c = t.TryGetComp<CompCanBeDormant>(); return c != null && !c.Awake; }
            catch { return false; }
        }

        public static string HostileStatusOf(Thing t)
            => ObservationRules.HostileStatus(t is Pawn p && p.Downed, IsDormant(t));

        /// <summary>A hostile with its state attached, so "not fighting" can never again read as "not there".</summary>
        public static JObject HostileHandle(Map map, Thing t)
        {
            var o = t is Pawn hp ? Engine.Render.PawnHandle(hp) : Engine.Render.ThingHandle(t);
            o["status"] = HostileStatusOf(t);
            o["dist_home"] = (int)t.Position.DistanceTo(HomeCenter(map));
            if (t is Pawn p)
            {
                o["health"] = Math.Round(p.health.summaryHealth.SummaryHealthPercent * 100);
                if (p.health.hediffSet.BleedRateTotal > 0.01f) o["bleeding"] = Math.Round(p.health.hediffSet.BleedRateTotal, 2);
            }
            return o;
        }

        /// <summary>Visible hostiles split by status. Used by the summary and by the danger ledger event.</summary>
        public static ObservationRules.HostileTally Tally(Map map)
        {
            var tally = new ObservationRules.HostileTally();
            foreach (var t in VisibleHostiles(map)) tally.Add(HostileStatusOf(t));
            return tally;
        }

        public static JObject PawnBrief(Pawn p)
        {
            var o = Engine.Render.PawnHandle(p);
            o["mood"] = p.needs?.mood != null ? Math.Round(p.needs.mood.CurLevelPercentage * 100) : (double?)null;
            o["health"] = Math.Round(p.health.summaryHealth.SummaryHealthPercent * 100);
            o["job"] = JobText(p);
            if (p.InMentalState) o["mental_state"] = p.MentalStateDef?.defName;
            if (p.health.hediffSet.BleedRateTotal > 0.01f) o["bleeding"] = Math.Round(p.health.hediffSet.BleedRateTotal, 2);
            if (p.health.HasHediffsNeedingTend()) o["needs_tending"] = true;
            // Top 3 on purpose, not a truncation: the brief reports what a pawn is for, not every skill.
            var top = p.skills?.skills?.Where(s => !s.TotallyDisabled).OrderByDescending(s => s.Level).Take(3).Select(s => $"{s.def.defName} {s.Level}{(s.passion == Passion.Major ? "!!" : s.passion == Passion.Minor ? "!" : "")}");
            if (top != null) o["top_skills"] = string.Join(", ", top);
            o["weapon"] = p.equipment?.Primary?.LabelCap.ToString();
            return o;
        }

        public static string JobText(Pawn p)
        {
            try
            {
                if (p.Dead) return "dead";
                if (p.Downed) return "downed";
                if (p.jobs?.curDriver != null) return p.jobs.curDriver.GetReport().StripTags();
                return p.CurJob?.def?.defName ?? "idle";
            }
            catch { return p.CurJob?.def?.defName ?? "?"; }
        }

        /// <summary>
        /// Hunt designations that no colonist can action.
        ///
        /// Game rule (1.6): <c>WorkGiver_HunterHunt.ShouldSkip</c> blocks a pawn with no ranged hunting weapon and
        /// a pawn wearing a shield belt with a ranged weapon. It ignores the forced flag. The game says so once,
        /// as a transient message at designation time, so the condition is invisible afterwards.
        /// </summary>
        public static List<string> HuntingProblems(Map map)
        {
            int designations = map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Hunt).Count();
            int assigned = 0, armed = 0, shielded = 0;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Downed || p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hunting)) continue;
                assigned++;
                if (!WorkGiver_HunterHunt.HasHuntingWeapon(p)) continue;
                armed++;
                if (WorkGiver_HunterHunt.HasShieldAndRangedWeapon(p)) shielded++;
            }
            return HuntingRules.Problems(designations, assigned, armed, shielded);
        }

        public static JArray Alerts()
        {
            var arr = new JArray();
            try
            {
                if (!(Find.UIRoot is UIRoot_Play play)) return arr;
                var field = AccessTools.Field(typeof(AlertsReadout), "AllAlerts");
                if (field?.GetValue(play.alerts) is List<Alert> all)
                    foreach (var a in all)
                    {
                        if (!a.Active) continue;
                        string label;
                        try { label = a.GetLabel().StripTags(); } catch { continue; }
                        var jo = new JObject { ["label"] = label, ["priority"] = a.Priority.ToString() };
                        try { jo["explanation"] = Trunc(a.GetExplanation().ToString().StripTags(), 400); } catch { }
                        arr.Add(jo);
                    }
            }
            catch (Exception ex) { arr.Add(new JObject { ["error"] = ex.Message }); }
            return arr;
        }

        public static JObject PowerSummary(Map map)
        {
            float gen = 0, use = 0, stored = 0, cap = 0; int nets = 0;
            foreach (var net in map.powerNetManager.AllNetsListForReading)
            {
                nets++;
                gen += net.CurrentEnergyGainRate() * 60f; // Wd/tick -> W
                stored += net.CurrentStoredEnergy();
                foreach (var b in net.batteryComps) cap += b.Props.storedEnergyMax;
                foreach (var c in net.powerComps) if (c.PowerOn && c.PowerOutput < 0) use += -c.PowerOutput;
            }
            return new JObject { ["nets"] = nets, ["stored_wd"] = Math.Round(stored), ["capacity_wd"] = Math.Round(cap), ["consumption_w"] = Math.Round(use), ["net_gain_w"] = Math.Round(gen) };
        }

        static readonly string[] KeyDefs = { "WoodLog", "Steel", "Plasteel", "ComponentIndustrial", "Silver", "Gold", "MedicineHerbal", "MedicineIndustrial", "Cloth", "Leather_Plain", "MealSimple", "MealFine", "Pemmican", "RawRice", "RawPotatoes", "RawCorn", "Hay", "Kibble", "Chemfuel", "Uranium", "Jade", "Beer", "SmokeleafJoint", "Penoxycyline" };

        public static JObject KeyStocks(Map map)
        {
            var o = new JObject();
            foreach (var d in KeyDefs)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(d);
                if (def == null) continue;
                int n = map.resourceCounter.GetCount(def);
                if (n > 0) o[d] = n;
            }
            o["meat_all"] = map.resourceCounter.GetCountIn(ThingCategoryDefOf.MeatRaw);
            o["stone_blocks"] = map.resourceCounter.GetCountIn(ThingCategoryDefOf.StoneBlocks);
            o["meals_all"] = map.resourceCounter.GetCountIn(DefDatabase<ThingCategoryDef>.GetNamed("FoodMeals"));
            return o;
        }

        /// <summary>Haulable items lying outside any storage, the things that rot, deteriorate and get stolen.</summary>
        public static JObject OutsideStorage(Map map)
        {
            int stacks = 0, food = 0, rotting = 0, corpses = 0, forbidden = 0, unroofed = 0, damaged = 0;
            var byCat = new Dictionary<string, int>();
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (!t.Spawned || t.Position.Fogged(map) || t.IsInAnyStorage()) continue;
                if (t.def.thingCategories != null && t.def.thingCategories.Any(c => c.defName == "Chunks" || c.defName == "StoneChunks")) continue; // natural debris, not stock
                stacks++;
                if (t.IsForbidden(Faction.OfPlayer)) forbidden++;
                string cat = t.def.FirstThingCategory?.defName ?? t.def.category.ToString();
                byCat[cat] = byCat.TryGetValue(cat, out var n) ? n + 1 : 1;
                if (t.def.IsNutritionGivingIngestible) food++;
                if (t is Corpse) corpses++;
                var rot = t.TryGetComp<CompRottable>();
                if (rot != null && rot.Stage != RotStage.Fresh) rotting++;
                if (t.def.CanEverDeteriorate && !t.Position.Roofed(map)) unroofed++;   // deteriorates in the open (rain/sun)
                if (t.def.useHitPoints && t.HitPoints < t.MaxHitPoints) damaged++;
            }
            return new JObject { ["stacks"] = stacks, ["forbidden"] = forbidden, ["food_stacks"] = food, ["rotting"] = rotting, ["corpses"] = corpses, ["unroofed_deteriorating"] = unroofed, ["damaged"] = damaged, ["by_category"] = Engine.Render.Truncated(JObject.FromObject(byCat.OrderByDescending(kv => kv.Value).Take(10).ToDictionary(kv => kv.Key, kv => kv.Value)), byCat.Count, 10), ["storage_cells_free"] = FreeStorageCells(map) };
        }

        /// <summary>
        /// A zone, and for a stockpile whether it is actually protecting what is in it.
        ///
        /// The roof check used to exist only in OutsideStorage, which skips stored things, so a stockpile could
        /// rot in the open while every number in the payload looked healthy.
        /// </summary>
        public static JObject ZoneBrief(Zone z, Map map)
        {
            var o = new JObject
            {
                ["label"] = z.label,
                ["type"] = z is Zone_Growing zg ? "growing:" + zg.GetPlantDefToGrow()?.defName : (z is Zone_Stockpile ? "stockpile" : z.GetType().Name),
                ["cells"] = z.Cells.Count,
                ["at"] = Cell(z.Cells.Count > 0 ? z.Cells[0] : IntVec3.Invalid),
            };
            if (!(z is Zone_Stockpile)) return o;
            int unroofed = 0, stacks = 0, deteriorating = 0, rotting = 0;
            foreach (var c in z.Cells)
            {
                bool roofed = c.Roofed(map);
                if (!roofed) unroofed++;
                foreach (var th in c.GetThingList(map))
                {
                    if (!th.def.EverStorable(false)) continue;
                    stacks++;
                    if (th.def.CanEverDeteriorate && !roofed) deteriorating++;
                    var rot = th.TryGetComp<CompRottable>();
                    if (rot != null && rot.Stage != RotStage.Fresh) rotting++;
                }
            }
            o["unroofed_cells"] = unroofed;
            o["stacks"] = stacks;
            o["problems"] = new JArray(StorageRules.Problems(z.Cells.Count, unroofed, deteriorating, rotting));
            return o;
        }

        /// <summary>Human-edible nutrition lying outside storage, and how much of it is merely forbidden.</summary>
        public sealed class LooseFoodTally
        {
            public float Nutrition;
            public float Forbidden;
        }

        /// <summary>
        /// Food on the ground is food. Forbidden food is food too: unforbidding is a single action, so reporting a
        /// famine because nobody has claimed the drop-pod loot yet is the same lie wearing a different hat. It is
        /// reported separately -- nutrition_forbidden -- so the model can see the action it needs to take, but it
        /// is not subtracted from what the colony has.
        /// </summary>
        public static LooseFoodTally LooseFood(Map map)
        {
            var t = new LooseFoodTally();
            foreach (var th in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (!th.Spawned || th.IsInAnyStorage()) continue;   // stored nutrition is already in resourceCounter
                var def = th.def;
                if (!def.IsNutritionGivingIngestible || def.ingestible == null || !def.ingestible.HumanEdible) continue;
                var rot = th.TryGetComp<CompRottable>();
                bool fresh = rot == null || rot.Stage == RotStage.Fresh;
                if (!FoodRules.CountsAsFood(true, fresh, th.Position.Fogged(map))) continue;
                float n = def.GetStatValueAbstract(StatDefOf.Nutrition) * th.stackCount;
                t.Nutrition += n;
                if (th.IsForbidden(Faction.OfPlayer)) t.Forbidden += n;
            }
            return t;
        }

        static int FreeStorageCells(Map map)
        {
            int free = 0;
            foreach (var z in map.zoneManager.AllZones.OfType<Zone_Stockpile>())
                foreach (var c in z.Cells) if (!c.GetThingList(map).Any(t => t.def.EverStorable(false))) free++;
            return free;
        }

        /// <summary>Indoor rooms by role with sizes, makes "everything is a 3x3 box" visible.</summary>
        public static JArray RoomDigest(Map map)
        {
            var arr = new JArray();
            var rooms = map.regionGrid.AllRooms.Where(r => !r.PsychologicallyOutdoors && !r.TouchesMapEdge && r.CellCount < 2000 && r.Role != null && r.Role != RoomRoleDefOf.None).OrderByDescending(r => r.CellCount).ToList();
            foreach (var r in rooms.Take(12))
                arr.Add(new JObject { ["role"] = r.Role.defName, ["cells"] = r.CellCount, ["temp"] = Math.Round(r.Temperature), ["impressiveness"] = Math.Round(r.GetStat(RoomStatDefOf.Impressiveness)), ["owners"] = string.Join(",", r.Owners.Select(x => x.LabelShort)), ["at"] = Cell(r.Cells.FirstOrDefault()) });
            return Engine.Render.Truncated(arr, rooms.Count, 12);
        }

        public static IntVec3 HomeCenter(Map map)
        {
            var home = map.areaManager.Home;
            if (home != null && home.ActiveCells.Any())
            {
                long x = 0, z = 0; int n = 0;
                foreach (var c in home.ActiveCells) { x += c.x; z += c.z; n++; }
                return new IntVec3((int)(x / n), 0, (int)(z / n));
            }
            var bs = map.listerBuildings.allBuildingsColonist;
            if (bs.Count > 0) return new IntVec3((int)bs.Average(b => b.Position.x), 0, (int)bs.Average(b => b.Position.z));
            var cols = map.mapPawns.FreeColonistsSpawned;
            if (cols.Count > 0) return new IntVec3((int)cols.Average(p => p.Position.x), 0, (int)cols.Average(p => p.Position.z));
            return map.Center;
        }

        public static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

        public static JToken Cell(IntVec3 c) => c.IsValid ? new JArray(c.x, c.z) : JValue.CreateNull();
    }
}
