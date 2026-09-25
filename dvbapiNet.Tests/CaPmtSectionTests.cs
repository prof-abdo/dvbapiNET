using dvbapiNet.Oscam;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt die Längenkodierung des AOT_CA_PMT-Headers (ASN.1, EN 50221 S.11) ab.
    /// Ein Fehler hier bricht jeden Kanal, deshalb alle vier Zweige explizit.
    ///
    /// Hinweis: AotCaPmt = 0x9F803200 - das letzte Byte des Opcodes ist laut dvbapi
    /// bewusst das Längenfeld ("least significant byte is length"). Header[3] ist daher
    /// die Länge bzw. der Long-Form-Marker, nicht 0x00.
    /// </summary>
    public class CaPmtSectionTests
    {
        private static int Cmd() => unchecked((int)DvbApiCommand.AotCaPmt);

        [Theory]
        [InlineData(0, 4)]
        [InlineData(1, 4)]
        [InlineData(127, 4)]
        [InlineData(128, 5)]
        [InlineData(255, 5)]
        [InlineData(256, 6)]
        [InlineData(65535, 6)]
        [InlineData(65536, 7)]
        [InlineData(0xFFFFFF, 7)]
        public void BuildHeader_HasExpectedLength(int len, int expectedHeaderLen)
        {
            Assert.Equal(expectedHeaderLen, CaPmtSection.BuildHeader(Cmd(), len).Length);
        }

        [Fact]
        public void BuildHeader_WritesOpcodePrefix()
        {
            byte[] h = CaPmtSection.BuildHeader(Cmd(), 42);

            Assert.Equal(0x9F, h[0]);
            Assert.Equal(0x80, h[1]);
            Assert.Equal(0x32, h[2]);
        }

        [Fact]
        public void BuildHeader_ShortForm_LengthInFourthByte()
        {
            // kurze Form: Länge direkt in Byte 3 (Opcode-High-Byte ist die Länge)
            Assert.Equal(42, CaPmtSection.BuildHeader(Cmd(), 42)[3]);
        }

        [Fact]
        public void BuildHeader_TwoByteLength()
        {
            byte[] h = CaPmtSection.BuildHeader(Cmd(), 300);

            Assert.Equal(0x82, h[3]);
            Assert.Equal(300 >> 8, h[4]);
            Assert.Equal(300 & 0xFF, h[5]);
        }

        [Fact]
        public void BuildHeader_ThreeByteLength()
        {
            byte[] h = CaPmtSection.BuildHeader(Cmd(), 70000);

            Assert.Equal(0x83, h[3]);
            Assert.Equal(70000 >> 16, h[4]);
            Assert.Equal((70000 >> 8) & 0xFF, h[5]);
            Assert.Equal(70000 & 0xFF, h[6]);
        }

        [Fact]
        public void BuildHeader_OneByteFormIsUsedBelow128()
        {
            Assert.Equal(4, CaPmtSection.BuildHeader(Cmd(), 127).Length);
            Assert.Equal(5, CaPmtSection.BuildHeader(Cmd(), 128).Length);
        }

        [Fact]
        public void BuildHeader_EncodesLengthInMinimalForm()
        {
            // 256 braucht 2 Längenbytes (0x82), nicht 3
            Assert.Equal(0x82, CaPmtSection.BuildHeader(Cmd(), 256)[3]);
            // 65536 braucht 3 Längenbytes (0x83)
            Assert.Equal(0x83, CaPmtSection.BuildHeader(Cmd(), 65536)[3]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(4096)]
        [InlineData(65535)]
        [InlineData(65536)]
        [InlineData(1000000)]
        public void BuildHeader_RoundTripsLength(int len)
        {
            byte[] h = CaPmtSection.BuildHeader(Cmd(), len);

            // Länge so zurücklesen, wie Oscam es tun würde
            int first = h[3];
            int encoded;

            if (first < 0x80)
                encoded = first;
            else if (first == 0x81)
                encoded = h[4];
            else if (first == 0x82)
                encoded = (h[4] << 8) | h[5];
            else
                encoded = (h[4] << 16) | (h[5] << 8) | h[6];

            Assert.Equal(len, encoded);
        }

        [Fact]
        public void BuildHeader_ZeroLength_IsValidShortForm()
        {
            byte[] h = CaPmtSection.BuildHeader(Cmd(), 0);

            Assert.Equal(4, h.Length);
            Assert.Equal(0, h[3]);
        }
    }
}
