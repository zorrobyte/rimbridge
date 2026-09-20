// Written for RimBridge (2026). Verse-free rules for reading a colonist's condition, shared with the unit tests
// in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// What "how is this colonist doing" means.
    ///
    /// The observation used to answer it with <c>health</c>, RimWorld's summary health percent. That number scores
    /// intact body parts, not whether the colonist is winning. Measured across one day on one colonist it reached
    /// its MAXIMUM (100) while he lay unconscious hours from death, and fell to its MINIMUM (60) at the moment the
    /// amputation cured him. It moves the wrong way for exactly the case that kills people.
    ///
    /// An immunizable infection is a race: RimWorld kills when severity reaches 1.0 and cures when immunity does.
    /// So neither number decides anything on its own -- a severity of 0.02 that is losing is worse than a 0.4 that
    /// is winning -- and the decision is which one arrives first. That is what these rules compute.
    /// </summary>
    public static class HealthRules
    {
        /// <summary>Days until a value climbing at <paramref name="perDay"/> reaches 1.0. Negative means never.</summary>
        public static double DaysToFull(double current, double perDay)
            => perDay <= 0 ? -1 : (1.0 - current) / perDay;

        /// <summary>
        /// Who wins the race: "winning", "losing", or "stable" when neither side is moving.
        ///
        /// Levels alone cannot answer this. A fresh infection is always ahead of an immunity that starts at zero,
        /// so comparing the two numbers would report "losing" for every infection a colonist is about to shrug off.
        /// The rates are what the reflection in episode 3 derived by hand, at the cost of nearly losing a colonist.
        /// </summary>
        public static string Race(double severity, double immunity, double severityPerDay, double immunityPerDay)
        {
            var toDeath = DaysToFull(severity, severityPerDay);
            var toImmune = DaysToFull(immunity, immunityPerDay);
            if (toDeath < 0) return toImmune < 0 ? "stable" : "winning";
            if (toImmune < 0) return "losing";
            return toImmune < toDeath ? "winning" : "losing";
        }

        /// <summary>
        /// The one-line condition summary for the per-step brief.
        ///
        /// Phrased as a sentence rather than as more fields because the brief is read every step and the point of
        /// the finding is that a number in an on-demand call is not the same as seeing it.
        /// </summary>
        public static string Summary(string label, double severity, double immunity, string race, double daysToDeath, double daysToImmune, bool tended)
        {
            var parts = new List<string>
            {
                label + " severity " + Round2(severity),
                "immunity " + Round2(immunity),
                race
            };
            if (race == "losing" && daysToDeath >= 0) parts.Add("~" + Round1(daysToDeath) + "d to fatal");
            if (race == "winning" && daysToImmune >= 0) parts.Add("~" + Round1(daysToImmune) + "d to immune");
            parts.Add(tended ? "tended" : "UNTENDED");
            return string.Join(", ", parts.ToArray());
        }

        static string Round2(double v) => System.Math.Round(v, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Round1(double v) => System.Math.Round(v, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
