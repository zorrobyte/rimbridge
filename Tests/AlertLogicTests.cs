using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class AlertLogicTests
    {
        [Fact]
        public void Suggest_KnownAlerts_MapToVerbs()
        {
            var fire = AlertLogic.Suggest("Alert_FireInHomeArea");
            Assert.Equal("beat out the fires", fire.Action);
            Assert.Contains("decision.candidates", fire.Via);

            var idle = AlertLogic.Suggest("Alert_ColonistsIdle");
            Assert.Contains("decision.candidates", idle.Via);
        }

        [Fact]
        public void Suggest_Unknown_FallsBackToInvestigate()
        {
            var s = AlertLogic.Suggest("Alert_SomeFutureDLCThing");
            Assert.Equal("investigate the culprits", s.Action);
            Assert.Equal("ui.select", s.Via);
        }
    }
}
