using dvbapiNet.Oscam.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Scan local IPv4 subnets for Oscam servers speaking the dvbapi protocol.
    /// Confirms a candidate by sending DvbApiClientInfo and expecting ServerInfo (0xFFFF0002).
    /// </summary>
    public static class OscamDiscovery
    {
        private const int cBatchSize = 32;
        private const int cConfirmTimeoutMs = 1200;

        public sealed class DiscoveredServer
        {
            public IPAddress Ip;
            public int Port;
            public string Version;
            public int ProtocolVersion;

            public override string ToString()
            {
                return $"{Ip}:{Port} ({Version})";
            }
        }

        public static async Task<List<DiscoveredServer>> ScanLocalSubnetsAsync(
            int port, int perHostTimeoutMs, CancellationToken ct)
        {
            var results = new Dictionary<string, DiscoveredServer>();
            var hosts = EnumerateCandidateHosts();

            for (int i = 0; i < hosts.Count; i += cBatchSize)
            {
                if (ct.IsCancellationRequested) break;
                int end = Math.Min(i + cBatchSize, hosts.Count);

                var batch = new List<Task<DiscoveredServer>>(end - i);
                for (int j = i; j < end; j++)
                    batch.Add(ProbeHostAsync(hosts[j], port, perHostTimeoutMs, ct));

                var done = await Task.WhenAll(batch).ConfigureAwait(false);
                foreach (var s in done)
                {
                    if (s == null) continue;
                    string key = s.Ip + ":" + s.Port;
                    if (!results.ContainsKey(key))
                        results[key] = s;
                }
            }

            return new List<DiscoveredServer>(results.Values);
        }

        private static List<IPAddress> EnumerateCandidateHosts()
        {
            var hosts = new List<IPAddress>();
            var seen = new HashSet<string>();

            // always include loopback
            if (seen.Add("127.0.0.1")) hosts.Add(IPAddress.Loopback);

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var ua in ipProps.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.GetAddressBytes();
                        // Build /24 around this IP
                        for (int last = 1; last <= 254; last++)
                        {
                            var bytes = new byte[] { ip[0], ip[1], ip[2], (byte)last };
                            var addr = new IPAddress(bytes);
                            string key = addr.ToString();
                            if (seen.Add(key)) hosts.Add(addr);
                        }
                    }
                }
            }
            catch { }

            return hosts;
        }

        private static async Task<DiscoveredServer> ProbeHostAsync(
            IPAddress ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    var connectTask = tcp.ConnectAsync(ip, port);
                    var timeoutTask = Task.Delay(timeoutMs, ct);
                    var done = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (done != connectTask || !tcp.Connected) return null;

                    using (var stream = tcp.GetStream())
                    {
                        stream.ReadTimeout = cConfirmTimeoutMs;
                        stream.WriteTimeout = cConfirmTimeoutMs;

                        var hello = new DvbApiClientInfo(2, Globals.Info).GetData();
                        await stream.WriteAsync(hello, 0, hello.Length, ct).ConfigureAwait(false);

                        // Expect ServerInfo: 4 bytes cmd (0xFFFF0002) + 2 bytes proto + 1 byte strlen + str
                        var buf = new byte[512];
                        var readTask = stream.ReadAsync(buf, 0, buf.Length, ct);
                        var readTimeout = Task.Delay(cConfirmTimeoutMs, ct);
                        var rDone = await Task.WhenAny(readTask, readTimeout).ConfigureAwait(false);
                        if (rDone != readTask) return null;

                        int n = readTask.Result;
                        if (n < 7) return null;

                        uint cmd = (uint)((buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3]);
                        if (cmd != (uint)DvbApiCommand.ServerInfo) return null;

                        int proto = (buf[4] << 8) | buf[5];
                        int slen = buf[6];
                        if (n < 7 + slen) slen = Math.Max(0, n - 7);
                        string ver = Encoding.UTF8.GetString(buf, 7, slen);

                        return new DiscoveredServer
                        {
                            Ip = ip,
                            Port = port,
                            Version = ver,
                            ProtocolVersion = proto
                        };
                    }
                }
            }
            catch { return null; }
        }
    }
}
