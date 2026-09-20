using System.Collections.Generic;

namespace RimBridge.World
{
    /// <summary>
    /// Verse-free contract: which life blocks apply to a pawn. The engine mapping stays in LifeRpc.
    /// </summary>
    public static class LifeLogic
    {
        public static List<string> Blocks(bool isChild, bool hasGenes, bool isMech)
        {
            var blocks = new List<string>();
            if (isChild) blocks.Add("child");
            if (hasGenes) blocks.Add("genes");
            if (isMech) blocks.Add("mech");
            return blocks;
        }
    }
}
