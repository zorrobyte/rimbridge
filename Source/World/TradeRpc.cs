using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RimBridge.Engine;
using RimBridge.Ledger;
using RimBridge.Server;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimBridge.World
{
    /// <summary>
    /// Trade at a caravan's destination: run the vanilla TradeSession against the settlement the
    /// caravan is parked at, apply buy(+count)/sell(-count) transfers, execute the deal.
    /// Refusals surface as errors, never silent no-ops.
    /// </summary>
    public static class TradeRpc
    {
        static void RequirePlaying() { GameCtl.GameControl.RequirePlaying(); }

        static Caravan FindCaravan(string which)
        {
            var caravans = Find.WorldObjects.AllWorldObjects.OfType<Caravan>().Where(c => c.IsPlayerControlled).ToList();
            if (int.TryParse(which, out int id))
            {
                var c = caravans.FirstOrDefault(x => x.ID == id);
                if (c != null) return c;
            }
            // Travelling pawns leave their map, so Lookup can't see them: search caravan rosters directly.
            foreach (var c in caravans)
            {
                List<Pawn>? members = null;
                try { members = c.PawnsListForReading; } catch { }
                if (members == null) continue;
                foreach (var m in members)
                {
                    if (string.Equals(m.ThingID, which, StringComparison.OrdinalIgnoreCase)
                        || (m.Name != null && (string.Equals(m.Name.ToStringShort, which, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(m.LabelShort, which, StringComparison.OrdinalIgnoreCase)))) return c;
                }
            }
            var pawn = Lookup.PawnOrNull(which);
            if (pawn != null)
            {
                var c = caravans.FirstOrDefault(x => x.PawnsListForReading.Contains(pawn));
                if (c != null) return c;
            }
            throw new RpcError($"no player caravan '{which}' (world id or a pawn travelling in it)");
        }

        [Rpc("trade.execute", "{caravan: world id|travelling pawn, transfers: [{def, count (+buy/-sell)}], gift?: bool} trade with the settlement the caravan is parked at")]
        public static JToken Execute(JObject p)
        {
            RequirePlaying();
            var caravan = FindCaravan(P.Str(p, "caravan"));
            var settlement = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().FirstOrDefault(s => s.Tile == caravan.Tile)
                ?? throw new RpcError($"caravan is at tile {caravan.Tile}, not at a settlement");
            bool canTrade = false;
            try { canTrade = settlement.CanTradeNow; } catch { }
            if (!canTrade) throw new RpcError($"{settlement.LabelCap} will not trade right now");
            bool gift = P.Bool(p, "gift", false);

            var asks = new List<TradeLogic.Ask>();
            if (p["transfers"] is JArray ta)
                foreach (var t in ta)
                {
                    var o = (JObject)t;
                    asks.Add(new TradeLogic.Ask { Def = P.Str(o, "def"), Count = P.Int(o, "count") });
                }
            if (asks.Count == 0) throw new RpcError("no transfers given");
            if (gift && asks.Any(a => a.Count > 0)) throw new RpcError("gift mode only accepts sell transfers (negative count)");

            int social(Pawn x) { try { return x.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0; } catch { return 0; } }
            var negotiator = caravan.PawnsListForReading
                .Where(x => x.IsColonist && !x.Dead && !x.Downed)
                .OrderByDescending(social).FirstOrDefault()
                ?? throw new RpcError("no conscious colonist in the caravan to negotiate");

            TradeSession.SetupWith(settlement, negotiator, gift);
            try
            {
                var deal = TradeSession.deal ?? throw new RpcError("trade session failed to open");
                try { deal.Reset(); } catch (Exception ex) { throw new RpcError("could not reset deal: " + ex.Message); }
                // AddAllTradeables is private in the reference assemblies, so reach it the same
                // way engine.call does; at runtime it is the game's own population step.
                var pop = Reflector.FindMethod(typeof(TradeDeal), "AddAllTradeables", new JArray(), out var popArgs)
                    ?? throw new RpcError("this game version hides the trade-goods list");
                try { pop.Invoke(deal, popArgs); }
                catch (TargetInvocationException tie) { throw new RpcError("could not list trade goods: " + (tie.InnerException?.Message ?? tie.Message)); }
                catch (Exception ex) { throw new RpcError("could not list trade goods: " + ex.Message); }
                var stocks = new List<TradeLogic.Stock>();
                var byDef = new Dictionary<string, List<Tradeable>>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in deal.AllTradeables)
                {
                    string def;
                    int colony, trader, max;
                    try
                    {
                        def = t.ThingDef?.defName ?? "?";
                        colony = t.CountHeldBy(Transactor.Colony);
                        trader = t.CountHeldBy(Transactor.Trader);
                        max = t.GetMaximumToTransfer();
                    }
                    catch { continue; }
                    if (colony <= 0 && trader <= 0) continue;
                    if (!byDef.TryGetValue(def, out var list)) { list = new List<Tradeable>(); byDef[def] = list; stocks.Add(new TradeLogic.Stock { Def = def }); }
                    list.Add(t);
                    var st = stocks.First(s => string.Equals(s.Def, def, StringComparison.OrdinalIgnoreCase));
                    st.Colony += colony; st.Trader += trader; st.MaxTransfer = Math.Min(st.MaxTransfer, max);
                }
                var (planned, errors) = TradeLogic.Plan(asks, stocks);
                if (planned.Count == 0) throw new RpcError("nothing to transfer: " + string.Join("; ", errors));
                foreach (var pl in planned)
                {
                    int remaining = pl.Count;
                    foreach (var t in byDef[pl.Def])
                    {
                        if (remaining <= 0) break;
                        int have = pl.Side == "buy" ? t.CountHeldBy(Transactor.Trader) : t.CountHeldBy(Transactor.Colony);
                        if (have <= 0) continue;
                        int cur = 0;
                        try { cur = t.CountToTransfer; } catch { }
                        try { t.AdjustTo(Math.Min(have, cur + remaining)); } catch { continue; }
                        int now = 0;
                        try { now = t.CountToTransfer; } catch { }
                        remaining -= Math.Max(0, now - cur);
                    }
                }
                bool traded;
                try
                {
                    if (!deal.TryExecute(out traded)) throw new RpcError("the game refused the deal (price moved or goods gone)");
                }
                catch (RpcError) { throw; }
                catch (Exception ex) { throw new RpcError("trade threw: " + ex.Message); }
                if (!traded) throw new RpcError("deal executed but nothing changed hands");
                EventLedger.Add("trade", $"{(gift ? "gifted" : "traded")} {planned.Count} lines at {settlement.LabelCap}", new JObject { ["settlement"] = settlement.LabelCap.ToString() });
                return new JObject
                {
                    ["traded"] = true, ["gift"] = gift, ["settlement"] = settlement.LabelCap.ToString(),
                    ["lines"] = new JArray(planned.Select(x => new JObject { ["def"] = x.Def, ["count"] = x.Count, ["side"] = x.Side })),
                    ["warnings"] = new JArray(errors),
                };
            }
            finally { try { TradeSession.Close(); } catch { } }
        }
    }
}
