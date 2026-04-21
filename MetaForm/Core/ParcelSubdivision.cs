using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace MetaForm.Core
{
    /// <summary>
    /// Recursive parcel subdivision using binary space partitioning
    /// </summary>
    public static class ParcelSubdivision
    {
        /// <summary>
        /// Subdivide a parcel recursively
        /// </summary>
        /// <param name="boundary">Parcel boundary curve</param>
        /// <param name="divisions">Number of recursive divisions</param>
        /// <param name="minArea">Minimum parcel area</param>
        /// <param name="streetWidth">Width of streets between parcels</param>
        /// <param name="rng">Random number generator</param>
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

            // Check minimum area
            double area = GeometryHelpers.GetCurveArea(boundary);
            if (area < minArea)
            {
                result.Add(boundary);
                return result;
            }

            // Define plane
            Plane plane = Plane.WorldXY;
            if (!boundary.TryGetPlane(out plane))
                plane = Plane.WorldXY;

            // Transform to local coordinates
            var xformToLocal = Transform.PlaneToPlane(plane, Plane.WorldXY);
            var curveLocal = boundary.DuplicateCurve();
            curveLocal.Transform(xformToLocal);

            var bbox = curveLocal.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;

            // Determine split axis (split the longer side)
            bool splitHoriz = width < height;

            // Random split position (30-70%)
            double splitRatio = rng.NextDouble() * 0.4 + 0.3; // 0.3 to 0.7

            double streetHalf = streetWidth / 2.0;
            double xSplit = 0, ySplit = 0;

            if (splitHoriz)
            {
                // Horizontal street
                ySplit = bbox.Min.Y + height * splitRatio;
            }
            else
            {
                // Vertical street
                xSplit = bbox.Min.X + width * splitRatio;
            }

            // Create street rectangle
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

            // Transform street back to world coordinates
            var xformToWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);
            streetCurve.Transform(xformToWorld);

            // Boolean difference
            var diff = Curve.CreateBooleanDifference(boundary, streetCurve, 0.001);

            if (diff == null || diff.Length < 2)
            {
                // Split failed, return original
                result.Add(boundary);
                return result;
            }

            // Recurse on children
            foreach (var childCurve in diff)
            {
                var children = Subdivide(childCurve, divisions - 1, minArea, streetWidth, rng);
                result.AddRange(children);
            }

            return result;
        }
    }
}
