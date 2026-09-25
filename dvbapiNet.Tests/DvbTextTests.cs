using dvbapiNet.Dvb;
using System;
using System.Text;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt die DVB-Textdekodierung (EN 300 468 Anhang C) ab.
    /// Die erwarteten Zeichen sind bewusst handverifiziert und nicht aus einer
    /// Referenzimplementierung abgeleitet.
    ///
    /// Wichtig: Alle Nicht-ASCII-Zeichen stehen als \u-Escape. Die Quelldatei hat
    /// keinen BOM, der Compiler liest sie sonst mit der System-Codepage - auf einem
    /// CI-Runner mit anderer Codepage wuerden die Literale sonst zerfallen.
    /// </summary>
    public class DvbTextTests
    {
        private static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (byte[] p in parts) len += p.Length;

            byte[] r = new byte[len];
            int pos = 0;

            foreach (byte[] p in parts)
            {
                Array.Copy(p, 0, r, pos, p.Length);
                pos += p.Length;
            }

            return r;
        }

        [Fact]
        public void Decode_Null_ReturnsEmpty()
        {
            Assert.Equal("", DvbText.Decode(null, 0, 10));
        }

        [Fact]
        public void Decode_ZeroOrNegativeLength_ReturnsEmpty()
        {
            byte[] d = { 0x01, (byte)'A' };

            Assert.Equal("", DvbText.Decode(d, 0, 0));
            Assert.Equal("", DvbText.Decode(d, 0, -1));
        }

        [Fact]
        public void Decode_OffsetBeyondArray_ReturnsEmpty()
        {
            byte[] d = { 0x01, (byte)'A' };

            Assert.Equal("", DvbText.Decode(d, 5, 1));
            Assert.Equal("", DvbText.Decode(d, -1, 1));
        }

        [Fact]
        public void Decode_TruncatedLength_StaysWithinArray()
        {
            // length behauptet mehr Daten als vorhanden - darf keine Ausnahme werfen
            byte[] d = { 0x01, (byte)'A', (byte)'B' };

            Assert.Equal("AB", DvbText.Decode(d, 0, 50));
        }

        [Fact]
        public void Decode_OnlyCharsetNoData_ReturnsEmpty()
        {
            byte[] d = { 0x01 };

            Assert.Equal("", DvbText.Decode(d, 0, 1));
        }

        // ---- Latin-1 (0x01) -------------------------------------------------

        [Fact]
        public void Decode_Latin1_Ascii()
        {
            byte[] d = { 0x01, (byte)'Z', (byte)'D', (byte)'F' };

            Assert.Equal("ZDF", DvbText.Decode(d, 0, 4));
        }

        [Fact]
        public void Decode_Latin1_Umlauts()
        {
            // 0xE4 = U+00E4 in ISO 8859-1
            byte[] d = { 0x01, (byte)'K', (byte)'a', 0xE4, (byte)'b', (byte)'e', (byte)'l' };

            Assert.Equal("Ka\u00E4bel", DvbText.Decode(d, 0, 7));
        }

        [Fact]
        public void Decode_Latin1_SharpS()
        {
            // 0xDF = U+00DF
            byte[] d = { 0x01, (byte)'S', (byte)'t', (byte)'r', (byte)'a', 0xDF, (byte)'e' };

            Assert.Equal("Stra\u00DFe", DvbText.Decode(d, 0, 7));
        }

        [Fact]
        public void Decode_Latin9_EuroSign()
        {
            // 0xA4 = U+20AC in ISO 8859-15 (in Latin-1 waere es U+00A4)
            byte[] d = { 0x0A, 0xA4 };

            Assert.Equal("\u20AC", DvbText.Decode(d, 0, 2));
        }

        [Fact]
        public void Decode_Latin1_AtEuroPosition_IsNotEuro()
        {
            // Gegenprobe: 0xA4 in Latin-1 ist das Waehrungszeichen, nicht der Euro
            byte[] d = { 0x01, 0xA4 };

            Assert.Equal("\u00A4", DvbText.Decode(d, 0, 2));
        }

        [Fact]
        public void Decode_Latin1_CurrencyAndLatin9Euro_Differ()
        {
            // derselbe Bytewert, zwei Zeichensaetze, zwei Ergebnisse
            Assert.NotEqual(DvbText.Decode(new byte[] { 0x01, 0xA4 }, 0, 2),
                            DvbText.Decode(new byte[] { 0x0A, 0xA4 }, 0, 2));
        }

        // ---- Zeichensatzkennzeichen 0x20-0x7F --------------------------------

        [Fact]
        public void Decode_EuroOcrRange_UsesByteAsCode()
        {
            // 0x41 ist unabhaengig vom Kennzeichen immer 'A'
            byte[] d = { 0x41, (byte)'A', (byte)'B', (byte)'C' };

            Assert.Equal("ABC", DvbText.Decode(d, 0, 4));
        }

        [Fact]
        public void Decode_UnknownCharsetId_FallsBackToLatin1()
        {
            byte[] d = { 0x77, (byte)'X', (byte)'Y' };

            Assert.Equal("XY", DvbText.Decode(d, 0, 3));
        }

        // ---- Steuerzeichen / Escapes ----------------------------------------

        [Fact]
        public void Decode_StripsCarriageReturnAndLineFeed()
        {
            byte[] d = { 0x01, (byte)'A', 0x0D, 0x0A, (byte)'B' };

            Assert.Equal("AB", DvbText.Decode(d, 0, 5));
        }

        [Fact]
        public void Decode_Escape_SkipsFollowingByte()
        {
            // 0xEF kuendigt eine Escape-Sequenz an, die hier nicht ausgewertet wird
            byte[] d = { 0x01, (byte)'A', 0xEF, 0x0A, (byte)'B' };

            Assert.Equal("AB", DvbText.Decode(d, 0, 5));
        }

        [Fact]
        public void Escape_AtEnd_DoesNotReadBeyondArray()
        {
            byte[] d = { 0x01, (byte)'A', 0xEF };

            Assert.Equal("A", DvbText.Decode(d, 0, 3));
        }

        [Fact]
        public void Decode_TrimsTrailingSpaces()
        {
            // DVB-Textfelder sind haeufig auf 8 Byte aufgefuellt
            byte[] d = { 0x01, (byte)'A', (byte)'B', (byte)' ', (byte)' ', (byte)' ' };

            Assert.Equal("AB", DvbText.Decode(d, 0, 6));
        }

        [Fact]
        public void Decode_KeepsLeadingSpaces()
        {
            byte[] d = { 0x01, (byte)' ', (byte)'A' };

            Assert.Equal(" A", DvbText.Decode(d, 0, 3));
        }

        [Fact]
        public void Decode_AllSpaces_ReturnsEmpty()
        {
            byte[] d = { 0x01, (byte)' ', (byte)' ' };

            Assert.Equal("", DvbText.Decode(d, 0, 3));
        }

        // ---- UTF-16 ---------------------------------------------------------

        [Fact]
        public void Decode_Utf16BigEndian()
        {
            // 0x10 kennzeichnet UTF-16BE; muss als Einheit dekodiert werden
            byte[] d = Concat(new byte[] { 0x10 }, Encoding.BigEndianUnicode.GetBytes("ZDF"));

            Assert.Equal("ZDF", DvbText.Decode(d, 0, d.Length));
        }

        [Fact]
        public void Decode_Utf16_Umlaut()
        {
            byte[] d = Concat(new byte[] { 0x10 }, Encoding.BigEndianUnicode.GetBytes("K\u00E4se"));

            Assert.Equal("K\u00E4se", DvbText.Decode(d, 0, d.Length));
        }

        [Fact]
        public void Decode_Utf16_RoundTripsNonAscii()
        {
            // sicherstellen, dass die Mehrbyte-Dekodierung wirklich zusammengehaelt wird
            string src = "K\u00E4se \u00DF";
            byte[] d = Concat(new byte[] { 0x10 }, Encoding.BigEndianUnicode.GetBytes(src));

            Assert.Equal(src, DvbText.Decode(d, 0, d.Length));
        }

        [Fact]
        public void Decode_Utf16_OddLength_DoesNotThrow()
        {
            // ungerade Bytelänge bei UTF-16 - darf keine Ausnahme werfen
            byte[] d = { 0x10, 0x00, (byte)'A' };

            var r = DvbText.Decode(d, 0, 3);

            Assert.NotNull(r);
        }
    }
}
