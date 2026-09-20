// Verse-free tests for whether a hunt designation can be actioned.
//
// The game warns once, with a transient message at designation time. After
// that the condition is invisible: no standing alert exists for a colony with
// hunt designations and no ranged hunter.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class HuntingRulesTests
    {
        [Fact]
        public void NoDesignationsIsNotAProblem()
        {
            Assert.Empty(HuntingRules.Problems(designations: 0, assigned: 0, armed: 0, shielded: 0));
        }

        [Fact]
        public void AnArmedHunterWithNoShieldHasNoProblems()
        {
            Assert.Empty(HuntingRules.Problems(designations: 4, assigned: 2, armed: 2, shielded: 0));
        }

        [Fact]
        public void DesignationsWithNobodyAssignedToHuntingAreReported()
        {
            var p = HuntingRules.Problems(designations: 3, assigned: 0, armed: 0, shielded: 0);
            Assert.Contains("3 hunt designation(s) but no colonist is assigned to Hunting", p);
        }

        [Fact]
        public void AssignedButUnarmedHuntersAreToldThatMeleeHuntingIsNotPossible()
        {
            // ShouldSkip ignores the forced flag, so there is no override to find.
            var p = HuntingRules.Problems(designations: 3, assigned: 2, armed: 0, shielded: 0);
            Assert.Contains("3 hunt designation(s) but no hunter has a ranged weapon; melee hunting is not possible", p);
        }

        [Fact]
        public void TheUnarmedCaseDoesNotAlsoClaimNobodyIsAssigned()
        {
            var p = HuntingRules.Problems(designations: 3, assigned: 2, armed: 0, shielded: 0);
            Assert.DoesNotContain("assigned to Hunting", string.Join(" ", p));
        }

        [Fact]
        public void AShieldBeltOnAHunterIsReported()
        {
            var p = HuntingRules.Problems(designations: 5, assigned: 3, armed: 3, shielded: 1);
            Assert.Contains("1 hunter(s) using a ranged weapon and wearing a shield belt, which prevents shooting at range", p);
        }

        [Fact]
        public void OneShieldedHunterAmongOthersDoesNotStopTheHunt()
        {
            var p = HuntingRules.Problems(designations: 5, assigned: 3, armed: 3, shielded: 1);
            Assert.DoesNotContain("no hunter able to shoot", string.Join(" ", p));
        }

        [Fact]
        public void EveryHunterShieldedLeavesNobodyAbleToShoot()
        {
            var p = HuntingRules.Problems(designations: 5, assigned: 2, armed: 2, shielded: 2);
            Assert.Contains("5 hunt designation(s) with no hunter able to shoot", p);
        }

        [Fact]
        public void HuntersWithoutDesignationsAreNotReported()
        {
            Assert.Empty(HuntingRules.Problems(designations: 0, assigned: 3, armed: 0, shielded: 0));
        }
    }
}
