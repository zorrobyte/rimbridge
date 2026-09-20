using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class PodsLogicTests
    {
        [Fact]
        public void LaunchBlocker_ClearAndBlocked()
        {
            Assert.Null(PodsLogic.LaunchBlocker(100f, 40f, true, true));
            Assert.Equal("pod is gone", PodsLogic.LaunchBlocker(100f, 40f, true, false));
            Assert.Equal("loading still in progress", PodsLogic.LaunchBlocker(100f, 40f, false, true));
            Assert.Equal("out of range (150 tiles, fuel reaches 100)", PodsLogic.LaunchBlocker(100f, 150f, true, true));
        }
    }
}
