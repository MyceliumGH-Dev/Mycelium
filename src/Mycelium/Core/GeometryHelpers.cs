using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Helper functions for geometric operations.
    /// </summary>
    public static class GeometryHelpers
    {
        /// <summary>
        /// Get the area of a closed curve; returns 0 for open or invalid curves.
        /// </summary>
        public static double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed)
                return 0.0;

            var amp = AreaMassProperties.Compute(curve);
            return amp != null ? amp.Area : 0.0;
        }

        /// <summary>
        /// Get an oriented bounding box plane aligned to the longest straight edge of the curve.
        /// </summary>
        public static Plane GetOrientedBoundingBoxPlane(Curve curve)
        {
            var plane = Plane.WorldXY;
            if (curve == null) return plane;

            Polyline polyline = null;
            if (!curve.TryGetPolyline(out polyline))
            {
                var plc = curve.ToPolyline(0.01, 0.1, 0.0, 0.0);
                if (plc != null)
                    plc.TryGetPolyline(out polyline);
            }

            if (polyline != null)
            {
                double maxLength = 0;
                Line longestEdge = new Line();
                for (int i = 0; i < polyline.SegmentCount; i++)
                {
                    Line segment = polyline.SegmentAt(i);
                    if (segment.Length > maxLength)
                    {
                        maxLength = segment.Length;
                        longestEdge = segment;
                    }
                }

                if (maxLength > 0.001)
                {
                    Vector3d xAxis = longestEdge.Direction;
                    xAxis.Unitize();
                    Vector3d zAxis = Vector3d.ZAxis;
                    Vector3d yAxis = Vector3d.CrossProduct(zAxis, xAxis);
                    yAxis.Unitize();
                    plane = new Plane(longestEdge.From, xAxis, yAxis);
                }
            }

            return plane;
        }

        /// <summary>
        /// Offset a closed curve; negative distances offset inward for counter-clockwise curves.
        /// The input curve is reversed in place if it is clockwise.
        /// </summary>
        public static Curve[] OffsetCurve(Curve curve, double distance, Plane plane)
        {
            if (curve == null)
                return new Curve[0];

            // Ensure CCW orientation so the offset direction is predictable
            if (curve.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise)
                curve.Reverse();

            return curve.Offset(plane, distance, 0.001, CurveOffsetCornerStyle.Sharp);
        }

        /// <summary>
        /// Robustly intersects a footprint curve with a buildable area.
        /// Falls back to the footprint if Rhino's boolean intersection fails
        /// (for example due to coincident edges).
        /// </summary>
        public static Curve[] SafeBooleanIntersection(Curve curve, Curve buildable, double tolerance = 0.001)
        {
            if (curve == null || buildable == null)
                return new Curve[0];

            var result = Curve.CreateBooleanIntersection(curve, buildable, tolerance);
            if (result != null && result.Length > 0)
                return result;

            return new Curve[] { curve };
        }

        /// <summary>
        /// Extrude footprint curves vertically into closed building masses.
        /// Curves are turned into planar breps together first so that holes
        /// (courtyards) are preserved.
        /// </summary>
        public static List<Brep> ExtrudeFootprints(List<Curve> footprints, double height)
        {
            var masses = new List<Brep>();

            if (footprints == null || footprints.Count == 0)
                return masses;

            var planarBreps = Brep.CreatePlanarBreps(footprints, 0.001);

            if (planarBreps != null && planarBreps.Length > 0)
            {
                foreach (var planar in planarBreps)
                {
                    var face = planar.Faces[0];
                    var direction = new LineCurve(Point3d.Origin, new Point3d(0, 0, height));
                    var extruded = face.CreateExtrusion(direction.ToNurbsCurve(), true);
                    if (extruded != null)
                        masses.Add(extruded);
                }
            }
            else
            {
                // Fallback: extrude each curve individually
                foreach (var fp in footprints)
                {
                    var extrusion = Extrusion.Create(fp, height, true);
                    if (extrusion != null)
                        masses.Add(extrusion.ToBrep(false));
                }
            }

            return masses;
        }
    }
}
