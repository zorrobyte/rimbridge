// Verse-free tests for reading a colonist's condition.
//
// Episode 3, day 5: Sugar had a WoundInfection at severity 0.02 with immunity
// 0.015 -- already losing -- and the observation said health: 86.0. By the time
// anything fired, `health` had climbed to 100.0 while he lay unconscious hours
// from death, and it fell to 60.0 at the moment the amputation cured him. The
// number is a body-part score and it moved the wrong way throughout.
//
// The reflection afterwards derived the real rule by hand, at that cost:
// severity +0.84/day against immunity +0.644/day. Both rates are readable from
// HediffCompProperties_Immunizable. These are the tests for using them.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class HealthRulesTests
    {
        [Fact]
        public void FreshInfectionWithFasterImmunityIsWinning()
        {
            // The case levels alone get wrong: immunity is BEHIND severity here and the colonist is still fine.
            Assert.Equal("winning", HealthRules.Race(severity: 0.02, immunity: 0.015, severityPerDay: 0.3, immunityPerDay: 0.6));
        }

        [Fact]
        public void SugarsInfectionIsLosing()
        {
            Assert.Equal("losing", HealthRules.Race(severity: 0.02, immunity: 0.015, severityPerDay: 0.84, immunityPerDay: 0.644));
        }

        [Fact]
        public void HighSeverityWellAheadOnImmunityIsWinning()
        {
            // The mirror case: a severity of 0.4 that is winning is better news than a 0.02 that is losing.
            Assert.Equal("winning", HealthRules.Race(severity: 0.4, immunity: 0.9, severityPerDay: 0.5, immunityPerDay: 0.5));
        }

        [Fact]
        public void NothingMovingIsStable()
        {
            Assert.Equal("stable", HealthRules.Race(severity: 0.3, immunity: 0.0, severityPerDay: 0, immunityPerDay: 0));
        }

        [Fact]
        public void NoImmunityAgainstARisingSeverityIsLosing()
        {
            Assert.Equal("losing", HealthRules.Race(severity: 0.3, immunity: 0.0, severityPerDay: 0.5, immunityPerDay: 0));
        }

        [Fact]
        public void AStalledSeverityIsWinningEvenWithoutImmunity()
        {
            Assert.Equal("winning", HealthRules.Race(severity: 0.3, immunity: 0.2, severityPerDay: 0, immunityPerDay: 0.4));
        }

        [Fact]
        public void DaysToFullIsNegativeWhenNothingIsClimbing()
        {
            Assert.True(HealthRules.DaysToFull(0.5, 0) < 0);
            Assert.Equal(1.0, HealthRules.DaysToFull(0.5, 0.5), 3);
        }

        [Fact]
        public void SummaryLeadsWithTheVerdictAndTheDeadline()
        {
            var s = HealthRules.Summary("WoundInfection (left arm)", 0.02, 0.015, "losing", daysToDeath: 1.2, daysToImmune: 1.5, tended: false);
            Assert.Contains("WoundInfection (left arm) severity 0.02", s);
            Assert.Contains("immunity 0.02", s);
            Assert.Contains("losing", s);
            Assert.Contains("~1.2d to fatal", s);
            Assert.Contains("UNTENDED", s);
        }

        [Fact]
        public void ATendedWinningInfectionSaysSoWithoutAlarm()
        {
            var s = HealthRules.Summary("Flu", 0.3, 0.6, "winning", daysToDeath: 4.0, daysToImmune: 1.0, tended: true);
            Assert.Contains("~1d to immune", s);
            Assert.DoesNotContain("fatal", s);
            Assert.Contains("tended", s);
            Assert.DoesNotContain("UNTENDED", s);
        }
    }
}
