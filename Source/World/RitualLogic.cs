using System.Collections.Generic;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free ritual role planning: greedy-fill roles from candidate pawn ids.
    /// The RPC extracts candidates via CandidatesForRole and applies the plan via TryAssign.
    /// </summary>
    public static class RitualLogic
    {
        public sealed class RoleSlot
        {
            public string RoleId = "";
            public bool Required;
            public int Max = 1;
        }

        public sealed class RolePlan
        {
            public Dictionary<string, List<string>> Assigned = new Dictionary<string, List<string>>();
            public List<string> UnfilledRequired = new List<string>();
        }

        public static RolePlan AssignRoles(
            IList<RoleSlot> roles,
            IDictionary<string, IList<string>> candidatesByRole,
            ISet<string>? allowed = null)
        {
            var plan = new RolePlan();
            var used = new HashSet<string>();
            foreach (var role in roles)
            {
                var got = new List<string>();
                if (candidatesByRole.TryGetValue(role.RoleId, out var cands))
                {
                    foreach (var c in cands)
                    {
                        if (got.Count >= role.Max) break;
                        if (used.Contains(c)) continue;
                        if (allowed != null && !allowed.Contains(c)) continue;
                        used.Add(c);
                        got.Add(c);
                    }
                }
                plan.Assigned[role.RoleId] = got;
                if (role.Required && got.Count == 0) plan.UnfilledRequired.Add(role.RoleId);
            }
            return plan;
        }
    }
}
