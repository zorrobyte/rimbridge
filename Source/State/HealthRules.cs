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
    /// So neither number decides anything on its own -- a severity of 0.02 that is losing the race is worse than a
    /// 0.4 that is winning it -- and the levels alone cannot tell them apart, because immunity starts at zero and
    /// is behind at the start of every infection a colonist will shrug off.
    ///
    /// <b>These rules report both levels, both rates, and how long each has to run. They do not judge the race.</b>
    /// An earlier version of this file returned "winning"/"losing" and shouted "UNTENDED". That is us playing the
    /// colony. The episode 3 reflection derived the decision rule unaided once it had numbers -- severity
    /// +0.84/day against immunity +0.644/day -- so numbers are what it needs, and the verdict is its own to form.
    /// </summary>
    public static class HealthRules
    {
        /// <summary>Days until a value climbing at <paramref name="perDay"/> reaches 1.0. Negative means never.</summary>
        public static double DaysToFull(double current, double perDay)
            => perDay <= 0 ? -1 : (1.0 - current) / perDay;

        /// <summary>
        /// One condition, as levels and rates.
        ///
        /// Reported every step on purpose. The pair already existed in <c>state.pawn</c>, and finding 12 is that a
        /// field in an on-demand call is not the same as seeing it: the model made 28 <c>state.pawn</c> calls in
        /// one episode and still did not notice the infection until it was nearly fatal.
        /// </summary>
        public static string Summary(string label, double severity, double immunity, double severityPerDay, double immunityPerDay, bool tended)
        {
            var parts = new List<string>
            {
                label + ": severity " + Track(severity, severityPerDay),
                "immunity " + Track(immunity, immunityPerDay),
                "tended: " + (tended ? "yes" : "no")
            };
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>A level, its rate, and when it reaches 1.0 at that rate. "0.02 +0.84/day (1.2d to 1.0)".</summary>
        static string Track(double level, double perDay)
        {
            var s = Round2(level);
            if (perDay <= 0) return s + " (not rising)";
            return s + " +" + Round2(perDay) + "/day (" + Round1(DaysToFull(level, perDay)) + "d to 1.0)";
        }

        static string Round2(double v) => System.Math.Round(v, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Round1(double v) => System.Math.Round(v, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
