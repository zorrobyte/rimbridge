// Verse-free tests for whether a room's chairs and tables can be used together.
//
// Toils_Ingest.TryFindChairOrSpot accepts a chair only when a table is the
// edifice of one of the 4 cardinal neighbours of the sitting cell. A chair set
// diagonally from the table looks placed and is never used, and nothing in the
// game says so.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class DiningRulesTests
    {
        [Fact]
        public void ARoomWithNoTableIsNotADiningRoom()
        {
            Assert.Empty(DiningRules.Problems(tables: 0, chairs: 4, chairsAtTable: 0, tablesWithChair: 0));
        }

        [Fact]
        public void AChairOnEachSideOfTheTableHasNoProblems()
        {
            Assert.Empty(DiningRules.Problems(tables: 1, chairs: 2, chairsAtTable: 2, tablesWithChair: 1));
        }

        [Fact]
        public void ChairsThatReachNoTableAreReported()
        {
            // The diagonal case: every chair is placed, none is usable.
            var p = DiningRules.Problems(tables: 1, chairs: 4, chairsAtTable: 0, tablesWithChair: 0);
            Assert.Contains("4 chair(s) but none cardinally adjacent to a table", p);
        }

        [Fact]
        public void ATableWithNoChairIsReported()
        {
            var p = DiningRules.Problems(tables: 2, chairs: 2, chairsAtTable: 2, tablesWithChair: 1);
            Assert.Contains("1 table(s) with no cardinally adjacent chair", p);
        }

        [Fact]
        public void ATableWithNoChairsAtAllIsStillReported()
        {
            var p = DiningRules.Problems(tables: 1, chairs: 0, chairsAtTable: 0, tablesWithChair: 0);
            Assert.Contains("1 table(s) with no cardinally adjacent chair", p);
        }

        [Fact]
        public void ARoomWithNoChairsDoesNotGetTheChairLine()
        {
            var p = DiningRules.Problems(tables: 1, chairs: 0, chairsAtTable: 0, tablesWithChair: 0);
            Assert.DoesNotContain("chair(s) but none", string.Join(" ", p));
        }

        [Fact]
        public void BothSidesOfTheMismatchAreReportedTogether()
        {
            var p = DiningRules.Problems(tables: 3, chairs: 5, chairsAtTable: 0, tablesWithChair: 0);
            Assert.Contains("5 chair(s) but none cardinally adjacent to a table", p);
            Assert.Contains("3 table(s) with no cardinally adjacent chair", p);
        }

        [Fact]
        public void SomeChairsAtATableIsEnoughToDropTheChairLine()
        {
            var p = DiningRules.Problems(tables: 1, chairs: 5, chairsAtTable: 1, tablesWithChair: 1);
            Assert.Empty(p);
        }
    }
}
