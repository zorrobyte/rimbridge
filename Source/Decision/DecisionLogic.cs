using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridge.Decision
{
    /// <summary>
    /// Verse-free decision helpers: candidate id codec, staleness, ranking, break-risk, base priorities.
    /// Nothing here touches the game, so it is unit-tested in RimBridge.Tests. The Verse-bound builders
    /// in Candidates.cs / Contexts.cs are thin wrappers over these rules.
    /// </summary>
    public static class DecisionLogic
    {
        // ---------- Candidate ids: "PawnId:action" or "PawnId:action:target" ----------

        public static string FormatId(string pawnId, string action, string? target)
            => string.IsNullOrEmpty(target) ? pawnId + ":" + action : pawnId + ":" + action + ":" + target;

        public static bool TryParseId(string id, out string pawnId, out string action, out string? target)
        {
            pawnId = ""; action = ""; target = null;
            if (string.IsNullOrEmpty(id)) return false;
            var parts = id.Split(':');
            if (parts.Length < 2) return false;
            pawnId = parts[0]; action = parts[1];
            if (pawnId.Length == 0 || action.Length == 0) return false;
            if (parts.Length > 2) target = string.Join(":", parts, 2, parts.Length - 2);
            return true;
        }

        // ---------- Freshness: a candidate is a snapshot; refuse to run it when the world moved on ----------

        /// <summary>True when more than maxStaleTicks game ticks passed since issue. Negative max disables the check.</summary>
        public static bool IsStale(int issuedTick, int nowTick, int maxStaleTicks)
            => maxStaleTicks >= 0 && (nowTick - issuedTick) > maxStaleTicks;

        // ---------- Mental break risk from mood vs the pawn's own thresholds (all 0..100) ----------

        public static string BreakRisk(double moodPct, double minor, double major, double extreme)
        {
            if (moodPct <= extreme) return "extreme";
            if (moodPct <= major) return "major";
            if (moodPct <= minor) return "minor";
            return "none";
        }

        // ---------- Ranking: emergencies first, then priority, capped ----------

        public struct RankItem
        {
            public string Id;
            public double Priority;
            public bool Emergency;
        }

        public static List<RankItem> Rank(IEnumerable<RankItem> items, int max)
        {
            if (max < 1) max = 1;
            return items.OrderByDescending(x => x.Emergency).ThenByDescending(x => x.Priority).Take(max).ToList();
        }

        // ---------- Candidate sources: "builtin" or "workgiver:<defName>" ----------

        public const string WorkgiverPrefix = "workgiver:";

        public static string WorkgiverSource(string defName) => WorkgiverPrefix + defName;

        public static bool TrySplitSource(string source, out string defName)
        {
            defName = "";
            if (source == null || !source.StartsWith(WorkgiverPrefix, StringComparison.Ordinal)) return false;
            defName = source.Substring(WorkgiverPrefix.Length);
            return defName.Length > 0;
        }

        // ---------- Throttle: fire at most once per interval (ticks) ----------

        public static bool ShouldFire(int lastTick, int nowTick, int intervalTicks)
            => nowTick - lastTick >= intervalTicks;

        // ---------- Base priorities: life-safety first, economy last ----------

        public static double BasePriority(string action) => action switch
        {
            "extinguish" => 90,
            "rescue" => 85,
            "tend" => 80,
            "attack" => 70,
            "take_cover" => 65,
            "rest" => 55,
            "continue_current_job" => 50,
            "cook" => 40,
            "craft" => 40,
            "construct" => 38,
            "repair" => 36,
            "haul" => 30,
            "mine" => 28,
            "harvest" => 26,
            "cut" => 26,
            "sow" => 24,
            "hunt" => 55,
            "research" => 32,
            "deconstruct" => 24,
            _ => 10,
        };
    }
}
