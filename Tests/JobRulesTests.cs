// Verse-free tests for the job line.
//
// JobDriver_DoBill keeps the bill's label through its ingredient-fetch toils, so
// a surgeon walking 75 cells for herbal medicine reported "Removing body part."
// JobDriver_TendPatient does the same. The model believed surgery was in
// progress, watched its surgeon walk away, and forced her back six times. Each
// order aborted the medicine round trip, the bill re-triggered, and she walked
// out again. It ran with the infection at 0.87 against a lethal 1.0.
//
// The destination is the fact that separates "fetch" from "operate". Comparing
// it to the patient's position is left to the reader.
using RimBridge.State;
using Xunit;

namespace RimBridge.Tests
{
    public class JobRulesTests
    {
        [Fact]
        public void AStationaryEmptyHandedPawnReadsExactlyAsBefore()
        {
            Assert.Equal("Removing body part.", JobRules.Describe("Removing body part.", null!, null!));
            Assert.Equal("Removing body part.", JobRules.Describe("Removing body part.", "", ""));
        }

        [Fact]
        public void TheFetchLegIsNowDistinguishableFromTheOperation()
        {
            var s = JobRules.Describe("Removing body part.", null!, JobRules.Place("MedicineHerbal", 170, 196));
            Assert.Equal("Removing body part. (going to MedicineHerbal [170, 196])", s);
        }

        [Fact]
        public void TheReturnLegShowsWhatIsBeingCarried()
        {
            var s = JobRules.Describe("Removing body part.", "MedicineHerbal x2", JobRules.Place(null!, 130, 122));
            Assert.Equal("Removing body part. (carrying MedicineHerbal x2, going to [130, 122])", s);
        }

        [Fact]
        public void PlaceFallsBackToTheBareCellWhenThereIsNoThing()
        {
            Assert.Equal("[10, 20]", JobRules.Place(null!, 10, 20));
            Assert.Equal("[10, 20]", JobRules.Place("", 10, 20));
        }

        [Fact]
        public void CarryingWithoutMovingIsStillWorthSaying()
        {
            Assert.Equal("Hauling. (carrying Steel x75)", JobRules.Describe("Hauling.", "Steel x75", null!));
        }
    }
}
