// Written for RimBridge (2026). Verse-free rules for explaining a refusal, shared with the unit tests in mod/Tests.
using System.Collections.Generic;

namespace RimBridge.State
{
    /// <summary>
    /// Why a designation was refused, when the game gives no reason.
    ///
    /// RimWorld's <c>CanDesignateThing</c> returns a bare <c>false</c> when a designation is simply unnecessary,
    /// and an <c>AcceptanceReport</c> built from a bool carries an EMPTY string, not null -- so the bridge's
    /// <c>r.Reason ?? "not applicable"</c> never fired and every refusal came back as <c>"reason": ""</c>.
    ///
    /// The model asked to haul medicine, received applied: 0 with a blank reason for every item, wrote
    /// "haul designations failed w/ empty reason - left them", and stopped trying. The medicine was still 100
    /// cells away when a colonist needed it. The refusal was honest and unusable.
    ///
    /// These sentences state what is observably true of the thing. They do not say what to do about it.
    /// </summary>
    public static class DesignationRules
    {
        /// <summary>
        /// A reason built from what can be observed. <paramref name="existingDesignation"/> is the def name of a
        /// designation already on the thing, or null.
        /// </summary>
        public static string Explain(string existingDesignation, bool forbidden, bool haulable, bool inValidStorage)
        {
            var facts = new List<string>();
            if (!string.IsNullOrEmpty(existingDesignation)) facts.Add("already designated " + existingDesignation);
            if (inValidStorage) facts.Add("already in valid storage");
            if (forbidden) facts.Add("forbidden");
            if (!haulable) facts.Add("not haulable");
            if (facts.Count == 0) return "no designation needed (the game gave no reason)";
            return "no designation needed: " + string.Join(", ", facts.ToArray());
        }
    }
}
