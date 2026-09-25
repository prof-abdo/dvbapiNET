using dvbapiNet.Dvb.Descriptors;
using dvbapiNet.Dvb.Types;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace dvbapiNet.Tests
{
    /// <summary>
    /// Deckt den Service Descriptor (0x48) und sein Parsing im SDT-Eintrag ab.
    /// </summary>
    public class ServiceDescriptorTests
    {
        /// <summary>
        /// Baut einen Service Descriptor.
        /// Aufbau: tag, länge, service_type, provider_len, provider, name_len, name
        /// </summary>
        private static byte[] Build(byte serviceType, string provider, string name)
        {
            byte[] prov = EncodeLatin1(provider);
            byte[] nm = EncodeLatin1(name);

            byte[] d = new byte[2 + 1 + 1 + prov.Length + 1 + nm.Length];

            int i = 0;
            d[i++] = 0x48;
            d[i++] = (byte)(1 + 1 + prov.Length + 1 + nm.Length); // länge hinter tag+len
            d[i++] = serviceType;
            d[i++] = (byte)prov.Length;
            Array.Copy(prov, 0, d, i, prov.Length); i += prov.Length;
            d[i++] = (byte)nm.Length;
            Array.Copy(nm, 0, d, i, nm.Length);

            return d;
        }

        private static byte[] EncodeLatin1(string s)
        {
            var sb = new List<byte> { 0x01 }; // Kennzeichen ISO 8859-1

            foreach (char c in s)
                sb.Add((byte)c);

            return sb.ToArray();
        }

        [Fact]
        public void ParsesProviderAndServiceName()
        {
            byte[] d = Build(0x01, "ZDF", "ZDF HD");

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal("ZDF", sd.ProviderName);
            Assert.Equal("ZDF HD", sd.ServiceName);
            Assert.Equal(0x01, sd.ServiceType);
        }

        [Fact]
        public void HasExpectedTagAndLength()
        {
            byte[] d = Build(0x01, "ZDF", "ZDF HD");

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal(DescriptorTag.ServiceDescriptor, sd.Tag);
            Assert.Equal(d.Length, sd.Length);
        }

        [Fact]
        public void DecodesUmlautsInName()
        {
            // Nicht-ASCII als \u-Escape: die Quelldatei hat keinen BOM und wuerde
            // sonst mit der System-Codepage gelesen (siehe DvbTextTests).
            byte[] d = Build(0x01, "Kabel eins", "Kr\u00E4he TV");

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal("Kr\u00E4he TV", sd.ServiceName);
            Assert.Equal("Kabel eins", sd.ProviderName);
        }

        [Fact]
        public void HandlesTrailingPadding()
        {
            // SDT-Textfelder werden häufig mit Leerzeichen aufgefüllt
            byte[] d = Build(0x01, "ZDF   ", "ZDF HD  ");

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal("ZDF", sd.ProviderName);
            Assert.Equal("ZDF HD", sd.ServiceName);
        }

        [Fact]
        public void EmptyNames_ReturnEmptyStrings()
        {
            byte[] d = Build(0x01, "", "");

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal("", sd.ServiceName);
            Assert.Equal("", sd.ProviderName);
        }

        [Fact]
        public void TruncatedDescriptor_DoesNotThrow()
        {
            // nur tag, länge und service_type - der Rest fehlt.
            // (Null-Daten sind nicht abgesichert: DescriptorBase liest data[0] im
            // Basiskonstruktor, und die Factory übergibt nie null.)
            byte[] d = { 0x48, 0x10, 0x01 };

            var sd = new ServiceDescriptor(d, 0);

            Assert.Equal("", sd.ProviderName);
            Assert.Equal("", sd.ServiceName);
        }

        [Fact]
        public void Factory_ReturnsServiceDescriptor()
        {
            byte[] d = Build(0x19, "Sky", "Sky HD");

            DescriptorBase desc = DescriptorFactory.CreateDescriptor(d, 0);

            var sd = Assert.IsType<ServiceDescriptor>(desc);
            Assert.Equal("Sky HD", sd.ServiceName);
            Assert.Equal(0x19, sd.ServiceType);
        }

        [Fact]
        public void Factory_HonoursOffset()
        {
            byte[] d = Build(0x01, "RTL", "RTL HD");
            byte[] padded = new byte[d.Length + 3];
            Array.Copy(d, 0, padded, 3, d.Length);

            var sd = Assert.IsType<ServiceDescriptor>(DescriptorFactory.CreateDescriptor(padded, 3));

            Assert.Equal("RTL HD", sd.ServiceName);
        }

        // ---- Einbettung im SDT-Eintrag --------------------------------------

        /// <summary>
        /// Baut einen kompletten SDT-Service-Eintrag um einen Service Descriptor herum.
        /// </summary>
        private static byte[] BuildSdtEntry(int serviceId, byte[] descriptor)
        {
            byte[] e = new byte[5 + descriptor.Length];

            e[0] = (byte)(serviceId >> 8);
            e[1] = (byte)serviceId;
            e[2] = 0x00;                          // Flags
            e[3] = (byte)(descriptor.Length >> 8); // 12-Bit Descriptor-Loop-Länge
            e[4] = (byte)descriptor.Length;
            Array.Copy(descriptor, 0, e, 5, descriptor.Length);

            return e;
        }

        [Fact]
        public void SdtEntry_ExposesServiceName()
        {
            byte[] desc = Build(0x01, "ZDF", "ZDF HD");
            byte[] entry = BuildSdtEntry(0x1F45, desc);

            var sdt = new ServiceDescriptionTable(entry, 0);

            Assert.Equal(0x1F45, sdt.ServiceId);

            var sd = sdt.FindDescriptor<ServiceDescriptor>();

            Assert.NotNull(sd);
            Assert.Equal("ZDF HD", sd.ServiceName);
            Assert.Equal("ZDF", sd.ProviderName);
        }

        [Fact]
        public void SdtEntry_FindDescriptor_MissingTypeReturnsNull()
        {
            byte[] entry = BuildSdtEntry(0x1F45, Build(0x01, "ZDF", "ZDF HD"));

            var sdt = new ServiceDescriptionTable(entry, 0);

            Assert.Null(sdt.FindDescriptor<CaDescriptor>());
        }

        [Fact]
        public void SdtEntry_DescriptorsAreExposed()
        {
            byte[] entry = BuildSdtEntry(0x1F45, Build(0x01, "ZDF", "ZDF HD"));

            var sdt = new ServiceDescriptionTable(entry, 0);

            Assert.NotNull(sdt.Descriptors);
            Assert.NotEmpty(sdt.Descriptors);
        }

        [Fact]
        public void SdtEntry_ServiceIdIsTopTenBits()
        {
            // Längenfeld darf das oberste Bit nicht mit auslesen
            byte[] entry = BuildSdtEntry(0x1F45, Build(0x01, "ZDF", "ZDF HD"));

            var sdt = new ServiceDescriptionTable(entry, 0);

            Assert.Equal(0x1F45, sdt.ServiceId);
            Assert.Equal(0, sdt.RunningStatus);
        }
    }
}
