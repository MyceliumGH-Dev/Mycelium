namespace Mycelium.Core
{
    /// <summary>
    /// Street-network families and their sub-options.
    /// </summary>
    /// <remarks>
    /// Declared apart from the generator so the selection vocabulary carries no dependency on the
    /// geometry kernel and can be enumerated or parsed by tooling and tests.
    /// Enum values are part of the saved-file format and of the case manifest, so existing members
    /// must keep their numeric values.
    /// </remarks>
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
}
