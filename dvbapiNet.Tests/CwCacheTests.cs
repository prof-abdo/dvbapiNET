using dvbapiNet.Dvb.Crypto;
using dvbapiNet.Oscam;
using Xunit;

namespace dvbapiNet.Tests
{
    public class CwCacheTests
    {
        [Fact]
        public void Disabled_ReturnsNullAndDoesNotStore()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = false;
            c.Store(123, DescramblerParity.Even, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Assert.Null(c.Take(123));
            Assert.Equal(0, c.Stores);
        }

        [Fact]
        public void Store_AndTake_RoundTrip()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            var cw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            c.Store(42, DescramblerParity.Odd, cw);
            var got = c.Take(42);
            Assert.NotNull(got);
            Assert.Single(got);
            Assert.Equal(DescramblerParity.Odd, got[0].Parity);
            Assert.Equal(cw, got[0].Cw);
        }

        [Fact]
        public void Store_ReplacesSameParity()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            c.Store(7, DescramblerParity.Even, new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 });
            c.Store(7, DescramblerParity.Even, new byte[] { 2, 2, 2, 2, 2, 2, 2, 2 });
            var got = c.Take(7);
            Assert.Single(got);
            Assert.Equal(2, got[0].Cw[0]);
        }

        [Fact]
        public void Store_KeepsBothParities()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            c.Store(99, DescramblerParity.Even, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            c.Store(99, DescramblerParity.Odd, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
            var got = c.Take(99);
            Assert.Equal(2, got.Length);
        }

        [Fact]
        public void Take_UnknownSid_ReturnsNull_IncrementsMisses()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            Assert.Null(c.Take(9999));
            Assert.Equal(1, c.Misses);
        }

        [Fact]
        public void Take_KnownSid_IncrementsHits()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            c.Store(11, DescramblerParity.Even, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            c.Take(11);
            Assert.Equal(1, c.Hits);
        }

        [Fact]
        public void Clear_ResetsEverything()
        {
            var c = CwCache.Instance;
            c.Enabled = true;
            c.Store(1, DescramblerParity.Even, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            c.Take(1);
            c.Clear();
            Assert.Equal(0, c.Size);
            Assert.Equal(0, c.Hits);
            Assert.Equal(0, c.Stores);
        }

        [Fact]
        public void InvalidInputs_AreIgnored()
        {
            var c = CwCache.Instance;
            c.Clear();
            c.Enabled = true;
            c.Store(0, DescramblerParity.Even, new byte[] { 1, 2, 3, 4 });
            c.Store(-1, DescramblerParity.Even, new byte[] { 1, 2, 3, 4 });
            c.Store(5, DescramblerParity.Even, null);
            c.Store(5, DescramblerParity.Even, new byte[0]);
            Assert.Equal(0, c.Stores);
        }
    }
}
