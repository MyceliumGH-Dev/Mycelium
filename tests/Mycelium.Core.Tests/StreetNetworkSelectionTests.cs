using Mycelium.Core;
using Xunit;

namespace Mycelium.Core.Tests
{
    /// <summary>
    /// The street-network sub-option has to be reachable as a parameter, not only from the
    /// component context menu, so a batch campaign can sweep it.
    /// </summary>
    public class StreetNetworkSelectionTests
    {
        [Fact]
        public void EveryCanonicalNameRoundTrips()
        {
            foreach (string name in StreetNetworkSelection.CanonicalNames)
            {
                Assert.True(StreetNetworkSelection.TryParse(name, out var selection),
                    $"'{name}' should parse");
                Assert.Equal(name, selection.ToCanonicalName());
            }
        }

        [Theory]
        [InlineData("Regular Grid", StreetNetworkType.OrthogonalGrid)]
        [InlineData("Cerdà Grid", StreetNetworkType.OrthogonalGrid)]
        [InlineData("cerda", StreetNetworkType.OrthogonalGrid)]
        [InlineData("Hierarchical Superblock", StreetNetworkType.OrthogonalGrid)]
        [InlineData("Recursive Orthogonal", StreetNetworkType.IrregularGrid)]
        [InlineData("Deformed Grid", StreetNetworkType.IrregularGrid)]
        [InlineData("Staggered Grid", StreetNetworkType.IrregularGrid)]
        [InlineData("Single Axis", StreetNetworkType.DiagonalGrid)]
        [InlineData("Cross Axes", StreetNetworkType.DiagonalGrid)]
        [InlineData("Orthogonal Overlay", StreetNetworkType.DiagonalGrid)]
        [InlineData("Civic Core", StreetNetworkType.RadialConcentricGrid)]
        [InlineData("Polygonal Radial", StreetNetworkType.RadialConcentricGrid)]
        [InlineData("Fan Plan", StreetNetworkType.RadialConcentricGrid)]
        public void MenuLabelsResolveToTheirFamily(string label, StreetNetworkType expected)
        {
            Assert.True(StreetNetworkSelection.TryParse(label, out var selection));
            Assert.Equal(expected, selection.Family);
        }

        [Fact]
        public void AccentsSeparatorsAndCaseAreIgnored()
        {
            Assert.True(StreetNetworkSelection.TryParse("Orthogonal/Cerdà", out var slashed));
            Assert.True(StreetNetworkSelection.TryParse("CERDA GRID", out var shouted));

            Assert.Equal(OrthogonalGridType.Cerda, slashed.Orthogonal);
            Assert.Equal(slashed.ToCanonicalName(), shouted.ToCanonicalName());
        }

        [Fact]
        public void OrthogonalOverlayStaysInTheDiagonalFamily()
        {
            // The name begins with another family's name, so the family/subtype split must not
            // strand it in the orthogonal family.
            Assert.True(StreetNetworkSelection.TryParse("Diagonal/OrthogonalOverlay", out var selection));
            Assert.Equal(StreetNetworkType.DiagonalGrid, selection.Family);
            Assert.Equal(DiagonalGridType.OrthogonalOverlay, selection.Diagonal);
        }

        [Fact]
        public void BareFamilyNamesResolveToTheirDefaultSubOption()
        {
            Assert.True(StreetNetworkSelection.TryParse("Orthogonal", out var orthogonal));
            Assert.Equal(OrthogonalGridType.Regular, orthogonal.Orthogonal);

            Assert.True(StreetNetworkSelection.TryParse("Radial-Concentric Grid", out var radial));
            Assert.Equal(StreetNetworkType.RadialConcentricGrid, radial.Family);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Hexagonal Lattice")]
        public void UnknownOrEmptyNamesAreRejected(string text)
        {
            Assert.False(StreetNetworkSelection.TryParse(text, out _));
        }
    }
}
