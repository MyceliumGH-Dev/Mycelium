using System;
using System.Collections.Generic;
using Mycelium.Core;
using Xunit;

namespace Mycelium.Core.Tests
{
    public class BuildingTypeConfigTests
    {
        private static HashSet<int> DrawMany(double minFloors, double maxFloors)
        {
            var config = BuildingTypeConfig.Default;
            config.MinFloors = minFloors;
            config.MaxFloors = maxFloors;

            var rng = new Random(7);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
                seen.Add(config.DrawFloorCount(rng));
            return seen;
        }

        [Fact]
        public void FloorCount_CoversEveryWholeFloorInRange()
        {
            Assert.Equal(new HashSet<int> { 3, 4, 5, 6 }, DrawMany(3.0, 6.0));
        }

        [Fact]
        public void FloorCount_RoundsFractionalBoundsInward()
        {
            Assert.Equal(new HashSet<int> { 3, 4 }, DrawMany(2.2, 4.8));
        }

        [Theory]
        [InlineData(0.0, 0.0, 1)]   // at least one floor
        [InlineData(-3.0, 1.0, 1)]
        [InlineData(5.0, 2.0, 5)]   // max below min collapses to min
        [InlineData(5.5, 5.7, 6)]   // empty rounded range keeps the rounded-up minimum
        public void FloorCount_HandlesDegenerateRanges(double minFloors, double maxFloors, int expected)
        {
            Assert.Equal(new HashSet<int> { expected }, DrawMany(minFloors, maxFloors));
        }

        [Theory]
        [InlineData(1000.0, 250.0, 4)]   // exact
        [InlineData(1125.0, 250.0, 5)]   // 4.5 rounds away from zero
        [InlineData(1100.0, 250.0, 4)]   // 4.4 rounds to nearest
        [InlineData(100.0, 250.0, 3)]    // below range clamps to MinFloors
        [InlineData(1e12, 250.0, 6)]     // above range clamps to MaxFloors
        [InlineData(0.0, 250.0, 3)]      // no target gives the minimum
        [InlineData(1000.0, 0.0, 3)]     // no footprint gives the minimum
        public void FloorCountForTarget_RoundsAndClamps(double targetGfa, double footprint, int expected)
        {
            var config = BuildingTypeConfig.Default; // 3 to 6 floors
            Assert.Equal(expected, config.FloorCountForTarget(targetGfa, footprint));
        }

        [Fact]
        public void FloorCount_ConsumesExactlyOneRandomValue()
        {
            // The generator shares one stream across all parcels, so the draw must use the same
            // number of values as the fractional draw it replaced.
            var config = BuildingTypeConfig.Default;
            var drawn = new Random(42);
            var reference = new Random(42);

            config.DrawFloorCount(drawn);
            reference.NextDouble();

            Assert.Equal(reference.NextDouble(), drawn.NextDouble());
        }
    }
}
