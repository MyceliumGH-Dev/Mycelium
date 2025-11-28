using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace FormFlux.Core
{
    /// <summary>
    /// Generators for different building typologies
    /// </summary>
    public static class BuildingGenerators
    {
        /// <summary>
        /// Generate perimeter block (courtyard) building
        /// Returns building footprints and courtyard interior curves
        /// </summary>
        public static (List<Curve> footprints, List<Curve> courtyards) GeneratePerimeterBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var courtyards = new List<Curve>();
            var plane = Plane.WorldXY;

            // Offset for setback
            var outerOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (outerOffsets == null || outerOffsets.Length == 0)
                return (footprints, courtyards);

            foreach (var outerCurve in outerOffsets)
            {
                if (!outerCurve.IsClosed)
                    outerCurve.MakeClosed(0.001);

                // Check if parcel is large enough for courtyard
                if (!outerCurve.TryGetPlane(out var checkPlane))
                    checkPlane = Plane.WorldXY;

                var bbox = outerCurve.GetBoundingBox(checkPlane);
                double minDim = Math.Min(bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y);
                double minCourtyard = depth;
                double requiredWidth = 2 * depth + minCourtyard;

                if (minDim <= requiredWidth)
                {
                    // Too small for courtyard - fallback to point block
                    var pointFootprints = GeneratePointBlock(parcel, setback, depth);
                    return (pointFootprints, new List<Curve>());
                }

                // Offset for building depth (create courtyard)
                var innerOffsets = GeometryHelpers.OffsetCurve(outerCurve, -depth, plane);

                if (innerOffsets != null && innerOffsets.Length > 0)
                {
                    // Boolean difference to create courtyard
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
                        // Add courtyard interiors for tree generation
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
        /// Generate linear block (bar building)
        /// </summary>
        public static List<Curve> GenerateLinearBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var plane = Plane.WorldXY;

            // Offset for setback
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return footprints;

            foreach (var buildableCurve in buildableOffsets)
            {
                if (!buildableCurve.IsClosed)
                    buildableCurve.MakeClosed(0.001);

                // Get oriented bounding box
                if (!buildableCurve.TryGetPlane(out var obbPlane))
                    obbPlane = Plane.WorldXY;

                var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
                var curveLocal = buildableCurve.DuplicateCurve();
                curveLocal.Transform(xform);
                var bbox = curveLocal.GetBoundingBox(true);

                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;
                
                // Create bar along longer axis
                Rectangle3d bar;
                if (width > height)
                {
                    // Horizontal bar
                    bar = new Rectangle3d(Plane.WorldXY,
                        new Point3d(bbox.Min.X, bbox.Center.Y - depth / 2, 0),
                        new Point3d(bbox.Max.X, bbox.Center.Y + depth / 2, 0));
                }
                else
                {
                    // Vertical bar
                    bar = new Rectangle3d(Plane.WorldXY,
                        new Point3d(bbox.Center.X - depth / 2, bbox.Min.Y, 0),
                        new Point3d(bbox.Center.X + depth / 2, bbox.Max.Y, 0));
                }

                var barCurve = bar.ToNurbsCurve();
                var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                barCurve.Transform(xformBack);

                // Intersect with buildable area
                var intersection = Curve.CreateBooleanIntersection(buildableCurve, barCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }

        /// <summary>
        /// Generate point block (tower)
        /// </summary>
        public static List<Curve> GeneratePointBlock(Curve parcel, double setback, double depth)
        {
            var footprints = new List<Curve>();
            var plane = Plane.WorldXY;

            // Offset for setback
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return footprints;

            foreach (var buildableCurve in buildableOffsets)
            {
                if (!buildableCurve.IsClosed)
                    buildableCurve.MakeClosed(0.001);

                // Get oriented bounding box
                if (!buildableCurve.TryGetPlane(out var obbPlane))
                    obbPlane = Plane.WorldXY;

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

                // Intersect with buildable area
                var intersection = Curve.CreateBooleanIntersection(buildableCurve, pointCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }

        /// <summary>
        /// Generate L-shaped building
        /// </summary>
        public static List<Curve> GenerateLShape(Curve parcel, double setback, double depth, Random rng)
        {
            var plane = Plane.WorldXY;

            // Offset for setback
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return new List<Curve>();

            var buildable = buildableOffsets[0];
            if (!buildable.IsClosed)
                buildable.MakeClosed(0.001);

            // Get OBB
            if (!buildable.TryGetPlane(out var obbPlane))
                obbPlane = Plane.WorldXY;

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Create two rectangles forming an L
            int config = rng.Next(0, 4);
            var rects = new List<Curve>();

            if (config == 0) // Bottom-Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + depth, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + depth, maxY, 0)).ToNurbsCurve());
            }
            else if (config == 1) // Bottom-Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + depth, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - depth, minY, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
            }
            else if (config == 2) // Top-Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - depth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - depth, minY, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
            }
            else // Top-Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - depth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve());
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + depth, maxY, 0)).ToNurbsCurve());
            }

            // Union rectangles
            var unified = Curve.CreateBooleanUnion(rects, 0.001);
            if (unified == null || unified.Length == 0)
                return new List<Curve>();

            // Transform back
            var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
            var unifiedWorld = new List<Curve>();
            foreach (var u in unified)
            {
                u.Transform(xformBack);
                unifiedWorld.Add(u);
            }

            // Intersect with buildable area
            var final = new List<Curve>();
            foreach (var u in unifiedWorld)
            {
                var ints = Curve.CreateBooleanIntersection(u, buildable, 0.001);
                if (ints != null)
                    final.AddRange(ints);
            }

            return final;
        }

        /// <summary>
        /// Generate U-shaped building
        /// </summary>
        public static List<Curve> GenerateUShape(Curve parcel, double setback, double depth, Random rng)
        {
            var plane = Plane.WorldXY;

            // Offset for setback
            var buildableOffsets = GeometryHelpers.OffsetCurve(parcel, -setback, plane);
            if (buildableOffsets == null || buildableOffsets.Length == 0)
                return new List<Curve>();

            var buildable = buildableOffsets[0];
            if (!buildable.IsClosed)
                buildable.MakeClosed(0.001);

            // Get OBB
            if (!buildable.TryGetPlane(out var obbPlane))
                obbPlane = Plane.WorldXY;

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double minDim = Math.Min(width, height);

            // Check minimum size for U-shape
            if (minDim < 3 * depth)
            {
                // Too small - fallback to linear block
                return GenerateLinearBlock(parcel, setback, depth);
            }

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Create three rectangles forming a U
            int config = rng.Next(0, 4);
            var rects = new List<Curve>();

            if (config == 0) // Open Top
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + depth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + depth, maxY, 0)).ToNurbsCurve()); // left
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - depth, minY, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // right
            }
            else if (config == 1) // Open Right
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - depth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + depth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + depth, maxY, 0)).ToNurbsCurve()); // left
            }
            else if (config == 2) // Open Bottom
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - depth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(minX + depth, maxY, 0)).ToNurbsCurve()); // left
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - depth, minY, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // right
            }
            else // Open Left
            {
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, maxY - depth, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // top
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(minX, minY, 0), new Point3d(maxX, minY + depth, 0)).ToNurbsCurve()); // bottom
                rects.Add(new Rectangle3d(Plane.WorldXY, new Point3d(maxX - depth, minY, 0), new Point3d(maxX, maxY, 0)).ToNurbsCurve()); // right
            }

            // Union rectangles
            var unified = Curve.CreateBooleanUnion(rects, 0.001);
            if (unified == null || unified.Length == 0)
                return new List<Curve>();

            // Transform back
            var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
            var unifiedWorld = new List<Curve>();
            foreach (var u in unified)
            {
                u.Transform(xformBack);
                unifiedWorld.Add(u);
            }

            // Intersect with buildable area
            var final = new List<Curve>();
            foreach (var u in unifiedWorld)
            {
                var ints = Curve.CreateBooleanIntersection(u, buildable, 0.001);
                if (ints != null)
                    final.AddRange(ints);
            }

            return final;
        }
    }
}
