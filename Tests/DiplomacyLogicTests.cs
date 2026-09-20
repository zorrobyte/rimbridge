using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class DiplomacyLogicTests
    {
        [Fact]
        public void Levers_FriendlyFactionWithSettlements_TradeAndGift()
        {
            var levers = DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing
            {
                Hostile = false, PermanentEnemy = false, Defeated = false,
                HasSettlements = true, HasPrisoners = false,
            });

            Assert.Contains("trade_via_caravan", levers);
            Assert.Contains("gift_via_transport_pods", levers);
            Assert.DoesNotContain("raid_settlement_via_caravan", levers);
        }

        [Fact]
        public void Levers_HostileFaction_OffersRoadsNotTrade()
        {
            var levers = DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing
            {
                Hostile = true, PermanentEnemy = false, Defeated = false,
                HasSettlements = true, HasPrisoners = true,
            });

            Assert.Contains("peace_talks_via_caravan", levers);
            Assert.Contains("release_prisoners", levers);
            Assert.Contains("raid_settlement_via_caravan", levers);
            Assert.DoesNotContain("trade_via_caravan", levers);
        }

        [Fact]
        public void Levers_PermanentEnemy_NoPeaceRoad()
        {
            var levers = DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing
            {
                Hostile = true, PermanentEnemy = true, Defeated = false,
                HasSettlements = true, HasPrisoners = false,
            });

            Assert.DoesNotContain("peace_talks_via_caravan", levers);
            Assert.Contains("raid_settlement_via_caravan", levers);
        }

        [Fact]
        public void Levers_PlayerAndDefeated_Nothing()
        {
            Assert.Empty(DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing { IsPlayer = true }));
            Assert.Empty(DiplomacyLogic.AvailableLevers(new DiplomacyLogic.Standing { Defeated = true, Hostile = true }));
        }

        [Theory]
        [InlineData(false, false, false, "peace")]
        [InlineData(true, false, false, "war")]
        [InlineData(true, true, false, "war_permanent")]
        [InlineData(true, false, true, "defeated")]
        public void Posture_MatchesStanding(bool hostile, bool permanent, bool defeated, string expected)
        {
            Assert.Equal(expected, DiplomacyLogic.Posture(hostile, permanent, defeated));
        }
    }
}
