using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimBridge.State;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimBridge.Decision
{
    /// <summary>
    /// A validated job target: either a live thing (resolved by id at execute time) or a map cell.
    /// Stored on the candidate so action.execute rebuilds the job from fresh references instead of
    /// holding stale engine objects across ticks.
    /// </summary>
    public sealed class JobTargetSpec
    {
        public string? ThingId;
        public bool IsCell;
        public int X, Z;
        public static JobTargetSpec None() => new JobTargetSpec();
        public static JobTargetSpec From(LocalTargetInfo t)
        {
            var s = new JobTargetSpec();
            if (t.HasThing) s.ThingId = t.Thing.ThingID;
            else if (t.IsValid) { s.IsCell = true; s.X = t.Cell.x; s.Z = t.Cell.z; }
            return s;
        }
        public LocalTargetInfo Resolve(Map map)
        {
            if (ThingId != null)
            {
                var t = Lookup.ThingOrNull(ThingId);
                if (t == null || t.Destroyed) throw new RpcError($"candidate target {ThingId} is gone");
                return new LocalTargetInfo(t);
            }
            if (IsCell) return new LocalTargetInfo(new IntVec3(X, 0, Z));
            return LocalTargetInfo.Invalid;
        }
    }

    /// <summary>
    /// One legal choice for a pawn, validated against the live game at generation time and re-validated
    /// at execute time. Ids look like "Human1234:tend:Human1235", "Human1234:take_cover:84,61" or
    /// "Human1234:continue_current_job".
    /// </summary>
    public sealed class Candidate
    {
        public string Id = "";
        public string PawnId = "";
        public string Action = "";
        public string Label = "";
        public string Why = "";
        public double Priority;
        public bool Emergency;
        public bool Executable = true;
        public int Tick;
        public string? TargetId;
        public string? TargetLabel;
        // How to rebuild the job at execute time.
        public string Source = "";          // "builtin" or "workgiver:<WorkGiverDef defName>"
        public string JobDef = "";
        public JobTargetSpec TA = JobTargetSpec.None();
        public JobTargetSpec TB = JobTargetSpec.None();
        public JobTargetSpec TC = JobTargetSpec.None();
        public int Count = -1;
        public JobTag Tag = JobTag.Misc;
        public bool DraftFirst;

        public JObject ToJson()
        {
            var o = new JObject
            {
                ["id"] = Id, ["action"] = Action, ["label"] = Label, ["why"] = Why,
                ["priority"] = Math.Round(Priority, 1), ["emergency"] = Emergency,
                ["executable"] = Executable, ["tick"] = Tick,
            };
            if (TargetId != null) o["target"] = TargetId;
            if (TargetLabel != null) o["target_label"] = TargetLabel;
            if (DraftFirst) o["side_effects"] = new JArray("draft");
            return o;
        }
    }

    /// <summary>
    /// decision.candidates + action.execute. Generation asks RimWorld what a pawn can ACTUALLY legally do:
    /// dedicated providers for combat/triage/fire (things the job system doesn't offer on its own) plus a
    /// generic pass over the game's own WorkGiverDefs for economy work (bill, construct, repair, haul...).
    /// Every provider is exception-isolated: one bad giver never breaks the list.
    /// </summary>
    public static class Candidates
    {
        const int MaxCandidates = 100;
        static readonly Dictionary<string, Candidate> Cache = new Dictionary<string, Candidate>();

        // WorkGiver worker class -> candidate action. Matched against the live DefDatabase, so renames
        // and modded givers fail safe (no match = skipped). Tend/rescue/fire have dedicated providers.
        static readonly Dictionary<string, string> WorkActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorkGiver_DoBill"] = "bill",
            ["WorkGiver_ConstructFinishFrames"] = "construct",
            ["WorkGiver_ConstructDeliverResources"] = "construct",
            ["WorkGiver_ConstructDeliverResourcesToBlueprints"] = "construct",
            ["WorkGiver_ConstructDeliverResourcesToFrames"] = "construct",
            ["WorkGiver_Repair"] = "repair",
            ["WorkGiver_Deconstruct"] = "deconstruct",
            ["WorkGiver_Miner"] = "mine",
            ["WorkGiver_GrowerHarvest"] = "harvest",
            ["WorkGiver_PlantsCut"] = "cut",
            ["WorkGiver_GrowerSow"] = "sow",
            ["WorkGiver_HunterHunt"] = "hunt",
            ["WorkGiver_Researcher"] = "research",
            ["WorkGiver_FixBrokenDownBuilding"] = "repair",
            ["WorkGiver_HaulGeneral"] = "haul",
            ["WorkGiver_HaulCorpses"] = "haul",
        };
        const int PerGiverCap = 12;
        const int WorkTotalCap = 40;

        static Map Map() { GameCtl.GameControl.RequirePlaying(); return Find.CurrentMap; }

        // ---------- decision.candidates ----------

        [Rpc("decision.candidates", "{pawn: id|name, mode?: all|combat|triage|work|emergency (default all), max?: 60} what this pawn can legally do right now: 20-100 validated choices with stable ids for action.execute. Combat/triage/fire come from dedicated legality checks; economy work comes from the game's own workgivers.")]
        public static JToken List(JObject p)
        {
            var map = Map();
            var pawn = Lookup.Colonist(P.Str(p, "pawn"));
            if (pawn.Dead) throw new RpcError("pawn is dead");
            if (!pawn.Spawned || pawn.Map != map) throw new RpcError("pawn is not on the current map");
            string mode = P.Str(p, "mode", "all").ToLowerInvariant();
            if (mode != "all" && mode != "combat" && mode != "triage" && mode != "work" && mode != "emergency")
                throw new RpcError("mode must be all|combat|triage|work|emergency");
            int max = Math.Min(MaxCandidates, Math.Max(1, P.Int(p, "max", 60)));

            var all = new List<Candidate>();
            bool emo = mode == "emergency";
            if (mode == "all" || mode == "work") Safe(all, () => ContinueProvider(all, pawn));
            if (mode == "all" || mode == "combat" || emo) Safe(all, () => CombatProvider(all, pawn, map, emo));
            if (mode == "all" || mode == "triage" || emo) Safe(all, () => TriageProvider(all, pawn, map, emo));
            if (mode == "all" || emo) Safe(all, () => FireProvider(all, pawn, map));
            if (mode == "all" || mode == "work") Safe(all, () => WorkProvider(all, pawn, map));

            var ranked = DecisionLogic.Rank(all.Select(c => new DecisionLogic.RankItem { Id = c.Id, Priority = c.Priority, Emergency = c.Emergency }), max);
            var byId = all.ToDictionary(c => c.Id);
            lock (Cache) foreach (var c in all) Cache[c.Id] = c;
            var arr = new JArray(ranked.Select(r => byId[r.Id].ToJson()));
            return new JObject { ["pawn"] = pawn.ThingID, ["mode"] = mode, ["tick"] = Find.TickManager.TicksGame, ["count"] = arr.Count, ["candidates"] = arr };
        }

        static void Safe(List<Candidate> all, Action provider)
        {
            try { provider(); }
            catch (Exception ex) { BridgeLog.Warning("candidates provider: " + ex.Message); }
        }

        static Candidate New(Pawn pawn, string action, string? target, string label, string why, bool emergency, double priorityBoost = 0)
        {
            var c = new Candidate
            {
                PawnId = pawn.ThingID, Action = action, Label = label, Why = why,
                Emergency = emergency, Priority = DecisionLogic.BasePriority(action) + priorityBoost,
                Tick = Find.TickManager.TicksGame, TargetId = target,
            };
            c.Id = DecisionLogic.FormatId(c.PawnId, action, target);
            return c;
        }

        // ---------- providers ----------

        static readonly HashSet<string> IdleJobs = new HashSet<string> { "Wait_Wander", "GotoWander", "Wait_MaintainPosture", "Wait" };

        static void ContinueProvider(List<Candidate> all, Pawn pawn)
        {
            var job = pawn.CurJob;
            if (job == null || job.def == null || IdleJobs.Contains(job.def.defName)) return;
            var c = New(pawn, "continue_current_job", null, "Continue: " + Snapshot.JobText(pawn), "pawn has an active job", false);
            c.Executable = false;
            c.TargetLabel = job.targetA.HasThing ? job.targetA.Thing.LabelCap.ToString() : null;
            all.Add(c);
        }

        static void CombatProvider(List<Candidate> all, Pawn pawn, Map map, bool emergencyOnly)
        {
            if (pawn.Downed || pawn.drafter == null) return;
            var enemies = new List<Pawn>();
            foreach (var t in map.attackTargetsCache.TargetsHostileToColony)
            {
                if (t.Thing is Pawn e && e.Spawned && !e.Dead) enemies.Add(e);
                if (enemies.Count >= 40) break;
            }
            if (enemies.Count == 0) return;
            enemies.Sort((a, b) => pawn.Position.DistanceToSquared(a.Position).CompareTo(pawn.Position.DistanceToSquared(b.Position)));
            int attacks = 0;
            foreach (var e in enemies.Take(12))
            {
                try
                {
                    var verb = pawn.TryGetAttackVerb(e);
                    if (verb == null) continue;
                    float range = verb.verbProps?.range ?? 1.5f;
                    float dist = pawn.Position.DistanceTo(e.Position);
                    bool melee = verb.IsMeleeAttack;
                    if (melee)
                    {
                        if (!pawn.CanReach(e, PathEndMode.OnCell, Danger.Deadly)) continue;
                    }
                    else if (dist > range || !GenSight.LineOfSight(pawn.Position, e.Position, map)) continue;
                    if (emergencyOnly && dist > 30f && !pawn.Drafted) continue;
                    var c = New(pawn, "attack", e.ThingID, (melee ? "Melee " : "Shoot ") + e.LabelShortCap, $"{e.kindDef?.label ?? e.def.label} at {Math.Round(dist)} cells" + (melee ? "" : ", in range with line of sight"), pawn.Drafted || dist < 12f);
                    c.TargetLabel = e.LabelCap.ToString();
                    c.Source = "builtin"; c.JobDef = melee ? "AttackMelee" : "AttackStatic";
                    c.TA = JobTargetSpec.From(new LocalTargetInfo(e));
                    c.Tag = JobTag.DraftedOrder; c.DraftFirst = true;
                    all.Add(c);
                    if (++attacks >= 8) break;
                }
                catch (Exception ex) { BridgeLog.Warning("candidates attack: " + ex.Message); }
            }
            // Take cover: standable reachable cells near the pawn with a full-fillage wall between cell and enemy.
            try
            {
                bool hasRangedWeapon = pawn.equipment?.Primary?.def?.Verbs?.Any(v => v.range > 4f) == true;
                if (!hasRangedWeapon || emergencyOnly) return;
                var foe = enemies[0];
                int added = 0;
                foreach (var cell in GenRadial.RadialCellsAround(pawn.Position, 10f, true).Take(250))
                {
                    if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                    if (!pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) continue;
                    if (!HasWallBetween(cell, foe.Position, map)) continue;
                    var c = New(pawn, "take_cover", cell.x + "," + cell.z, "Take cover " + Locate.Describe(cell, map), "wall between cell and " + foe.LabelShortCap, false);
                    c.Source = "builtin"; c.JobDef = "Goto";
                    c.TA = JobTargetSpec.From(new LocalTargetInfo(cell));
                    c.Tag = JobTag.DraftedOrder; c.DraftFirst = true;
                    all.Add(c);
                    if (++added >= 3) break;
                }
            }
            catch (Exception ex) { BridgeLog.Warning("candidates cover: " + ex.Message); }
        }

        static bool HasWallBetween(IntVec3 cell, IntVec3 foe, Map map)
        {
            // Any full-fillage building adjacent to the cell on the foe's side counts as cover.
            foreach (var adj in GenAdj.CellsAdjacent8Way(new TargetInfo(cell, map)))
            {
                if (!adj.InBounds(map)) continue;
                var edifice = adj.GetEdifice(map);
                if (edifice != null && edifice.def.Fillage == FillCategory.Full)
                {
                    // Roughly between cell and foe: the wall must be closer to the foe than the cell is.
                    if (adj.DistanceTo(foe) < cell.DistanceTo(foe)) return true;
                }
            }
            return false;
        }

        static void TriageProvider(List<Candidate> all, Pawn pawn, Map map, bool emergencyOnly)
        {
            if (pawn.Downed) return;
            // Rescue: the game's own rescue workgiver decides legality (faction, bed available, reservable).
            try
            {
                var worker = RescueWorker();
                if (worker != null)
                {
                    int n = 0;
                    foreach (var v in map.mapPawns.SpawnedDownedPawns.Take(20))
                    {
                        if (v == pawn || v.Dead) continue;
                        if (!worker.HasJobOnThing(pawn, v, false)) continue;
                        var job = worker.JobOnThing(pawn, v, false);
                        if (job == null) continue;
                        bool urgent = v.health.hediffSet.BleedRateTotal > 0.3f || HealthUtility.TicksUntilDeathDueToBloodLoss(v) < 30000;
                        if (emergencyOnly && !urgent) continue;
                        var c = FromJob(pawn, "rescue", v, "Rescue " + v.LabelShortCap, $"downed{(urgent ? ", urgent" : "")}; bed reserved by the job", urgent);
                        if (c != null) all.Add(c);
                        if (++n >= 6) break;
                    }
                }
            }
            catch (Exception ex) { BridgeLog.Warning("candidates rescue: " + ex.Message); }
            // Tend: needs-tend-by-player + reachable medicine + reservable. Patients: colonists + prisoners.
            try
            {
                int n = 0;
                var patients = map.mapPawns.FreeColonists.Concat(map.mapPawns.PrisonersOfColony).Take(30);
                foreach (var pt in patients)
                {
                    if (pt == pawn || pt.Dead) continue;
                    if (!HealthAIUtility.ShouldBeTendedNowByPlayer(pt)) continue;
                    if (HealthAIUtility.FindBestMedicine(pawn, pt) == null) continue;
                    if (!pawn.CanReserveAndReach(pt, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, false)) continue;
                    bool urgent = pt.health.hediffSet.BleedRateTotal > 0.3f || HealthUtility.TicksUntilDeathDueToBloodLoss(pt) < 30000;
                    if (emergencyOnly && !urgent) continue;
                    var c = New(pawn, "tend", pt.ThingID, "Tend " + pt.LabelShortCap, $"needs tending{(urgent ? " urgently" : "")}; medicine available and reservable", urgent);
                    c.TargetLabel = BleedLabel(pt);
                    c.Source = "builtin"; c.JobDef = "TendPatient";
                    c.TA = JobTargetSpec.From(new LocalTargetInfo(pt));
                    all.Add(c);
                    if (++n >= 6) break;
                }
            }
            catch (Exception ex) { BridgeLog.Warning("candidates tend: " + ex.Message); }
            // Rest until healed, when the game itself says the pawn needs medical rest.
            try
            {
                if (!emergencyOnly && HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn) && !pawn.InBed())
                {
                    var c = New(pawn, "rest", null, "Rest until healed", "pawn needs medical rest", true);
                    c.Source = "builtin"; c.JobDef = "LayDown";
                    all.Add(c);
                }
            }
            catch (Exception ex) { BridgeLog.Warning("candidates rest: " + ex.Message); }
        }

        static string BleedLabel(Pawn pt)
        {
            var worst = pt.health.hediffSet.hediffs.Where(h => h.Visible).OrderByDescending(h => h.Severity).FirstOrDefault();
            string s = worst?.LabelCap.ToString() ?? "injured";
            if (pt.health.hediffSet.BleedRateTotal > 0.01f) s += $", bleeding {Math.Round(pt.health.hediffSet.BleedRateTotal, 2)}/s";
            return Snapshot.Trunc(s, 120);
        }

        static WorkGiver_Scanner? RescueWorker()
        {
            foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
                if (def.Worker is WorkGiver_Scanner w && w.GetType().Name == "WorkGiver_RescueDowned") return w;
            return null;
        }

        static void FireProvider(List<Candidate> all, Pawn pawn, Map map)
        {
            if (pawn.Downed || pawn.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter)) return;
            var home = map.areaManager.Home;
            if (home == null) return;
            try
            {
                int n = 0;
                foreach (var fire in map.listerThings.ThingsOfDef(ThingDefOf.Fire).Take(15))
                {
                    if (!fire.Spawned || !home[fire.Position]) continue;
                    if (!pawn.CanReach(fire, PathEndMode.Touch, Danger.Deadly)) continue;
                    if (!pawn.CanReserve(fire, 1, -1)) continue;
                    var c = New(pawn, "extinguish", fire.ThingID, "Extinguish fire " + Locate.Describe(fire.Position, map), "fire in home area, reachable", true);
                    c.Source = "builtin"; c.JobDef = "BeatFire";
                    c.TA = JobTargetSpec.From(new LocalTargetInfo(fire));
                    all.Add(c);
                    if (++n >= 8) break;
                }
            }
            catch (Exception ex) { BridgeLog.Warning("candidates fire: " + ex.Message); }
        }

        static void WorkProvider(List<Candidate> all, Pawn pawn, Map map)
        {
            if (pawn.Downed || pawn.workSettings == null) return;
            int total = 0;
            foreach (var def in DefDatabase<WorkGiverDef>.AllDefs)
            {
                if (total >= WorkTotalCap) break;
                var worker = def.Worker as WorkGiver_Scanner;
                if (worker == null) continue;
                if (!WorkActions.TryGetValue(worker.GetType().Name, out var kind)) continue;
                // Work type disabled for this pawn (or incapable): vanilla WouldDoWorkType check via priorities.
                try
                {
                    int n = 0;
                    foreach (var t in PotentialThings(worker, pawn).Take(40))
                    {
                        if (total >= WorkTotalCap || n >= PerGiverCap) break;
                        if (t == null || !t.Spawned || t.Map != map) continue;
                        if (!worker.HasJobOnThing(pawn, t, false)) continue;
                        Job? job;
                        try { job = worker.JobOnThing(pawn, t, false); } catch { continue; }
                        if (job == null) continue;
                        var c = FromJob(pawn, kind == "bill" ? BillAction(t) : kind, t, null, null, false, job, worker, t);
                        if (c != null) { all.Add(c); n++; total++; }
                    }
                    // Cell-based givers (mining): designated cells.
                    foreach (var cell in PotentialCells(worker, pawn).Take(40))
                    {
                        if (total >= WorkTotalCap || n >= PerGiverCap) break;
                        if (!cell.InBounds(map)) continue;
                        if (!worker.HasJobOnCell(pawn, cell, false)) continue;
                        Job? job;
                        try { job = worker.JobOnCell(pawn, cell, false); } catch { continue; }
                        if (job == null) continue;
                        var c = FromJob(pawn, kind, null, null, null, false, job, worker, null, cell);
                        if (c != null) { all.Add(c); n++; total++; }
                    }
                }
                catch (Exception ex) { BridgeLog.Warning($"candidates work {def.defName}: " + ex.Message); }
            }
        }

        static IEnumerable<Thing> PotentialThings(WorkGiver_Scanner worker, Pawn pawn)
        {
            IEnumerable<Thing>? e = null;
            try { e = worker.PotentialWorkThingsGlobal(pawn); } catch { }
            return e ?? Enumerable.Empty<Thing>();
        }

        static IEnumerable<IntVec3> PotentialCells(WorkGiver_Scanner worker, Pawn pawn)
        {
            IEnumerable<IntVec3>? e = null;
            try { e = worker.PotentialWorkCellsGlobal(pawn); } catch { }
            return e ?? Enumerable.Empty<IntVec3>();
        }

        static string BillAction(Thing table)
        {
            // Food-producing bills read as cook, everything else as craft.
            try
            {
                if (table is IBillGiver bg)
                    foreach (var b in bg.BillStack.Bills)
                    {
                        if (b.suspended || !b.ShouldDoNow()) continue;
                        if (b.recipe?.products?.Any(p => p.thingDef.IsNutritionGivingIngestible) == true) return "cook";
                        return "craft";
                    }
            }
            catch { }
            return "craft";
        }

        static Candidate? FromJob(Pawn pawn, string action, Thing? target, string? label, string? why, bool emergency, Job? job = null, WorkGiver_Scanner? worker = null, Thing? jobThing = null, IntVec3 jobCell = default)
        {
            job ??= worker?.JobOnThing(pawn, jobThing!, false);
            if (job?.def == null) return null;
            string? targetId = target?.ThingID ?? (jobCell.IsValid ? jobCell.x + "," + jobCell.z : null);
            string targetLabel = target != null ? Snapshot.Trunc(target.LabelCap.ToString(), 80)
                : jobCell.IsValid ? Locate.Describe(jobCell, pawn.Map) : "";
            var c = New(pawn, action, targetId,
                label ?? $"{Cap(action)} {targetLabel}",
                why ?? JobWhy(action, targetLabel, job, worker, pawn), emergency);
            c.TargetLabel = targetLabel == "" ? null : targetLabel;
            c.Source = worker != null ? DecisionLogic.WorkgiverSource(WorkerDefName(worker)) : "builtin";
            if (worker != null) { try { c.Tag = worker.def.tagToGive; } catch { } }
            c.JobDef = job.def.defName;
            c.TA = JobTargetSpec.From(job.targetA);
            c.TB = JobTargetSpec.From(job.targetB);
            c.TC = JobTargetSpec.From(job.targetC);
            try { c.Count = job.count; } catch { }
            try
            {
                TargetInfo? ti = null;
                if (job.targetA.HasThing) ti = new TargetInfo(job.targetA.Thing);
                else if (job.targetA.IsValid) ti = new TargetInfo(job.targetA.Cell, pawn.Map);
                if (ti.HasValue) c.Priority += Math.Max(0, Math.Min(9, worker?.GetPriority(pawn, ti.Value) ?? 0f));
            }
            catch { }
            return c;
        }

        static string WorkerDefName(WorkGiver_Scanner worker)
        {
            try { return worker.def?.defName ?? ""; } catch { return ""; }
        }

        static string JobWhy(string action, string targetLabel, Job job, WorkGiver_Scanner? worker, Pawn pawn)
            => action switch
            {
                "rescue" => "workgiver validated rescue",
                "haul" => "needs hauling to storage",
                "construct" => "blueprint/frame needs work",
                "repair" => "building damaged",
                "mine" => "designated mining",
                "harvest" => "harvestable crop or marked tree",
                "cut" => "cuttable plant",
                "sow" => "growing zone needs sowing",
                "hunt" => "marked for hunting",
                "research" => "active research bench",
                "deconstruct" => "marked for deconstruction",
                "cook" or "craft" => "active bill",
                _ => "workgiver validated",
            };

        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ---------- action.execute ----------

        [Rpc("action.execute", "{candidate: id from decision.candidates, max_stale_ticks?: 2500} re-validate and run a candidate through the real job system. Rejects stale ids (tick age) and targets that moved on (dead, reserved, healed, burned out). Returns the job the pawn actually took.")]
        public static JToken Execute(JObject p)
        {
            var map = Map();
            string id = P.Str(p, "candidate");
            int maxStale = P.Int(p, "max_stale_ticks", 2500);
            Candidate c;
            lock (Cache) Cache.TryGetValue(id, out c!);
            if (c == null) throw new RpcError("unknown or expired candidate id; call decision.candidates again");
            if (!c.Executable) return new JObject { ["ok"] = true, ["noop"] = true, ["id"] = id, ["job"] = "(already doing it)" };
            int now = Find.TickManager.TicksGame;
            if (DecisionLogic.IsStale(c.Tick, now, maxStale))
                throw new RpcError($"candidate is stale (issued tick {c.Tick}, now {now}); call decision.candidates again");
            var pawn = Lookup.PawnOrNull(c.PawnId);
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map != map)
                throw new RpcError("pawn is gone (dead, despawned or off-map)");
            if (pawn.Downed && c.Action != "rest") throw new RpcError("pawn is downed");

            Job job = Rebuild(pawn, c, map);
            job.playerForced = true;
            if (c.DraftFirst && pawn.drafter != null && !pawn.Drafted) pawn.drafter.Drafted = true;
            if (!pawn.jobs.TryTakeOrderedJob(job, c.Tag))
                throw new RpcError("the game refused the job (reservation lost or target invalid)");
            Hooks.RaiseManualTouch(pawn, "action.execute:" + c.Action);
            var o = new JObject { ["ok"] = true, ["id"] = id, ["action"] = c.Action, ["job"] = Snapshot.JobText(pawn), ["tick"] = now };
            if (c.DraftFirst) o["drafted"] = pawn.Drafted;
            EventLedger.Add("action", $"{pawn.LabelShortCap}: {c.Label}", new JObject { ["candidate"] = id, ["action"] = c.Action }, pawn.PositionHeld, pawn.ThingID);
            return o;
        }

        static Job Rebuild(Pawn pawn, Candidate c, Map map)
        {
            // Workgiver candidates go back through the giver: it re-checks reservations, bills, reachability.
            if (DecisionLogic.TrySplitSource(c.Source, out string giverDef))
            {
                var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giverDef);
                var worker = def?.Worker as WorkGiver_Scanner;
                if (worker == null) throw new RpcError("workgiver is gone");
                var t = c.TA.Resolve(map);
                if (t.HasThing)
                {
                    if (!worker.HasJobOnThing(pawn, t.Thing, false))
                        throw new RpcError("no longer legal (reservation lost, bill done, or unreachable)");
                    return worker.JobOnThing(pawn, t.Thing, false) ?? throw new RpcError("workgiver produced no job");
                }
                if (t.IsValid)
                {
                    if (!worker.HasJobOnCell(pawn, t.Cell, false))
                        throw new RpcError("no longer legal on that cell");
                    return worker.JobOnCell(pawn, t.Cell, false) ?? throw new RpcError("workgiver produced no job");
                }
                throw new RpcError("candidate target is gone");
            }
            // Builtins re-validate against the same rules used at generation.
            return c.Action switch
            {
                "tend" => BuildTend(pawn, c, map),
                "rescue" => BuildRescue(pawn, c, map),
                "extinguish" => BuildExtinguish(pawn, c, map),
                "attack" => BuildAttack(pawn, c, map),
                "take_cover" => BuildCover(pawn, c, map),
                "rest" => BuildRest(pawn),
                _ => throw new RpcError($"unknown candidate action '{c.Action}'"),
            };
        }

        static Pawn TargetPawn(Candidate c, Map map, string action)
        {
            var t = c.TA.Resolve(map);
            if (!t.HasThing || t.Thing is not Pawn p || p.Dead) throw new RpcError($"{action} target is gone");
            return p;
        }

        static Job BuildTend(Pawn pawn, Candidate c, Map map)
        {
            var pt = TargetPawn(c, map, "tend");
            if (!HealthAIUtility.ShouldBeTendedNowByPlayer(pt)) throw new RpcError($"{pt.LabelShortCap} no longer needs tending");
            if (HealthAIUtility.FindBestMedicine(pawn, pt) == null) throw new RpcError("no reachable medicine");
            if (!pawn.CanReserveAndReach(pt, PathEndMode.OnCell, Danger.Deadly, 1, -1, null, false))
                throw new RpcError("patient no longer reservable/reachable");
            var job = JobMaker.MakeJob(JobDefOf.TendPatient, pt);
            Hooks.RaiseManualTouch(pt, "action.execute:tend");
            return job;
        }

        static Job BuildRescue(Pawn pawn, Candidate c, Map map)
        {
            var victim = TargetPawn(c, map, "rescue");
            var worker = RescueWorker() ?? throw new RpcError("rescue workgiver missing");
            if (!worker.HasJobOnThing(pawn, victim, false))
                throw new RpcError("rescue no longer legal (enemy near, no bed, or reserved)");
            Hooks.RaiseManualTouch(victim, "action.execute:rescue");
            return worker.JobOnThing(pawn, victim, false) ?? throw new RpcError("rescue produced no job");
        }

        static Job BuildExtinguish(Pawn pawn, Candidate c, Map map)
        {
            var t = c.TA.Resolve(map);
            if (!t.HasThing || t.Thing.Destroyed || !t.Thing.Spawned || t.Thing.def != ThingDefOf.Fire)
                throw new RpcError("fire is out");
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter)) throw new RpcError("pawn cannot fight fires");
            if (!pawn.CanReach(t.Thing, PathEndMode.Touch, Danger.Deadly) || !pawn.CanReserve(t.Thing, 1, -1))
                throw new RpcError("fire no longer reachable/reservable");
            return JobMaker.MakeJob(JobDefOf.BeatFire, t.Thing);
        }

        static Job BuildAttack(Pawn pawn, Candidate c, Map map)
        {
            var enemy = TargetPawn(c, map, "attack");
            if (!enemy.HostileTo(Faction.OfPlayer)) throw new RpcError("target is no longer hostile");
            var verb = pawn.TryGetAttackVerb(enemy) ?? throw new RpcError("no usable weapon against that target");
            float range = verb.verbProps?.range ?? 1.5f;
            if (verb.IsMeleeAttack)
            {
                if (!pawn.CanReach(enemy, PathEndMode.OnCell, Danger.Deadly)) throw new RpcError("cannot reach target");
                pawn.mindState.enemyTarget = enemy;
                return JobMaker.MakeJob(JobDefOf.AttackMelee, enemy);
            }
            if (pawn.Position.DistanceTo(enemy.Position) > range || !GenSight.LineOfSight(pawn.Position, enemy.Position, map))
                throw new RpcError("target out of range or line of sight");
            pawn.mindState.enemyTarget = enemy;
            var job = JobMaker.MakeJob(JobDefOf.AttackStatic, enemy);
            job.endIfCantShootTargetFromCurPos = false;
            return job;
        }

        static Job BuildCover(Pawn pawn, Candidate c, Map map)
        {
            var t = c.TA.Resolve(map);
            if (!t.IsValid || !t.Cell.InBounds(map) || !t.Cell.Standable(map))
                throw new RpcError("cover cell is gone");
            if (!pawn.CanReach(t.Cell, PathEndMode.OnCell, Danger.Deadly)) throw new RpcError("cannot reach cover");
            return JobMaker.MakeJob(JobDefOf.Goto, t.Cell);
        }

        static Job BuildRest(Pawn pawn)
        {
            if (!HealthAIUtility.ShouldSeekMedicalRest(pawn)) throw new RpcError("pawn no longer needs medical rest");
            return JobMaker.MakeJob(JobDefOf.LayDown);
        }
    }
}
