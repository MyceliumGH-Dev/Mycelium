using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Mycelium.Core
{
    public enum StreetNetworkType
    {
        IrregularGrid = 0,
        OrthogonalGrid = 1,
        DiagonalGrid = 2,
        RadialConcentricGrid = 3
    }

    public enum OrthogonalGridType
    {
        Regular = 0,
        Rectangular = 1,
        Cerda = 2,
        HierarchicalSuperblock = 3
    }

    public enum IrregularGridType
    {
        RecursiveOrthogonal = 0,
        DeformedGrid = 1,
        StaggeredGrid = 2
    }

    public enum DiagonalGridType
    {
        SingleAxis = 0,
        CrossAxes = 1,
        OrthogonalOverlay = 2
    }

    public enum RadialConcentricGridType
    {
        CivicCore = 0,
        PolygonalRadial = 1,
        FanPlan = 2
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
                StreetNetworkType.IrregularGrid);
        }

        public static List<Curve> Subdivide(Curve boundary, int divisions, double minArea,
            double streetWidth, Random rng, StreetNetworkType networkType,
            OrthogonalGridType orthogonalGridType = OrthogonalGridType.Regular,
            IrregularGridType irregularGridType = IrregularGridType.RecursiveOrthogonal,
            DiagonalGridType diagonalGridType = DiagonalGridType.SingleAxis,
            RadialConcentricGridType radialGridType = RadialConcentricGridType.CivicCore)
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
                case StreetNetworkType.OrthogonalGrid:
                    return SubdivideOrthogonalGrid(boundary, divisions, minArea, streetWidth,
                        orthogonalGridType);
                case StreetNetworkType.DiagonalGrid:
                    return SubdivideDiagonalGrid(boundary, divisions, minArea, streetWidth,
                        rng, diagonalGridType);
                case StreetNetworkType.RadialConcentricGrid:
                    return SubdivideRadialConcentricGrid(boundary, divisions, minArea,
                        streetWidth, radialGridType);
                case StreetNetworkType.IrregularGrid:
                default:
                    return SubdivideIrregularGrid(boundary, divisions, minArea, streetWidth,
                        rng, irregularGridType);
            }
        }

        private static List<Curve> SubdivideIrregularGrid(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng, IrregularGridType gridType)
        {
            switch (gridType)
            {
                case IrregularGridType.DeformedGrid:
                    return SubdivideDeformedGrid(boundary, divisions, minArea, streetWidth, rng);
                case IrregularGridType.StaggeredGrid:
                    return SubdivideStaggeredGrid(boundary, divisions, minArea, streetWidth);
                case IrregularGridType.RecursiveOrthogonal:
                default:
                    return SubdivideRecursiveOrthogonal(boundary, divisions, minArea, streetWidth, rng);
            }
        }

        private static List<Curve> SubdivideRecursiveOrthogonal(Curve boundary, int divisions,
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
                result.AddRange(SubdivideRecursiveOrthogonal(piece, divisions - 1, minArea, streetWidth, rng));
            return result;
        }

        private static List<Curve> SubdivideDeformedGrid(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            ComputeGridDimensions(boundary, divisions, minArea, bbox, out int columns, out int rows);
            double cellWidth = (bbox.Max.X - bbox.Min.X) / columns;
            double cellHeight = (bbox.Max.Y - bbox.Min.Y) / rows;
            var nodes = new Point3d[columns + 1, rows + 1];

            for (int row = 0; row <= rows; row++)
            for (int column = 0; column <= columns; column++)
            {
                double x = bbox.Min.X + column * cellWidth;
                double y = bbox.Min.Y + row * cellHeight;
                if (column > 0 && column < columns)
                    x += (rng.NextDouble() - 0.5) * cellWidth * 0.35;
                if (row > 0 && row < rows)
                    y += (rng.NextDouble() - 0.5) * cellHeight * 0.35;
                nodes[column, row] = new Point3d(x, y, 0);
            }

            var result = new List<Curve>();
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                var polygon = new Polyline(new[]
                {
                    nodes[column, row], nodes[column + 1, row],
                    nodes[column + 1, row + 1], nodes[column, row + 1],
                    nodes[column, row]
                }).ToNurbsCurve();
                var offsets = polygon.Offset(Plane.WorldXY, -streetWidth / 2.0,
                    Tolerance, CurveOffsetCornerStyle.Sharp);
                if (offsets == null) continue;
                foreach (var offset in offsets)
                {
                    offset.Transform(toWorld);
                    AddIntersections(boundary, offset, result);
                }
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static List<Curve> SubdivideStaggeredGrid(Curve boundary, int divisions,
            double minArea, double streetWidth)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            ComputeGridDimensions(boundary, divisions, minArea, bbox, out int columns, out int rows);
            double cellWidth = (bbox.Max.X - bbox.Min.X) / columns;
            double cellHeight = (bbox.Max.Y - bbox.Min.Y) / rows;
            double insetX = Math.Min(streetWidth / 2.0, cellWidth * 0.40);
            double insetY = Math.Min(streetWidth / 2.0, cellHeight * 0.40);
            var result = new List<Curve>();

            for (int row = 0; row < rows; row++)
            {
                double shift = row % 2 == 0 ? 0.0 : cellWidth / 2.0;
                for (int column = -1; column <= columns; column++)
                {
                    double x0 = bbox.Min.X + column * cellWidth + shift + insetX;
                    double x1 = bbox.Min.X + (column + 1) * cellWidth + shift - insetX;
                    double y0 = bbox.Min.Y + row * cellHeight + insetY;
                    double y1 = bbox.Min.Y + (row + 1) * cellHeight - insetY;
                    if (x1 <= bbox.Min.X || x0 >= bbox.Max.X) continue;
                    var cell = RectangleCurve(x0, y0, x1, y1);
                    cell.Transform(toWorld);
                    AddIntersections(boundary, cell, result);
                }
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static void ComputeGridDimensions(Curve boundary, int divisions, double minArea,
            BoundingBox bbox, out int columns, out int rows)
        {
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double target = Math.Pow(2.0, Math.Min(divisions, 16));
            columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(
                target * width / Math.Max(height, Tolerance))));
            rows = Math.Max(1, (int)Math.Ceiling(target / columns));
            while (columns * rows > 1 && GeometryHelpers.GetCurveArea(boundary) / (columns * rows) < minArea)
            {
                if (columns >= rows && columns > 1) columns--;
                else if (rows > 1) rows--;
                else break;
            }
        }

        private static List<Curve> SubdivideOrthogonalGrid(Curve boundary, int divisions,
            double minArea, double streetWidth, OrthogonalGridType gridType)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double target = Math.Pow(2.0, Math.Min(divisions, 16));
            double desiredCellAspect = 1.0;
            if (gridType == OrthogonalGridType.Rectangular)
                desiredCellAspect = width >= height ? 1.8 : 1.0 / 1.8;

            if (gridType == OrthogonalGridType.HierarchicalSuperblock)
                target = Math.Max(target, 9.0);

            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(
                target * width / (Math.Max(height, Tolerance) * desiredCellAspect))));
            int rows = Math.Max(1, (int)Math.Ceiling(target / columns));

            if (gridType == OrthogonalGridType.HierarchicalSuperblock)
            {
                columns = Math.Max(3, (int)Math.Ceiling(columns / 3.0) * 3);
                rows = Math.Max(3, (int)Math.Ceiling(rows / 3.0) * 3);
                while (columns * rows > 9 && GeometryHelpers.GetCurveArea(boundary) / (columns * rows) < minArea)
                {
                    if (columns >= rows && columns > 3) columns -= 3;
                    else if (rows > 3) rows -= 3;
                    else break;
                }
            }
            else
            {
                while (columns * rows > 1 && GeometryHelpers.GetCurveArea(boundary) / (columns * rows) < minArea)
                {
                    if (columns >= rows && columns > 1) columns--;
                    else if (rows > 1) rows--;
                    else break;
                }
            }

            double cellWidth = width / columns;
            double cellHeight = height / rows;
            var result = new List<Curve>();

            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                double boundaryInsetX = gridType == OrthogonalGridType.HierarchicalSuperblock
                    ? Math.Min(streetWidth, cellWidth * 0.45) : 0.0;
                double boundaryInsetY = gridType == OrthogonalGridType.HierarchicalSuperblock
                    ? Math.Min(streetWidth, cellHeight * 0.45) : 0.0;
                double leftInset = column == 0 ? boundaryInsetX : GridLineInset(column, streetWidth, cellWidth, gridType);
                double rightInset = column == columns - 1 ? boundaryInsetX : GridLineInset(column + 1, streetWidth, cellWidth, gridType);
                double bottomInset = row == 0 ? boundaryInsetY : GridLineInset(row, streetWidth, cellHeight, gridType);
                double topInset = row == rows - 1 ? boundaryInsetY : GridLineInset(row + 1, streetWidth, cellHeight, gridType);
                double x0 = bbox.Min.X + column * cellWidth + leftInset;
                double x1 = bbox.Min.X + (column + 1) * cellWidth - rightInset;
                double y0 = bbox.Min.Y + row * cellHeight + bottomInset;
                double y1 = bbox.Min.Y + (row + 1) * cellHeight - topInset;

                Curve cell;
                if (gridType == OrthogonalGridType.Cerda)
                {
                    double minDimension = Math.Min(x1 - x0, y1 - y0);
                    double chamfer = Math.Min(minDimension * 0.18,
                        Math.Max(streetWidth * 2.0, minDimension * 0.10));
                    cell = ChamferedRectangleCurve(x0, y0, x1, y1, chamfer);
                }
                else
                {
                    cell = RectangleCurve(x0, y0, x1, y1);
                }
                cell.Transform(toWorld);
                AddIntersections(boundary, cell, result);
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static double GridLineInset(int lineIndex, double streetWidth,
            double cellDimension, OrthogonalGridType gridType)
        {
            double width = streetWidth;
            if (gridType == OrthogonalGridType.HierarchicalSuperblock && lineIndex % 3 == 0)
                width *= 2.0;
            return Math.Min(width / 2.0, cellDimension * 0.45);
        }

        private static List<Curve> SubdivideDiagonalGrid(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng, DiagonalGridType gridType)
        {
            switch (gridType)
            {
                case DiagonalGridType.CrossAxes:
                    return SubdivideCrossAxes(boundary, divisions, minArea, streetWidth, rng);
                case DiagonalGridType.OrthogonalOverlay:
                    return SubdivideOrthogonalOverlay(boundary, divisions, minArea, streetWidth, rng);
                case DiagonalGridType.SingleAxis:
                default:
                    return SubdivideSingleDiagonalAxis(boundary, divisions, minArea, streetWidth, rng);
            }
        }

        private static List<Curve> SubdivideSingleDiagonalAxis(Curve boundary, int divisions,
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
                return SubdivideRecursiveOrthogonal(boundary, divisions, minArea, streetWidth, rng);

            var result = new List<Curve>();
            foreach (var piece in pieces)
                result.AddRange(SubdivideRecursiveOrthogonal(piece, divisions - 1, minArea, streetWidth, rng));
            return result;
        }

        private static List<Curve> SubdivideCrossAxes(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double length = Math.Sqrt(width * width + height * height) * 1.5;
            double angle = Math.PI / 5.0;
            var cutters = new[]
            {
                CreateOrientedStrip(bbox.Center,
                    new Vector2d(Math.Cos(angle), Math.Sin(angle)), length, streetWidth),
                CreateOrientedStrip(bbox.Center,
                    new Vector2d(Math.Cos(-angle), Math.Sin(-angle)), length, streetWidth)
            };
            foreach (var cutter in cutters) cutter.Transform(toWorld);

            var pieces = new List<Curve> { boundary };
            foreach (var cutter in cutters)
            {
                var next = new List<Curve>();
                foreach (var piece in pieces)
                {
                    var difference = Curve.CreateBooleanDifference(piece, cutter, Tolerance);
                    if (difference == null || difference.Length == 0) next.Add(piece);
                    else next.AddRange(difference);
                }
                pieces = next;
            }

            var result = new List<Curve>();
            foreach (var piece in pieces)
                result.AddRange(SubdivideRecursiveOrthogonal(piece, divisions - 1,
                    minArea, streetWidth, rng));
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static List<Curve> SubdivideOrthogonalOverlay(Curve boundary, int divisions,
            double minArea, double streetWidth, Random rng)
        {
            var baseParcels = SubdivideOrthogonalGrid(boundary, divisions, minArea,
                streetWidth, OrthogonalGridType.Regular);
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;
            double angle = (rng.NextDouble() < 0.5 ? -1.0 : 1.0) * Math.PI / 5.0;
            double length = Math.Sqrt(width * width + height * height) * 1.5;
            var cutter = CreateOrientedStrip(bbox.Center,
                new Vector2d(Math.Cos(angle), Math.Sin(angle)), length, streetWidth * 1.5);
            cutter.Transform(toWorld);

            var result = new List<Curve>();
            foreach (var parcel in baseParcels)
            {
                var difference = Curve.CreateBooleanDifference(parcel, cutter, Tolerance);
                if (difference == null || difference.Length == 0) result.Add(parcel);
                else
                {
                    foreach (var piece in difference)
                        if (GeometryHelpers.GetCurveArea(piece) > Tolerance)
                            result.Add(piece);
                }
            }
            return result.Count > 0 ? result : baseParcels;
        }

        private static List<Curve> SubdivideRadialConcentricGrid(Curve boundary, int divisions,
            double minArea, double streetWidth, RadialConcentricGridType gridType)
        {
            GetLocalBoundary(boundary, out _, out var local, out var toWorld);
            var bbox = local.GetBoundingBox(true);
            var areaProperties = AreaMassProperties.Compute(local);
            var center = areaProperties?.Centroid ?? bbox.Center;
            double maxRadius = 0.0;
            foreach (var corner in bbox.GetCorners())
                maxRadius = Math.Max(maxRadius, corner.DistanceTo(center));
            maxRadius += streetWidth;

            int target = Math.Max(6, (int)Math.Ceiling(Math.Pow(2.0, Math.Min(divisions, 14))));
            int rings = Math.Max(1, (int)Math.Round(Math.Sqrt(target / 6.0)));
            int sectors = Math.Max(6, (int)Math.Ceiling((double)target / rings));

            // A real radial plan terminates at a civic space or focal block, rather than
            // forcing every parcel to an unusable mathematical point. The central core is
            // scaled to the site but remains large enough to support the selected street width.
            double siteWidth = bbox.Max.X - bbox.Min.X;
            double siteHeight = bbox.Max.Y - bbox.Min.Y;
            double centerRadius = Math.Max(streetWidth * 3.0, Math.Min(siteWidth, siteHeight) * 0.12);
            centerRadius = Math.Min(centerRadius, maxRadius * 0.30);

            // Limit spoke density by the frontage available at the first ring. This prevents
            // narrow wedge parcels even when a high subdivision depth is requested.
            double minFrontage = Math.Max(streetWidth * 3.0, Math.Sqrt(Math.Max(minArea, 1.0)) * 0.5);
            int frontageLimit = Math.Max(6,
                (int)Math.Floor(2.0 * Math.PI * centerRadius / minFrontage));
            sectors = Math.Min(Math.Min(sectors, frontageLimit), 24);

            while (rings * sectors > 6 && GeometryHelpers.GetCurveArea(boundary) / (rings * sectors) < minArea)
            {
                if (sectors > 6) sectors--;
                else if (rings > 1) rings--;
                else break;
            }

            double ringDepth = (maxRadius - centerRadius) / rings;
            var result = new List<Curve>();

            // The first parcel is a finite central civic/focal block. The unallocated band
            // around it becomes the innermost ring street in the Streets output.
            double coreRadius = Math.Max(Tolerance, centerRadius - streetWidth / 2.0);
            var core = RegularPolygon(center, coreRadius, sectors);
            core.Transform(toWorld);
            AddIntersections(boundary, core, result);

            for (int ring = 0; ring < rings; ring++)
            {
                double inner = centerRadius + ring * ringDepth + streetWidth / 2.0;
                double outer = centerRadius + (ring + 1) * ringDepth -
                    (ring == rings - 1 ? 0.0 : streetWidth / 2.0);
                if (outer <= inner + Tolerance) continue;

                if (gridType == RadialConcentricGridType.FanPlan)
                {
                    double fanSpan = Math.PI * 0.90;
                    double fanStart = -Math.PI / 2.0 - fanSpan / 2.0;
                    int fanSectors = Math.Max(4,
                        (int)Math.Round(sectors * fanSpan / (Math.PI * 2.0)));
                    for (int sector = 0; sector < fanSectors; sector++)
                    {
                        double a0 = fanStart + sector * fanSpan / fanSectors;
                        double a1 = fanStart + (sector + 1) * fanSpan / fanSectors;
                        AddRadialCell(boundary, center, inner, outer, a0, a1,
                            streetWidth, false, toWorld, result);
                    }

                    // The back of the focal building is intentionally much less permeable,
                    // matching fan cities where the urban quarter occupies one principal sector.
                    AddRadialCell(boundary, center, inner, outer, fanStart + fanSpan,
                        fanStart + Math.PI * 2.0, streetWidth, false, toWorld, result);
                }
                else
                {
                    bool polygonal = gridType == RadialConcentricGridType.PolygonalRadial;
                    for (int sector = 0; sector < sectors; sector++)
                    {
                        double a0 = sector * Math.PI * 2.0 / sectors;
                        double a1 = (sector + 1) * Math.PI * 2.0 / sectors;
                        AddRadialCell(boundary, center, inner, outer, a0, a1,
                            streetWidth, polygonal, toWorld, result);
                    }
                }
            }
            return result.Count > 0 ? result : new List<Curve> { boundary };
        }

        private static void AddRadialCell(Curve boundary, Point3d center, double inner,
            double outer, double start, double end, double streetWidth, bool polygonal,
            Transform toWorld, List<Curve> result)
        {
            var cell = polygonal
                ? PolygonalAnnularSector(center, inner, outer, start, end, streetWidth)
                : AnnularSector(center, inner, outer, start, end, streetWidth);
            cell.Transform(toWorld);
            AddIntersections(boundary, cell, result);
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

        private static Curve ChamferedRectangleCurve(double x0, double y0, double x1,
            double y1, double chamfer)
        {
            var points = new[]
            {
                new Point3d(x0 + chamfer, y0, 0),
                new Point3d(x1 - chamfer, y0, 0),
                new Point3d(x1, y0 + chamfer, 0),
                new Point3d(x1, y1 - chamfer, 0),
                new Point3d(x1 - chamfer, y1, 0),
                new Point3d(x0 + chamfer, y1, 0),
                new Point3d(x0, y1 - chamfer, 0),
                new Point3d(x0, y0 + chamfer, 0),
                new Point3d(x0 + chamfer, y0, 0)
            };
            return new Polyline(points).ToNurbsCurve();
        }

        private static Curve RegularPolygon(Point3d center, double radius, int sides)
        {
            var points = new List<Point3d>();
            for (int i = 0; i < sides; i++)
                points.Add(Polar(center, radius, Math.PI * 2.0 * i / sides));
            points.Add(points[0]);
            return new Polyline(points).ToNurbsCurve();
        }

        private static Curve AnnularSector(Point3d center, double inner, double outer,
            double start, double end, double streetWidth)
        {
            const int samples = 8;
            var points = new List<Point3d>();
            double innerGap = Math.Asin(Math.Min(0.95, streetWidth / (2.0 * inner)));
            double outerGap = Math.Asin(Math.Min(0.95, streetWidth / (2.0 * outer)));
            double innerStart = start + innerGap;
            double innerEnd = end - innerGap;
            double outerStart = start + outerGap;
            double outerEnd = end - outerGap;

            if (inner <= Tolerance)
                points.Add(center);
            else
                for (int i = 0; i <= samples; i++)
                    points.Add(Polar(center, inner,
                        innerStart + (innerEnd - innerStart) * i / samples));

            for (int i = samples; i >= 0; i--)
                points.Add(Polar(center, outer,
                    outerStart + (outerEnd - outerStart) * i / samples));
            points.Add(points[0]);
            return new Polyline(points).ToNurbsCurve();
        }

        private static Curve PolygonalAnnularSector(Point3d center, double inner,
            double outer, double start, double end, double streetWidth)
        {
            double innerGap = Math.Asin(Math.Min(0.95, streetWidth / (2.0 * inner)));
            double outerGap = Math.Asin(Math.Min(0.95, streetWidth / (2.0 * outer)));
            var points = new[]
            {
                Polar(center, inner, start + innerGap),
                Polar(center, inner, end - innerGap),
                Polar(center, outer, end - outerGap),
                Polar(center, outer, start + outerGap),
                Polar(center, inner, start + innerGap)
            };
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
