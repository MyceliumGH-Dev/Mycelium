using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Places procedural trees (spheres) inside park and courtyard boundaries.
    /// </summary>
    public static class TreeGenerator
    {
        /// <summary>
        /// Generate random tree spheres within a boundary using rejection sampling.
        /// </summary>
        /// <param name="parkCurve">Closed boundary curve of the park or courtyard.</param>
        /// <param name="rng">Random number generator.</param>
        /// <param name="densityPercent">Tree density in percent (0-100); 100% is one tree per 25 m².</param>
        /// <param name="minDiameter">Minimum tree diameter in meters.</param>
        /// <param name="maxDiameter">Maximum tree diameter in meters.</param>
        public static List<Brep> GenerateTrees(Curve parkCurve, Random rng, double densityPercent = 100.0, double minDiameter = 2.0, double maxDiameter = 5.0)
        {
            var trees = new List<Brep>();

            if (parkCurve == null || !parkCurve.IsClosed)
                return trees;

            densityPercent = Math.Max(0, Math.Min(100, densityPercent));
            minDiameter = Math.Max(0.1, minDiameter);
            maxDiameter = Math.Max(minDiameter, maxDiameter);

            double area = GeometryHelpers.GetCurveArea(parkCurve);
            int numTrees = (int)(area / 25.0 * (densityPercent / 100.0));

            if (numTrees == 0)
                return trees;

            if (!parkCurve.TryGetPlane(out var plane))
                plane = Plane.WorldXY;

            var xform = Transform.PlaneToPlane(plane, Plane.WorldXY);
            var curveLocal = parkCurve.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            // Rejection sampling: three attempts per requested tree
            int attempts = numTrees * 3;
            int count = 0;

            for (int i = 0; i < attempts && count < numTrees; i++)
            {
                double x = rng.NextDouble() * (bbox.Max.X - bbox.Min.X) + bbox.Min.X;
                double y = rng.NextDouble() * (bbox.Max.Y - bbox.Min.Y) + bbox.Min.Y;
                Point3d ptLocal = new Point3d(x, y, 0);

                var containment = curveLocal.Contains(ptLocal, Plane.WorldXY, 0.001);
                if (containment == PointContainment.Inside)
                {
                    var xformBack = Transform.PlaneToPlane(Plane.WorldXY, plane);
                    ptLocal.Transform(xformBack);

                    double minRadius = minDiameter / 2.0;
                    double maxRadius = maxDiameter / 2.0;
                    double radius = rng.NextDouble() * (maxRadius - minRadius) + minRadius;

                    // Raise the center by the radius so the sphere sits on the ground
                    Point3d center = ptLocal + new Vector3d(0, 0, radius);

                    var sphere = new Sphere(center, radius);
                    trees.Add(sphere.ToBrep());
                    count++;
                }
            }

            return trees;
        }
    }
}
