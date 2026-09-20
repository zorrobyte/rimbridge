using System;
using System.Collections.Generic;
using RimBridge.World;
using Xunit;

namespace RimBridge.Tests
{
    public class CaravanLogicTests
    {
        [Fact]
        public void Centroid_AveragesPositions()
        {
            var cells = new List<(int x, int z)> { (100, 100), (110, 104), (105, 102) };

            Assert.Equal((105, 102), CaravanLogic.Centroid(cells));
        }

        [Fact]
        public void Centroid_SingleCellAndEmpty()
        {
            Assert.Equal((7, 9), CaravanLogic.Centroid(new List<(int x, int z)> { (7, 9) }));
            Assert.Throws<ArgumentException>(() => CaravanLogic.Centroid(new List<(int x, int z)>()));
        }
    }
}
