using dvbapiNet.Dvb;
using dvbapiNet.Dvb.Descriptors;
using dvbapiNet.Dvb.Types;
using dvbapiNet.Oscam.Descriptors;
using System.IO;
using System.Net;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// CaPMT für Oscam - Enthält alle nötigen Informationen damit Oscam die Entschlüsselung einleiten kann
    /// </summary>
    public class CaPmtSection
    {
        public enum ListManagement : byte
        {
            More = 0,
            First = 1,
            Last = 2,
            Only = 3,
            Add = 4,
            Update = 5,
        }

        private byte _Ca, _Dmx, _Adapter;
        private int _pmtPid, _TsId, _Nid;
        private PmtSection _Source;
        private ListManagement _Type;

        public CaPmtSection(PmtSection pmtSource, int pmtPid, int tsid, int nid, byte ca, byte dmx, byte adapter, ListManagement type)
        {
            _Source = pmtSource;
            _pmtPid = pmtPid;
            _Ca = ca;
            _Dmx = dmx;
            _Adapter = adapter;
            _Type = type;
            _TsId = tsid;
            _Nid = nid;
        }

        /// <summary>
        /// Erzeugt den AOT_CA_PMT-Header inklusive der Längenangabe nach ASN.1 (EN 50221 S.11).
        /// </summary>
        /// <param name="cmd">DvbApiCommand.AotCaPmt</param>
        /// <param name="len">Länge der CaPMT, die hinter dem Header folgt</param>
        /// <returns>Headerbytes, unmittelbar gefolgt von den <paramref name="len"/> Payload-Bytes</returns>
        public static byte[] BuildHeader(int cmd, int len)
        {
            byte[] header;

            // Längengenerierung nach ASN.1:
            if (len < 128)
            {
                header = new byte[4];
                header[3] = (byte)len;
            }
            else if (len < 256)
            {
                header = new byte[5];
                header[3] = 0x81;
                header[4] = (byte)len;
            }
            else if (len < 65536)
            {
                header = new byte[6];
                header[3] = 0x82;
                header[4] = (byte)(len >> 8);
                header[5] = (byte)len;
            }
            else // bis 16MiB
            {
                header = new byte[7];
                header[3] = 0x83;
                header[4] = (byte)(len >> 16);
                header[5] = (byte)(len >> 8);
                header[6] = (byte)len;
            }

            header[0] = (byte)(cmd >> 24);
            header[1] = (byte)(cmd >> 16);
            header[2] = (byte)(cmd >> 8);

            return header;
        }

        /// <summary>
        /// Erstellt die CaPMT für den Versand an Oscam.
        /// Header und Länge (nach ASN.1, EN 50221 S.11) müssen noch vorangestellt werden!
        /// </summary>
        public byte[] Create()
        {
            byte[] res;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // CaPmt teil NACH Length_field() (EN 50221 page 30)
                bw.Write((byte)_Type);

                bw.Write(IPAddress.HostToNetworkOrder((short)_Source.ServiceId));

                byte tmp = (byte)(_Source.Version << 1);

                if (_Source.CurrentNext > 0)
                    tmp |= 1;

                ms.WriteByte(tmp);

                int i = 0;
                DescriptorBase d;

                // Program Info Loop erstellen:
                using (MemoryStream piMs = new MemoryStream())
                {
                    // ca_pmt_cmd_id -> descramble
                    piMs.WriteByte(1);

                    // Inject Oscam specific Descriptors:
                    // "bug" in oscam? oscam prüft bei capmt update auch enigma namespace, aktualisiert selbst vorher über PAT aber die ts-id.
                    // dadurch unterschiede in den IDs.
                    (new EnigmaNamespace(_Nid, _TsId)).Write(piMs);
                    (new AdapterDevice(_Adapter)).Write(piMs);
                    (new DemuxDevice(_Dmx)).Write(piMs);
                    (new CaDevice(_Ca)).Write(piMs);
                    (new PmtPid((ushort)_pmtPid)).Write(piMs);

                    while (true)
                    {
                        d = _Source.GetDescriptor(i++);

                        if (d == null)
                            break;

                        if (d.Tag == DescriptorTag.ConditionalAccess)
                        {
                            d.Write(piMs);
                        }
                    }

                    bw.Write(IPAddress.HostToNetworkOrder((short)piMs.Length));
                    bw.Flush();
                    ms.Write(piMs.ToArray(), 0, (int)piMs.Length);
                }

                // ES Loop:
                ElementaryStream es;
                i = 0;

                while (true)
                {
                    es = _Source.GetEsInfo(i++);

                    if (es == null)
                        break;

                    ms.WriteByte((byte)es.StreamType);

                    bw.Write(IPAddress.HostToNetworkOrder((short)es.Pid));

                    using (MemoryStream esMs = new MemoryStream())
                    {
                        // ca_pmt_cmd_id -> descramble
                        esMs.WriteByte(1);

                        foreach (DescriptorBase esd in es)
                        {
                            if (esd.Tag == DescriptorTag.ConditionalAccess)
                                esd.Write(esMs);
                        }

                        if (esMs.Length > 1)
                        {
                            bw.Write(IPAddress.HostToNetworkOrder((short)esMs.Length));
                            ms.Write(esMs.ToArray(), 0, (int)esMs.Length);
                        }
                        else
                        {
                            bw.Write((short)0);
                        }
                    }
                }

                res = ms.ToArray();
            }

            return res;
        }
    }
}
