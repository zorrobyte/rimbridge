using System.Collections.Generic;
using RimBridge.Decision;
using Xunit;

namespace RimBridge.Tests
{
    public class MedicalLogicTests
    {
        readonly List<string> Parts = new List<string> { "left leg", "left foot", "right leg", "spine", "left eye" };

        [Fact]
        public void BestLabelMatch_PrefersExactOverPrefixOverSubstring()
        {
            Assert.Equal(0, MedicalLogic.BestLabelMatch(Parts, "left leg"));
            Assert.Equal(0, MedicalLogic.BestLabelMatch(Parts, "LEFT LEG"));
            Assert.Equal(0, MedicalLogic.BestLabelMatch(Parts, "left"));
            Assert.Equal(3, MedicalLogic.BestLabelMatch(Parts, "spine"));
            Assert.Equal(1, MedicalLogic.BestLabelMatch(Parts, "foot"));
        }

        [Fact]
        public void BestLabelMatch_RejectsEmptyAndMissing()
        {
            Assert.Equal(-1, MedicalLogic.BestLabelMatch(Parts, ""));
            Assert.Equal(-1, MedicalLogic.BestLabelMatch(Parts, "antenna"));
            Assert.Equal(-1, MedicalLogic.BestLabelMatch(new List<string>(), "leg"));
        }
    }
}
