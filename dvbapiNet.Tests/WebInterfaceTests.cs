using dvbapiNet.Oscam;
using System;
using System.Text;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt die HTTP-Schicht der Weboberfläche ab: Credential-Leak und Request-Trunkierung.
    /// </summary>
    public class WebInterfaceTests
    {
        // ---- RedactUrl -------------------------------------------------------

        [Fact]
        public void RedactUrl_Empty_ReturnsEmpty()
        {
            Assert.Equal("", WebInterface.RedactUrl(null));
            Assert.Equal("", WebInterface.RedactUrl(""));
            Assert.Equal("", WebInterface.RedactUrl("   "));
        }

        [Fact]
        public void RedactUrl_KeepsSchemeAndHost()
        {
            string r = WebInterface.RedactUrl("https://example.com/hook");

            Assert.StartsWith("https://example.com", r);
        }

        [Fact]
        public void RedactUrl_DoesNotLeakSlackStyleToken()
        {
            // Slack-URLs tragen das Secret direkt im Pfad - genau der Fall, der nicht
            // über /api/config ausgeleitet werden darf.
            const string secret = "T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX";
            string r = WebInterface.RedactUrl("https://hooks.slack.com/services/" + secret);

            Assert.DoesNotContain(secret, r, StringComparison.Ordinal);
            Assert.DoesNotContain("XXXXXXXX", r, StringComparison.Ordinal);
            Assert.Contains("hooks.slack.com", r);
        }

        [Fact]
        public void RedactUrl_DoesNotLeakQueryToken()
        {
            const string secret = "s3cr3t-token-value";
            string r = WebInterface.RedactUrl("https://example.com/hook?token=" + secret);

            Assert.DoesNotContain(secret, r, StringComparison.Ordinal);
            Assert.Contains("<redacted>", r);
        }

        [Fact]
        public void RedactUrl_TruncatesLongPath()
        {
            string r = WebInterface.RedactUrl("https://example.com/aVeryLongSecretPath/segment");

            Assert.Contains("...", r);
            // nur die ersten 6 Zeichen des Pfades dürfen sichtbar sein
            Assert.Contains("/aVery", r);
            Assert.DoesNotContain("LongSecret", r, StringComparison.Ordinal);
        }

        [Fact]
        public void RedactUrl_KeepsShortPathIntact()
        {
            string r = WebInterface.RedactUrl("https://example.com/hook");

            Assert.DoesNotContain("...", r);
        }

        [Fact]
        public void RedactUrl_KeepsNonDefaultPort()
        {
            Assert.Contains(":8443", WebInterface.RedactUrl("https://example.com:8443/hook"));
        }

        [Fact]
        public void RedactUrl_OmitsDefaultPort()
        {
            Assert.DoesNotContain(":443", WebInterface.RedactUrl("https://example.com/hook"));
        }

        [Fact]
        public void RedactUrl_Malformed_DoesNotThrow()
        {
            // darf keine Ausnahme nach außen geben - WebInterface fängt zwar, aber
            // RedactUrl soll selbst robust sein.
            Assert.Equal("<redacted>", WebInterface.RedactUrl("http://"));
        }

        // ---- HasHeaderEnd ---------------------------------------------------

        private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public void HasHeaderEnd_FindsTerminator()
        {
            byte[] b = Bytes("GET /api/status HTTP/1.1\r\nHost: x\r\n\r\n");

            Assert.True(WebInterface.HasHeaderEnd(b, b.Length));
        }

        [Fact]
        public void HasHeaderEnd_FindsTerminatorAtFirstPossiblePosition()
        {
            // 4 Bytes sind das Minimum, damit ein CRLFCRLF überhaupt passen kann.
            byte[] b = Bytes("\r\n\r\n");

            Assert.True(WebInterface.HasHeaderEnd(b, 4));
        }

        [Fact]
        public void HasHeaderEnd_NoTerminator_ReturnsFalse()
        {
            byte[] b = Bytes("GET /api/status HTTP/1.1\r\nHost: x\r\n");

            Assert.False(WebInterface.HasHeaderEnd(b, b.Length));
        }

        [Fact]
        public void HasHeaderEnd_TooShort_ReturnsFalse()
        {
            byte[] b = Bytes("GET");

            Assert.False(WebInterface.HasHeaderEnd(b, b.Length));
        }

        [Fact]
        public void HasHeaderEnd_IgnoresPartialTrailingSequence()
        {
            // Nur "\r\n\r" empfangen - darf nicht als Terminator gelten.
            byte[] b = Bytes("GET / HTTP/1.1\r\n\r");

            Assert.False(WebInterface.HasHeaderEnd(b, b.Length));
        }

        [Fact]
        public void HasHeaderEnd_LfOnlyLineEndings_NotTerminator()
        {
            // HTTP verlangt CRLF; ein einzelnes LF darf den Header nicht abschließen.
            byte[] b = Bytes("GET / HTTP/1.1\nHost: x\n\n");

            Assert.False(WebInterface.HasHeaderEnd(b, b.Length));
        }

        [Fact]
        public void HasHeaderEnd_RespectsLengthArgument()
        {
            // Der Terminator liegt innerhalb der Daten, aber außerhalb von len.
            byte[] b = Bytes("A\r\n\r\n");

            Assert.False(WebInterface.HasHeaderEnd(b, 3));
            Assert.True(WebInterface.HasHeaderEnd(b, 5));
        }
    }
}
