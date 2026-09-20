// Verse-free tests for reading a colonist's condition.
//
// Episode 3, day 5: Sugar had a WoundInfection at severity 0.02 with immunity
// 0.015 -- already losing the race -- and the observation said health: 86.0.
// By the time anything fired, `health` had climbed to 100.0 while he lay
// unconscious hours from death, and it fell to 60.0 at the moment the
// amputation cured him. The number is a body-part score and it moved the wrong
// way throughout.
//
// These rules report levels, rates and times. They deliberately do NOT return a
// verdict: the episode 3 reflection derived the decision rule unaided once it
// had the numbers, which is the evidence that numbers are the whole job.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class HealthRulesTests
    {
        [Fact]
        public void DaysToFullIsNegativeWhenNothingIsClimbing()
        {
            Assert.True(HealthRules.DaysToFull(0.5, 0) < 0);
        }

        [Fact]
        public void DaysToFullMeasuresTheRemainingDistance()
        {
            Assert.Equal(1.0, HealthRules.DaysToFull(0.5, 0.5), 3);
            Assert.Equal(2.0, HealthRules.DaysToFull(0.0, 0.5), 3);
        }

        [Fact]
        public void SugarsInfectionReportsBothRacesWithoutJudgingThem()
        {
            // The real numbers. Severity reaches 1.0 in 1.2d, immunity in 1.5d. The reader draws the conclusion.
            var s = HealthRules.Summary("WoundInfection (left arm)", 0.02, 0.015, 0.84, 0.644, tended: false);
            Assert.Contains("severity 0.02 +0.84/day (1.2d to 1.0)", s);
            Assert.Contains("immunity 0.02 +0.64/day (1.5d to 1.0)", s);
            Assert.Contains("tended: no", s);
        }

        [Fact]
        public void NoVerdictWordsAppearAnywhere()
        {
            // The guard on the constraint: this reports, it does not advise.
            var s = HealthRules.Summary("Flu", 0.3, 0.6, 0.2, 0.9, tended: true);
            Assert.DoesNotContain("winning", s);
            Assert.DoesNotContain("losing", s);
            Assert.DoesNotContain("UNTENDED", s);
            Assert.DoesNotContain("fatal", s);
            Assert.Contains("tended: yes", s);
        }

        [Fact]
        public void AStalledTrackSaysSoRatherThanProjectingNonsense()
        {
            var s = HealthRules.Summary("Scar", 0.3, 0.0, 0, 0, tended: false);
            Assert.Contains("severity 0.3 (not rising)", s);
            Assert.Contains("immunity 0 (not rising)", s);
        }

        [Fact]
        public void TheLabelLeadsSoTheLineIsReadableInAList()
        {
            var s = HealthRules.Summary("WoundInfection (left arm)", 0.02, 0.015, 0.84, 0.644, tended: true);
            Assert.StartsWith("WoundInfection (left arm): severity", s);
        }
    }
}
