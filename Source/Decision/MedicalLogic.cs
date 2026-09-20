using System;
using System.Collections.Generic;
using System.Linq;

namespace RimBridge.Decision
{
    /// <summary>
    /// Verse-free helpers for surgery bills: match a requested body-part label against the parts a
    /// recipe can actually apply to. The engine mapping (BodyPartRecord -> label) stays in MedicalRpc.
    /// </summary>
    public static class MedicalLogic
    {
        /// <summary>Index of the best matching label, or -1. Exact beats prefix beats substring.</summary>
        public static int BestLabelMatch(IList<string> labels, string want)
        {
            if (labels == null || labels.Count == 0 || string.IsNullOrWhiteSpace(want)) return -1;
            string w = want.Trim();
            for (int i = 0; i < labels.Count; i++)
                if (string.Equals(labels[i], w, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < labels.Count; i++)
                if (labels[i] != null && labels[i].StartsWith(w, StringComparison.OrdinalIgnoreCase)) return i;
            for (int i = 0; i < labels.Count; i++)
                if (labels[i] != null && labels[i].IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return -1;
        }
    }
}
