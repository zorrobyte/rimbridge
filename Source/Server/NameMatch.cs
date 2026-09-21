using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridge.Server
{
    /// <summary>
    /// "Did you mean" for a name the caller got wrong.
    ///
    /// The tools are named after the methods with the dots replaced by underscores, so rw_steward_orders_explain
    /// reads back as steward.orders_explain -- which does not exist. "unknown method" alone does not say that
    /// steward.orders.explain does, and a caller who has only that answer has nowhere to go but guessing.
    /// </summary>
    public static class NameMatch
    {
        static string Key(string s) => s.Replace(".", "").Replace("_", "").ToLowerInvariant();

        /// <summary>
        /// Known names worth offering for <paramref name="wanted"/>, best first: same name once dots and
        /// underscores are ignored, then a name that contains it or is contained by it.
        /// </summary>
        public static List<string> Near(string wanted, IEnumerable<string> known, int take = 8)
        {
            if (string.IsNullOrEmpty(wanted)) return new List<string>();
            string key = Key(wanted);
            var exact = new List<string>();
            var partial = new List<string>();
            foreach (var k in known.Distinct().OrderBy(k => k, StringComparer.Ordinal))
            {
                if (Key(k) == key) exact.Add(k);
                else if (k.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0
                      || wanted.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) partial.Add(k);
            }
            return exact.Concat(partial).Take(take).ToList();
        }
    }
}
