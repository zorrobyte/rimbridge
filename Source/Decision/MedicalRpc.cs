using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Server;
using RimWorld;
using Verse;

namespace RimBridge.Decision
{
    /// <summary>
    /// Surgery bills on pawns (the Health tab's operation list), which ui.add_bill deliberately doesn't
    /// cover (it targets work tables). Creation goes through vanilla's own
    /// HealthCardUtility.CreateSurgeryBill with UI messages suppressed; legality comes from the recipe
    /// worker's AvailableOnNow/GetPartsToApplyOn, same as the game.
    /// </summary>
    public static class MedicalRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static bool IsSurgery(RecipeDef r)
        {
            try { return typeof(Recipe_Surgery).IsAssignableFrom(r.workerClass); } catch { return false; }
        }

        static List<BodyPartRecord> ViableParts(Pawn pawn, RecipeDef recipe)
        {
            var parts = new List<BodyPartRecord>();
            try
            {
                foreach (var part in recipe.Worker.GetPartsToApplyOn(pawn, recipe).Take(40))
                {
                    bool ok;
                    try { ok = recipe.Worker.AvailableOnNow(pawn, part); } catch { continue; }
                    if (ok) parts.Add(part);
                }
            }
            catch { }
            return parts;
        }

        [Rpc("medical.bills", "{pawn} queued operation bills: recipe, part, suspended")]
        public static JToken Bills(JObject p)
        {
            RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            var arr = new JArray();
            try
            {
                foreach (var b in pawn.BillStack.Bills.OfType<Bill_Medical>())
                    arr.Add(new JObject
                    {
                        ["id"] = b.GetUniqueLoadID(), ["label"] = b.LabelCap.ToString(),
                        ["recipe"] = b.recipe?.defName, ["part"] = b.Part?.LabelCap, ["suspended"] = b.suspended,
                    });
            }
            catch (Exception ex) { throw new RpcError("cannot read bills: " + ex.Message); }
            return arr;
        }

        [Rpc("medical.options", "{pawn} operations the game would offer in the Health tab: recipe, viable body parts, implant/skill/violation notes")]
        public static JToken Options(JObject p)
        {
            RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            if (pawn.Dead) throw new RpcError("pawn is dead");
            var arr = new JArray();
            foreach (var r in DefDatabase<RecipeDef>.AllDefs.Where(r => IsSurgery(r) && r.AvailableNow))
            {
                var o = new JObject { ["recipe"] = r.defName, ["label"] = r.label, ["success_factor"] = Math.Round(r.surgerySuccessChanceFactor, 2) };
                bool needsImplant = false;
                try { needsImplant = typeof(Recipe_InstallImplant).IsAssignableFrom(r.workerClass); } catch { }
                if (needsImplant) o["needs_implant_item"] = true;
                List<BodyPartRecord> parts;
                if (r.targetsBodyPart)
                {
                    parts = ViableParts(pawn, r);
                    if (parts.Count == 0) continue; // the game wouldn't list it either
                    o["parts"] = new JArray(parts.Take(20).Select(x => (JToken)x.LabelCap.ToString()));
                }
                else
                {
                    bool ok;
                    try { ok = r.Worker.AvailableOnNow(pawn, null); } catch { continue; }
                    if (!ok) continue;
                }
                arr.Add(o);
                if (arr.Count >= 40) break;
            }
            var out_ = new JObject { ["options"] = arr };
            try
            {
                var docs = Find.CurrentMap.mapPawns.FreeColonists
                    .Where(d => d != pawn && !d.Dead && !d.Downed && d.skills != null)
                    .Select(d => d.skills.GetSkill(SkillDefOf.Medicine)?.Level ?? 0);
                out_["best_doctor_skill"] = docs.Any() ? docs.Max() : 0;
            }
            catch { }
            return out_;
        }

