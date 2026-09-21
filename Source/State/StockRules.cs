// Written for RimBridge (2026). Verse-free rules for what the colony owns, shared with the unit tests in mod/Tests.

namespace RimBridge.State
{
    /// <summary>
    /// What the colony has.
    ///
    /// <c>state.stocks</c> and <c>key_stocks</c> both came from <c>map.resourceCounter</c>, which iterates the
    /// haul-destination groups -- storage. That answers "what can a bill consume?", and it was being used to
    /// answer "what do we own?". This is the same root cause as the food_days defect, which was fixed for food
    /// alone and left every other resource lying.
    ///
    /// The cost lands on turn one of every episode, because a crashlanding has nothing in a stockpile yet. On day
    /// 0 of episode 1 the colony was told <c>{"meat_all": 0, "stone_blocks": 0, "meals_all": 0}</c> and no WoodLog
    /// key at all, with 521 wood in eleven unforbidden stacks, the nearest two cells from the base, and twelve meal
    /// stacks on the ground. The model wrote "we have 0 wood, so I must cut trees to build anything".
    ///
    /// It also had to watch the interface disagree with itself: the steward's forestry job counts loose wood, so
    /// the model was handed both numbers at once and said so -- "Forestry reports 521 wood available vs 500 target
    /// ... but we have 0 wood stored" -- then spent a step chasing designations to explain the contradiction.
    ///
    /// Wood on the ground is wood. Forbidden wood is wood too: unforbidding is one action. Both are counted, and
    /// the unhauled part is reported beside the total so the model can see the work without being told to do it.
    /// </summary>
    public static class StockRules
    {
        /// <summary>Whether a stack counts toward what the colony owns, wherever it is lying.</summary>
        public static bool CountsAsStock(bool spawned, bool fogged) => spawned && !fogged;

        /// <summary>One resource, split by where it is. The split is the point: a total alone cannot say
        /// "you own this but nobody has carried it in yet".</summary>
        public sealed class Tally
        {
            public int Stored;
            public int Loose;
            public int Forbidden;

            public int Total { get { return Stored + Loose; } }

            public void Add(int count, bool stored, bool forbidden)
            {
                if (stored) Stored += count; else Loose += count;
                if (forbidden) Forbidden += count;
            }
        }
    }
}
