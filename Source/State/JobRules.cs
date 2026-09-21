// Written for RimBridge (2026). Verse-free rules for describing what a pawn is doing, shared with the unit tests
// in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// The job line.
    ///
    /// <c>JobDriver.GetReport()</c> names the JOB, and for the two drivers that matter most it names the bill
    /// rather than the activity. <c>JobDriver_DoBill</c> keeps the bill's label through its ingredient-fetch
    /// toils, so a surgeon walking 75 cells to collect herbal medicine reports "Removing body part."; so does
    /// <c>JobDriver_TendPatient</c>. Nothing separated "fetch the ingredients" from "operate", and the only tell
    /// was to compare two positions the model had to look up separately.
    ///
    /// What is added here is where the pawn is actually going and what they are actually holding. Both are facts
    /// the game already knows. The comparison between that destination and the patient is left to the reader --
    /// the point of the fix is that a reader who wants to make it no longer has to go and find the numbers.
    /// </summary>
    public static class JobRules
    {
        /// <summary>
        /// One line: what the job says it is, plus where the pawn is going and what they carry.
        /// Any part may be absent; a stationary empty-handed pawn reads exactly as it did before.
        /// </summary>
        public static string Describe(string report, string carrying, string destination)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(carrying)) parts.Add("carrying " + carrying);
            if (!string.IsNullOrEmpty(destination)) parts.Add("going to " + destination);
            if (parts.Count == 0) return report;
            return report + " (" + string.Join(", ", parts.ToArray()) + ")";
        }

        /// <summary>A destination as the model reads positions everywhere else: label first when there is one.</summary>
        public static string Place(string label, int x, int z)
        {
            var cell = "[" + x + ", " + z + "]";
            return string.IsNullOrEmpty(label) ? cell : label + " " + cell;
        }
    }
}
