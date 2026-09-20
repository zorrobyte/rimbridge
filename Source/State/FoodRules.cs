// Written for RimBridge (2026). Verse-free rules for what counts as food, shared with the unit tests in mod/Tests.
using System;

namespace RimBridge.State
{
    /// <summary>
    /// What the colony has to eat.
    ///
    /// The headline <c>food_days</c> came from <c>resourceCounter.TotalHumanEdibleNutrition</c>, which iterates the
    /// haul-destination groups -- i.e. storage. That answers "what can a bill consume?". The observation was using
    /// it to answer "will we starve?", and on turn 1 of episode 1 it reported a famine to a colony with 32 food
    /// stacks lying in the open, costing a whole step. Vanilla hits the same projection and papers over it: the
    /// low-food alert is suppressed for the first 150,000 ticks.
    ///
    /// Food on the ground is food. Forbidden food is food too -- unforbidding is one action, so reporting a famine
    /// because nobody has claimed the drop-pod loot yet is the same lie in new clothes. It is tallied separately so
    /// the model can see the action it needs to take, not subtracted from what the colony owns.
    /// </summary>
    public static class FoodRules
    {
        /// <summary>Nutrition one colonist eats per day.</summary>
        public const double NutritionPerColonistDay = 1.6;

        /// <summary>Whether a stack counts toward what the colony has to eat, wherever it is lying.</summary>
        public static bool CountsAsFood(bool humanEdible, bool fresh, bool fogged)
            => humanEdible && fresh && !fogged;

        /// <summary>Days of food for `colonists`, rounded the way the observation reports it.</summary>
        public static double FoodDays(double nutrition, int colonists)
            => colonists <= 0 ? 0.0 : Math.Round(nutrition / (colonists * NutritionPerColonistDay), 1);
    }
}
