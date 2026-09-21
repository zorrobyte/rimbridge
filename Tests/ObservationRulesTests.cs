// Verse-free tests for what the observation is allowed to hide.
//
// Episode 1 lost a raid to these rules. A raider was downed 18 cells from the
// colony, bleeding, and the observation reported no hostiles and danger None,
// because RimWorld's ThreatDisabled -- written to answer "should combat AI
// engage?" -- was used to answer "is anyone there?". The model recorded "FLED.
// No engagement." as permanent fact, stood down its defences, and cancelled a
// trap blueprint on the cell the body was lying on.
//
// The rule these pin: what the PLAYER can see decides what is reported (fog),
// and everything else is a flag rather than a filter.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class ObservationRulesTests
    {
        // ── what is reported at all ──

        [Fact]
        public void ActiveHostileInTheOpen_IsReported()
        {
            Assert.True(ObservationRules.VisibleHostile(spawned: true, fogged: false, dead: false));
        }

        [Fact]
        public void DownedHostile_IsStillReported()
        {
            // The whole point. Downed is not gone: she is lying there bleeding,
            // she may get back up, and her body blocks the cell.
            Assert.True(ObservationRules.VisibleHostile(spawned: true, fogged: false, dead: false));
        }

        [Fact]
        public void FoggedHostile_IsHidden()
        {
            // The model must not know more than a player. Ancient-danger sleepers
            // sit in an unopened, fogged room -- this is what keeps them secret,
            // not the dormancy flag.
            Assert.False(ObservationRules.VisibleHostile(spawned: true, fogged: true, dead: false));
        }

        [Fact]
        public void DeadAndUnspawned_AreNotHostiles()
        {
            Assert.False(ObservationRules.VisibleHostile(spawned: true, fogged: false, dead: true));
            Assert.False(ObservationRules.VisibleHostile(spawned: false, fogged: false, dead: false));
        }

        // ── how it is labelled ──

        [Fact]
        public void StatusDistinguishesActiveFromDownedFromDormant()
        {
            Assert.Equal("active", ObservationRules.HostileStatus(downed: false, dormant: false));
            Assert.Equal("downed", ObservationRules.HostileStatus(downed: true, dormant: false));
            Assert.Equal("dormant", ObservationRules.HostileStatus(downed: false, dormant: true));
        }

        [Fact]
        public void DownedBeatsDormant()
        {
            // A downed sleeper is downed: "dormant" would imply it can still wake up fighting.
            Assert.Equal("downed", ObservationRules.HostileStatus(downed: true, dormant: true));
        }

        // ── the danger event text, which is what the model actually read ──

        [Fact]
        public void DangerText_NamesDownedHostilesInsteadOfImplyingTheyLeft()
        {
            // The line the model believed was "danger Low -> None (0 hostile targets)".
            var s = ObservationRules.DangerText("Low", "None", active: 0, downed: 1, dormant: 0);
            Assert.Contains("Low -> None", s);
            Assert.Contains("0 active", s);
            Assert.Contains("1 downed", s);
            Assert.DoesNotContain("dormant", s);   // nothing dormant: do not mention it
        }

        [Fact]
        public void Tally_CountsByStatus_AndTotalsThemAll()
        {
            var t = new ObservationRules.HostileTally();
            t.Add("active");
            t.Add("downed");
            t.Add("downed");
            t.Add("dormant");
            Assert.Equal(1, t.Active);
            Assert.Equal(2, t.Downed);
            Assert.Equal(1, t.Dormant);
            Assert.Equal(4, t.Total);
        }

        [Fact]
        public void Tally_TreatsAnUnknownStatusAsActive()
        {
            // Better to over-report a threat than to drop one, which is the mistake this whole file exists for.
            var t = new ObservationRules.HostileTally();
            t.Add("something-new");
            Assert.Equal(1, t.Active);
        }

        [Fact]
        public void DangerText_MentionsOnlyWhatIsThere()
        {
            Assert.Equal("danger None -> Low (2 active)", ObservationRules.DangerText("None", "Low", 2, 0, 0));
            Assert.Equal("danger Low -> None (no hostiles on the map)", ObservationRules.DangerText("Low", "None", 0, 0, 0));
            Assert.Contains("3 dormant", ObservationRules.DangerText("None", "Low", 1, 0, 3));
        }
    }
}
