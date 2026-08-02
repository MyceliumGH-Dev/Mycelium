using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Mycelium
{
    /// <summary>
    /// Grasshopper assembly metadata for the Mycelium plugin.
    /// </summary>
    public class MyceliumInfo : GH_AssemblyInfo
    {
        public override string Name => "Mycelium";

        public override Bitmap Icon => ComponentIcons.Get("Mycelium");

        public override string Description =>
            "Generative urban massing: subdivide parcels and grow building typologies, parks, trees, and terrain.";

        public override Guid Id => new Guid("20543B24-AA77-4377-B31B-A778C91CB192");

        public override string AuthorName => "Ilker Karadag, Patrick Kastner";

        public override string AuthorContact => "https://github.com/MyceliumGH-Dev/Mycelium";
    }
}
