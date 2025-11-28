using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace FormFlux
{
    public class FormFluxInfo : GH_AssemblyInfo
    {
        public override string Name => "Form Flux";

        public override Bitmap Icon => Properties.Resources.icon_24x24;

        public override string Description => "Generate building masses with multiple typologies from parcel boundaries";

        public override Guid Id => new Guid("8DD5A26C-63F9-4E4F-9A7B-6C5B8D1E4F3A");

        public override string AuthorName => "";

        public override string AuthorContact => "";
    }
}
