using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class AttackRulesTests
    {
        // The episode 2 case: 14.4 cells, a 36.9-cell rifle, no shot, because the wall is on the line.
        [Fact]
        public void InRangeAndRefusedIsBlockedNotOutOfRange()
        {
            Assert.Equal(AttackRules.Blocked, AttackRules.Reason(canHit: false, melee: false, moved: false, 14.4, 36.9));
            string note = AttackRules.Note(canHit: false, melee: false, moved: false, 14.4, 36.9)!;
            Assert.DoesNotContain("out of range", note);
            Assert.Contains("blocked", note);
            Assert.Contains("14.4 of 36.9", note);
        }

        [Fact]
        public void BeyondRangeIsOutOfRange()
        {
            Assert.Equal(AttackRules.OutOfRange, AttackRules.Reason(canHit: false, melee: false, moved: false, 48.0, 36.9));
            Assert.Contains("out of range (48 of 36.9)", AttackRules.Note(canHit: false, melee: false, moved: false, 48.0, 36.9));
        }

        [Fact]
        public void EqualToRangeIsBlocked()
        {
            Assert.Equal(AttackRules.Blocked, AttackRules.Reason(canHit: false, melee: false, moved: false, 36.9, 36.9));
        }

        [Theory]
        [InlineData(true, false, false)]   // can shoot
        [InlineData(false, true, false)]   // melee chases on its own
        [InlineData(false, false, true)]   // already moving to a cast position
        public void NothingToSayWhenTheShotIsFineOrThePawnIsMoving(bool canHit, bool melee, bool moved)
        {
            Assert.Null(AttackRules.Reason(canHit, melee, moved, 14.4, 36.9));
            Assert.Null(AttackRules.Note(canHit, melee, moved, 14.4, 36.9));
        }

        // The note reports and does not recommend. An earlier one recommended approach for every refused
        // shot; that is the sentence that lost the colony in episode 2.
        [Fact]
        public void TheBlockedNoteDoesNotTellTheCallerWhatToDo()
        {
            string note = AttackRules.Note(canHit: false, melee: false, moved: false, 14.4, 36.9)!;
            foreach (string word in new[] { "should", "recommend", "hold position", "do not approach", "use approach" })
                Assert.DoesNotContain(word, note, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
