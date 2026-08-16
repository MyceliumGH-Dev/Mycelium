using System;
using System.IO;
using Mycelium.Core;
using Xunit;

namespace Mycelium.Core.Tests
{
    /// <summary>
    /// Pins the version-comparison and channel rules of the Yak update check. Adopted, together
    /// with the check itself, from Eddy3D — the beta-channel cases below are the ones that caught
    /// a real field bug there, where every stable user was told to "update" to a pre-release.
    ///
    /// Nothing here touches the network: CheckAsync's HTTP path is deliberately not covered, the
    /// pure decision functions it delegates to are.
    /// </summary>
    public class MyceliumUpdateCheckTests
    {
        [Theory]
        [InlineData("0.1.0.4", "0.1.1.4", true)]      // patch bump
        [InlineData("0.1.0.4", "0.2.0.4", true)]      // minor bump
        [InlineData("0.1.0.4", "0.1.0.5", true)]      // build bump
        [InlineData("0.1.0.4", "0.1.0.4", false)]     // same version
        [InlineData("0.2.0.4", "0.1.0.4", false)]     // installed already newer
        [InlineData("0.2.0-beta.4", "0.2.0.4", true)] // beta -> stable of the same triple is newer (4th octet)
        [InlineData("0.2.0.4", "0.2.0-beta.4", false)]// stable is not older than its own beta
        [InlineData("garbage", "0.1.0.4", false)]     // unparseable installed
        [InlineData("0.1.0.4", "", false)]            // empty latest (offline)
        public void IsNewer_ComparesStableVersions(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, MyceliumUpdateCheck.IsNewer(installed, latest));
        }

        /// A registry listing shaped like Yak's own: betas share the array with the stables, and the
        /// SemVer sort puts the newest beta FIRST — which is what makes "take element 0" wrong.
        private static readonly string[] RegistryListing =
        {
            "0.3.0-beta.4", "0.2.0.4", "0.2.0-beta.4", "0.1.9.4", "0.1.8.4", "0.1.7-beta.4", "0.1.6.4"
        };

        [Fact]
        public void StableUser_IsNeverOfferedABeta()
        {
            var latest = MyceliumUpdateCheck.SelectLatestFor("0.2.0.4", RegistryListing);

            Assert.Equal("0.2.0.4", latest);
            Assert.False(MyceliumUpdateCheck.IsNewer("0.2.0.4", latest));
        }

        [Fact]
        public void BetaUser_IsOfferedTheNewerBeta()
        {
            var latest = MyceliumUpdateCheck.SelectLatestFor("0.2.0-beta.4", RegistryListing);

            Assert.Equal("0.3.0-beta.4", latest);
            Assert.True(MyceliumUpdateCheck.IsNewer("0.2.0-beta.4", latest));
        }

        [Fact]
        public void BetaUser_IsOfferedTheStableThatSupersedesTheirBeta()
        {
            // Once X.Y.Z ships it outranks X.Y.Z-beta.W — a beta user must land on the stable
            // rather than being stranded on the pre-release.
            var listing = new[] { "0.2.0.4", "0.2.0-beta.4", "0.1.9.4" };
            var latest = MyceliumUpdateCheck.SelectLatestFor("0.2.0-beta.4", listing);

            Assert.Equal("0.2.0.4", latest);
            Assert.True(MyceliumUpdateCheck.IsNewer("0.2.0-beta.4", latest));
        }

        [Fact]
        public void SelectLatestFor_DoesNotDependOnRegistryOrder()
        {
            var reversed = (string[])RegistryListing.Clone();
            Array.Reverse(reversed);

            Assert.Equal("0.2.0.4", MyceliumUpdateCheck.SelectLatestFor("0.2.0.4", reversed));
            Assert.Equal("0.2.0.4", MyceliumUpdateCheck.SelectLatestFor("0.1.9.4", reversed));
            Assert.Equal("0.3.0-beta.4", MyceliumUpdateCheck.SelectLatestFor("0.2.0-beta.4", reversed));
        }

        [Fact]
        public void SelectLatestFor_HandlesAnAllBetaRegistry()
        {
            var betasOnly = new[] { "0.3.0-beta.4", "0.2.0-beta.4" };

            Assert.Null(MyceliumUpdateCheck.SelectLatestFor("0.2.0.4", betasOnly));
            Assert.Equal("0.3.0-beta.4", MyceliumUpdateCheck.SelectLatestFor("0.2.0-beta.4", betasOnly));
            Assert.Null(MyceliumUpdateCheck.SelectLatestFor("0.2.0.4", null));
        }

        [Theory]
        [InlineData("0.2.0.4", "0.3.0.4", true)]            // stable user, stable offer
        [InlineData("0.2.0.4", "0.3.0-beta.4", false)]      // stable user, beta offer — the field bug
        [InlineData("0.2.0-beta.4", "0.3.0-beta.4", true)]  // beta user, beta offer
        [InlineData("0.2.0-beta.4", "0.3.0.4", true)]       // beta user, stable offer
        public void IsOfferable_FollowsTheInstalledChannel(string installed, string candidate, bool expected)
        {
            Assert.Equal(expected, MyceliumUpdateCheck.IsOfferable(installed, candidate));
        }

        [Theory]
        [InlineData("0.3.0-beta.4", true)]
        [InlineData("0.1.7-beta.4", true)]
        [InlineData("0.2.0.4", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsPreRelease_DetectsTheSemVerHyphenSuffix(string version, bool expected)
        {
            Assert.Equal(expected, MyceliumUpdateCheck.IsPreRelease(version));
        }

        [Fact]
        public void IsNewer_StaysAPureComparison_SoCallersMustFilterPreReleases()
        {
            // IsNewer deliberately still ranks a beta above the installed stable: the
            // "don't offer pre-releases" policy lives in SelectLatestFor/IsOfferable.
            Assert.True(MyceliumUpdateCheck.IsNewer("0.2.0.4", "0.3.0-beta.4"));
        }

        [Fact]
        public void EffectiveInstalledVersion_UsesLocallyInstalledYakPackage()
        {
            // Right after an update and before a Rhino restart, the loaded assembly is still the old
            // one while the package folder already holds the new version — without this, the user is
            // nagged about a version they have already installed.
            var originalRoot = Environment.GetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable);
            var root = Path.Combine(Path.GetTempPath(), "MyceliumUpdateCheckTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "8.0", "Mycelium", "0.1.1.4"));
                Environment.SetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable, root);

                var effective = MyceliumUpdateCheck.GetEffectiveInstalledVersion("0.1.0.4");

                Assert.Equal("0.1.1.4", effective);
                Assert.False(MyceliumUpdateCheck.IsNewer(effective, "0.1.1.4"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable, originalRoot);
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void EffectiveInstalledVersion_KeepsTheAssemblyVersion_WhenNoPackageIsNewer()
        {
            var originalRoot = Environment.GetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable);
            var root = Path.Combine(Path.GetTempPath(), "MyceliumUpdateCheckTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "8.0", "Mycelium", "0.0.9.4"));
                Environment.SetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable, root);

                Assert.Equal("0.1.0.4", MyceliumUpdateCheck.GetEffectiveInstalledVersion("0.1.0.4"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(MyceliumUpdateCheck.PackageRootEnvironmentVariable, originalRoot);
                try { Directory.Delete(root, true); } catch { /* best effort */ }
            }
        }
    }
}
