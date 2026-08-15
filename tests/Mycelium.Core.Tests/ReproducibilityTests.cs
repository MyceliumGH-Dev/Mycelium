using System.Collections.Generic;
using Mycelium.Core;
using Rhino.Geometry;
using Xunit;

namespace Mycelium.Core.Tests
{
    /// <summary>
    /// Guards the reproducibility contract: a case identifier must depend on everything that
    /// changes the generated case, and on nothing that does not.
    /// </summary>
    public class ReproducibilityTests
    {
        private static List<Point3d> Square(double size, double originX = 0.0, double originY = 0.0)
        {
            return new List<Point3d>
            {
                new Point3d(originX, originY, 0),
                new Point3d(originX + size, originY, 0),
                new Point3d(originX + size, originY + size, 0),
                new Point3d(originX, originY + size, 0)
            };
        }

        [Fact]
        public void Canonicalization_IsInvariantToSeamWindingAndClosingVertex()
        {
            var baseline = BoundaryCanonicalizer.CanonicalizeVertices(Square(100.0));

            // Same ring started at a different vertex.
            var rotated = BoundaryCanonicalizer.CanonicalizeVertices(new List<Point3d>
            {
                new Point3d(100, 100, 0), new Point3d(0, 100, 0),
                new Point3d(0, 0, 0), new Point3d(100, 0, 0)
            });

            // Same ring traversed clockwise.
            var reversed = Square(100.0);
            reversed.Reverse();

            // Same ring with an explicit repeated closing vertex.
            var closed = Square(100.0);
            closed.Add(closed[0]);

            Assert.NotNull(baseline);
            Assert.Equal(baseline, rotated);
            Assert.Equal(baseline, BoundaryCanonicalizer.CanonicalizeVertices(reversed));
            Assert.Equal(baseline, BoundaryCanonicalizer.CanonicalizeVertices(closed));
        }

        [Fact]
        public void Canonicalization_DistinguishesDifferentSites()
        {
            var small = BoundaryCanonicalizer.DigestOf(
                BoundaryCanonicalizer.CanonicalizeVertices(Square(100.0)));
            var large = BoundaryCanonicalizer.DigestOf(
                BoundaryCanonicalizer.CanonicalizeVertices(Square(120.0)));
            var translated = BoundaryCanonicalizer.DigestOf(
                BoundaryCanonicalizer.CanonicalizeVertices(Square(100.0, 500.0, 0.0)));

            Assert.NotEqual(small, large);
            Assert.NotEqual(small, translated);
        }

        [Fact]
        public void Canonicalization_RejectsDegenerateRings()
        {
            Assert.Null(BoundaryCanonicalizer.CanonicalizeVertices(null));
            Assert.Null(BoundaryCanonicalizer.CanonicalizeVertices(new List<Point3d>
            {
                new Point3d(0, 0, 0), new Point3d(1, 0, 0)
            }));
        }

        private static CaseManifest ManifestFor(string boundaryDigest,
            IReadOnlyList<string> configurations)
        {
            return new CaseManifest
            {
                Generator = new GeneratorProvenance { Version = "0.1.0.4" },
                Parameters = new GenerationParameters
                {
                    Seed = 42,
                    ModelUnits = "Meters",
                    ModelAbsoluteTolerance = 0.001,
                    BoundaryDigest = boundaryDigest,
                    BoundaryCanonicalTolerance = BoundaryCanonicalizer.DefaultTolerance,
                    BoundaryCanonicalDecimals = BoundaryCanonicalizer.DefaultDecimals,
                    FloorHeight = 4.0,
                    Divisions = 2,
                    StreetWidth = 8.0,
                    StreetNetworkFamily = "Orthogonal Grid",
                    StreetNetworkSubtype = "Orthogonal/Cerda",
                    BuildingConfigurations = configurations,
                    AnalysisDirection = new Direction2D { X = 1, Y = 0 }
                }
            };
        }

        [Fact]
        public void CaseId_DependsOnTheBoundary()
        {
            var configurations = new[] { "0|3|6" };
            string first = ManifestFor("aaaa", configurations).CalculateCaseId();
            string second = ManifestFor("bbbb", configurations).CalculateCaseId();

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void CaseId_IgnoresBuildingConfigurationOrder()
        {
            string ordered = ManifestFor("aaaa", new[] { "0|3|6", "1|4|8" }).CalculateCaseId();
            string shuffled = ManifestFor("aaaa", new[] { "1|4|8", "0|3|6" }).CalculateCaseId();

            Assert.Equal(ordered, shuffled);
        }

        [Fact]
        public void CaseId_IsStableForIdenticalInput()
        {
            var configurations = new[] { "0|3|6" };
            Assert.Equal(
                ManifestFor("aaaa", configurations).CalculateCaseId(),
                ManifestFor("aaaa", configurations).CalculateCaseId());
        }

        [Fact]
        public void CaseId_DependsOnModelTolerance()
        {
            var baseline = ManifestFor("aaaa", new[] { "0|3|6" });
            var retoleranced = ManifestFor("aaaa", new[] { "0|3|6" });
            retoleranced.Parameters.ModelAbsoluteTolerance = 0.01;

            Assert.NotEqual(baseline.CalculateCaseId(), retoleranced.CalculateCaseId());
        }
    }
}
