using System;
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
        /// Counts guarded boolean fallbacks taken on the calling thread since the last
        /// <see cref="ResetBooleanFallbackCount"/>. A fallback means Rhino's boolean
        /// intersection failed and an untrimmed input curve was substituted, so the result
        /// may violate the requested setback or buildable area. Recording the count turns a
        /// silent geometric compromise into an auditable per-case quality signal.
        /// </summary>
        [ThreadStatic]
        private static int _booleanFallbackCount;

        public static int BooleanFallbackCount => _booleanFallbackCount;

        public static void ResetBooleanFallbackCount()
        {
            _booleanFallbackCount = 0;
        }

        /// <summary>
        /// Get the area of a closed curve; returns 0 for open or invalid curves.
        /// </summary>
        /// <remarks>
        /// This is a single-loop area. It does NOT subtract inner loops, so summing it over a
        /// multi-loop region (for example a perimeter block returned as an outer curve plus a
        /// courtyard curve) double-counts the void. Use <see cref="GetRegionArea"/> for any
        /// area that must be topologically correct.
        /// </remarks>
        public static double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed)
                return 0.0;

            var amp = AreaMassProperties.Compute(curve);
            return amp != null ? amp.Area : 0.0;
        }

        /// <summary>
        /// Area of the planar region bounded by a set of curves, with inner loops subtracted.
        /// Curves are resolved into planar Breps together so that a perimeter block returned as
        /// an outer boundary plus a courtyard loop yields the built area only.
        /// Falls back to a plain per-curve sum when planar Brep construction fails.
        /// </summary>
        public static double GetRegionArea(IReadOnlyList<Curve> curves)
        {
            if (curves == null || curves.Count == 0)
                return 0.0;

            var planarRegions = Brep.CreatePlanarBreps(curves, 0.001);
            if (planarRegions == null || planarRegions.Length == 0)
            {
                double fallbackSum = 0.0;
                foreach (var curve in curves)
                    fallbackSum += GetCurveArea(curve);
                return fallbackSum;
            }

            double sum = 0.0;
            foreach (var region in planarRegions)
            {
                using (var properties = AreaMassProperties.Compute(region))
                {
                    if (properties != null)
                        sum += properties.Area;
                }
            }
            return sum;
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
        /// <remarks>
        /// The fallback returns the UNTRIMMED input, which may extend past the buildable area
        /// and violate the requested setback. Each fallback increments
        /// <see cref="BooleanFallbackCount"/> so a case can be quarantined rather than silently
        /// accepted.
        /// </remarks>
        public static Curve[] SafeBooleanIntersection(Curve curve, Curve buildable, double tolerance = 0.001)
        {
            if (curve == null || buildable == null)
                return new Curve[0];

            var result = Curve.CreateBooleanIntersection(curve, buildable, tolerance);
            if (result != null && result.Length > 0)
                return result;

            _booleanFallbackCount++;
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
