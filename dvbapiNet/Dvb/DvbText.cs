using System;
using System.Text;

namespace dvbapiNet.Dvb
{
    /// <summary>
    /// Dekodiert DVB-Texte (character strings) nach EN 300 468 Anhang C.
    ///
    /// Ein DVB-Text besteht aus einem Kennzeichen für den Zeichensatz, gefolgt von den
    /// Zeichencodes. Die meisten europäischen Sender verwenden 0x01 (ISO 8859-1) oder
    /// 0x0A (ISO 8859-15); 0x20-0x7F tragen den Zeichencode direkt.
    ///
    /// Unbekannte bzw. in .NET nicht verfügbare Zeichensätze fallen auf ISO 8859-1
    /// zurück, damit ein fehlender Eintrag nie einen leeren Kanalnamen erzeugt.
    /// </summary>
    public static class DvbText
    {
        /// <summary>
        /// ISO/IEC 8859-1 als Rückfallwert, entspricht Kennzeichen 0x01.
        /// </summary>
        private const int cLatin1 = 28591;

        /// <summary>
        /// Dekodiert einen DVB-Text.
        /// </summary>
        /// <param name="data">Quelldaten</param>
        /// <param name="offset">Start des Zeichensatzkennzeichens</param>
        /// <param name="length">Anzahl der zu dekodierenden Bytes</param>
        /// <returns>Dekodierter Text, ohne abschließende Leerzeichen</returns>
        public static string Decode(byte[] data, int offset, int length)
        {
            if (data == null || length <= 0)
                return "";

            if (offset < 0 || offset >= data.Length)
                return "";

            if (offset + length > data.Length)
                length = data.Length - offset; // toleranter bei abgeschnittenen Daten

            if (length < 2)
                return "";

            int start = offset + 1; // erstes Byte nach dem Kennzeichen
            int len = length - 1;
            Encoding enc = ResolveEncoding(data[offset]);

            string text;

            if (IsMultiByte(enc))
            {
                // UTF-16/UCS-2 muss als Einheit dekodiert werden - eine Dekodierung
                // Byte für Byte würde jedes zweite Byte verschieben.
                text = enc.GetString(data, start, len);
            }
            else
            {
                var sb = new StringBuilder(len);

                for (int i = start; i < offset + length; i++)
                {
                    byte b = data[i];

                    // 0x0D/0x0A sind bei DVB-Texten als Zeilenumbruch zu entfernen.
                    if (b == 0x0D || b == 0x0A)
                        continue;

                    // Escape: 0xEF kündigt eine Folge an, die hier nicht ausgewertet wird.
                    if (b == 0xEF)
                    {
                        if (i + 1 < offset + length)
                            i++; // Escape-Sequenz überspringen

                        continue;
                    }

                    sb.Append(enc.GetString(new[] { b }));
                }

                text = sb.ToString();
            }

            return Clean(text);
        }

        /// <summary>
        /// Entfernt Zeilenumbrüche und abschließende Leerzeichen.
        /// </summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            var sb = new StringBuilder(s.Length);

            foreach (char c in s)
            {
                if (c == '\r' || c == '\n' || c == '\0')
                    continue;

                sb.Append(c);
            }

            return sb.ToString().TrimEnd(' ');
        }

        private static bool IsMultiByte(Encoding enc)
        {
            return enc.CodePage == 1200 || enc.CodePage == 1201; // UTF-16LE / UTF-16BE
        }

        /// <summary>
        /// Ermittelt den Zeichensatz anhand des Kennzeichens (EN 300 468 Anhang C).
        /// </summary>
        /// <param name="charsetId">Zeichensatzkennzeichen</param>
        /// <returns>Encoding für das Kennzeichen</returns>
        private static Encoding ResolveEncoding(byte charsetId)
        {
            switch (charsetId)
            {
                case 0x00: return Get(28595);   // ISO 8859-5  (Cyrillic)
                case 0x01: return Get(cLatin1); // ISO 8859-1  (Latin-1)
                case 0x02: return Get(28598);   // ISO 8859-6  (Arabic)
                case 0x03: return Get(28597);   // ISO 8859-7  (Greek)
                case 0x04: return Get(28599);   // ISO 8859-8  (Hebrew)
                case 0x05: return Get(28596);   // ISO 8859-9  (Turkish)
                // 0x06 = ISO 8859-10 (Latin-6) ist in .NET nicht vorhanden -> Latin-1
                case 0x06: return Get(cLatin1);
                case 0x07: return Get(874);     // ISO 8859-11 (Thai)
                case 0x08: return Get(28603);   // ISO 8859-13 (Latin-7)
                case 0x09: return Get(28604);   // ISO 8859-14 (Latin-8)
                case 0x0A: return Get(28605);   // ISO 8859-15 (Latin-9)
                case 0x0B: return Get(28606);   // ISO 8859-16 (Latin-10)
                case 0x10: return Encoding.BigEndianUnicode; // UTF-16BE
                case 0x11: return Encoding.Unicode;          // UCS-2BE
                case 0x15: return Get(28596);   // ISO 8859-9 (Legacy-Alias)
                default:
                    // 0x20-0x7F: der Bytewert IST der Zeichencode (Euro-OCR).
                    return Get(cLatin1);
            }
        }

        private static Encoding Get(int codePage)
        {
            try
            {
                return Encoding.GetEncoding(codePage);
            }
            catch (ArgumentException)
            {
                return Encoding.GetEncoding(cLatin1);
            }
        }
    }
}
