using System.Linq;
using RimBridge.Decision;
using Xunit;

namespace RimBridge.Tests
{
    public class DecisionLogicTests
    {
        [Fact]
        public void Id_RoundTrips_WithThingTarget()
        {
            string id = DecisionLogic.FormatId("Human1234", "tend", "Human1235");

            Assert.True(DecisionLogic.TryParseId(id, out string pawn, out string action, out string? target));
            Assert.Equal("Human1234", pawn);
            Assert.Equal("tend", action);
            Assert.Equal("Human1235", target);
        }

        [Fact]
        public void Id_RoundTrips_WithCellTarget()
        {
            string id = DecisionLogic.FormatId("Human1234", "take_cover", "84,61");

            Assert.True(DecisionLogic.TryParseId(id, out _, out string action, out string? target));
            Assert.Equal("take_cover", action);
            Assert.Equal("84,61", target);
        }

        [Fact]
        public void Id_WithoutTarget_ParsesWithNullTarget()
        {
            Assert.True(DecisionLogic.TryParseId("Human1234:continue_current_job", out string pawn, out string action, out string? target));
            Assert.Equal("Human1234", pawn);
            Assert.Equal("continue_current_job", action);
            Assert.Null(target);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no-colons")]
        [InlineData(":tend:Human1")]
        [InlineData("Human1::Human2")]
        public void Id_Malformed_DoesNotParse(string id)
        {
            Assert.False(DecisionLogic.TryParseId(id, out _, out _, out _));
        }

        [Fact]
        public void Stale_RespectsBoundary()
        {
            Assert.False(DecisionLogic.IsStale(1000, 1000, 2500));
            Assert.False(DecisionLogic.IsStale(1000, 3500, 2500));
            Assert.True(DecisionLogic.IsStale(1000, 3501, 2500));
            Assert.False(DecisionLogic.IsStale(1000, 999999, -1)); // negative disables the check
        }

        [Fact]
        public void Rank_EmergenciesFirstThenPriorityAndCaps()
        {
            var items = new[]
            {
                new DecisionLogic.RankItem { Id = "haul", Priority = 30, Emergency = false },
                new DecisionLogic.RankItem { Id = "tend", Priority = 80, Emergency = true },
                new DecisionLogic.RankItem { Id = "attack", Priority = 70, Emergency = false },
                new DecisionLogic.RankItem { Id = "rescue", Priority = 85, Emergency = true },
            };

            var ranked = DecisionLogic.Rank(items, 3);

            Assert.Equal(new[] { "rescue", "tend", "attack" }, ranked.Select(r => r.Id));
        }

        [Theory]
        [InlineData(90, 30, 15, 5, "none")]
        [InlineData(25, 30, 15, 5, "minor")]
        [InlineData(10, 30, 15, 5, "major")]
        [InlineData(3, 30, 15, 5, "extreme")]
        public void BreakRisk_FollowsThresholds(double mood, double minor, double major, double extreme, string expected)
        {
            Assert.Equal(expected, DecisionLogic.BreakRisk(mood, minor, major, extreme));
        }

        [Fact]
        public void BasePriority_LifeSafetyOutranksEconomy()
        {
            Assert.True(DecisionLogic.BasePriority("extinguish") > DecisionLogic.BasePriority("attack"));
            Assert.True(DecisionLogic.BasePriority("attack") > DecisionLogic.BasePriority("haul"));
            Assert.True(DecisionLogic.BasePriority("rescue") > DecisionLogic.BasePriority("construct"));
            Assert.True(DecisionLogic.BasePriority("hunt") > DecisionLogic.BasePriority("haul"));
            Assert.True(DecisionLogic.BasePriority("research") > DecisionLogic.BasePriority("sow"));
        }

        [Fact]
        public void WorkgiverSource_RoundTripsDefName()
        {
            string source = DecisionLogic.WorkgiverSource("PlantsCut");

            Assert.True(DecisionLogic.TrySplitSource(source, out string defName));
            Assert.Equal("PlantsCut", defName);
            Assert.False(DecisionLogic.TrySplitSource("builtin", out _));
            Assert.False(DecisionLogic.TrySplitSource("workgiver:", out _));
        }

        [Fact]
        public void Throttle_FiresAtMostOncePerInterval()
        {
            Assert.True(DecisionLogic.ShouldFire(0, 1500, 1500));
            Assert.False(DecisionLogic.ShouldFire(100, 1500, 1500));
            Assert.True(DecisionLogic.ShouldFire(100, 1600, 1500));
        }
    }
}
