using dvbapiNet.Oscam;
using System;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt den Versionsvergleich des Auto-Updaters ab.
    /// </summary>
    public class UpdateCheckerTests
    {
        // AssemblyVersion ist "X.Y.Z.*": Build trägt den Patch-Level, Revision ist
        // compiler-generiert und darf nie verglichen werden.
        private static Version Local(int maj, int min, int bld, int rev) => new Version(maj, min, bld, rev);

        [Theory]
        [InlineData("v2.3.2", 2, 3, 1, 0, true)]   // Patch höher
        [InlineData("2.3.2", 2, 3, 1, 0, true)]    // ohne v-Präfix
        [InlineData("v2.4.0", 2, 3, 1, 0, true)]   // Minor höher
        [InlineData("v3.0.0", 2, 3, 1, 0, true)]   // Major höher
        [InlineData("v2.3.1", 2, 3, 1, 0, false)]  // identisch
        [InlineData("v2.3.0", 2, 3, 1, 0, false)]  // Patch niedriger
        [InlineData("v2.2.9", 2, 3, 1, 0, false)]  // Minor niedriger
        [InlineData("v1.9.9", 2, 3, 1, 0, false)]  // Major niedriger
        public void IsNewerThanCurrent_ComparesMajorMinorPatch(string tag, int maj, int min, int bld, int rev, bool expected)
        {
            Assert.Equal(expected, UpdateChecker.IsNewerThanCurrent(tag, Local(maj, min, bld, rev)));
        }

        [Fact]
        public void IsNewerThanCurrent_IgnoresRevision()
        {
            // Revision ist compiler-generiert: ein höherer Remote-Revision-Wert darf
            // niemals als "neuer" gelten.
            Assert.False(UpdateChecker.IsNewerThanCurrent("v2.3.1.99999", Local(2, 3, 1, 1)));
        }

        [Fact]
        public void IsNewerThanCurrent_IgnoresLocalRevision()
        {
            // Umgekehrt: ein astronomisch hoher lokaler Revision darf kein Update blockieren.
            Assert.True(UpdateChecker.IsNewerThanCurrent("v2.3.2", Local(2, 3, 1, 99999)));
        }

        [Fact]
        public void IsNewerThanCurrent_TagWithoutPatch_IsTreatedAsPatchZero()
        {
            // "2.4" parst mit Build = -1, was ohne Clamp zu einem Vergleich
            // -1 > 1 => false führen würde.
            Assert.True(UpdateChecker.IsNewerThanCurrent("v2.4", Local(2, 3, 1, 0)));
        }

        [Fact]
        public void IsNewerThanCurrent_TagWithoutPatch_NotNewerWhenSameMinor()
        {
            Assert.False(UpdateChecker.IsNewerThanCurrent("v2.3", Local(2, 3, 1, 0)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nightly")]
        [InlineData("v2.3.1-beta")]
        [InlineData("vX.Y.Z")]
        public void IsNewerThanCurrent_UnparseableTag_ReturnsFalse(string tag)
        {
            Assert.False(UpdateChecker.IsNewerThanCurrent(tag, Local(2, 3, 1, 0)));
        }

        [Fact]
        public void IsNewerThanCurrent_HandlesUppercasePrefix()
        {
            Assert.True(UpdateChecker.IsNewerThanCurrent("V2.3.2", Local(2, 3, 1, 0)));
        }

        [Fact]
        public void IsNewerThanCurrent_HandlesSurroundingWhitespace()
        {
            Assert.True(UpdateChecker.IsNewerThanCurrent("  v2.3.2  ", Local(2, 3, 1, 0)));
        }

        [Fact]
        public void IsNewerThanCurrent_TwoComponentLocalVersion()
        {
            // lokale Version ohne Build (z.B. aus einer alten Assembly)
            Assert.True(UpdateChecker.IsNewerThanCurrent("v2.3.1", new Version(2, 3)));
        }
    }
}
