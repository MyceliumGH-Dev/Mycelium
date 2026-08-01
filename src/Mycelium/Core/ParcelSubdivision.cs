using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    public enum StreetNetworkType
    {
        Rectilinear,
        Checkerboard,
        Hybrid,
        Radial
    }

    /// <summary>
    /// Creates buildable parcels whose gaps form a selected street-network pattern.
    /// </summary>
    public static class ParcelSubdivision
    {
        private const double Tolerance = 0.001;

        /// <summary>
        /// Backwards-compatible overload. Existing callers retain the original irregular,
        /// rectilinear binary subdivision.
        /// </summary>
        public static List<Curve> Subdivide(Curve boundary, int divisions, double minArea,
            double streetWidth, Random rng)
        {
            return Subdivide(boundary, divisions, minArea, streetWidth, rng,
                StreetNetworkType.Rectilinear);
        }

        public static List<Curve> Subdivide(Curve boundary, int divisions, double minArea,
            double streetWidth, Random rng, StreetNetworkType networkType)
        {
            var result = new List<Curve>();
            if (boundary == null || !boundary.IsClosed)
                return result;

            divisions = Math.Max(0, divisions);
            streetWidth = Math.Max(0.0, streetWidth);
            if (divisions == 0)
            {
                result.Add(boundary);
                return result;
            }

            switch (networkType)
            {
                case StreetNetworkType.Checkerboard:
                    return SubdivideCheckerboard(boundary, divisions, minArea, streetWidth);
                case StreetNetworkType.Hybrid:
                    return SubdivideHybrid(boundary, divisions, minArea, streetWidth, rng);
                case StreetNetworkType.Radial:
                    return SubdivideRadial(boundary, divisions, minArea, streetWidth);
                case StreetNetworkType.Rectilinear:
                default:
                    return SubdivideRectilinear(boundary, divisions, minArea, streetWidth, rng);
            }
        }

        private static List<Curve> SubdivideRectilinear(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng)
        {
            var result = new List<Curve>();
            if (divisions <= 0 || GeometryHelpers.GetCurveArea(boundary) < minArea)
            {
                result.Add(boundary);
                return result;
            }

            GetLocalBoundary(boundary, out var plane, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            bool horizontal = width < height;
            double splitRatio = rng.NextDouble() * 0.4 + 0.3;
            double split = horizontal
                ? bbox.Min.Y + height * splitRatio
                : bbox.Min.X + width * splitRatio;

            Curve cutter = CreateAxisStrip(bbox, horizontal, split, streetWidth);
            cutter.Transform(toWorld);
            var pieces = Curve.CreateBooleanDifference(boundary, cutter, Tolerance);
            if (pieces == null || pieces.Length < 2)
            {
                result.Add(boundary);
                return result;
            }

            foreach (var piece in pieces)
                result.AddRange(SubdivideRectilinear(piece, divisions - 1, minArea, streetWidth, rng));
            return result;
        }

        private static List<Curve> SubdivideCheckerboard(Curve boundary, int divisions,
            double minArea, double streetWidth)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double target = Math.Pow(2.0, Math.Min(divisions, 16));
            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(target * width / Math.Max(height, Tolerance))));
            int rows = Math.Max(1, (int)Math.Ceiling(target / columns));

            while (columns * rows > 1 && GeometryHelpers.GetCurveArea(boundary) / (columns * rows) < minArea)
            {
                if (columns >= rows && columns > 1) columns--;
                else if (rows > 1) rows--;
                else break;
            }

            double cellWidth = width / columns;
            double cellHeight = height / rows;
            double insetX = Math.Min(streetWidth / 2.0, cellWidth * 0.45);
            double insetY = Math.Min(streetWidth / 2.0, cellHeight * 0.45);
            var result = new List<Curve>();

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                double x0 = bbox.Min.X + column * cellWidth + (column == 0 ? 0 : insetX);
                double x1 = bbox.Min.X + (column + 1) * cellWidth - (column == columns - 1 ? 0 : insetX);
                double y0 = bbox.Min.Y + row * cellHeight + (row == 0 ? 0 : insetY);
                double y1 = bbox.Min.Y + (row + 1) * cellHeight - (row == rows - 1 ? 0 : insetY);
                var cell = RectangleCurve(x0, y0, x1, y1);
                cell.Transform(toWorld);
                AddIntersections(boundary, cell, result);
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static List<Curve> SubdivideHybrid(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            var center = bbox.Center;
            double angle = (rng.NextDouble() < 0.5 ? -1.0 : 1.0) * Math.PI / 5.0;
            var direction = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            double length = Math.Sqrt(width * width + height * height) * 1.5;
            var cutter = CreateOrientedStrip(center, direction, length, streetWidth);
            cutter.Transform(toWorld);

            var pieces = Curve.CreateBooleanDifference(boundary, cutter, Tolerance);
            if (pieces == null || pieces.Length < 2)
                return SubdivideRectilinear(boundary, divisions, minArea, streetWidth, rng);

            var result = new List<Curve>();
            foreach (var piece in pieces)
                result.AddRange(SubdivideRectilinear(piece, divisions - 1, minArea, streetWidth, rng));
            return result;
        }

        private static List<Curve> SubdivideRadial(Curve boundary, int divisions,
            double minArea, double streetWidth)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            var center = bbox.Center;
            double maxRadius = 0.0;
            foreach (var corner in bbox.GetCorners())
                maxRadius = Math.Max(maxRadius, corner.DistanceTo(center));
            maxRadius += streetWidth;

            int target = Math.Max(4, (int)Math.Ceiling(Math.Pow(2.0, Math.Min(divisions, 14))));
            int rings = Math.Max(1, (int)Math.Round(Math.Sqrt(target / 6.0)));
            int sectors = Math.Max(4, (int)Math.Ceiling((double)target / rings));
            while (rings * sectors > 4 && GeometryHelpers.GetCurveArea(boundary) / (rings * sectors) < minArea)
            {
                if (sectors > 4) sectors--;
                else if (rings > 1) rings--;
                else break;
            }

            double ringDepth = maxRadius / rings;
            var result = new List<Curve>();
            for (int ring = 0; ring < rings; ring++)
            {
                double inner = ring == 0 ? 0.0 : ring * ringDepth + streetWidth / 2.0;
                double outer = (ring + 1) * ringDepth - (ring == rings - 1 ? 0.0 : streetWidth / 2.0);
                if (outer <= inner + Tolerance) continue;

                double midRadius = Math.Max((inner + outer) / 2.0, streetWidth);
                double angularGap = Math.Min(Math.PI / sectors * 0.8, streetWidth / (2.0 * midRadius));
                for (int sector = 0; sector < sectors; sector++)
                {
                    double a0 = sector * Math.PI * 2.0 / sectors + angularGap;
                    double a1 = (sector + 1) * Math.PI * 2.0 / sectors - angularGap;
                    var cell = AnnularSector(center, inner, outer, a0, a1);
                    cell.Transform(toWorld);
                    AddIntersections(boundary, cell, result);
                }
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static void GetLocalBoundary(Curve boundary, out Plane plane, out Curve local,
            out Transform toWorld)
        {
            plane = Plane.WorldXY;
            if (!boundary.TryGetPlane(out plane)) plane = Plane.WorldXY;
            var toLocal = Transform.PlaneToPlane(plane, Plane.WorldXY);
            toWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);
            local = boundary.DuplicateCurve();
            local.Transform(toLocal);
        }

        private static Curve CreateAxisStrip(BoundingBox bbox, bool horizontal, double split, double width)
        {
            double margin = Math.Max(bbox.Diagonal.Length, width) + 1.0;
            return horizontal
                ? RectangleCurve(bbox.Min.X - margin, split - width / 2.0,
                    bbox.Max.X + margin, split + width / 2.0)
                : RectangleCurve(split - width / 2.0, bbox.Min.Y - margin,
                    split + width / 2.0, bbox.Max.Y + margin);
        }

        private static Curve CreateOrientedStrip(Point3d center, Vector2d direction,
            double length, double width)
        {
            var along = new Vector3d(direction.X, direction.Y, 0) * length;
            var across = new Vector3d(-direction.Y, direction.X, 0) * (width / 2.0);
            var points = new[]
            {
                center - along - across, center + along - across,
                center + along + across, center - along + across,
                center - along - across
            };
            return new Polyline(points).ToNurbsCurve();
        }

        private static Curve RectangleCurve(double x0, double y0, double x1, double y1)
        {
            return new Rectangle3d(Plane.WorldXY, new Point3d(x0, y0, 0),
                new Point3d(x1, y1, 0)).ToNurbsCurve();
        }

        private static Curve AnnularSector(Point3d center, double inner, double outer,
            double start, double end)
        {
            const int samples = 8;
            var points = new List<Point3d>();
            if (inner <= Tolerance)
                points.Add(center);
            else
                for (int i = 0; i <= samples; i++)
                    points.Add(Polar(center, inner, start + (end - start) * i / samples));

            for (int i = samples; i >= 0; i--)
                points.Add(Polar(center, outer, start + (end - start) * i / samples));
            points.Add(points[0]);
            return new Polyline(points).ToNurbsCurve();
        }

        private static Point3d Polar(Point3d center, double radius, double angle)
        {
            return new Point3d(center.X + radius * Math.Cos(angle),
                center.Y + radius * Math.Sin(angle), 0);
        }

        private static void AddIntersections(Curve boundary, Curve cell, List<Curve> result)
        {
            var intersections = Curve.CreateBooleanIntersection(boundary, cell, Tolerance);
            if (intersections == null) return;
            foreach (var intersection in intersections)
                if (intersection != null && intersection.IsClosed && GeometryHelpers.GetCurveArea(intersection) > Tolerance)
                    result.Add(intersection);
        }
    }
}
