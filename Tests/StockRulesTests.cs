// Verse-free tests for what the colony owns.
//
// Episode 1, day 0, turn one. key_stocks said {"meat_all": 0, "stone_blocks": 0,
// "meals_all": 0} and carried no WoodLog key at all. On the map: 521 wood in
// eleven unforbidden stacks, nearest two cells from the base, plus twelve meal
// stacks and 35 raw-resource stacks on the ground.
//
// The model wrote "we have 0 wood, so I must cut trees to build anything", and
// then had to watch the interface contradict itself -- the steward's forestry
// job counts loose wood, so it was handed 521 and 0 at once and spent a step
// chasing designations to reconcile them.
//
// A crashlanding has nothing in a stockpile. Storage-only counting is therefore
// wrong on turn one of every episode.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class StockRulesTests
    {
        [Fact]
        public void WoodOnTheGroundIsWood()
        {
            var t = new StockRules.Tally();
            t.Add(521, stored: false, forbidden: false);
            Assert.Equal(521, t.Total);
            Assert.Equal(521, t.Loose);
            Assert.Equal(0, t.Stored);
        }

        [Fact]
        public void TheSplitSurvivesSoHaulingIsVisibleWithoutBeingOrdered()
        {
            var t = new StockRules.Tally();
            t.Add(100, stored: true, forbidden: false);
            t.Add(421, stored: false, forbidden: false);
            Assert.Equal(521, t.Total);
            Assert.Equal(100, t.Stored);
            Assert.Equal(421, t.Loose);
        }

        [Fact]
        public void ForbiddenStockStillCounts()
        {
            // Unforbidding is one action. Reporting nothing because nobody has claimed the crash loot yet is the
            // same lie as reporting a famine over food lying in the open.
            var t = new StockRules.Tally();
            t.Add(720, stored: false, forbidden: true);
            Assert.Equal(720, t.Total);
            Assert.Equal(720, t.Forbidden);
        }

        [Fact]
        public void ForbiddenIsReportedBesideTheTotalNotSubtractedFromIt()
        {
            var t = new StockRules.Tally();
            t.Add(50, stored: false, forbidden: true);
            t.Add(50, stored: false, forbidden: false);
            Assert.Equal(100, t.Total);
            Assert.Equal(50, t.Forbidden);
        }

        [Fact]
        public void FoggedStockIsNotOwned()
        {
            Assert.False(StockRules.CountsAsStock(spawned: true, fogged: true));
            Assert.True(StockRules.CountsAsStock(spawned: true, fogged: false));
        }

        [Fact]
        public void UnspawnedStockIsNotOwned()
        {
            Assert.False(StockRules.CountsAsStock(spawned: false, fogged: false));
        }

        [Fact]
        public void AnEmptyTallyIsZeroNotAbsent()
        {
            // The other half of the defect: KeyStocks omitted a def whose count was 0, so "no wood" and "wood not
            // reported" were the same observation. A zero has to be sayable.
            var t = new StockRules.Tally();
            Assert.Equal(0, t.Total);
        }
    }
}
