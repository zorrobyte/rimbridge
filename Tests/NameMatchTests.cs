// Verse-free tests for the "did you mean" list on an unknown method name.
//
// The case that cost six calls: the tool is rw_steward_orders_explain, so the
// method reads back as steward.orders_explain. The real name is
// steward.orders.explain, and the only answer was "unknown method".
using RimBridge.Server;
using Xunit;

namespace RimBridge.Tests
{
    public class NameMatchTests
    {
        static readonly string[] Known =
        {
            "steward.orders", "steward.orders.explain", "steward.orders.set", "steward.posture",
            "state.pawn", "state.quests", "map.find", "ui.letter",
        };

        [Fact]
        public void UnderscoreForDotFindsTheMethod()
        {
            var near = NameMatch.Near("steward.orders_explain", Known);
            Assert.Equal("steward.orders.explain", near[0]);
        }

        [Fact]
        public void AllUnderscoresFindTheMethodToo()
        {
            Assert.Equal("steward.orders.explain", NameMatch.Near("steward_orders_explain", Known)[0]);
        }

        [Fact]
        public void TheExactNameComesBeforeTheOnesThatMerelyContainIt()
        {
            var near = NameMatch.Near("steward.orders", Known);
            Assert.Equal("steward.orders", near[0]);
            Assert.Contains("steward.orders.set", near);
        }

        [Fact]
        public void APrefixOffersWhatIsUnderIt()
        {
            Assert.Contains("state.quests", NameMatch.Near("state.", Known));
        }

        [Fact]
        public void NothingLikeItOffersNothing()
        {
            Assert.Empty(NameMatch.Near("ledger.recent", Known));
        }

        [Fact]
        public void AnEmptyNameOffersNothing()
        {
            Assert.Empty(NameMatch.Near("", Known));
        }

        [Fact]
        public void TheListIsCapped()
        {
            var many = new[] { "a.one", "a.two", "a.three", "a.four", "a.five" };
            Assert.Equal(2, NameMatch.Near("a.", many, take: 2).Count);
        }
    }
}
