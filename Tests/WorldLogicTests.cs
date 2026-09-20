using System.Linq;
using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class WorldLogicTests
    {
        static WorldLogic.GoodRow Row(string def, int count, float value) =>
            new WorldLogic.GoodRow { Def = def, Count = count, Value = value };

        [Fact]
        public void SummarizeGoods_TopIsValueOrderedAndTotalsCoverAll()
        {
            var rows = new[]
            {
                Row("Steel", 75, 150f),
                Row("Silver", 200, 2000f),
                Row("WoodLog", 100, 140f),
            };

            var s = WorldLogic.SummarizeGoods(rows, 2);

            Assert.Equal(2, s.Top.Count);
            Assert.Equal("Silver", s.Top[0].Def);
            Assert.Equal("Steel", s.Top[1].Def);
            Assert.Equal(3, s.TotalStacks);
            Assert.Equal(2290f, s.TotalValue);
        }

        [Fact]
        public void SummarizeGoods_EmptyAndCaps()
        {
            var empty = WorldLogic.SummarizeGoods(Enumerable.Empty<WorldLogic.GoodRow>(), 10);

            Assert.Empty(empty.Top);
            Assert.Equal(0, empty.TotalStacks);
            Assert.Equal(0, empty.TotalValue);

            var one = WorldLogic.SummarizeGoods(new[] { Row("MealSimple", 5, 30f) }, 0);
            Assert.Single(one.Top); // max clamps to at least 1
        }
    }
}
