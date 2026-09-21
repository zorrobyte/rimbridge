namespace RimBridge.State
{
    /// <summary>
    /// Why a drafted attack order will not produce a shot.
    ///
    /// ui.attack used to answer this with one sentence that began "out of range", fired on nothing but
    /// !CanHitTarget. CanHitTarget is false for a blocked line as well as for distance, so the sentence
    /// appeared beside distance 14.4 and weapon_range 36.9 and said the opposite of both. The two cases
    /// need opposite actions -- out of range: close; blocked by your own wall: do not walk out of it --
    /// so they are separated here and named, and the caller decides.
    /// </summary>
    public static class AttackRules
    {
        public const string OutOfRange = "out_of_range";
        public const string Blocked = "blocked";

        /// <summary>The machine-readable reason, or null when the pawn can shoot or is already moving.</summary>
        public static string? Reason(bool canHit, bool melee, bool moved, double distance, double range)
        {
            if (canHit || moved || melee) return null;
            return distance > range ? OutOfRange : Blocked;
        }

        /// <summary>
        /// The same fact in a sentence. It says what is true and what the parameters do. It does not say
        /// which to use: an earlier version of this note recommended approach for every refused shot, and
        /// that recommendation walked a colonist out of his own wall and lost the colony.
        /// </summary>
        public static string? Note(bool canHit, bool melee, bool moved, double distance, double range)
        {
            string? reason = Reason(canHit, melee, moved, distance, range);
            if (reason == null) return null;
            string where = $"{distance} of {range}";
            if (reason == OutOfRange)
                return $"out of range ({where}) and not moving: AttackStatic has no goto toil, so the pawn holds "
                     + "the job where it stands. approach=true closes the distance; ui.goto moves the pawn.";
            return $"in range ({where}) and the shot is blocked, so distance is not the cause: a wall, a door or "
                 + "cover is on the line. approach=true moves the pawn to a cell with a clear line, which can be "
                 + "outside the wall it is standing behind; ui.goto moves the pawn.";
        }
    }
}
