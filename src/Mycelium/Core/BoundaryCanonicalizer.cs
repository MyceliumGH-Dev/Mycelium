using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Rhino.Geometry;

namespace Mycelium.Core
{
    /// <summary>
    /// Produces a stable textual form of a site boundary so that a case identifier can depend on
    /// the site as well as on the numeric parameters.
    /// </summary>
    /// <remarks>
    /// Two boundaries that describe the same region must hash identically even when they differ in
    /// representation. Canonicalization therefore removes the degrees of freedom that carry no
    /// geometric meaning: curve type and parameterization (reduced to a polyline at a declared
    /// tolerance), winding direction (forced counter-clockwise), seam position (rotated to the
    /// lexicographically smallest vertex), and floating-point noise below the declared tolerance
    /// (quantized to a fixed number of decimals).
    ///
    /// The tolerance and decimal count are part of the canonical form and are recorded in the
    /// manifest, because changing either changes the digest.
    /// </remarks>
    public static class BoundaryCanonicalizer
    {
        public const double DefaultTolerance = 0.001;
        public const int DefaultDecimals = 6;

        /// <summary>
        /// Canonical text for a boundary curve, or null when no polyline form can be recovered.
        /// </summary>
        public static string Canonicalize(Curve boundary,
            double tolerance = DefaultTolerance, int decimals = DefaultDecimals)
        {
            return CanonicalizeVertices(ExtractVertices(boundary, tolerance), tolerance, decimals);
        }

        /// <summary>
        /// Canonical text for an already-extracted vertex ring. Separated from
        /// <see cref="Canonicalize"/> so the invariants can be exercised without the geometry
        /// kernel; a trailing vertex coincident with the first is dropped if present.
        /// </summary>
        public static string CanonicalizeVertices(IReadOnlyList<Point3d> ring,
            double tolerance = DefaultTolerance, int decimals = DefaultDecimals)
        {
            if (ring == null)
                return null;

            var vertices = new List<Point3d>(ring);
            while (vertices.Count > 1 &&
                   vertices[0].DistanceTo(vertices[vertices.Count - 1]) <= tolerance)
            {
                vertices.RemoveAt(vertices.Count - 1);
            }

            if (vertices.Count < 3)
                return null;

            if (SignedArea(vertices) < 0.0)
                vertices.Reverse();

            RotateToCanonicalStart(vertices, decimals);

            var text = new StringBuilder();
            text.Append("poly:").Append(vertices.Count.ToString(CultureInfo.InvariantCulture));
            text.Append(":tol=").Append(tolerance.ToString("R", CultureInfo.InvariantCulture));
            text.Append(":dp=").Append(decimals.ToString(CultureInfo.InvariantCulture));
            foreach (var vertex in vertices)
            {
                text.Append(';');
                text.Append(Quantize(vertex.X, decimals)).Append(',');
                text.Append(Quantize(vertex.Y, decimals)).Append(',');
                text.Append(Quantize(vertex.Z, decimals));
            }
            return text.ToString();
        }

        /// <summary>
        /// SHA-256 of the canonical form, or null when the boundary cannot be canonicalized.
        /// </summary>
        public static string Digest(Curve boundary,
            double tolerance = DefaultTolerance, int decimals = DefaultDecimals)
        {
            string canonical = Canonicalize(boundary, tolerance, decimals);
            return canonical == null ? null : DigestOf(canonical);
        }

        public static string DigestOf(string canonical)
        {
            if (canonical == null)
                return null;

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static List<Point3d> ExtractVertices(Curve boundary, double tolerance)
        {
            if (boundary == null)
                return null;

            Polyline polyline;
            if (!boundary.TryGetPolyline(out polyline) || polyline == null)
            {
                // Non-polyline input: approximate at the declared tolerance. The approximation is
                // part of the canonical form, which is why the tolerance is recorded alongside it.
                var approximated = boundary.ToPolyline(tolerance, tolerance, 0.0, 0.0);
                if (approximated == null || !approximated.TryGetPolyline(out polyline) || polyline == null)
                    return null;
            }

            var vertices = new List<Point3d>(polyline.Count);
            foreach (var point in polyline)
                vertices.Add(point);
            return vertices;
        }

        /// <summary>Shoelace area in the XY plane; sign encodes winding direction.</summary>
        private static double SignedArea(IReadOnlyList<Point3d> vertices)
        {
            double twiceArea = 0.0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Count];
                twiceArea += current.X * next.Y - next.X * current.Y;
            }
            return twiceArea / 2.0;
        }

        private static void RotateToCanonicalStart(List<Point3d> vertices, int decimals)
        {
            int startIndex = 0;
            for (int i = 1; i < vertices.Count; i++)
            {
                if (CompareQuantized(vertices[i], vertices[startIndex], decimals) < 0)
                    startIndex = i;
            }

            if (startIndex == 0)
                return;

            var rotated = new List<Point3d>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
                rotated.Add(vertices[(startIndex + i) % vertices.Count]);

            vertices.Clear();
            vertices.AddRange(rotated);
        }

        private static int CompareQuantized(Point3d left, Point3d right, int decimals)
        {
            int comparison = string.CompareOrdinal(Quantize(left.X, decimals), Quantize(right.X, decimals));
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(Quantize(left.Y, decimals), Quantize(right.Y, decimals));
            if (comparison != 0) return comparison;
            return string.CompareOrdinal(Quantize(left.Z, decimals), Quantize(right.Z, decimals));
        }

        /// <summary>
        /// Fixed-width decimal text. Padding keeps ordinal string comparison consistent with
        /// numeric ordering, and normalizing negative zero keeps the digest stable.
        /// </summary>
        private static string Quantize(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = 0.0;

            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            if (rounded == 0.0)
                rounded = 0.0;

            string formatted = rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
            bool negative = formatted.StartsWith("-", StringComparison.Ordinal);
            string magnitude = negative ? formatted.Substring(1) : formatted;
            return (negative ? "-" : "+") + magnitude.PadLeft(decimals + 16, '0');
        }
    }
}
