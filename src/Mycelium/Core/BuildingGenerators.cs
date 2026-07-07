using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Footprint generators for the building typologies in <see cref="BuildingType"/>.
    /// All generators work on a single parcel curve and return footprints in world coordinates.
    /// </summary>
    public static class BuildingGenerators
    {
        /// <summary>
        /// Generate a perimeter (courtyard) block.
        /// Returns building footprints and the courtyard interior curves.
        /// </summary>
        public static (List<Curve> footprints, List<Curve> courtyards) GeneratePerimeterBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var courtyards = new List<Curve>();
            var plane = Plane.WorldXY;

            var outerOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (outerOffsets == null || outerOffsets.Length == 0)
                return (footprints, courtyards);

            foreach (var outerCurve in outerOffsets)
            {
                if (!outerCurve.IsClosed)
                    outerCurve.MakeClosed(0.001);

                if (!outerCurve.TryGetPlane(out var checkPlane))
                    checkPlane = Plane.WorldXY;

                var bbox = outerCurve.GetBoundingBox(checkPlane);
                double minDim = Math.Min(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);

                // Limit depth to guarantee a visible courtyard
                double actualDepth = Math.Min(depth, minDim * 0.35);

                var innerOffsets = GeometryHelpers.OffsetCurve(outerCurve, -actualDepth, plane);

                if (innerOffsets != null && innerOffsets.Length > 0)
                {
                    var validInner = new List<Curve>();
                    foreach (var ic in innerOffsets)
                    {
                        if (!ic.IsClosed)
                            ic.MakeClosed(0.001);
                        if (ic.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise)
                            ic.Reverse();
                        validInner.Add(ic);
                    }

                    var diff = Curve.CreateBooleanDifference(outerCurve, validInner.ToArray(), 0.001);
                    if (diff != null)
                    {
                        footprints.AddRange(diff);
                        courtyards.AddRange(validInner);
                    }
                }
                else
                {
                    // No inner offset possible, solid block
                    footprints.Add(outerCurve);
                }
            }

            return (footprints, courtyards);
        }

        /// <summary>
        /// Generate a linear block (bar building) along the parcel's longer axis.
        /// </summary>
        public static List<Curve> GenerateLinearBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var plane = Plane.WorldXY;

            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return footprints;

            foreach (var buildableCurve in buildableOffsets)
            {
                if (!buildableCurve.IsClosed)
                    buildableCurve.MakeClosed(0.001);

                var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

                var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
                var curveLocal = buildableCurve.DuplicateCurve();
                curveLocal.Transform(xform);
                var bbox = curveLocal.GetBoundingBox(true);

                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;

                // Bar runs along the longer axis
                Rectangle3d bar;
                if (width > height)
                {
                    bar = new Rectangle3d(Plane.WorldXY,
                        new Point3d(bbox.Min.X, bbox.Center.Y - depth / 2, 0),
                        new Point3d(bbox.Max.X, bbox.Center.Y + depth / 2, 0));
                }
                else
                {
                    bar = new Rectangle3d(Plane.WorldXY,
                        new Point3d(bbox.Center.X - depth / 2, bbox.Min.Y, 0),
                        new Point3d(bbox.Center.X + depth / 2, bbox.Max.Y, 0));
                }

                var barCurve = bar.ToNurbsCurve();
                var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                barCurve.Transform(xformBack);

                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, barCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }

        /// <summary>
        /// Generate a point block (compact tower) centered in the parcel.
        /// </summary>
        public static List<Curve> GeneratePointBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var plane = Plane.WorldXY;

            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return footprints;

            foreach (var buildableCurve in buildableOffsets)
            {
                if (!buildableCurve.IsClosed)
                    buildableCurve.MakeClosed(0.001);

                var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

                var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
                var curveLocal = buildableCurve.DuplicateCurve();
                curveLocal.Transform(xform);
                var bbox = curveLocal.GetBoundingBox(true);

                var center = bbox.Center;
                double side = Math.Min(Math.Min(depth * 1.5, bbox.Max.X - bbox.Min.X), bbox.Max.Y - bbox.Min.Y);

                var square = new Rectangle3d(Plane.WorldXY,
                    new Point3d(center.X - side / 2, center.Y - side / 2, 0),
                    new Point3d(center.X + side / 2, center.Y + side / 2, 0));

                var pointCurve = square.ToNurbsCurve();
                var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                pointCurve.Transform(xformBack);

                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, pointCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }

        /// <summary>
        /// Generate an L-shaped building with a random corner orientation.
        /// Falls back to a linear block if the boolean union fails entirely.
        /// </summary>
        public static List<Curve> GenerateLShape(Curve parcel, double setback, double depth, Random rng)
        {
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, Plane.WorldXY);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return new List<Curve>();

            var buildable = buildableOffsets[0];
            if (!buildable.IsClosed)
                buildable.MakeClosed(0.001);

            var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double minDim = Math.Min(width, height);

            // Limit depth so the L-shape keeps a visible notch, even on small parcels
            double actualDepth = Math.Min(depth, minDim * 0.45);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Two rectangles form the L; they overlap slightly so the boolean union succeeds
            double overlap = 1.0;
            int config = rng.Next(0, 4);
            var rects = new List<Curve>();

            if (config == 0) // Bottom-Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + actualDepth, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY + actualDepth - overlap, 0), new Point3d(minX + actualDepth, maxY, 0)).ToNurbsCurve());
            }
            else if (config == 1) // Bottom-Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + actualDepth, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - actualDepth, minY + actualDepth - overlap, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
            }
            else if (config == 2) // Top-Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - actualDepth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - actualDepth, minY, 0), new Point3d(maxX, maxY - actualDepth + overlap, 0)).ToNurbsCurve());
            }
            else // Top-Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - actualDepth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + actualDepth, maxY - actualDepth + overlap, 0)).ToNurbsCurve());
            }

            return UnionAndTrim(rects, buildable, obbPlane, parcel, setback, depth);
        }

        /// <summary>
        /// Generate a U-shaped building with a random opening orientation.
        /// Falls back to a linear block if the boolean union fails entirely.
        /// </summary>
        public static List<Curve> GenerateUShape(Curve parcel, double setback, double depth, Random rng)
        {
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, Plane.WorldXY);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return new List<Curve>();

            var buildable = buildableOffsets[0];
            if (!buildable.IsClosed)
                buildable.MakeClosed(0.001);

            var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double minDim = Math.Min(width, height);

            // Limit depth so the U-shape keeps a visible opening
            double actualDepth = Math.Min(depth, minDim * 0.35);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Three rectangles form the U; they overlap slightly so the boolean union succeeds
            double overlap = 1.0;
            int config = rng.Next(0, 4);
            var rects = new List<Curve>();

            if (config == 0) // Open Top
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + actualDepth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY + actualDepth - overlap, 0), new Point3d(minX + actualDepth, maxY, 0)).ToNurbsCurve()); // left
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - actualDepth, minY + actualDepth - overlap, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // right
            }
            else if (config == 1) // Open Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - actualDepth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + actualDepth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY + actualDepth - overlap, 0), new Point3d(minX + actualDepth, maxY - actualDepth + overlap, 0)).ToNurbsCurve()); // left
            }
            else if (config == 2) // Open Bottom
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - actualDepth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + actualDepth, maxY - actualDepth + overlap, 0)).ToNurbsCurve()); // left
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - actualDepth, minY, 0), new Point3d(maxX, maxY - actualDepth + overlap, 0)).ToNurbsCurve()); // right
            }
            else // Open Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - actualDepth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + actualDepth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - actualDepth, minY + actualDepth - overlap, 0), new Point3d(maxX, maxY - actualDepth + overlap, 0)).ToNurbsCurve()); // right
            }

            return UnionAndTrim(rects, buildable, obbPlane, parcel, setback, depth);
        }

        /// <summary>
        /// Generate a tall building (rectangular tower) covering roughly 40% of the parcel.
        /// </summary>
        public static List<Curve> GenerateTallBuilding(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var plane = Plane.WorldXY;

            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return footprints;

            foreach (var buildableCurve in buildableOffsets)
            {
                if (!buildableCurve.IsClosed)
                    buildableCurve.MakeClosed(0.001);

                var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

                var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
                var curveLocal = buildableCurve.DuplicateCurve();
                curveLocal.Transform(xform);
                var bbox = curveLocal.GetBoundingBox(true);

                var center = bbox.Center;
                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;

                // Use 40% of width and height, at least 'depth' wide, clamped to the parcel
                double towerWidth = Math.Max(depth, width * 0.4);
                double towerHeight = Math.Max(depth, height * 0.4);
                towerWidth = Math.Min(towerWidth, width);
                towerHeight = Math.Min(towerHeight, height);

                var rect = new Rectangle3d(Plane.WorldXY,
                    new Point3d(center.X - towerWidth / 2, center.Y - towerHeight / 2, 0),
                    new Point3d(center.X + towerWidth / 2, center.Y + towerHeight / 2, 0));

                var towerCurve = rect.ToNurbsCurve();
                var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                towerCurve.Transform(xformBack);

                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, towerCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }

        /// <summary>
        /// Shared tail of the L/U generators: union the local-space rectangles, transform back
        /// to world space and trim to the buildable area, with per-rectangle fallback when the
        /// union fails.
        /// </summary>
        private static List<Curve> UnionAndTrim(List<Curve> rects, Curve buildable, Plane obbPlane,
            Curve parcel, double setback, double depth)
        {
            // Larger tolerance handles near-coincident edges between the rectangles
            var unified = Curve.CreateBooleanUnion(rects, 0.01);
            var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);

            if (unified == null || unified.Length == 0)
            {
                // Fallback: trim each rectangle individually
                var fallbackResult = new List<Curve>();
                foreach (var r in rects)
                {
                    r.Transform(xformBack);
                    var ints = GeometryHelpers.SafeBooleanIntersection(r, buildable, 0.001);
                    if (ints != null && ints.Length > 0)
                        fallbackResult.AddRange(ints);
                }
                return fallbackResult.Count > 0 ? fallbackResult : GenerateLinearBlock(parcel, setback, depth);
            }

            var final = new List<Curve>();
            foreach (var u in unified)
            {
                u.Transform(xformBack);
                var ints = GeometryHelpers.SafeBooleanIntersection(u, buildable, 0.001);
                if (ints != null && ints.Length > 0)
                    final.AddRange(ints);
                else
                    final.Add(u); // If the intersection fails, keep the un-trimmed shape
            }

            return final;
        }
    }
}
