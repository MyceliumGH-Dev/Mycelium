using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Recursive parcel subdivision using binary space partitioning.
    /// </summary>
    public static class ParcelSubdivision
    {
        /// <summary>
        /// Subdivide a parcel recursively. Each split carves a street strip out of the
        /// parent parcel, so streets emerge from the subdivision itself.
        /// </summary>
        /// <param name="boundary">Parcel boundary curve (closed).</param>
        /// <param name="divisions">Number of recursive divisions.</param>
        /// <param name="minArea">Minimum parcel area; smaller parcels are not split further.</param>
        /// <param name="streetWidth">Width of streets between parcels.</param>
        /// <param name="rng">Random number generator.</param>
        public static List<Curve> Subdivide(Curve boundary, int divisions, double minArea, double streetWidth, Random rng)
        {
            var result = new List<Curve>();

            if (boundary == null || !boundary.IsClosed)
                return result;

            // Base case: no more divisions
            if (divisions == 0)
            {
                result.Add(boundary);
                return result;
            }

            // Stop splitting below the minimum area
            double area = GeometryHelpers.GetCurveArea(boundary);
            if (area < minArea)
            {
                result.Add(boundary);
                return result;
            }

            Plane plane = Plane.WorldXY;
            if (!boundary.TryGetPlane(out plane))
                plane = Plane.WorldXY;

            // Work in local coordinates of the parcel plane
            var xformToLocal = Transform.PlaneToPlane(plane, Plane.WorldXY);
            var curveLocal = boundary.DuplicateCurve();
            curveLocal.Transform(xformToLocal);

            var bbox = curveLocal.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;

            // Split across the longer side, at a random 30-70% position
            bool splitHoriz = width < height;
            double splitRatio = rng.NextDouble() * 0.4 + 0.3;

            double streetHalf = streetWidth / 2.0;
            double xSplit = 0, ySplit = 0;

            if (splitHoriz)
                ySplit = bbox.Min.Y + height * splitRatio;
            else
                xSplit = bbox.Min.X + width * splitRatio;

            // Street rectangle extends well past the parcel so the difference always cuts through
            Rectangle3d street;
            if (splitHoriz)
            {
                Point3d p1 = new Point3d(bbox.Min.X - 100, ySplit - streetHalf, 0);
                Point3d p2 = new Point3d(bbox.Max.X + 100, ySplit + streetHalf, 0);
                street = new Rectangle3d(Plane.WorldXY, p1, p2);
            }
            else
            {
                Point3d p1 = new Point3d(xSplit - streetHalf, bbox.Min.Y - 100, 0);
                Point3d p2 = new Point3d(xSplit + streetHalf, bbox.Max.Y + 100, 0);
                street = new Rectangle3d(Plane.WorldXY, p1, p2);
            }

            var streetCurve = street.ToNurbsCurve();
            var xformToWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);
            streetCurve.Transform(xformToWorld);

            var diff = Curve.CreateBooleanDifference(boundary, streetCurve, 0.001);

            if (diff == null || diff.Length < 2)
            {
                // Split failed, keep the parcel whole
                result.Add(boundary);
                return result;
            }

            foreach (var childCurve in diff)
            {
                var children = Subdivide(childCurve, divisions - 1, minArea, streetWidth, rng);
                result.AddRange(children);
            }

            return result;
        }
    }
}
