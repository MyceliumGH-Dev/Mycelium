using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace FormFlux.Core
{
    /// <summary>
    /// Helper functions for geometric operations
    /// </summary>
    public static class GeometryHelpers
    {
        /// <summary>
        /// Get the area of a closed curve
        /// </summary>
        public static double GetCurveArea(Curve curve)
        {
            if (curve == null || !curve.IsClosed)
                return 0.0;

            var amp = AreaMassProperties.Compute(curve);
            return amp != null ? amp.Area : 0.0;
        }

        /// <summary>
        /// Offset a curve inward or outward
        /// </summary>
        public static Curve[] OffsetCurve(Curve curve, double distance, Plane plane)
        {
            if (curve == null)
                return new Curve[0];

            // Ensure CCW orientation
            if (curve.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise)
                curve.Reverse();

            return curve.Offset(plane, distance, 0.001, CurveOffsetCornerStyle.Sharp);
        }

        /// <summary>
        /// Create boolean difference between curves
        /// </summary>
        public static Curve[] BooleanDifference(Curve curveA, Curve[] curvesB)
        {
            return Curve.CreateBooleanDifference(curveA, curvesB, 0.001);
        }

        /// <summary>
        /// Create boolean intersection between curves
        /// </summary>
        public static Curve[] BooleanIntersection(Curve curveA, Curve curveB)
        {
            return Curve.CreateBooleanIntersection(curveA, curveB, 0.001);
        }

        /// <summary>
        /// Create boolean union of curves
        /// </summary>
        public static Curve[] BooleanUnion(List<Curve> curves)
        {
            if (curves == null || curves.Count == 0)
                return new Curve[0];

            return Curve.CreateBooleanUnion(curves, 0.001);
        }

        /// <summary>
        /// Create planar breps from curves (handles holes)
        /// </summary>
        public static Brep[] CreatePlanarBreps(List<Curve> curves)
        {
            return Brep.CreatePlanarBreps(curves, 0.001);
        }

        /// <summary>
        /// Extrude curves vertically to create building masses
        /// </summary>
        public static Brep ExtrudeCurveVertically(Curve curve, double height)
        {
            if (curve == null || !curve.IsClosed)
                return null;

            // Create planar breps (handles courtyards with holes)
            var planars = Brep.CreatePlanarBreps(curve, 0.001);
            if (planars == null || planars.Length == 0)
            {
                // Fallback: simple extrusion
                var extrusion = Extrusion.Create(curve, height, true);
                return extrusion?.ToBrep(false);
            }

            // Extrude the planar surface
            var baseSurface = planars[0].Faces[0];
            var direction = new LineCurve(Point3d.Origin, new Point3d(0, 0, height));
            return baseSurface.CreateExtrusion(direction.ToNurbsCurve(), true);
        }

        /// <summary>
        /// Extrude multiple curves and create masses
        /// </summary>
        public static List<Brep> ExtrudeFootprints(List<Curve> footprints, double height)
        {
            var masses = new List<Brep>();

            if (footprints == null || footprints.Count == 0)
                return masses;

            // Orient curves
            foreach (var fp in footprints)
            {
                if (fp.ClosedCurveOrientation(Plane.WorldXY) == CurveOrientation.Clockwise)
                    fp.Reverse();
            }

            // Create planar breps from all curves together (handles holes)
            var planarBreps = Brep.CreatePlanarBreps(footprints, 0.001);

            if (planarBreps != null && planarBreps.Length > 0)
            {
                // Extrude each planar surface
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
