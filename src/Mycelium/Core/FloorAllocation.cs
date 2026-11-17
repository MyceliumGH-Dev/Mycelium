using System;
using System.Collections.Generic;
using System.Linq;

namespace Mycelium.Core
{
    /// <summary>
    /// Shares a site's target gross floor area among its buildings. Rhino-free.
    /// </summary>
    public static class FloorAllocation
    {
        /// <summary>
        /// Scale s such that the buildings' floor areas sum to <paramref name="targetGfa"/> when
        /// building i gets s * weights[i] of floor area, held within its floor range. Floor area a
        /// building cannot take (or must take) because of that range is thereby passed on to the
        /// others. A target outside what the ranges allow gives every building its minimum or
        /// its maximum.
        /// </summary>
        public static double SolveScale(IReadOnlyList<double> footprintAreas, IReadOnlyList<double> weights,
            IReadOnlyList<int> minFloors, IReadOnlyList<int> maxFloors, double targetGfa)
        {
            double Gfa(double scale)
            {
                double sum = 0.0;
                for (int i = 0; i < footprintAreas.Count; i++)
                    sum += ContinuousShare(scale * weights[i], footprintAreas[i], minFloors[i], maxFloors[i]);
                return sum;
            }

            // Smallest scale at which every building has reached its maximum.
            double hi = 0.0;
            for (int i = 0; i < footprintAreas.Count; i++)
                if (footprintAreas[i] > 0.0 && weights[i] > 0.0)
                    hi = Math.Max(hi, maxFloors[i] * footprintAreas[i] / weights[i]);

            if (!(targetGfa > 0.0) || hi <= 0.0 || Gfa(hi) <= targetGfa)
                return hi;

            // Total floor area never decreases with the scale, so bisection finds the target.
            double lo = 0.0;
            for (int k = 0; k < 100; k++)
            {
                double mid = 0.5 * (lo + hi);
                if (Gfa(mid) < targetGfa) lo = mid; else hi = mid;
            }
            return hi;
        }

        /// <summary>
        /// Floor area a building takes of <paramref name="demand"/> before rounding to whole floors.
        /// </summary>
        public static double ContinuousShare(double demand, double footprintArea, int minFloors, int maxFloors)
        {
            if (!(footprintArea > 0.0))
                return 0.0;
            double floors = demand > 0.0 ? demand / footprintArea : 0.0;
            return footprintArea * Math.Min(maxFloors, Math.Max(minFloors, floors));
        }

        /// <summary>
        /// Density factor per parcel from its distance to the nearest hub: <paramref name="ratio"/>
        /// at the nearest parcel, 1 at the farthest, linear in between. All 1 when the distances
        /// do not differ.
        /// </summary>
        public static double[] HubFactors(IReadOnlyList<double> distances, double ratio)
        {
            if (distances.Count == 0)
                return new double[0];
            double near = distances.Min();
            double span = distances.Max() - near;
            return distances
                .Select(d => span > 0.0 ? ratio + (1.0 - ratio) * (d - near) / span : 1.0)
                .ToArray();
        }
    }
}
