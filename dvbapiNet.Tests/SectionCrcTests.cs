using dvbapiNet.Dvb;
using System;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt die MPEG-2 CRC32-Berechnung ab. Verwendet bewusst nur die definierende
    /// Eigenschaft (CRC über Daten+CRC ergibt 0) statt externer Testvektoren.
    /// </summary>
    public class SectionCrcTests
    {
        private static byte[] Payload(int len)
        {
            // deterministisch, aber nicht konstant - konstante Daten wouldn't catch
            // Zugriffsfehler auf unterschiedliche Byte-Positionen.
            byte[] b = new byte[len];
            for (int i = 0; i < len; i++)
                b[i] = (byte)((i * 37 + 11) & 0xFF);

            return b;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(188)]
        [InlineData(1024)]
        [InlineData(4096)]
        public void Compute_AppendedCrcYieldsZero(int len)
        {
            byte[] data = Payload(len);
            byte[] crc = SectionCrc.Compute(data, len);

            byte[] withCrc = new byte[len + 4];
            Array.Copy(data, withCrc, len);
            Array.Copy(crc, 0, withCrc, len, 4);

            Assert.Equal(0u, SectionCrc.ComputeInt(withCrc, withCrc.Length));
        }

        [Fact]
        public void Compute_ReturnsFourBytes()
        {
            Assert.Equal(4, SectionCrc.Compute(Payload(10), 10).Length);
        }

        [Fact]
        public void Compute_IsBigEndian()
        {
            byte[] data = Payload(32);
            uint crc = SectionCrc.ComputeInt(data, data.Length);
            byte[] bytes = SectionCrc.Compute(data, data.Length);

            Assert.Equal((byte)(crc >> 24), bytes[0]);
            Assert.Equal((byte)(crc >> 16), bytes[1]);
            Assert.Equal((byte)(crc >> 8), bytes[2]);
            Assert.Equal((byte)crc, bytes[3]);
        }

        [Fact]
        public void Compute_IsDeterministic()
        {
            byte[] data = Payload(64);

            Assert.Equal(SectionCrc.ComputeInt(data, data.Length),
                         SectionCrc.ComputeInt(data, data.Length));
        }

        [Fact]
        public void Compute_DiffersForDifferentData()
        {
            // ein falscher Poly/Init würde Kollisionen erzeugen
            byte[] a = Payload(64);
            byte[] b = Payload(64);
            b[10] ^= 0x01;

            Assert.NotEqual(SectionCrc.ComputeInt(a, a.Length),
                            SectionCrc.ComputeInt(b, b.Length));
        }

        [Fact]
        public void Compute_HonoursLengthArgument()
        {
            byte[] data = Payload(64);
            data[0] = 0xAA;
            data[1] = 0xBB;

            // nur die ersten 2 Bytes einbeziehen
            uint with2 = SectionCrc.ComputeInt(data, 2);
            uint with64 = SectionCrc.ComputeInt(data, 64);

            Assert.NotEqual(with2, with64);
        }

        [Fact]
        public void Compare_AcceptsMatchingCrc()
        {
            byte[] data = Payload(24);
            uint crc = SectionCrc.ComputeInt(data, data.Length);

            Assert.True(SectionCrc.Compare(data, data.Length, crc));
        }

        [Fact]
        public void Compare_RejectsWrongCrc()
        {
            byte[] data = Payload(24);
            uint crc = SectionCrc.ComputeInt(data, data.Length);

            Assert.False(SectionCrc.Compare(data, data.Length, crc ^ 0xFFFFFFFF));
        }

        [Fact]
        public void Compare_RejectsCorruptedData()
        {
            byte[] data = Payload(24);
            uint crc = SectionCrc.ComputeInt(data, data.Length);

            data[5] ^= 0xFF;

            Assert.False(SectionCrc.Compare(data, data.Length, crc));
        }
    }
}
