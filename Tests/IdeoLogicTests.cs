using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class IdeoLogicTests
    {
        [Fact]
        public void ObligationStatus_ReadyAnytimeWaiting()
        {
            Assert.Equal("ready at altar", IdeoLogic.ObligationStatus(true, "altar", false));
            Assert.Equal("can begin anytime", IdeoLogic.ObligationStatus(false, null, true));
            Assert.Equal("waiting for trigger", IdeoLogic.ObligationStatus(false, null, false));
        }
    }
}
