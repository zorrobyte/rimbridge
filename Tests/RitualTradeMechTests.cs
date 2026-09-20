using System.Collections.Generic;
using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class RitualLogicTests
    {
        static Dictionary<string, IList<string>> Cands(params (string, string[])[] xs)
        {
            var d = new Dictionary<string, IList<string>>();
            foreach (var (k, v) in xs) d[k] = v;
            return d;
        }

        [Fact]
        public void AssignRoles_GreedyNoDoubleAssign()
        {
            var roles = new List<RitualLogic.RoleSlot>
            {
                new RitualLogic.RoleSlot { RoleId = "leader", Required = true },
                new RitualLogic.RoleSlot { RoleId = "singer", Required = true },
            };
            var plan = RitualLogic.AssignRoles(roles, Cands(("leader", new[] { "A", "B" }), ("singer", new[] { "A", "C" })));
            Assert.Equal(new[] { "A" }, plan.Assigned["leader"]);
            Assert.Equal(new[] { "C" }, plan.Assigned["singer"]);
            Assert.Empty(plan.UnfilledRequired);
        }

        [Fact]
        public void AssignRoles_RequiredUnfilledAndAllowedFilter()
        {
            var roles = new List<RitualLogic.RoleSlot>
            {
                new RitualLogic.RoleSlot { RoleId = "sacrifice", Required = true },
                new RitualLogic.RoleSlot { RoleId = "crowd", Required = false, Max = 3 },
            };
            var plan = RitualLogic.AssignRoles(
                roles,
                Cands(("sacrifice", new string[] { }), ("crowd", new[] { "A", "B", "C", "D" })),
                new HashSet<string> { "A", "B" });
            Assert.Equal(new[] { "sacrifice" }, plan.UnfilledRequired);
            Assert.Equal(new[] { "A", "B" }, plan.Assigned["crowd"]);
        }
    }

    public class TradeLogicTests
    {
        static List<TradeLogic.Stock> Stocks() => new List<TradeLogic.Stock>
        {
            new TradeLogic.Stock { Def = "Silver", Colony = 500, Trader = 1200 },
            new TradeLogic.Stock { Def = "Medicine", Colony = 0, Trader = 6, MaxTransfer = 6 },
        };

        [Fact]
        public void Plan_BuySellClampAndUnknown()
        {
            var asks = new List<TradeLogic.Ask>
            {
                new TradeLogic.Ask { Def = "Medicine", Count = 20 },
                new TradeLogic.Ask { Def = "Silver", Count = -9999 },
                new TradeLogic.Ask { Def = "Nope", Count = 1 },
            };
            var (planned, errors) = TradeLogic.Plan(asks, Stocks());
            Assert.Equal(2, planned.Count);
            Assert.Equal("buy", planned[0].Side);
            Assert.Equal(6, planned[0].Count);
            Assert.Equal("sell", planned[1].Side);
            Assert.Equal(500, planned[1].Count);
            Assert.Single(errors);
        }

        [Fact]
        public void Plan_EmptyStockIsError()
        {
            var (planned, errors) = TradeLogic.Plan(
                new List<TradeLogic.Ask> { new TradeLogic.Ask { Def = "Medicine", Count = -2 } }, Stocks());
            Assert.Empty(planned);
            Assert.Single(errors);
        }
    }

    public class MechShuttleGeneLogicTests
    {
        [Fact]
        public void ResolveMode_CaseInsensitiveAndListsAvailable()
        {
            var (m, e) = MechLogic.ResolveMode("escort", new List<string> { "Work", "Escort" });
            Assert.Equal("Escort", m);
            Assert.Null(e);
            var (m2, e2) = MechLogic.ResolveMode("nap", new List<string> { "Work", "Escort" });
            Assert.Null(m2);
            Assert.Contains("Work", e2);
        }

        [Fact]
        public void ShuttleBlocker_Cases()
        {
            Assert.Null(ShuttleLogic.LaunchBlocker(true, "", 5, 100, 10f, 50f));
            Assert.Equal("shuttle refuses launch: no pilot", ShuttleLogic.LaunchBlocker(false, "no pilot", 5, 100, 10f, 50f));
            Assert.Equal("tile 500 out of range", ShuttleLogic.LaunchBlocker(true, "", 500, 100, 10f, 50f));
            Assert.Equal("out of range (80 tiles, shuttle reaches 50)", ShuttleLogic.LaunchBlocker(true, "", 5, 100, 80f, 50f));
        }

        [Fact]
        public void ExtractBlocker_Cases()
        {
            Assert.Null(GeneLogic.ExtractBlocker(true, false, true, ""));
            Assert.Equal("extractor has no power", GeneLogic.ExtractBlocker(false, false, true, ""));
            Assert.Equal("extractor already holds a pawn", GeneLogic.ExtractBlocker(true, true, true, ""));
            Assert.Equal("extractor refuses pawn: too big", GeneLogic.ExtractBlocker(true, false, false, "too big"));
        }
    }
}
