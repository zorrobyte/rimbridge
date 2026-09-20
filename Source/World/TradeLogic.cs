using System.Collections.Generic;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free trade planning: match buy (+count) / sell (-count) asks against the two
    /// sides' stock, clamping to what is actually there. Engine mapping stays in TradeRpc.
    /// </summary>
    public static class TradeLogic
    {
        public sealed class Ask
        {
            public string Def = "";
            public int Count;
        }

        public sealed class Stock
        {
            public string Def = "";
            public int Colony;
            public int Trader;
            public int MaxTransfer = int.MaxValue;
        }

        public sealed class Planned
        {
            public string Def = "";
            public int Count;
            public string Side = ""; // buy|sell
        }

        public static (List<Planned> planned, List<string> errors) Plan(IList<Ask> asks, IList<Stock> stocks)
        {
            var planned = new List<Planned>();
            var errors = new List<string>();
            foreach (var ask in asks)
            {
                if (ask.Count == 0) continue;
                Stock? s = null;
                foreach (var x in stocks)
                    if (string.Equals(x.Def, ask.Def, System.StringComparison.OrdinalIgnoreCase)) { s = x; break; }
                if (s == null) { errors.Add($"not in this trade: {ask.Def}"); continue; }
                if (ask.Count > 0)
                {
                    int n = System.Math.Min(ask.Count, System.Math.Min(s.Trader, s.MaxTransfer));
                    if (n <= 0) { errors.Add($"trader has no {ask.Def} to sell"); continue; }
                    planned.Add(new Planned { Def = s.Def, Count = n, Side = "buy" });
                }
                else
                {
                    int n = System.Math.Min(-ask.Count, System.Math.Min(s.Colony, s.MaxTransfer));
                    if (n <= 0) { errors.Add($"caravan has no {ask.Def} to sell"); continue; }
                    planned.Add(new Planned { Def = s.Def, Count = n, Side = "sell" });
                }
            }
            return (planned, errors);
        }
    }
}
