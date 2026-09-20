// Verse-free tests for what counts as food.
//
// Turn 1 of episode 1: "food_days 0.0 - this is an emergency". There was no
// emergency. food_days came from resourceCounter, which counts what is in a
// stockpile, and the colony had 32 food stacks lying on the ground in the same
// payload. resourceCounter answers "what can a bill consume?"; it was used to
// answer "will we starve?".
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class FoodRulesTests
    {
        [Fact]
        public void FreshHumanEdibleFoodCounts_WhereverItIsLying()
        {
            Assert.True(FoodRules.CountsAsFood(humanEdible: true, fresh: true, fogged: false));
        }

        [Fact]
        public void ForbiddenFoodStillCounts()
        {
            // Forbidden is one click from edible, so reporting a famine because
            // nobody has claimed the pod loot yet is the same lie in new clothes.
            // It is reported separately so the model knows to unforbid, but it is
            // not subtracted from what the colony has.
            Assert.True(FoodRules.CountsAsFood(humanEdible: true, fresh: true, fogged: false));
        }

        [Fact]
        public void RottenFoodDoesNot()
        {
            Assert.False(FoodRules.CountsAsFood(humanEdible: true, fresh: false, fogged: false));
        }

        [Fact]
        public void KibbleAndHayDoNot()
        {
            Assert.False(FoodRules.CountsAsFood(humanEdible: false, fresh: true, fogged: false));
        }

        [Fact]
        public void FoodBehindFogDoesNot()
        {
            // Same rule as hostiles: the model may not count what the player cannot see.
            Assert.False(FoodRules.CountsAsFood(humanEdible: true, fresh: true, fogged: true));
        }

        [Fact]
        public void FoodDaysIsNutritionOverColonistsPerDay()
        {
            // RimWorld colonists eat 1.6 nutrition/day.
            Assert.Equal(7.5, FoodRules.FoodDays(36.0, 3), 3);
            Assert.Equal(0.0, FoodRules.FoodDays(0.0, 3), 3);
        }

        [Fact]
        public void FoodDaysWithNoColonistsIsZero_NotInfinity()
        {
            Assert.Equal(0.0, FoodRules.FoodDays(36.0, 0), 3);
        }

        [Fact]
        public void TheHeadlineCountsLooseFoodToo()
        {
            // The turn-1 case: nothing in the stockpile, plenty on the ground.
            Assert.Equal(0.0, FoodRules.FoodDays(0.0, 3), 3);                    // stored-only, the old number
            Assert.True(FoodRules.FoodDays(0.0 + 36.0, 3) > 7.0);                // stored + loose, the honest one
        }
    }
}
