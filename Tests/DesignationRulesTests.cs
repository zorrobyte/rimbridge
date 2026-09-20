// Verse-free tests for explaining a refusal the game gives no reason for.
//
// CanDesignateThing returns a bare false when a designation is unnecessary, and
// an AcceptanceReport built from a bool carries an EMPTY string -- so the
// bridge's `r.Reason ?? "not applicable"` never fired. The model asked to haul
// medicine, got applied: 0 with "reason": "" for every item, wrote "haul
// designations failed w/ empty reason - left them", and stopped trying. That
// medicine was 100 cells away when a colonist needed it.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class DesignationRulesTests
    {
        [Fact]
        public void TheHaulCaseSaysWhyNoDesignationWasNeeded()
        {
            var s = DesignationRules.Explain(null!, forbidden: false, haulable: true, inValidStorage: true);
            Assert.Equal("no designation needed: already in valid storage", s);
        }

        [Fact]
        public void AnExistingDesignationIsNamed()
        {
            var s = DesignationRules.Explain("Haul", forbidden: false, haulable: true, inValidStorage: false);
            Assert.Contains("already designated Haul", s);
        }

        [Fact]
        public void SeveralFactsAreListedTogether()
        {
            var s = DesignationRules.Explain("Haul", forbidden: true, haulable: false, inValidStorage: true);
            Assert.Contains("already designated Haul", s);
            Assert.Contains("already in valid storage", s);
            Assert.Contains("forbidden", s);
            Assert.Contains("not haulable", s);
        }

        [Fact]
        public void AnEmptyReasonIsNeverAnEmptyString()
        {
            var s = DesignationRules.Explain(null!, forbidden: false, haulable: true, inValidStorage: false);
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.Contains("the game gave no reason", s);
        }

        [Fact]
        public void NothingHereTellsTheReaderWhatToDo()
        {
            // The guard on the constraint: these sentences state what is true, not what to do about it.
            var s = DesignationRules.Explain(null!, forbidden: true, haulable: true, inValidStorage: false);
            Assert.DoesNotContain("unforbid", s);
            Assert.DoesNotContain("should", s);
            Assert.DoesNotContain("try", s);
        }
    }
}
