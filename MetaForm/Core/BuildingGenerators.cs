using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace MetaForm.Core
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
                
                // Limit depth to guarantee a visible courtyard
                double actualDepth = Math.Min(depth, minDim * 0.35);

                // Offset for building depth (create courtyard)
                var innerOffsets = GeometryHelpers.OffsetCurve(outerCurve, -actualDepth, plane);

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
                var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

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
                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, barCurve, 0.001);
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

                // Intersect with buildable area
                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, pointCurve, 0.001);
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
            var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double minDim = Math.Min(width, height);
            
            // Limit depth so the L-shape actually has a visible notch, even on small parcels
            double actualDepth = Math.Min(depth, minDim * 0.45);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Create two rectangles forming an L
            // Overlap only the internal joints to ensure Boolean Union succeeds
            double overlap = 1.0; // 1m internal overlap
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

            // Union rectangles (use larger tolerance to handle near-coincident edges)
            var unified = Curve.CreateBooleanUnion(rects, 0.01);
            if (unified == null || unified.Length == 0)
            {
                // Fallback: return the two rectangles as separate footprints
                // Transform back and intersect each individually
                var xformBack2 = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                var fallbackResult = new List<Curve>();
                foreach (var r in rects)
                {
                    r.Transform(xformBack2);
                    var ints = GeometryHelpers.SafeBooleanIntersection(r, buildable, 0.001);
                    if (ints != null)
                        fallbackResult.AddRange(ints);
                }
                return fallbackResult.Count > 0 ? fallbackResult : GenerateLinearBlock(parcel, setback, depth);
            }

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
                var ints = GeometryHelpers.SafeBooleanIntersection(u, buildable, 0.001);
                if (ints != null && ints.Length > 0)
                    final.AddRange(ints);
                else
                    final.Add(u); // Fallback: if intersection fails, use the un-trimmed shape
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
            var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

            var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
            var curveLocal = buildable.DuplicateCurve();
            curveLocal.Transform(xform);
            var bbox = curveLocal.GetBoundingBox(true);

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double minDim = Math.Min(width, height);

            // Limit depth so the U-shape actually has a visible opening
            double actualDepth = Math.Min(depth, minDim * 0.35);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;

            // Create three rectangles forming a U
            double overlap = 1.0; // 1m internal overlap
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

            // Union rectangles (use larger tolerance to handle near-coincident edges)
            var unified = Curve.CreateBooleanUnion(rects, 0.01);
            if (unified == null || unified.Length == 0)
            {
                // Fallback: return the three rectangles as separate footprints
                var xformBack2 = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                var fallbackResult = new List<Curve>();
                foreach (var r in rects)
                {
                    r.Transform(xformBack2);
                    var ints = GeometryHelpers.SafeBooleanIntersection(r, buildable, 0.001);
                    if (ints != null && ints.Length > 0)
                        fallbackResult.AddRange(ints);
                }
                return fallbackResult.Count > 0 ? fallbackResult : GenerateLinearBlock(parcel, setback, depth);
            }

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
                var ints = GeometryHelpers.SafeBooleanIntersection(u, buildable, 0.001);
                if (ints != null && ints.Length > 0)
                    final.AddRange(ints);
                else
                    final.Add(u); // Fallback: if intersection fails, use the un-trimmed shape
            }

            return final;
        }
        /// <summary>
        /// Generate tall building (rectangular tower)
        /// </summary>
        public static List<Curve> GenerateTallBuilding(Curve parcel, double setback, double depth)
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
                var obbPlane = GeometryHelpers.GetOrientedBoundingBoxPlane(parcel);

                var xform = Transform.PlaneToPlane(obbPlane, Plane.WorldXY);
                var curveLocal = buildableCurve.DuplicateCurve();
                curveLocal.Transform(xform);
                var bbox = curveLocal.GetBoundingBox(true);

                var center = bbox.Center;
                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;

                // Create a rectangular tower footprint (smaller than full buildable area)
                // Use 40% of width and height, but ensure it's at least 'depth' wide
                double towerWidth = Math.Max(depth, width * 0.4);
                double towerHeight = Math.Max(depth, height * 0.4);

                // Clamp to max available size
                towerWidth = Math.Min(towerWidth, width);
                towerHeight = Math.Min(towerHeight, height);

                var rect = new Rectangle3d(Plane.WorldXY,
                    new Point3d(center.X - towerWidth / 2, center.Y - towerHeight / 2, 0),
                    new Point3d(center.X + towerWidth / 2, center.Y + towerHeight / 2, 0));

                var towerCurve = rect.ToNurbsCurve();
                var xformBack = Transform.PlaneToPlane(Plane.WorldXY, obbPlane);
                towerCurve.Transform(xformBack);

                // Intersect with buildable area
                var intersection = GeometryHelpers.SafeBooleanIntersection(buildableCurve, towerCurve, 0.001);
                if (intersection != null)
                    footprints.AddRange(intersection);
            }

            return footprints;
        }
    }
}