        [Rpc("medical.add_bill", "{pawn, recipe: RecipeDef, part?: body-part label (required when the recipe targets a part), implant?: thing id (for bionic/archotech installs)} queue an operation exactly like the Health tab would")]
        public static JToken AddBill(JObject p)
        {
            RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            if (pawn.Dead) throw new RpcError("pawn is dead");
            var recipe = Lookup.Def<RecipeDef>(P.Str(p, "recipe"));
            if (!IsSurgery(recipe)) throw new RpcError($"{recipe.defName} is not a surgery recipe");
            if (!recipe.AvailableNow) throw new RpcError("recipe not available now (research?)");

            BodyPartRecord? part = null;
            if (recipe.targetsBodyPart)
            {
                var parts = ViableParts(pawn, recipe);
                if (parts.Count == 0) throw new RpcError("no viable body part for this operation on this pawn");
                string want = P.Str(p, "part", "");
                int idx = MedicalLogic.BestLabelMatch(parts.Select(x => x.LabelCap.ToString()).ToList(), want);
                if (idx < 0)
                    throw new RpcError($"no body part matching '{want}'. Options: " + string.Join(", ", parts.Take(20).Select(x => x.LabelCap)));
                part = parts[idx];
                if (!recipe.Worker.AvailableOnNow(pawn, part)) throw new RpcError("operation no longer available on that part");
                try
                {
                    if (pawn.BillStack.Bills.OfType<Bill_Medical>().Any(b => b.recipe == recipe && b.Part == part))
                        throw new RpcError("that operation is already queued on that part");
                }
                catch (RpcError) { throw; }
                catch { }
            }

            List<Thing>? implants = null;
            bool needsImplant = false;
            try { needsImplant = typeof(Recipe_InstallImplant).IsAssignableFrom(recipe.workerClass); } catch { }
            if (needsImplant)
            {
                string iid = P.Str(p, "implant", "");
                var item = string.IsNullOrEmpty(iid) ? null : Lookup.ThingOrNull(iid);
                if (item == null) throw new RpcError("this operation installs an item: pass implant=<thing id> (see medical.options needs_implant_item)");
                implants = new List<Thing> { item };
            }

            string? violation = null;
            try
            {
                if (pawn.Faction != null && !pawn.Faction.Hidden && !pawn.Faction.HostileTo(Faction.OfPlayer)
                    && recipe.Worker.IsViolationOnPawn(pawn, part, Faction.OfPlayer))
                    violation = $"operating on {pawn.LabelShortCap} will anger {pawn.HomeFaction?.Name ?? "their faction"}";
            }
            catch { }

            Bill_Medical bill;
            try { bill = HealthCardUtility.CreateSurgeryBill(pawn, recipe, part, implants, sendMessages: false); }
            catch (Exception ex) { throw new RpcError("the game refused the bill: " + ex.Message); }
            Hooks.RaiseManualTouch(pawn, "medical.add_bill:" + recipe.defName);
            var o = new JObject { ["id"] = bill.GetUniqueLoadID(), ["label"] = bill.LabelCap.ToString(), ["pawn"] = pawn.LabelShortCap };
            if (violation != null) o["warning"] = violation;
            return o;
        }

        [Rpc("medical.bill", "{pawn, id|index, action: delete|suspend|resume} modify a queued operation")]
        public static JToken BillEdit(JObject p)
        {
            RequirePlaying();
            var pawn = Lookup.Pawn(P.Str(p, "pawn"));
            Bill_Medical? bill = null;
            var bills = pawn.BillStack.Bills.OfType<Bill_Medical>().ToList();
            if (p["id"] != null) bill = bills.FirstOrDefault(b => b.GetUniqueLoadID() == P.Str(p, "id"));
            else if (p["index"] != null && P.Int(p, "index") >= 0 && P.Int(p, "index") < bills.Count) bill = bills[P.Int(p, "index")];
            if (bill == null) throw new RpcError("operation bill not found");
            switch (P.Str(p, "action"))
            {
                case "delete": pawn.BillStack.Delete(bill); break;
                case "suspend": bill.suspended = true; break;
                case "resume": bill.suspended = false; break;
                default: throw new RpcError("action must be delete|suspend|resume");
            }
            Hooks.RaiseManualTouch(pawn, "medical.bill:" + P.Str(p, "action"));
            return new JObject { ["ok"] = true, ["bills"] = bills.Count - (P.Str(p, "action") == "delete" ? 1 : 0) };
        }
    }
}
