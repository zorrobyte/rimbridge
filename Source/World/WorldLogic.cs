using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free world helpers: goods summarization over plain rows so the caravan/settlement/ship
    /// summaries are unit-tested in RimBridge.Tests. The Verse-bound WorldRpc maps Things to rows.
    /// </summary>
    public static class WorldLogic
    {
        public struct GoodRow
        {
            public string Def;
            public int Count;
            public float Value;
        }

        public struct TopRow
        {
            public string Def;
            public int Count;
            public float Value;
        }

        public sealed class GoodsSummary
        {
            public float TotalValue;
            public int TotalStacks;
            public readonly List<TopRow> Top = new List<TopRow>();
        }

        public static GoodsSummary SummarizeGoods(IEnumerable<GoodRow> goods, int top)
        {
            var s = new GoodsSummary();
            if (top < 1) top = 1;
            foreach (var g in goods.OrderByDescending(g => g.Value).Take(top))
                s.Top.Add(new TopRow { Def = g.Def, Count = g.Count, Value = (float)Math.Round(g.Value, 1) });
            // Totals cover everything, not just the top slice.
            int stacks = 0; float value = 0;
            foreach (var g in goods) { stacks++; value += g.Value; }
            s.TotalStacks = stacks;
            s.TotalValue = (float)Math.Round(value, 1);
            return s;
        }
    }
}
