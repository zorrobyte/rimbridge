using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimBridge.Ledger
{
    // Every patch is a postfix wrapped in try/catch: the ledger must never break the game.

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
    static class Patch_Letter
    {
        static void Postfix(Letter let)
        {
            try
            {
                string text = let is ChoiceLetter cl ? cl.Text.ToString().StripTags() : "";
                var data = new JObject { ["label"] = let.Label.ToString().StripTags(), ["def"] = let.def?.defName, ["text"] = text, ["id"] = let.ID };
                if (let is ChoiceLetter cl2 && cl2.quest != null) data["quest"] = cl2.quest.id;
                IntVec3? cell = null; string? tid = null;
                if (let.lookTargets != null && let.lookTargets.IsValid())
                {
                    var t = let.lookTargets.PrimaryTarget;
                    if (t.HasThing) { tid = t.Thing.ThingID; cell = t.Thing.PositionHeld; }
                    else if (t.Cell.IsValid) cell = t.Cell;
                }
                EventLedger.Add("letter", let.Label.ToString().StripTags(), data, cell, tid);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger letter: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), typeof(Message), typeof(bool))]
    static class Patch_Message
    {
        static void Postfix(Message msg)
        {
            try
            {
                if (msg.def == MessageTypeDefOf.SilentInput || msg.def == MessageTypeDefOf.RejectInput) return;
                IntVec3? cell = null; string? tid = null;
                if (msg.lookTargets != null && msg.lookTargets.IsValid())
                {
                    var t = msg.lookTargets.PrimaryTarget;
                    if (t.HasThing) { tid = t.Thing.ThingID; cell = t.Thing.PositionHeld; }
                    else if (t.Cell.IsValid) cell = t.Cell;
                }
                EventLedger.Add("message", msg.text.StripTags(), new JObject { ["type"] = msg.def?.defName }, cell, tid);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger message: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
    static class Patch_Incident
    {
        static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            try
            {
                if (!__result) return;
                var d = new JObject
                {
                    ["def"] = __instance.def?.defName,
                    ["category"] = __instance.def?.category?.defName,
                    ["points"] = parms?.points,
                    ["faction"] = parms?.faction?.Name,
                    ["strategy"] = parms?.raidStrategy?.defName,
                    ["arrival"] = parms?.raidArrivalMode?.defName,
                };
                EventLedger.Add("incident", __instance.def?.label ?? __instance.def?.defName ?? "incident", d);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger incident: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class Patch_Kill
    {
        static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (__instance.Faction != Faction.OfPlayer && !(__instance.HostileTo(Faction.OfPlayer))) return;
                bool colonist = __instance.IsColonist;
                string? bondedTo = null;
                try
                {
                    var rels = __instance.relations?.DirectRelations;
                    if (rels != null)
                        bondedTo = rels.FirstOrDefault(r => r.def == PawnRelationDefOf.Bond && r.otherPawn != null && r.otherPawn.Faction == Faction.OfPlayer)?.otherPawn.LabelShortCap;
                }
                catch { }
                var d = new JObject
                {
                    ["colonist"] = colonist,
                    ["faction"] = __instance.Faction?.Name,
                    ["kind"] = __instance.kindDef?.defName,
                    ["animal"] = __instance.RaceProps?.Animal ?? false,
                    ["bonded_to"] = bondedTo,
                    ["cause"] = dinfo?.Def?.defName,
                    ["by"] = (dinfo?.Instigator as Thing)?.ThingID,
                };
                EventLedger.Add(colonist ? "colonist_died" : "pawn_died", $"{__instance.LabelShortCap} died", d, __instance.PositionHeld, __instance.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger kill: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    static class Patch_Downed
    {
        static void Postfix(Pawn ___pawn, DamageInfo? dinfo)
        {
            try
            {
                if (___pawn == null) return;
                if (___pawn.IsColonist || ___pawn.IsPrisonerOfColony)
                {
                    EventLedger.Add("pawn_downed", $"{___pawn.LabelShortCap} downed", new JObject { ["cause"] = dinfo?.Def?.defName, ["colonist"] = ___pawn.IsColonist }, ___pawn.PositionHeld, ___pawn.ThingID);
                    if (___pawn.IsColonist)
                        EventLedger.Add("colonist_downed", $"{___pawn.LabelShortCap} downed", new JObject { ["cause"] = dinfo?.Def?.defName }, ___pawn.PositionHeld, ___pawn.ThingID);
                    return;
                }
                // Hostiles going down had no event at all, so the only trace of a won fight was the danger rating
                // dropping -- the very signal that reads as "they left". A downed raider is a prisoner, a corpse or
                // a pawn who gets back up, and in every case the colony needs to know she is lying there.
                if (___pawn.Spawned && !___pawn.Position.Fogged(___pawn.Map) && ___pawn.HostileTo(Faction.OfPlayer))
                    EventLedger.Add("hostile_downed", $"{___pawn.LabelShortCap} ({___pawn.Faction?.Name ?? "no faction"}) downed, still on the map",
                        new JObject { ["cause"] = dinfo?.Def?.defName, ["faction"] = ___pawn.Faction?.Name }, ___pawn.PositionHeld, ___pawn.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger downed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    static class Patch_MentalState
    {
        static void Postfix(Pawn ___pawn, MentalStateDef stateDef, string reason, bool __result)
        {
            try
            {
                if (!__result || ___pawn == null) return;
                if (___pawn.IsColonist)
                    EventLedger.Add("mental_break", $"{___pawn.LabelShortCap}: {stateDef.label}", new JObject { ["def"] = stateDef.defName, ["reason"] = reason }, ___pawn.PositionHeld, ___pawn.ThingID);
                else if (stateDef.IsAggro && ___pawn.Spawned && ___pawn.Map == Find.CurrentMap)
                    EventLedger.Add("manhunter", $"{___pawn.LabelShortCap} ({___pawn.kindDef?.label}) is {stateDef.label}", new JObject { ["def"] = stateDef.defName, ["kind"] = ___pawn.kindDef?.defName }, ___pawn.PositionHeld, ___pawn.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger mental: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    static class Patch_Research
    {
        static void Postfix(ResearchProjectDef proj)
        {
            try { EventLedger.Add("research_finished", proj.label, new JObject { ["def"] = proj.defName }); }
            catch (Exception ex) { BridgeLog.Warning("ledger research: " + ex.Message); }
            try { Hooks.RaiseResearchProjectFinished(proj); }
            catch (Exception ex) { BridgeLog.Warning("research-finished hook: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    static class Patch_Construction
    {
        static void Postfix(Frame __instance, Pawn worker)
        {
            try
            {
                if (__instance.Faction != Faction.OfPlayer) return;
                var def = __instance.def.entityDefToBuild;
                EventLedger.Add("built", $"{def?.label} built by {worker?.LabelShort}", new JObject { ["def"] = def?.defName, ["by"] = worker?.ThingID }, __instance.Position);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger built: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
    static class Patch_BuildingDestroyed
    {
        static void Prefix(Building __instance, DestroyMode mode)
        {
            try
            {
                if (__instance.Faction != Faction.OfPlayer || mode == DestroyMode.Deconstruct || mode == DestroyMode.Vanish || mode == DestroyMode.Cancel) return;
                if (__instance is Frame || __instance is Blueprint)
                {
                    if (mode == DestroyMode.FailConstruction)
                        EventLedger.Add("construction_failed", $"{__instance.LabelCap} failed (materials lost)", new JObject { ["def"] = __instance.def.defName }, __instance.Position);
                    return;
                }
                if (!__instance.def.building.isNaturalRock && __instance.def.category == ThingCategory.Building)
                    EventLedger.Add("building_lost", $"{__instance.LabelCap} destroyed ({mode})", new JObject { ["def"] = __instance.def.defName, ["mode"] = mode.ToString() }, __instance.Position, __instance.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger destroyed: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Add))]
    static class Patch_Quest
    {
        static void Postfix(Quest quest)
        {
            try
            {
                if (quest.hidden) return;
                EventLedger.Add("quest", quest.name, new JObject { ["id"] = quest.id, ["state"] = quest.State.ToString() });
            }
            catch (Exception ex) { BridgeLog.Warning("ledger quest: " + ex.Message); }
        }
    }

    /// <summary>
    /// The raid alarm.
    ///
    /// This used to emit in the AddLord postfix, and a Lord is created BEFORE its pawns are attached and before its
    /// LordJob is set. Both were empty every time, so every raid the model was ever woken for announced itself as
    /// "hostile group: Toxin Outfit x0 ()" -- zero raiders, unknown mission. state.threats was correct all along;
    /// only the alarm, which is the part that wakes the model mid-step, was wrong.
    ///
    /// So the lord is queued here and read a tick later, once the game has populated it.
    /// </summary>
    [HarmonyPatch(typeof(LordManager), nameof(LordManager.AddLord))]
    static class Patch_LordAdd
    {
        internal static readonly List<(Lord lord, int queuedTick)> Pending = new List<(Lord, int)>();

        /// <summary>Give up waiting after a second and report what we have, so an alarm is never silently dropped.</summary>
        internal const int DeadlineTicks = 60;

        static void Postfix(Lord newLord)
        {
            try
            {
                if (newLord.faction == null || !newLord.faction.HostileTo(Faction.OfPlayer)) return;
                Pending.Add((newLord, Find.TickManager.TicksGame));
            }
            catch (Exception ex) { BridgeLog.Warning("ledger lord: " + ex.Message); }
        }

        /// <summary>Emit every queued lord that is ready, or that has waited long enough. Called once per tick.</summary>
        internal static void Flush()
        {
            if (Pending.Count == 0) return;
            var now = Find.TickManager.TicksGame;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var (lord, queued) = Pending[i];
                var ready = lord.ownedPawns != null && lord.ownedPawns.Count > 0 && lord.LordJob != null;
                // now < queued means the clock moved backwards (a save was loaded); flush rather than wait forever.
                if (!ready && now >= queued && now - queued < DeadlineTicks) continue;
                Pending.RemoveAt(i);
                try
                {
                    if (lord.faction == null) continue;
                    var count = lord.ownedPawns?.Count ?? 0;
                    var job = lord.LordJob?.GetType().Name;
                    EventLedger.Add("hostile_group", $"hostile group: {lord.faction.Name} x{count} ({job})",
                        new JObject { ["faction"] = lord.faction.Name, ["count"] = count, ["job"] = job, ["lord"] = lord.loadID });
                }
                catch (Exception ex) { BridgeLog.Warning("ledger lord flush: " + ex.Message); }
            }
        }
    }

    [HarmonyPatch(typeof(LordManager), nameof(LordManager.RemoveLord))]
    static class Patch_LordRemove
    {
        static void Postfix(Lord oldLord)
        {
            try
            {
                if (oldLord.faction == null || !oldLord.faction.HostileTo(Faction.OfPlayer)) return;
                EventLedger.Add("hostile_group_gone", $"hostile group gone: {oldLord.faction.Name} ({oldLord.LordJob?.GetType().Name})",
                    new JObject { ["faction"] = oldLord.faction.Name, ["job"] = oldLord.LordJob?.GetType().Name, ["lord"] = oldLord.loadID });
            }
            catch (Exception ex) { BridgeLog.Warning("ledger lord: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    static class Patch_SetFaction
    {
        static void Prefix(Pawn __instance, Faction newFaction, out bool __state)
        {
            __state = __instance.Faction == Faction.OfPlayer;
        }
        static void Postfix(Pawn __instance, Faction newFaction, bool __state)
        {
            try
            {
                if (!__instance.RaceProps.Humanlike) return;
                bool now = newFaction == Faction.OfPlayer;
                if (now && !__state) EventLedger.Add("colonist_joined", $"{__instance.LabelShortCap} joined the colony", null, __instance.PositionHeld, __instance.ThingID);
                else if (!now && __state) EventLedger.Add("colonist_left", $"{__instance.LabelShortCap} left the colony", new JObject { ["to"] = newFaction?.Name }, __instance.PositionHeld, __instance.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger faction: " + ex.Message); }
        }
    }

    /// <summary>Shared filter for the social/character patches: "concerns the colony" = player faction or a prisoner of the colony.</summary>
    static class LedgerPawn
    {
        public static bool IsColony(Pawn? p) => p != null && (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony);

        public static void Relation(Pawn? a, Pawn? b, PawnRelationDef? def, bool added)
        {
            if (a == null || b == null || def == null) return;
            if (!IsColony(a) && !IsColony(b)) return;
            var d = new JObject
            {
                ["def"] = def.defName, ["label"] = def.label,
                ["a"] = a.LabelShortCap, ["b"] = b.LabelShortCap, ["a_id"] = a.ThingID, ["b_id"] = b.ThingID,
                ["added"] = added,
            };
            EventLedger.Add("relation", $"{a.LabelShortCap} and {b.LabelShortCap}: {(added ? "" : "no longer ")}{def.label}", d, a.PositionHeld, a.ThingID);
        }
    }

    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale))]
    static class Patch_Tale
    {
        static void Postfix(TaleDef def, object[] args)
        {
            try
            {
                if (def == null || args == null) return;
                var pawns = args.OfType<Pawn>().ToList();
                if (!pawns.Any(LedgerPawn.IsColony)) return;
                var names = pawns.Select(p => p.LabelShortCap).ToList();
                var d = new JObject
                {
                    ["def"] = def.defName, ["label"] = def.label,
                    ["pawns"] = new JArray(names), ["ids"] = new JArray(pawns.Select(p => p.ThingID)),
                };
                var first = pawns[0];
                EventLedger.Add("tale", $"{def.label ?? def.defName}: {string.Join(", ", names)}", d, first.PositionHeld, first.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger tale: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    static class Patch_Social
    {
        static readonly AccessTools.FieldRef<PlayLogEntry_Interaction, InteractionDef> IntDef = AccessTools.FieldRefAccess<PlayLogEntry_Interaction, InteractionDef>("intDef");
        static readonly AccessTools.FieldRef<PlayLogEntry_Interaction, Pawn> Initiator = AccessTools.FieldRefAccess<PlayLogEntry_Interaction, Pawn>("initiator");
        static readonly AccessTools.FieldRef<PlayLogEntry_Interaction, Pawn> Recipient = AccessTools.FieldRefAccess<PlayLogEntry_Interaction, Pawn>("recipient");
        static readonly HashSet<string> Minor = new HashSet<string> { "Chitchat", "DeepTalk", "BuildRapport", "AnimalChat", "Nuzzle", "BabyPlay", "LessonGeneric" };

        static void Postfix(LogEntry entry)
        {
            try
            {
                if (!(entry is PlayLogEntry_Interaction ie)) return;
                var def = IntDef(ie); var init = Initiator(ie); var recip = Recipient(ie);
                if (def == null || init == null) return;
                if (!LedgerPawn.IsColony(init) && !LedgerPawn.IsColony(recip)) return;
                // The game string must be rendered from a participant's POV: any other POV (including null) makes the
                // entry log an error, and a null recipient renders as an error placeholder.
                string text = recip != null ? ie.ToGameStringFromPOV(init, false).StripTags() : $"{init.LabelShortCap}: {def.label}";
                var d = new JObject
                {
                    ["def"] = def.defName,
                    ["initiator"] = init.LabelShortCap, ["recipient"] = recip?.LabelShortCap,
                    ["initiator_id"] = init.ThingID, ["recipient_id"] = recip?.ThingID,
                    ["text"] = text, ["minor"] = Minor.Contains(def.defName),
                };
                EventLedger.Add("social", text, d, init.PositionHeld, init.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger social: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    static class Patch_RelationAdd
    {
        static void Postfix(Pawn_RelationsTracker __instance, Pawn ___pawn, PawnRelationDef def, Pawn otherPawn)
        {
            try
            {
                // AddDirectRelation silently returns for implied/self relations; only record what actually landed.
                if (def == null || otherPawn == null || !__instance.DirectRelationExists(def, otherPawn)) return;
                LedgerPawn.Relation(___pawn, otherPawn, def, added: true);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger relation: " + ex.Message); }
        }
    }

    // TryRemoveDirectRelation is the single funnel: RemoveDirectRelation(def, pawn) and RemoveDirectRelation(relation)
    // both delegate to it, and breakups call it directly for Lover/Fiance.
    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.TryRemoveDirectRelation))]
    static class Patch_RelationRemove
    {
        static void Postfix(Pawn ___pawn, PawnRelationDef def, Pawn otherPawn, bool __result)
        {
            try
            {
                if (!__result || ___pawn == null || otherPawn == null) return;
                // Pawn.Discard clears every relation of a garbage-collected world pawn; that is bookkeeping, not drama.
                if (___pawn.Discarded || otherPawn.Discarded || ___pawn.Dead || otherPawn.Dead) return;
                LedgerPawn.Relation(___pawn, otherPawn, def, added: false);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger relation: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult))]
    static class Patch_Hediff
    {
        static void Postfix(Pawn ___pawn, Hediff hediff, BodyPartRecord part)
        {
            try
            {
                if (___pawn == null || hediff?.def == null || ___pawn.Dead || !LedgerPawn.IsColony(___pawn)) return;
                // Not on a map and not travelling: this is pawn generation (e.g. a joiner's old scars), not an event.
                if (___pawn.MapHeld == null && !___pawn.IsCaravanMember()) return;
                var def = hediff.def;
                bool missing = hediff is Hediff_MissingPart;
                if (!(missing || def.makesSickThought || def.lethalSeverity > 0f || def.IsAddiction)) return;
                part ??= hediff.Part;
                string label = def.label;
                try { label = hediff.LabelCap ?? def.label; } catch { }
                var d = new JObject
                {
                    ["def"] = def.defName, ["label"] = label, ["part"] = part?.Label,
                    ["serious"] = missing || def.lethalSeverity > 0f,
                };
                string what = missing && part != null ? $"lost {part.Label}" : label;
                EventLedger.Add("health", $"{___pawn.LabelShortCap}: {what}", d, ___pawn.PositionHeld, ___pawn.ThingID);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger health: " + ex.Message); }
        }
    }

    // Fires for both the human Accept button (Dialog_Trade) and the agent's ui.dialog path (DialogRpc), which call
    // TradeDeal.TryExecute. TryExecute calls Reset() before returning, so the deal is snapshotted in the prefix.
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    static class Patch_Trade
    {
        const int MaxItems = 12;

        static void Prefix(TradeDeal __instance, out JObject? __state)
        {
            __state = null;
            try
            {
                var trader = TradeSession.trader;
                var sold = new JArray(); var bought = new JArray();
                int nSold = 0, nBought = 0; float currencyCost = 0f;
                foreach (var t in __instance.AllTradeables)
                {
                    if (t.IsCurrency) continue;
                    var act = t.ActionToDo;
                    if (act == TradeAction.None) continue;
                    currencyCost += t.CurTotalCurrencyCostForSource; // same sum UpdateCurrencyCount() uses
                    string item = $"{Math.Abs(t.CountToTransfer)} {t.Label}";
                    // Engine convention: CountToTransfer > 0 (to destination) = PlayerSells = the colony gives.
                    if (act == TradeAction.PlayerSells) { if (nSold++ < MaxItems) sold.Add(item); }
                    else { if (nBought++ < MaxItems) bought.Add(item); }
                }
                __state = new JObject
                {
                    ["trader"] = trader?.TraderName, ["faction"] = trader?.Faction?.Name, ["gift"] = TradeSession.giftMode,
                    ["sold"] = sold, ["bought"] = bought, ["sold_count"] = nSold, ["bought_count"] = nBought,
                };
                var cur = __instance.CurrencyTradeable;
                if (cur != null && !TradeSession.giftMode)
                    __state[cur.IsFavor ? "favor_delta" : "silver_delta"] = -cur.CostToInt(currencyCost); // colony's net change
            }
            catch (Exception ex) { BridgeLog.Warning("ledger trade: " + ex.Message); }
        }

        static void Postfix(bool __result, bool actuallyTraded, JObject? __state)
        {
            try
            {
                if (!__result || !actuallyTraded || __state == null) return;
                string trader = (string?)__state["trader"] ?? "trader";
                var parts = new List<string>();
                if (__state["sold"] is JArray s && s.Count > 0) parts.Add(((bool?)__state["gift"] == true ? "gave " : "sold ") + string.Join(", ", s.Select(x => (string?)x)));
                if (__state["bought"] is JArray b && b.Count > 0) parts.Add("bought " + string.Join(", ", b.Select(x => (string?)x)));
                string text = $"traded with {trader}: {(parts.Count > 0 ? string.Join("; ", parts) : "nothing")}";
                EventLedger.Add("trade", text, __state);
            }
            catch (Exception ex) { BridgeLog.Warning("ledger trade: " + ex.Message); }
        }
    }

    /// <summary>Daily snapshot + new-game marker.</summary>
    public class LedgerComponent : GameComponent
    {
        private int _lastDay = -1;
        private StoryDanger _lastDanger = StoryDanger.None;
        // Who is being carried off, and which infections we have already announced. Both are "has this started?"
        // questions, and neither had an event: the model's whole trigger history for the raid that cost it a
        // colonist was four wakes, none of which was the chase, the kidnap or the interception.
        // Keyed by pawn id, holding the label, because the pawn we most need to name is the one that just left the map:
        // the "gone" lookup ran against AllPawnsSpawned and so answered null in exactly the kidnapping case.
        private readonly Dictionary<int, string> _carried = new Dictionary<int, string>();
        private readonly HashSet<string> _infections = new HashSet<string>();
        public LedgerComponent(Game game) { }

        public override void FinalizeInit()
        {
            EventLedger.Add("game", "game loaded/started", new JObject { ["day"] = GenDate.DaysPassed });
        }

        /// <summary>
        /// A colonist being carried away, and how close the carrier is to leaving with them.
        ///
        /// There was no pawn_kidnapped kind, no "hostile carrying a colonist" and no "hostile near the map edge".
        /// The only related kind was hostile_group_gone, which fires once the raider has left -- the one moment
        /// the information is worth nothing. The model slept three in-game hours through a kidnapping because
        /// nothing in its wake_on list could fire for one.
        /// </summary>
        void WatchCarried(Map m)
        {
            if (m == null) return;
            var seen = new HashSet<int>();
            foreach (var p in m.mapPawns.AllPawnsSpawned)
            {
                if (p.Faction == null || !p.HostileTo(Faction.OfPlayer)) continue;
                if (!(p.carryTracker?.CarriedThing is Pawn victim)) continue;
                if (victim.Faction != Faction.OfPlayer) continue;
                seen.Add(victim.thingIDNumber);
                int edge = EdgeDistance(m, p.Position);
                if (!_carried.ContainsKey(victim.thingIDNumber))
                {
                    _carried[victim.thingIDNumber] = victim.LabelShort;
                    EventLedger.Add("colonist_carried", $"{p.LabelShortCap} is carrying {victim.LabelShortCap} at {State.Snapshot.CellText(p.Position)}, {edge} cells from the map edge",
                        new JObject { ["colonist"] = victim.LabelShort, ["carrier"] = p.LabelShort, ["pos"] = State.Snapshot.Cell(p.Position), ["edge_distance"] = edge });
                }
            }
            foreach (var id in _carried.Keys.Where(i => !seen.Contains(i)).ToList())
            {
                string label = _carried[id];
                _carried.Remove(id);
                var who = m.mapPawns.AllPawnsSpawned.FirstOrDefault(x => x.thingIDNumber == id);
                // On the map or off it is the whole difference between a rescue and a kidnapping, and it is the one
                // thing we can state without guessing. What it means is the reader's call.
                var data = new JObject { ["colonist"] = label, ["on_map"] = who != null };
                if (who != null) { data["pos"] = State.Snapshot.Cell(who.Position); data["downed"] = who.Downed; }
                EventLedger.Add("colonist_carried_gone",
                    who != null
                        ? $"{label} is no longer being carried, and is on the map at {State.Snapshot.CellText(who.Position)}{(who.Downed ? ", downed" : "")}"
                        : $"{label} is no longer being carried, and is not on the map",
                    data);
            }
        }

        static int EdgeDistance(Map m, IntVec3 c)
            => Math.Max(0, Math.Min(Math.Min(c.x, c.z), Math.Min(m.Size.x - 1 - c.x, m.Size.z - 1 - c.z)));

        /// <summary>
        /// A new immunizable condition on a colonist, announced when it starts.
        ///
        /// The per-step brief now carries severity, immunity and both rates, but a step the model is asleep
        /// through carries nothing. An infection at severity 0.02 is cheap to treat and invisible; the game's own
        /// Alert_LifeThreateningHediff fires only after the cheap-intervention window has closed.
        ///
        /// The event states that it started and where. What it means is the reader's to decide.
        /// </summary>
        void WatchInfections(Map m)
        {
            if (m == null) return;
            var live = new HashSet<string>();
            foreach (var p in m.mapPawns.FreeColonists)
            {
                foreach (var h in p.health.hediffSet.hediffs)
                {
                    if (!h.Visible || h.TryGetComp<HediffComp_Immunizable>() == null) continue;
                    var key = p.thingIDNumber + ":" + h.def.defName + ":" + (h.Part?.Label ?? "whole");
                    live.Add(key);
                    if (!_infections.Add(key)) continue;
                    double imm = 0;
                    try { imm = p.health.immunity.GetImmunity(h.def); } catch { }
                    EventLedger.Add("infection", $"{p.LabelShortCap}: {h.LabelCap}{(h.Part != null ? " (" + h.Part.Label + ")" : "")} severity {Math.Round(h.Severity, 2)}, immunity {Math.Round(imm, 2)}",
                        new JObject { ["colonist"] = p.LabelShort, ["hediff"] = h.def.defName, ["part"] = h.Part?.Label, ["severity"] = Math.Round(h.Severity, 2), ["immunity"] = Math.Round(imm, 2) });
                }
            }
            _infections.RemoveWhere(k => !live.Contains(k));
        }

        public override void GameComponentTick()
        {
            // Not rate-limited: a raid alarm one tick late is the point, a raid alarm a second late is not.
            try { Patch_LordAdd.Flush(); }
            catch (Exception ex) { BridgeLog.Warning("lord flush: " + ex.Message); }
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                try { WatchCarried(Find.CurrentMap); }
                catch (Exception ex) { BridgeLog.Warning("carried watch: " + ex.Message); }
                try { WatchInfections(Find.CurrentMap); }
                catch (Exception ex) { BridgeLog.Warning("infection watch: " + ex.Message); }
                try
                {
                    var m = Find.CurrentMap;
                    if (m != null)
                    {
                        var d = m.dangerWatcher.DangerRating;
                        if (d != _lastDanger)
                        {
                            // This line is delivered mid-step as URGENT and is often the ONLY thing the model is told
                            // about a fight. It used to count with the same ThreatDisabled filter as the summary, so a
                            // raid that ended with a raider bleeding on the ground announced "(0 hostile targets)" --
                            // which the model reasonably read as "they left", and wrote down as fact.
                            var tally = State.Snapshot.Tally(m);
                            EventLedger.Add("danger", State.ObservationRules.DangerText(_lastDanger.ToString(), d.ToString(), tally.Active, tally.Downed, tally.Dormant),
                                new JObject { ["from"] = _lastDanger.ToString(), ["to"] = d.ToString(), ["hostiles"] = tally.Active, ["downed"] = tally.Downed, ["dormant"] = tally.Dormant });
                            _lastDanger = d;
                        }
                    }
                }
                catch (Exception ex) { BridgeLog.Warning("danger watch: " + ex.Message); }
            }
            if (Find.TickManager.TicksGame % 250 != 0) return;
            try
            {
                int day = GenDate.DaysPassed;
                if (day == _lastDay) return;
                _lastDay = day;
                var map = Find.CurrentMap;
                if (map == null) return;
                var snap = State.Snapshot.Daily(map);
                EventLedger.Add("day", $"day {day}", snap);
            }
            catch (Exception ex) { BridgeLog.Warning("daily snapshot: " + ex.Message); }
        }
    }
}
