using System;

namespace dvbapiNet.Dvb.Descriptors
{
    /// <summary>
    /// Implementiert den DVB Service Descriptor (Tag 0x48).
    /// Liefert den Service-Namen und den Anbieter-Namen eines Programms aus dem SDT.
    ///
    /// Aufbau hinter Tag und Längenbyte:
    ///   service_type            1 byte
    ///   service_provider_name_length  1 byte
    ///   service_provider_name   n bytes  (DVB-Text)
    ///   service_name_length     1 byte
    ///   service_name            m bytes  (DVB-Text)
    /// </summary>
    public sealed class ServiceDescriptor : DescriptorBase
    {
        private byte _ServiceType;
        private string _ServiceName;
        private string _ProviderName;

        /// <summary>
        /// Typ des Services, z.B. 0x01 für digitaler Fernsehen, 0x19 für HDTV.
        /// </summary>
        public byte ServiceType
        {
            get
            {
                return _ServiceType;
            }
        }

        /// <summary>
        /// Name des Programms, z.B. "ZDF HD". Leer, falls der Descriptor gekürzt ist.
        /// </summary>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
        }

        /// <summary>
        /// Name des Anbieters, z.B. "ZDF". Leer, falls der Descriptor gekürzt ist.
        /// </summary>
        public string ProviderName
        {
            get
            {
                return _ProviderName;
            }
        }

        public ServiceDescriptor(byte[] data, int offset)
            : base(data, offset)
        {
            // Prüfungen gegen abgeschnittene Descriptoren - der SDT wird aus dem
            // Transportstrom rekonstruiert und kann unvollständig ankommen.
            if (data == null || offset < 0 || offset + 4 > data.Length)
            {
                _ServiceName = "";
                _ProviderName = "";
                return;
            }

            _ServiceType = data[offset + 2];

            int p = offset + 3;

            int providerLen = data[p++];
            _ProviderName = ReadText(data, p, providerLen);

            p += providerLen;

            if (p >= data.Length)
            {
                _ServiceName = "";
                return;
            }

            int nameLen = data[p++];
            _ServiceName = ReadText(data, p, nameLen);
        }

        private static string ReadText(byte[] data, int offset, int len)
        {
            if (len <= 0)
                return "";

            if (offset < 0 || offset + len > data.Length)
                len = data.Length - offset; // nur den vorhandenen Rest dekodieren

            if (len <= 0)
                return "";

            return DvbText.Decode(data, offset, len);
        }
    }
}
