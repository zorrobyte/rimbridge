// Verse-free tests for whether a stockpile is actually keeping anything safe.
//
// Episode 1: zone "main" was outdoors, unroofed, and full of food. Crows ate
// from it and the rest rotted, while the payload reported
// unroofed_deteriorating: 0 and a comfortable food_days 7.4. Amy had to tell
// the model through the dashboard chat.
//
// Two blind spots met. OutsideStorage skips anything IsInAnyStorage(), so its
// roof check never runs on stored items; and BaseGraph's room "problems"
// channel -- the obvious place to put this -- rejects rooms that are
// PsychologicallyOutdoors, which an open-air stockpile always is. The stockpile
// was in no view at all. Nothing lied; nobody asked.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class StorageRulesTests
    {
        [Fact]
        public void RoofedStockpileWithFreshStock_HasNoProblems()
        {
            Assert.Empty(StorageRules.Problems(cells: 20, unroofed: 0, deteriorating: 0, rotting: 0));
        }

        [Fact]
        public void EntirelyUnroofedIsSaidPlainly()
        {
            var p = StorageRules.Problems(cells: 20, unroofed: 20, deteriorating: 0, rotting: 0);
            Assert.Contains("entirely unroofed", p);
        }

        [Fact]
        public void PartlyUnroofedGivesTheProportion()
        {
            var p = StorageRules.Problems(cells: 20, unroofed: 6, deteriorating: 0, rotting: 0);
            Assert.Contains("6 of 20 cells unroofed", p);
        }

        [Fact]
        public void StockActuallyDeterioratingIsTheHeadline()
        {
            // The number that matters: not "there is no roof" but "your things are being destroyed".
            var p = StorageRules.Problems(cells: 20, unroofed: 20, deteriorating: 14, rotting: 0);
            Assert.Contains("entirely unroofed", p);
            Assert.Contains("14 stack(s) deteriorating in the open", p);
        }

        [Fact]
        public void RottingIsReportedSeparately_BecauseARoofDoesNotFixIt()
        {
            var p = StorageRules.Problems(cells: 20, unroofed: 0, deteriorating: 0, rotting: 3);
            Assert.Contains("3 stack(s) rotting", p);
            Assert.DoesNotContain("unroofed", string.Join(" ", p));
        }

        [Fact]
        public void AnEmptyZoneIsNotAProblem()
        {
            Assert.Empty(StorageRules.Problems(cells: 0, unroofed: 0, deteriorating: 0, rotting: 0));
        }
    }
}
