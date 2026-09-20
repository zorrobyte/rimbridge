using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimBridge.World
{
    /// <summary>
    /// Rituals: list pending obligations with begin targets, and start a ritual headlessly by
    /// building the same LordJob_Ritual the "Begin ritual" dialog builds (role auto-fill +
    /// MakeNewLord). Refusals surface as errors, never silent no-ops.
    /// </summary>
    public static class RitualRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static IEnumerable<Ideo> ColonyIdeos(Map map)
        {
            return map.mapPawns.FreeColonists
                .Select(c => { try { return c.Ideo; } catch { return null; } })
                .Where(i => i != null).Distinct()!;
        }

        static Precept_Ritual FindRitual(Map map, string name, string? ideoName, int? obligationId = null)
        {
            var matches = new List<(Ideo, Precept_Ritual)>();
            foreach (var ideo in ColonyIdeos(map))
                foreach (var r in ideo.PreceptsListForReading.OfType<Precept_Ritual>())
                    if (string.Equals(r.def?.defName, name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.LabelCap.ToString().StripTags(), name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.def?.label, name, StringComparison.OrdinalIgnoreCase))
                        matches.Add((ideo, r));
            if (ideoName != null) matches = matches.Where(m => string.Equals(m.Item1.name, ideoName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) throw new RpcError($"no ritual '{name}' in colony ideologies");
            if (matches.Count > 1 && obligationId != null)
            {
                var holding = matches.Where(m => { try { return m.Item2.activeObligations.Any(o => o.ID == obligationId.Value); } catch { return false; } }).ToList();
                if (holding.Count == 1) return holding[0].Item2;
            }
            if (matches.Count > 1)
            {
                var anytime = matches.Where(m => { try { return m.Item2.isAnytime; } catch { return false; } }).ToList();
                if (anytime.Count == 1) return anytime[0].Item2;
            }
            if (matches.Count > 1) throw new RpcError($"'{name}' matches {matches.Count} rituals; pass ideo=<name> (or obligation=<id>): " + string.Join(" | ", matches.Select(m => { try { return m.Item2.LabelCap.ToString().StripTags() + " (" + m.Item1.name + ")"; } catch { return "?"; } })));
            return matches[0].Item2;
        }

        static JObject ObligationJson(RitualObligation ob)
        {
            string? targetId = null, targetLabel = null, cell = null;
            try
            {
                var t = ob.FirstValidTarget;
                if (t.IsValid)
                {
                    if (t.HasThing && t.Thing != null) { targetId = t.Thing.ThingID; targetLabel = t.Thing.LabelCap.ToString().StripTags(); }
                    else cell = t.Cell.x + "," + t.Cell.z;
                }
            }
            catch { }
            int days = -1;
            try { days = ob.TicksUntilExpiration / 60000; } catch { }
            return new JObject
            {
                ["id"] = ob.ID, ["label"] = ob.LetterLabel.ToString().StripTags(),
                ["target"] = targetId, ["target_label"] = targetLabel, ["cell"] = cell,
                ["expires_days"] = days,
            };
        }

        [Rpc("ritual.list", "colony rituals with pending obligations and begin targets")]
        public static JToken List(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap;
            var arr = new JArray();
            foreach (var ideo in ColonyIdeos(map))
                foreach (var r in ideo.PreceptsListForReading.OfType<Precept_Ritual>())
                {
                    string label = "?";
                    try { label = r.LabelCap.ToString().StripTags(); } catch { }
                    var obs = new JArray();
                    try { foreach (var ob in r.activeObligations.Where(o => { try { return o.StillValid; } catch { return false; } }).Take(8)) obs.Add(ObligationJson(ob)); } catch { }
                    arr.Add(new JObject
                    {
                        ["ritual"] = r.def?.defName ?? label, ["label"] = label,
                        ["ideo"] = ideo.name, ["anytime"] = r.isAnytime, ["obligations"] = obs,
                    });
                }
            return arr;
        }

        [Rpc("ritual.start", "{ritual: defName|label, obligation?: id, target?: thing|cell, organizer?: pawn, participants?: [pawn], ideo?} begin the ritual via the vanilla lord job")]
        public static JToken Start(JObject p)
        {
            RequirePlaying();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            int? wantOb = null;
            try { if (p["obligation"] != null) wantOb = P.Int(p, "obligation"); } catch { }
            var ritual = FindRitual(map, P.Str(p, "ritual"), p["ideo"] != null ? P.Str(p, "ideo") : null, wantOb);

            RitualObligation? obligation = null;
            if (p["obligation"] != null)
            {
                int oid = P.Int(p, "obligation");
                try { obligation = ritual.activeObligations.FirstOrDefault(o => o.ID == oid); } catch { }
                if (obligation == null) throw new RpcError($"obligation {oid} not active on {ritual.def?.defName}");
                bool valid = false;
                try { valid = obligation.StillValid; } catch { }
                if (!valid) throw new RpcError($"obligation {oid} is no longer valid");
            }
            else
            {
                try { obligation = ritual.activeObligations.FirstOrDefault(o => { try { return o.StillValid; } catch { return false; } }); } catch { }
                if (obligation == null && !ritual.isAnytime)
                    throw new RpcError($"{ritual.def?.defName} has no valid obligation and is not anytime");
            }

            var organizer = p["organizer"] != null ? Lookup.Colonist(P.Str(p, "organizer")) : map.mapPawns.FreeColonists.FirstOrDefault(c => !c.Downed && !c.Dead && c.Spawned);
            if (organizer == null) throw new RpcError("no organizer available");
            if (organizer.Map != map) throw new RpcError("organizer is not on the current map");

            TargetInfo target = TargetInfo.Invalid;
            try { if (obligation != null && obligation.FirstValidTarget.IsValid) target = obligation.FirstValidTarget; } catch { }
            if (p["target"] != null)
            {
                var tt = p["target"]!.ToString();
                var thing = Lookup.ThingOrNull(tt);
                if (thing != null && thing.Spawned) target = new TargetInfo(thing);
                else
                {
                    try { target = new TargetInfo(Lookup.Cell(p["target"]), map); }
                    catch { throw new RpcError($"bad target '{tt}' (thing id or [x,z])"); }
                }
            }
            if (!target.IsValid) target = new TargetInfo(organizer.Position, map);

            var assignments = new RitualRoleAssignments(ritual, target);
            // Mirror Dialog_BeginRitual: Setup populates the role/candidate tables; without it
            // every role reports zero candidates and Participants throws.
            var poolPawns = new List<Pawn>();
            try
            {
                foreach (var q in map.mapPawns.AllPawns)
                    if (q.Spawned && !q.Dead && (q.RaceProps.Humanlike || q.Faction == Faction.OfPlayer)) poolPawns.Add(q);
                assignments.Setup(poolPawns, new List<Pawn>(), new Dictionary<string, Pawn>(), new List<Pawn>(), organizer);
                var pool = assignments.AllCandidatePawns;
                foreach (var q in poolPawns) if (!pool.Contains(q)) pool.Add(q);
            }
            catch (Exception ex) { throw new RpcError("could not set up ritual roles: " + ex.Message); }
            HashSet<string>? allowed = null;
            if (p["participants"] is JArray pa)
            {
                allowed = new HashSet<string>();
                foreach (var id in pa)
                    try { allowed.Add(Lookup.Pawn(id.Value<string>() ?? "").ThingID); } catch (Exception ex) { throw new RpcError("bad participant: " + ex.Message); }
                allowed.Add(organizer.ThingID);
            }

            List<RitualRole>? roles = null;
            try { roles = ritual.behavior?.def?.stages != null ? ritual.behavior.def.roles : null; } catch { }
            if (roles == null) throw new RpcError("ritual has no behavior (cannot start)");
            // Role-less rituals (sky-lantern parties etc.) run as pure gatherings: no slots,
            // organizer + spectators become the lord. The game still refuses if it cannot.

            var slots = new List<RitualLogic.RoleSlot>();
            var cands = new Dictionary<string, IList<string>>();
            foreach (var role in roles)
            {
                slots.Add(new RitualLogic.RoleSlot { RoleId = role.id ?? role.Label, Required = role.required, Max = Math.Max(1, role.maxCount) });
                var list = new List<Pawn>();
                try { foreach (var c in assignments.CandidatesForRole(role, target, true, true, true)) if (c is Pawn cp) list.Add(cp); } catch { }
                try { role.OrderByDesirability(list); } catch { }
                cands[role.id ?? role.Label] = list.Where(c => c.Spawned && c.Map == map).Select(c => c.ThingID).ToList();
            }
            // Fallback + diagnostics: if the game's candidate query yields nothing, ask each
            // pool pawn directly so the error names the actual rejection reason.
            var diag = new List<string>();
            foreach (var role in roles)
            {
                string key = role.id ?? role.Label.ToString();
                if (cands.TryGetValue(key, out var have) && have.Count > 0) continue;
                var direct = new List<string>();
                foreach (var q in map.mapPawns.AllPawns)
                {
                    if (!q.Spawned || q.Dead || (!q.RaceProps.Humanlike && q.Faction != Faction.OfPlayer)) continue;
                    if (allowed != null && !allowed.Contains(q.ThingID)) continue;
                    string reason = "";
                    bool applies = false;
                    try { applies = role.AppliesToPawn(q, out reason, target, null, assignments, ritual, false); }
                    catch (Exception ex) { reason = "check threw: " + ex.GetType().Name; }
                    if (applies && !direct.Contains(q.ThingID)) direct.Add(q.ThingID);
                    else if (diag.Count < 6) diag.Add($"{q.LabelShortCap}/{key}: {reason}");
                }
                cands[key] = direct;
            }
            var plan = RitualLogic.AssignRoles(slots, cands, allowed);
            if (plan.UnfilledRequired.Count > 0)
                throw new RpcError("required roles unfilled: " + string.Join(", ", plan.UnfilledRequired)
                    + " | " + string.Join("; ", diag));

            List<RitualStage>? stages = null;
            try { stages = ritual.behavior.def.stages; } catch { }
            if (stages == null) throw new RpcError("ritual has no stages");

            foreach (var kv in plan.Assigned)
            {
                var role = roles.FirstOrDefault(r => (r.id ?? r.Label) == kv.Key);
                if (role == null) continue;
                foreach (var pid in kv.Value)
                {
                    var pawn = Lookup.PawnOrNull(pid);
                    if (pawn == null) continue;
                    try { assignments.TryAssign(pawn, role, out _, default, null); } catch (Exception ex) { throw new RpcError($"could not assign {pawn.LabelShortCap} to {kv.Key}: {ex.Message}"); }
                }
            }

            var parts = assignments.Participants.ToList();
            // Role-less gatherings have no assignments: the whole colony attends as the
            // lord, like the dialog's default spectator selection. The game still refuses
            // if it cannot run.
            if (parts.Count == 0 && roles.Count == 0)
            {
                foreach (var q in map.mapPawns.FreeColonists)
                    if (q.Spawned && !q.Dead && !parts.Contains(q)) parts.Add(q);
                if (!parts.Contains(organizer) && !organizer.Dead && organizer.Spawned && organizer.Map == map)
                    parts.Add(organizer);
            }
            if (parts.Count == 0) throw new RpcError("no participants could be assigned");
            LordJob_Ritual lordJob;
            try { lordJob = new LordJob_Ritual(target, ritual, obligation, stages, assignments, organizer, null); }
            catch (Exception ex) { throw new RpcError("could not build ritual lord job: " + ex.Message); }
            Lord lord;
            try { lord = LordMaker.MakeNewLord(organizer.Faction, lordJob, map, parts); }
            catch (Exception ex) { throw new RpcError("MakeNewLord threw: " + ex.Message); }
            if (lord == null || organizer.GetLord() != lord)
                throw new RpcError("the game refused the ritual (target invalid or pawns unavailable)");
            Hooks.RaiseManualTouch(organizer, "ritual.start");
            EventLedger.Add("ritual_started", $"began {ritual.def?.defName} with {parts.Count} participants", new JObject { ["ritual"] = ritual.def?.defName });
            return new JObject
            {
                ["started"] = true, ["ritual"] = ritual.def?.defName,
                ["participants"] = new JArray(parts.Select(x => x.LabelShortCap)),
            };
        }
    }
}
