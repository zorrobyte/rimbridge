using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class LifeLogicTests
    {
        [Fact]
        public void Blocks_OnlyAppliesRelevant()
        {
            Assert.Equal(new[] { "child", "genes", "mech" }, LifeLogic.Blocks(true, true, true));
            Assert.Equal(new[] { "genes" }, LifeLogic.Blocks(false, true, false));
            Assert.Empty(LifeLogic.Blocks(false, false, false));
        }
    }
}
