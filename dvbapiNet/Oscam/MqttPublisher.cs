using dvbapiNet.Log;
using dvbapiNet.Log.Locale;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Minimal MQTT 3.1.1 publisher (QoS 0). Hand-rolled to avoid a NuGet dependency.
    /// Single TCP connection in a background thread, PINGREQ every 30s, auto-reconnect.
    ///
    /// Publishes plugin state on configurable topics + Home Assistant auto-discovery
    /// when enabled. Disabled by default — opt-in via config.
    /// </summary>
    public sealed class MqttPublisher : IDisposable
    {
        private const string cLogSection = "mqtt";

        private readonly string _Host;
        private readonly int _Port;
        private readonly string _User;
        private readonly string _Password;
        private readonly string _ClientId;
        private readonly string _BaseTopic;
        private readonly bool _HaDiscovery;

        private TcpClient _Tcp;
        private NetworkStream _Stream;
        private Thread _Thread;
        private volatile bool _Running;
        private volatile bool _Connected;
        private readonly ConcurrentQueue<(string Topic, byte[] Payload, bool Retain)> _Queue = new ConcurrentQueue<(string, byte[], bool)>();
        private DateTime _LastPing = DateTime.UtcNow;
        private bool _DiscoveryPublished;

        public bool IsConnected => _Connected;

        public MqttPublisher(string host, int port, string user, string password,
                             string clientId, string baseTopic, bool haDiscovery)
        {
            _Host = host;
            _Port = port;
            _User = user ?? "";
            _Password = password ?? "";
            _ClientId = string.IsNullOrEmpty(clientId) ? "dvbapinet-" + Guid.NewGuid().ToString("N").Substring(0, 8) : clientId;
            _BaseTopic = string.IsNullOrEmpty(baseTopic) ? "dvbapinet" : baseTopic.TrimEnd('/');
            _HaDiscovery = haDiscovery;
        }

        public void Start()
        {
            if (_Running) return;
            _Running = true;
            _Thread = new Thread(Loop) { IsBackground = true, Name = "dvbapiNetMqtt" };
            _Thread.Start();
        }

        public void Publish(string subTopic, string payload, bool retain = false)
        {
            if (!_Running) return;
            _Queue.Enqueue(($"{_BaseTopic}/{subTopic}", Encoding.UTF8.GetBytes(payload ?? ""), retain));
        }

        public void PublishRaw(string topic, string payload, bool retain = false)
        {
            if (!_Running) return;
            _Queue.Enqueue((topic, Encoding.UTF8.GetBytes(payload ?? ""), retain));
        }

        private void Loop()
        {
            int backoffMs = 1000;
            while (_Running)
            {
                try
                {
                    if (!_Connected)
                    {
                        TryConnect();
                        if (!_Connected) { Thread.Sleep(backoffMs); backoffMs = Math.Min(backoffMs * 2, 30000); continue; }
                        backoffMs = 1000;
                        PublishOnConnect();
                    }

                    // Drain publish queue
                    while (_Queue.TryDequeue(out var item))
                        WritePublish(item.Topic, item.Payload, item.Retain);

                    // Heartbeat (PINGREQ every 30s)
                    if ((DateTime.UtcNow - _LastPing).TotalSeconds >= 30)
                    {
                        WritePingReq();
                        _LastPing = DateTime.UtcNow;
                    }

                    Thread.Sleep(250);
                }
                catch (Exception ex)
                {
                    LogProvider.Exception(cLogSection, Message.SingleParam, ex);
                    Disconnect();
                    Thread.Sleep(backoffMs);
                }
            }
            Disconnect();
        }

        private void TryConnect()
        {
            try
            {
                _Tcp = new TcpClient();
                _Tcp.Connect(_Host, _Port);
                _Stream = _Tcp.GetStream();
                _Stream.ReadTimeout = 5000;
                _Stream.WriteTimeout = 5000;
                WriteConnect();
                ReadConnAck();
                _Connected = true;
                _LastPing = DateTime.UtcNow;
                _DiscoveryPublished = false;
                LogProvider.Add(DebugLevel.Info, cLogSection, Message.SingleParam, $"MQTT connected {_Host}:{_Port}");
            }
            catch
            {
                Disconnect();
            }
        }

        private void PublishOnConnect()
        {
            // availability = online (retained)
            WritePublish($"{_BaseTopic}/availability", Encoding.UTF8.GetBytes("online"), true);

            if (_HaDiscovery && !_DiscoveryPublished)
            {
                PublishHaDiscovery();
                _DiscoveryPublished = true;
            }
        }

        private void PublishHaDiscovery()
        {
            string device = "{\"identifiers\":[\"dvbapinet\"],\"name\":\"dvbapiNET\",\"sw_version\":\"" +
                            (Globals.Info ?? "") + "\",\"model\":\"DVB Oscam decryption plugin\",\"manufacturer\":\"dvbapiNET community\"}";

            void PubDisc(string objectId, string component, string name, string valueTemplate, string deviceClass = null, string unit = null)
            {
                var sb = new StringBuilder("{");
                sb.Append("\"name\":\"").Append(JsonEsc(name)).Append("\",");
                sb.Append("\"unique_id\":\"dvbapinet_").Append(objectId).Append("\",");
                sb.Append("\"state_topic\":\"").Append(_BaseTopic).Append("/status\",");
                sb.Append("\"value_template\":\"").Append(JsonEsc(valueTemplate)).Append("\",");
                sb.Append("\"availability_topic\":\"").Append(_BaseTopic).Append("/availability\",");
                if (deviceClass != null) sb.Append("\"device_class\":\"").Append(deviceClass).Append("\",");
                if (unit != null) sb.Append("\"unit_of_measurement\":\"").Append(unit).Append("\",");
                sb.Append("\"device\":").Append(device).Append("}");

                WritePublish($"homeassistant/{component}/dvbapinet/{objectId}/config",
                             Encoding.UTF8.GetBytes(sb.ToString()), true);
            }

            PubDisc("connected", "binary_sensor", "dvbapiNET Connected", "{% if value_json.connected %}ON{% else %}OFF{% endif %}", "connectivity");
            PubDisc("tuned", "binary_sensor", "dvbapiNET Tuned", "{% if value_json.tuned %}ON{% else %}OFF{% endif %}");
            PubDisc("sid", "sensor", "dvbapiNET Service ID", "{{ value_json.sid }}");
            PubDisc("ecm_latency", "sensor", "dvbapiNET ECM Latency", "{{ value_json.last_ms }}", null, "ms");
            PubDisc("cw_total", "sensor", "dvbapiNET CW Total", "{{ value_json.cw_total }}");
            PubDisc("ecm_total", "sensor", "dvbapiNET ECM Total", "{{ value_json.ecm_total }}");
        }

        private void Disconnect()
        {
            _Connected = false;
            try { _Stream?.Dispose(); } catch { }
            try { _Tcp?.Close(); } catch { }
            _Stream = null;
            _Tcp = null;
        }

        public void Dispose()
        {
            _Running = false;
            try { _Thread?.Join(2000); } catch { }
            Disconnect();
        }

        // ---- MQTT 3.1.1 wire format helpers ----

        private void WriteConnect()
        {
            var payload = new MemoryStream();
            WriteString(payload, "MQTT");          // protocol name
            payload.WriteByte(4);                   // protocol level (3.1.1)
            byte flags = 0x02;                      // clean session
            if (_User.Length > 0) flags |= 0x80;
            if (_Password.Length > 0) flags |= 0x40;
            // last will
            flags |= 0x04 | 0x20;                   // will flag + will retain
            payload.WriteByte(flags);
            WriteUInt16(payload, 60);               // keep alive

            WriteString(payload, _ClientId);
            WriteString(payload, $"{_BaseTopic}/availability");
            byte[] willPayload = Encoding.UTF8.GetBytes("offline");
            WriteUInt16(payload, (ushort)willPayload.Length);
            payload.Write(willPayload, 0, willPayload.Length);

            if (_User.Length > 0) WriteString(payload, _User);
            if (_Password.Length > 0) WriteString(payload, _Password);

            WritePacket(0x10, payload.ToArray());
        }

        private void ReadConnAck()
        {
            byte[] hdr = new byte[2];
            ReadFully(hdr, 0, 2);
            if (hdr[0] != 0x20) throw new Exception("MQTT: expected CONNACK");
            byte[] body = new byte[hdr[1]];
            ReadFully(body, 0, body.Length);
            if (body.Length < 2 || body[1] != 0) throw new Exception("MQTT: CONNACK refused, code=" + (body.Length >= 2 ? body[1] : (byte)0xFF));
        }

        private void WritePublish(string topic, byte[] payload, bool retain)
        {
            byte fixedHeader = 0x30; // PUBLISH QoS 0
            if (retain) fixedHeader |= 0x01;
            var body = new MemoryStream();
            WriteString(body, topic);
            body.Write(payload, 0, payload.Length);
            WritePacket(fixedHeader, body.ToArray());
        }

        private void WritePingReq() => WritePacket(0xC0, new byte[0]);

        private void WritePacket(byte fixedHeader, byte[] body)
        {
            if (_Stream == null) throw new Exception("MQTT: not connected");
            _Stream.WriteByte(fixedHeader);
            WriteVarInt(_Stream, body.Length);
            if (body.Length > 0) _Stream.Write(body, 0, body.Length);
            _Stream.Flush();
        }

        private static void WriteString(Stream s, string str)
        {
            byte[] b = Encoding.UTF8.GetBytes(str);
            s.WriteByte((byte)(b.Length >> 8));
            s.WriteByte((byte)b.Length);
            s.Write(b, 0, b.Length);
        }

        private static void WriteUInt16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void WriteVarInt(Stream s, int value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value > 0) b |= 0x80;
                s.WriteByte(b);
            } while (value > 0);
        }

        private void ReadFully(byte[] buf, int off, int len)
        {
            int total = 0;
            while (total < len)
            {
                int n = _Stream.Read(buf, off + total, len - total);
                if (n <= 0) throw new EndOfStreamException();
                total += n;
            }
        }

        private static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
