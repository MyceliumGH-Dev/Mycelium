using System;
using System.Linq;
using Mycelium.Core;
using Xunit;

namespace Mycelium.Core.Tests
{
    public class FloorAllocationTests
    {
        private static double Total(double scale, double[] areas, double[] weights, int[] min, int[] max)
        {
            return areas.Select((a, i) => FloorAllocation.ContinuousShare(scale * weights[i], a, min[i], max[i])).Sum();
        }

        [Fact]
        public void SolveScale_WithoutClamping_IsTheProportionalShare()
        {
            var areas = new[] { 100.0, 200.0, 400.0 };
            var weights = new[] { 500.0, 1000.0, 1500.0 };
            var min = new[] { 1, 1, 1 };
            var max = new[] { 50, 50, 50 };

            double scale = FloorAllocation.SolveScale(areas, weights, min, max, 6000.0);

            Assert.Equal(2.0, scale, 9); // 6000 / (500 + 1000 + 1500)
        }

        [Fact]
        public void SolveScale_PassesClampedFloorAreaToTheOtherBuildings()
        {
            // Equal demand of 2000 each would need 20 floors on the first building; it stops at 5.
            var areas = new[] { 100.0, 100.0 };
            var weights = new[] { 1.0, 1.0 };
            var min = new[] { 1, 1 };
            var max = new[] { 5, 60 };

            double scale = FloorAllocation.SolveScale(areas, weights, min, max, 4000.0);

            Assert.Equal(4000.0, Total(scale, areas, weights, min, max), 6);
            Assert.Equal(500.0, FloorAllocation.ContinuousShare(scale, 100.0, 1, 5), 6);
            Assert.Equal(3500.0, FloorAllocation.ContinuousShare(scale, 100.0, 1, 60), 6);
        }

        [Fact]
        public void SolveScale_AbsorbsFloorAreaForcedByAMinimum()
        {
            // The first building must take 10 floors, more than its even share.
            var areas = new[] { 100.0, 100.0 };
            var weights = new[] { 1.0, 1.0 };
            var min = new[] { 10, 1 };
            var max = new[] { 20, 20 };

            double scale = FloorAllocation.SolveScale(areas, weights, min, max, 1200.0);

            Assert.Equal(1200.0, Total(scale, areas, weights, min, max), 6);
            Assert.Equal(200.0, FloorAllocation.ContinuousShare(scale, 100.0, 1, 20), 6);
        }

        [Theory]
        [InlineData(1e9, 900.0)]  // above the range: everything at its maximum
        [InlineData(1.0, 300.0)]  // below the range: everything at its minimum
        public void SolveScale_OutsideTheFloorRange_SaturatesEveryBuilding(double target, double expected)
        {
            var areas = new[] { 100.0, 50.0 };
            var weights = new[] { 3.0, 1.0 };
            var min = new[] { 2, 2 };
            var max = new[] { 6, 6 };

            double scale = FloorAllocation.SolveScale(areas, weights, min, max, target);

            Assert.Equal(expected, Total(scale, areas, weights, min, max), 6);
        }

        [Fact]
        public void SolveScale_AgreesWithTheWholeFloorSolveOfTheConfig()
        {
            // The generator feeds scale * weight into FloorCountForTarget; at the top of the
            // range that must land on MaxFloors, not one below it.
            var config = BuildingTypeConfig.Default; // 3 to 6 floors
            config.FloorBounds(out int min, out int max);
            var areas = new[] { 137.0, 411.0 };
            var weights = new[] { 733.0, 291.0 };

            double scale = FloorAllocation.SolveScale(areas, weights, new[] { min, min }, new[] { max, max }, 1e9);

            Assert.Equal(6, config.FloorCountForTarget(scale * weights[0], areas[0]));
            Assert.Equal(6, config.FloorCountForTarget(scale * weights[1], areas[1]));
        }

        [Fact]
        public void SolveScale_HandlesNoBuildings()
        {
            Assert.Equal(0.0, FloorAllocation.SolveScale(new double[0], new double[0], new int[0], new int[0], 1000.0));
        }

        [Fact]
        public void HubFactors_RunLinearlyFromRatioToOne()
        {
            double[] factors = FloorAllocation.HubFactors(new[] { 10.0, 60.0, 110.0 }, 3.0);

            Assert.Equal(new[] { 3.0, 2.0, 1.0 }, factors.Select(f => Math.Round(f, 9)));
        }

        [Fact]
        public void HubFactors_AreUniformWhenDistancesDoNotDiffer()
        {
            Assert.Equal(new[] { 1.0, 1.0 }, FloorAllocation.HubFactors(new[] { 25.0, 25.0 }, 4.0));
            Assert.Empty(FloorAllocation.HubFactors(new double[0], 4.0));
        }
    }
}
