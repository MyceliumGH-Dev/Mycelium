using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace Mycelium.Core
{
    /// <summary>
    /// Helper functions for placing buildings, streets, and trees onto terrain.
    /// </summary>
    public static class TerrainMassingHelpers
    {
        /// <summary>
        /// Samples the terrain elevation at a 2D point.
        /// </summary>
        public static double SampleElevation(Brep terrain, Point3d pt, double fallbackZ = 0.0)
        {
            if (terrain == null) return fallbackZ;

            var line = new Line(new Point3d(pt.X, pt.Y, -10000), new Point3d(pt.X, pt.Y, 10000));
            var lineCurve = new LineCurve(line);

            if (Intersection.CurveBrep(lineCurve, terrain, 0.001, out _, out var intersectionPoints))
            {
                if (intersectionPoints != null && intersectionPoints.Length > 0)
                    return intersectionPoints[0].Z;
            }

            // Fallback: closest point on terrain
            if (terrain.ClosestPoint(new Point3d(pt.X, pt.Y, 0), out Point3d closestPt, out _, out _, out _, 10000.0, out _))
            {
                return closestPt.Z;
            }

            return fallbackZ;
        }

        /// <summary>
        /// Calculates the average, minimum, and maximum terrain elevation under a building footprint.
        /// </summary>
        public static (double zPad, double zMin, double zMax) GetFootprintElevationStats(
            Brep terrain,
            Curve footprint,
            Curve parcelCurve = null)
        {
            if (terrain == null || footprint == null)
                return (0.0, 0.0, 0.0);

            var samplePoints = new List<Point3d>();

            // Sample perimeter points
            if (footprint.TryGetPolyline(out var polyline))
            {
                for (int i = 0; i < polyline.Count; i++)
                    samplePoints.Add(polyline[i]);
            }

            var divisionParams = footprint.DivideByCount(32, true);
            if (divisionParams != null)
            {
                foreach (var t in divisionParams)
                    samplePoints.Add(footprint.PointAt(t));
            }

            // Sample centroid
            var amp = AreaMassProperties.Compute(footprint);
            if (amp != null)
                samplePoints.Add(amp.Centroid);

            // Sample interior grid points
            var bbox = footprint.GetBoundingBox(true);
            if (bbox.IsValid)
            {
                int gridSteps = 5;
                double dx = (bbox.Max.X - bbox.Min.X) / gridSteps;
                double dy = (bbox.Max.Y - bbox.Min.Y) / gridSteps;

                for (int gx = 1; gx < gridSteps; gx++)
                {
                    for (int gy = 1; gy < gridSteps; gy++)
                    {
                        var gridPt = new Point3d(bbox.Min.X + gx * dx, bbox.Min.Y + gy * dy, 0);
                        if (footprint.Contains(gridPt, Plane.WorldXY, 0.001) == PointContainment.Inside)
                        {
                            samplePoints.Add(gridPt);
                        }
                    }
                }
            }

            // Sample parcel perimeter
            if (parcelCurve != null && parcelCurve.IsValid)
            {
                var parcelParams = parcelCurve.DivideByCount(16, true);
                if (parcelParams != null)
                {
                    foreach (var t in parcelParams)
                        samplePoints.Add(parcelCurve.PointAt(t));
                }
            }

            if (samplePoints.Count == 0)
                samplePoints.Add(bbox.Center);

            double zMin = double.MaxValue;
            double zMax = double.MinValue;
            double zSum = 0.0;

            foreach (var pt in samplePoints)
            {
                double z = SampleElevation(terrain, pt, 0.0);
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
                zSum += z;
            }

            double zPad = zSum / samplePoints.Count;
            return (zPad, zMin, zMax);
        }

        /// <summary>
        /// Extrudes building footprints into closed 3D masses on terrain.
        /// </summary>
        public static List<Brep> ExtrudeBuildingOnTerrain(
            List<Curve> footprints,
            Curve parcelCurve,
            double height,
            double floorHeight,
            Brep terrain,
            out double zPadOut)
        {
            var masses = new List<Brep>();
            zPadOut = 0.0;

            if (footprints == null || footprints.Count == 0)
                return masses;

            if (terrain == null)
            {
                // Flat extrusion at Z=0
                return GeometryHelpers.ExtrudeFootprints(footprints, height);
            }

            // Get elevation range across footprints
            double zMinBlock = double.MaxValue;
            double zMaxBlock = double.MinValue;
            double zPadSum = 0.0;

            foreach (var fp in footprints)
            {
                var (zPad, zMin, zMax) = GetFootprintElevationStats(terrain, fp, parcelCurve);
                if (zMin < zMinBlock) zMinBlock = zMin;
                if (zMax > zMaxBlock) zMaxBlock = zMax;
                zPadSum += zPad;
            }

            double zPadMean = zPadSum / footprints.Count;

            // Ensure the roof clears the highest ground point
            double minClearance = Math.Min(floorHeight, 2.5);
            double minPadForClearance = zMaxBlock + minClearance - height;

            // Use the higher of average elevation or clearance elevation
            double zPadBlock = Math.Max(zPadMean, minPadForClearance);
            zPadOut = zPadBlock;

            // Extend foundation base below lowest ground point
            double zPlinthBase = zMinBlock - 0.5;
            double totalExtrusionHeight = (zPadBlock + height) - zPlinthBase;

            // Create planar faces, move to base elevation, and extrude
            var planarBreps = Brep.CreatePlanarBreps(footprints, 0.001);

            if (planarBreps != null && planarBreps.Length > 0)
            {
                foreach (var planar in planarBreps)
                {
                    var movedPlanar = planar.DuplicateBrep();
                    movedPlanar.Translate(new Vector3d(0, 0, zPlinthBase));

                    var face = movedPlanar.Faces[0];
                    var direction = new LineCurve(new Point3d(0, 0, zPlinthBase), new Point3d(0, 0, zPlinthBase + totalExtrusionHeight));
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
                    var movedFp = fp.DuplicateCurve();
                    movedFp.Translate(new Vector3d(0, 0, zPlinthBase));

                    var extrusion = Extrusion.Create(movedFp, totalExtrusionHeight, true);
                    if (extrusion != null)
                        masses.Add(extrusion.ToBrep(false));
                }
            }

            return masses;
        }

        /// <summary>
        /// Projects 2D curves (streets, park boundaries, parcel lines) onto the terrain surface.
        /// </summary>
        public static List<Curve> ProjectCurvesToTerrain(IEnumerable<Curve> curves, Brep terrain)
        {
            var projected = new List<Curve>();
            if (curves == null) return projected;

            if (terrain == null)
            {
                projected.AddRange(curves);
                return projected;
            }

            double tolerance = 0.001;

            foreach (var curve in curves)
            {
                if (curve == null || !curve.IsValid) continue;

                var proj = Curve.ProjectToBrep(curve, terrain, Vector3d.ZAxis, tolerance);
                if (proj == null || proj.Length == 0)
                    proj = Curve.ProjectToBrep(curve, terrain, -Vector3d.ZAxis, tolerance);

                if (proj != null && proj.Length > 0)
                {
                    projected.AddRange(proj);
                }
                else
                {
                    // Fallback: sample points along the curve
                    projected.Add(ProjectCurveBySampling(curve, terrain));
                }
            }

            return projected;
        }

        private static Curve ProjectCurveBySampling(Curve curve, Brep terrain)
        {
            if (curve.TryGetPolyline(out var polyline))
            {
                var pts3d = new List<Point3d>();
                for (int i = 0; i < polyline.Count; i++)
                {
                    var pt = polyline[i];
                    double z = SampleElevation(terrain, pt, 0.0);
                    pts3d.Add(new Point3d(pt.X, pt.Y, z));
                }
                return new PolylineCurve(pts3d);
            }

            var divisionParams = curve.DivideByCount(32, true);
            if (divisionParams != null)
            {
                var pts3d = new List<Point3d>();
                foreach (var t in divisionParams)
                {
                    var pt = curve.PointAt(t);
                    double z = SampleElevation(terrain, pt, 0.0);
                    pts3d.Add(new Point3d(pt.X, pt.Y, z));
                }
                var crv = Curve.CreateInterpolatedCurve(pts3d, 3);
                if (crv != null) return crv;
            }

            return curve;
        }

        /// <summary>
        /// Positions tree spheres so they sit on the terrain surface.
        /// </summary>
        public static List<Brep> AnchorTreesToTerrain(IEnumerable<Brep> trees, Brep terrain)
        {
            var anchored = new List<Brep>();
            if (trees == null) return anchored;

            if (terrain == null)
            {
                anchored.AddRange(trees);
                return anchored;
            }

            foreach (var tree in trees)
            {
                if (tree == null) continue;

                var bbox = tree.GetBoundingBox(true);
                var center2D = new Point3d(bbox.Center.X, bbox.Center.Y, 0);
                double zTerrain = SampleElevation(terrain, center2D, 0.0);

                var movedTree = tree.DuplicateBrep();
                movedTree.Translate(new Vector3d(0, 0, zTerrain));
                anchored.Add(movedTree);
            }

            return anchored;
        }
    }
}
