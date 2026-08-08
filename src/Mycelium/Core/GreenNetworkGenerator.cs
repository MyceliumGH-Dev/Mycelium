using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    public sealed class GreenNetworkResult
    {
        public List<Curve> Belt { get; } = new List<Curve>();
        public List<Curve> Corridors { get; } = new List<Curve>();
        public List<Curve> Refuges { get; } = new List<Curve>();
        public List<Curve> AllRegions { get; } = new List<Curve>();
        public List<Brep> Trees { get; } = new List<Brep>();
    }

    /// <summary>Creates deterministic perimeter belts, corridors, and refuge patches.</summary>
    public static class GreenNetworkGenerator
    {
        public static GreenNetworkResult Generate(Curve boundary, IEnumerable<Curve> guides,
            IEnumerable<Curve> obstacles, double beltWidth, double corridorWidth,
            int refugeCount, double refugeRadius, double treeDensity, int seed,
            double tolerance = 0.001)
        {
            if (boundary == null || !boundary.IsValid || !boundary.IsClosed)
                throw new ArgumentException("Boundary must be a valid closed planar curve.", nameof(boundary));

            var result = new GreenNetworkResult();
            Plane plane;
            if (!boundary.TryGetPlane(out plane))
                throw new ArgumentException("Boundary must be planar.", nameof(boundary));

            var cleanObstacles = ValidClosed(obstacles);
            if (beltWidth > tolerance)
            {
                Curve outer = boundary.DuplicateCurve();
                if (outer.ClosedCurveOrientation(plane) == CurveOrientation.Clockwise) outer.Reverse();
                Curve[] inner = outer.Offset(plane, -beltWidth, tolerance, CurveOffsetCornerStyle.Round);
                if (inner != null && inner.Length > 0)
                {
                    Curve[] belt = Curve.CreateBooleanDifference(outer, inner[0], tolerance);
                    AddClipped(result.Belt, belt, boundary, cleanObstacles, tolerance);
                }
            }

            var refugeCenters = SeededPoints(boundary, plane, Math.Max(0, refugeCount), seed, tolerance);
            foreach (Point3d center in refugeCenters)
            {
                var circle = new Circle(plane, center, Math.Max(refugeRadius, tolerance)).ToNurbsCurve();
                AddClipped(result.Refuges, new[] { circle }, boundary, cleanObstacles, tolerance);
            }

            var corridorGuides = new List<Curve>();
            if (guides != null)
                foreach (Curve guide in guides)
                    if (guide != null && guide.IsValid) corridorGuides.Add(guide);

            if (corridorGuides.Count == 0 && refugeCenters.Count > 0)
            {
                Point3d hub = AreaMassProperties.Compute(boundary)?.Centroid ?? refugeCenters[0];
                foreach (Point3d center in refugeCenters) corridorGuides.Add(new LineCurve(hub, center));
            }

            if (corridorWidth > tolerance)
                foreach (Curve guide in corridorGuides)
                    AddClipped(result.Corridors, Buffer(guide, plane, corridorWidth, tolerance),
                        boundary, cleanObstacles, tolerance);

            result.AllRegions.AddRange(result.Belt);
            result.AllRegions.AddRange(result.Corridors);
            result.AllRegions.AddRange(result.Refuges);

            if (treeDensity > 0)
            {
                var rng = new Random(seed);
                foreach (Curve region in result.AllRegions)
                    result.Trees.AddRange(TreeGenerator.GenerateTrees(region, rng, treeDensity));
            }
            return result;
        }

        private static Curve[] Buffer(Curve guide, Plane plane, double width, double tolerance)
        {
            double half = width * 0.5;
            Curve[] left = guide.Offset(plane, half, tolerance, CurveOffsetCornerStyle.Round);
            Curve[] right = guide.Offset(plane, -half, tolerance, CurveOffsetCornerStyle.Round);
            if (left == null || right == null || left.Length == 0 || right.Length == 0) return new Curve[0];
            if (guide.IsClosed)
            {
                Curve[] ring = Curve.CreateBooleanDifference(left[0], right[0], tolerance);
                return ring ?? new Curve[0];
            }
            var pieces = new Curve[] { left[0], new LineCurve(left[0].PointAtEnd, right[0].PointAtEnd),
                right[0], new LineCurve(right[0].PointAtStart, left[0].PointAtStart) };
            Curve[] joined = Curve.JoinCurves(pieces, tolerance);
            return joined ?? new Curve[0];
        }

        private static List<Point3d> SeededPoints(Curve boundary, Plane plane, int count, int seed, double tolerance)
        {
            var points = new List<Point3d>();
            var local = boundary.DuplicateCurve();
            Transform toLocal = Transform.PlaneToPlane(plane, Plane.WorldXY);
            local.Transform(toLocal);
            BoundingBox box = local.GetBoundingBox(true);
            var rng = new Random(seed);
            Transform fromLocal = Transform.PlaneToPlane(Plane.WorldXY, plane);
            for (int attempt = 0; attempt < Math.Max(100, count * 100) && points.Count < count; attempt++)
            {
                var p = new Point3d(box.Min.X + rng.NextDouble() * (box.Max.X - box.Min.X),
                    box.Min.Y + rng.NextDouble() * (box.Max.Y - box.Min.Y), 0);
                if (local.Contains(p, Plane.WorldXY, tolerance) != PointContainment.Inside) continue;
                p.Transform(fromLocal);
                points.Add(p);
            }
            return points;
        }

        private static List<Curve> ValidClosed(IEnumerable<Curve> curves)
        {
            var result = new List<Curve>();
            if (curves != null) foreach (Curve curve in curves)
                if (curve != null && curve.IsValid && curve.IsClosed) result.Add(curve);
            return result;
        }

        private static void AddClipped(List<Curve> target, IEnumerable<Curve> candidates,
            Curve boundary, List<Curve> obstacles, double tolerance)
        {
            if (candidates == null) return;
            foreach (Curve candidate in candidates)
            {
                if (candidate == null || !candidate.IsClosed) continue;
                Curve[] clipped = Curve.CreateBooleanIntersection(candidate, boundary, tolerance);
                if (clipped == null) continue;
                var current = new List<Curve>(clipped);
                foreach (Curve obstacle in obstacles)
                {
                    var next = new List<Curve>();
                    foreach (Curve curve in current)
                    {
                        Curve[] difference = Curve.CreateBooleanDifference(curve, obstacle, tolerance);
                        if (difference != null) next.AddRange(difference);
                    }
                    current = next;
                }
                target.AddRange(current);
            }
        }
    }
}
