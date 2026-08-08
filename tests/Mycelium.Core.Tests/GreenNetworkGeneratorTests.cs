using System.Collections.Generic;
using Mycelium.Core;
using Rhino.Geometry;
using Xunit;

public class GreenNetworkGeneratorTests
{
    private static Curve Boundary() => new Rectangle3d(Plane.WorldXY, 100, 80).ToNurbsCurve();

    [Fact(Skip = "Rhino geometry booleans require the Rhino native runtime.")]
    public void SameSeedProducesSameRefuges()
    {
        var a = GreenNetworkGenerator.Generate(Boundary(), null, null, 6, 4, 4, 7, 0, 42);
        var b = GreenNetworkGenerator.Generate(Boundary(), null, null, 6, 4, 4, 7, 0, 42);
        Assert.Equal(a.Refuges.Count, b.Refuges.Count);
        for (int i = 0; i < a.Refuges.Count; i++)
            Assert.True(a.Refuges[i].GetBoundingBox(true).Center.DistanceTo(
                b.Refuges[i].GetBoundingBox(true).Center) < 1e-9);
    }

    [Fact(Skip = "Rhino geometry booleans require the Rhino native runtime.")]
    public void GeneratesSeparateNetworkLayers()
    {
        var result = GreenNetworkGenerator.Generate(Boundary(), new List<Curve>
            { new LineCurve(new Point3d(10, 40, 0), new Point3d(90, 40, 0)) },
            null, 6, 4, 2, 7, 0, 7);
        Assert.NotEmpty(result.Belt);
        Assert.NotEmpty(result.Corridors);
        Assert.Equal(2, result.Refuges.Count);
        Assert.Equal(result.Belt.Count + result.Corridors.Count + result.Refuges.Count,
            result.AllRegions.Count);
    }
}
