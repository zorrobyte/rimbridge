using System;
using System.Collections.Generic;
using System.Linq;
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
    /// World-layer state: tiles, caravans, settlements, sites, passing ships â€” plus settlement.abandon
    /// as the one safe validated world action. Tile ids are plain ints on the wire (surface layer);
    /// the engine's PlanetTile converts implicitly.
    /// </summary>
    public static class WorldRpc
    {
        static void RequireWorld()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.World == null || Find.WorldGrid == null)
                throw new RpcError("no world (load or start a game first)");
        }

        static JObject GoodsSummary(IEnumerable<Thing> things, int top)
        {
            var rows = new List<WorldLogic.GoodRow>();
            foreach (var t in things.Take(300))
            {
                if (t == null || t.Destroyed) continue;
                float v = 0;
                try { v = t.MarketValue * t.stackCount; } catch { }
                rows.Add(new WorldLogic.GoodRow { Def = t.def?.defName ?? "?", Count = t.stackCount, Value = v });
            }
            var s = WorldLogic.SummarizeGoods(rows, top);
            return new JObject
            {
                ["total_value"] = s.TotalValue, ["total_stacks"] = s.TotalStacks,
                ["top"] = new JArray(s.Top.Select(r => new JObject { ["def"] = r.Def, ["count"] = r.Count, ["value"] = r.Value })),
            };
        }

        static JObject WorldObjectBrief(WorldObject o)
        {
            int tile = -1;
            try { tile = o.Tile; } catch { }
            return new JObject
            {
                ["id"] = o.ID, ["kind"] = o.GetType().Name, ["label"] = o.Label,
                ["tile"] = tile, ["faction"] = o.Faction?.Name,
            };
        }

        static WorldObject WorldObjectById(int id)
        {
            var o = Find.WorldObjects.AllWorldObjects.FirstOrDefault(x => x.ID == id);
            return o ?? throw new RpcError($"no world object with id {id}");
        }

        [Rpc("world.overview", "planet overview: home tile, counts of settlements/sites/caravans/ships by side, nearest tradable settlement")]
        public static JToken Overview(JObject p)
        {
            RequireWorld();
            var all = Find.WorldObjects.AllWorldObjects;
            var settlements = all.OfType<Settlement>().ToList();
            var sites = all.OfType<Site>().ToList();
            var caravans = all.OfType<Caravan>().ToList();
            int home = -1;
            try { if (Find.CurrentMap != null) home = Find.CurrentMap.Tile; } catch { }
            var o = new JObject { ["home_tile"] = home };
            o["settlements"] = settlements.Count;
            o["settlements_hostile"] = settlements.Count(s => s.Faction != null && s.Faction.HostileTo(Faction.OfPlayer));
            o["player_settlements"] = settlements.Count(s => s.Faction == Faction.OfPlayer);
            o["sites"] = sites.Count;
            o["caravans"] = caravans.Count;
            o["player_caravans"] = caravans.Count(c => c.IsPlayerControlled);
            int ships = 0;
            try { ships = Find.CurrentMap?.passingShipManager?.passingShips?.Count ?? 0; } catch { }
            o["trade_ships"] = ships;
            // Nearest settlement we can trade with right now.
            try
            {
                Settlement? best = null; float bestDist = float.MaxValue;
                foreach (var s in settlements)
                {
                    if (s.Faction == Faction.OfPlayer || !s.CanTradeNow) continue;
                    float d = home >= 0 ? Find.WorldGrid.ApproxDistanceInTiles(home, (int)s.Tile) : 0;
                    if (d < bestDist) { bestDist = d; best = s; }
                }
                if (best != null) o["nearest_trader"] = new JObject { ["id"] = best.ID, ["name"] = best.Name, ["faction"] = best.Faction?.Name, ["tiles"] = Math.Round(bestDist) };
            }
            catch { }
            return o;
        }

        [Rpc("world.tile", "{tile?: int (default home)} biome, hilliness, elevation, temperature, feature, world objects on it")]
        public static JToken Tile(JObject p)
        {
            RequireWorld();
            int t = P.Int(p, "tile", -1);
            if (t < 0)
            {
                if (Find.CurrentMap == null) throw new RpcError("no current map; pass tile explicitly");
                t = Find.CurrentMap.Tile;
            }
            if (t < 0 || t >= Find.WorldGrid.TilesCount) throw new RpcError($"tile {t} out of range (0..{Find.WorldGrid.TilesCount - 1})");
            Tile tile;
            try { tile = Find.WorldGrid.Surface[t]; }
            catch (Exception ex) { throw new RpcError("cannot read tile: " + ex.Message); }
            var o = new JObject
            {
                ["tile"] = t,
                ["biomes"] = new JArray(tile.Biomes.Select(b => b.defName)),
                ["hilliness"] = tile.hilliness.ToString(),
                ["elevation"] = Math.Round(tile.elevation),
                ["temperature"] = Math.Round(tile.temperature),
            };
            try { if (tile.feature != null) o["feature"] = Render.Value(tile.feature, 1); } catch { }
            try { o["objects"] = new JArray(Find.WorldObjects.AllWorldObjects.Where(x => { try { return (int)x.Tile == t; } catch { return false; } }).Select(WorldObjectBrief)); } catch { }
            return o;
        }

        [Rpc("world.caravans", "all caravans, brief: id, faction, tile, pawns, moving, destination, food days, stuck flags")]
        public static JToken Caravans(JObject p)
        {
            RequireWorld();
            return new JArray(Find.WorldObjects.AllWorldObjects.OfType<Caravan>().Select(CaravanBrief));
        }

        static JObject CaravanBrief(Caravan c)
        {
            int tile = -1, dest = -1;
            try { tile = c.Tile; } catch { }
            bool moving = false;
            try
            {
                moving = c.pather.Moving;
                if (c.pather.Destination.Valid) dest = c.pather.Destination;
            }
            catch { }
            float food = -1, rot = -1;
            try { var f = c.DaysWorthOfFood; food = f.days; rot = f.tillRot; } catch { }
            var o = new JObject
            {
                ["id"] = c.ID, ["tile"] = tile, ["faction"] = c.Faction?.Name, ["player"] = c.IsPlayerControlled,
                ["pawns"] = c.PawnsListForReading.Count, ["moving"] = moving, ["destination"] = dest,
                ["food_days"] = Math.Round(food, 1), ["food_rots_in"] = Math.Round(rot, 1),
                ["immobilized"] = c.ImmobilizedByMass, ["cant_move"] = c.CantMove, ["resting"] = c.NightResting,
            };
            return o;
        }

        [Rpc("world.caravan", "{id} full caravan: pawns, goods summary, mass, ticks per move, destination, food")]
        public static JToken Caravan(JObject p)
        {
            RequireWorld();
            var c = WorldObjectById(P.Int(p, "id")) as Caravan ?? throw new RpcError("not a caravan");
            var o = CaravanBrief(c);
            o["pawn_list"] = new JArray(c.PawnsListForReading.Take(20).Select(x => Render.PawnHandle(x)));
            try { o["goods"] = GoodsSummary(CaravanInventoryUtility.AllInventoryItems(c), 15); } catch { }
            try { o["mass"] = new JObject { ["used"] = Math.Round(c.MassUsage, 1), ["capacity"] = Math.Round(c.MassCapacity, 1) }; } catch { }
            try { o["ticks_per_move"] = c.TicksPerMove; } catch { }
            try { o["downed_owners"] = c.AllOwnersDowned; } catch { }
            return o;
        }

        [Rpc("world.settlements", "{faction?: name, tradable?: bool} settlements with goodwill, map status, trader kind/availability")]
        public static JToken Settlements(JObject p)
        {
            RequireWorld();
            string? fac = P.OptStr(p, "faction");
            bool tradableOnly = P.Bool(p, "tradable", false);
            var q = Find.WorldObjects.AllWorldObjects.OfType<Settlement>().AsEnumerable();
            if (fac != null)
            {
                var f = Lookup.FactionOrNull(fac) ?? throw new RpcError($"no faction '{fac}'");
                q = q.Where(s => s.Faction == f);
            }
            var arr = new JArray();
            foreach (var s in q.Take(200))
            {
                // World objects can be mid-despawn or factionless ruins; one bad entry
                // must not fail the whole list.
                try
                {
                    bool canTrade = false;
                    try { canTrade = s.CanTradeNow; } catch { }
                    if (tradableOnly && !canTrade) continue;
                    int tile = -1;
                    try { tile = s.Tile; } catch { }
                    string name = tile.ToString();
                    try { name = s.Name; } catch { try { name = s.Label; } catch { } }
                    var o = new JObject
                    {
                        ["id"] = s.ID, ["tile"] = tile, ["name"] = name,
                        ["faction"] = s.Faction?.Name, ["goodwill"] = s.Faction?.PlayerGoodwill,
                        ["hostile"] = s.Faction != null && s.Faction.HostileTo(Faction.OfPlayer),
                        ["has_map"] = s.HasMap, ["trader"] = s.TraderKind?.defName, ["can_trade"] = canTrade,
                    };
                    arr.Add(o);
                }
                catch { }
            }
            return arr;
        }

        [Rpc("world.settlement", "{id} settlement detail: trader goods summary, stock value")]
        public static JToken Settlement(JObject p)
        {
            RequireWorld();
            var s = WorldObjectById(P.Int(p, "id")) as Settlement ?? throw new RpcError("not a settlement");
            int tile = -1;
            try { tile = s.Tile; } catch { }
            string name = tile.ToString();
            try { name = s.Name; } catch { try { name = s.Label; } catch { } }
            var o = new JObject
            {
                ["id"] = s.ID, ["tile"] = tile, ["name"] = name,
                ["faction"] = s.Faction?.Name, ["goodwill"] = s.Faction?.PlayerGoodwill,
                ["has_map"] = s.HasMap, ["trader"] = s.TraderKind?.defName, ["can_trade"] = s.CanTradeNow,
                ["ever_visited"] = s.EverVisited,
            };
            try { o["goods"] = GoodsSummary(s.Goods, 15); } catch { }
            return o;
        }

        [Rpc("settlement.abandon", "{id} abandon one of YOUR settlements (the game's own abandon button, no confirm). Cannot be undone.")]
        public static JToken Abandon(JObject p)
        {
            RequireWorld();
            var s = WorldObjectById(P.Int(p, "id")) as Settlement ?? throw new RpcError("not a settlement");
            if (s.Faction != Faction.OfPlayer) throw new RpcError("refusing to abandon someone else's settlement");
            string name;
            try { name = s.Name; } catch { name = "tile " + (int)s.Tile; }
            s.Abandon(false);
            EventLedger.Add("settlement_abandoned", $"abandoned {name}", new JObject { ["id"] = s.ID });
            return new JObject { ["abandoned"] = name };
        }

        [Rpc("world.sites", "active world sites with parts and threat points")]
        public static JToken Sites(JObject p)
        {
            RequireWorld();
            var arr = new JArray();
            foreach (var s in Find.WorldObjects.AllWorldObjects.OfType<Site>().Take(100))
            {
                int tile = -1;
                try { tile = s.Tile; } catch { }
                float threat = -1;
                try { threat = s.ActualThreatPoints; } catch { }
                var parts = new JArray();
                try
                {
                    foreach (var part in s.parts.Take(8))
                    {
                        float tp = -1;
                        try { tp = part.parms?.threatPoints ?? -1; } catch { }
                        parts.Add(new JObject { ["def"] = part.def?.defName, ["threat"] = tp });
                    }
                }
                catch { }
                string main = "?";
                try { main = s.MainSitePartDef?.defName ?? "?"; } catch { }
                arr.Add(new JObject { ["id"] = s.ID, ["tile"] = tile, ["main"] = main, ["threat"] = Math.Round(threat), ["parts"] = parts });
            }
            return arr;
        }

        [Rpc("world.ships", "orbital trade ships around the current map with goods summaries")]
        public static JToken Ships(JObject p)
        {
            RequireWorld();
            var map = Find.CurrentMap ?? throw new RpcError("no current map");
            var arr = new JArray();
            List<PassingShip> ships;
            try { ships = map.passingShipManager?.passingShips?.ToList() ?? new List<PassingShip>(); }
            catch (Exception ex) { throw new RpcError("cannot list ships: " + ex.Message); }
            foreach (var sh in ships.OfType<TradeShip>())
            {
                var o = new JObject
                {
                    ["name"] = sh.TraderName, ["kind"] = sh.TraderKind?.defName,
                    ["faction"] = sh.Faction?.Name, ["can_trade"] = sh.CanTradeNow, ["silver"] = sh.Silver,
                };
                try { o["goods"] = GoodsSummary(sh.Goods, 15); } catch { }
                arr.Add(o);
            }
            return arr;
        }
    }
}
