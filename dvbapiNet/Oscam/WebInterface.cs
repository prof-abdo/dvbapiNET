using dvbapiNet.Log;
using dvbapiNet.Log.Locale;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Minimal HTTP server (TcpListener-based, no URL ACL required)
    /// serving a status page for the dvbapiNET plugin.
    /// </summary>
    public class WebInterface : IDisposable
    {
        private const string cLogSection = "webui";
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private readonly int _port;

        public int Port => _port;
        public bool IsRunning => _running;

        public WebInterface(int port)
        {
            _port = port;
        }

        public void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "dvbapiNetWeb" };
                _thread.Start();
                LogProvider.Add(DebugLevel.Info, cLogSection, Message.MdapiInitDone, $"web http://127.0.0.1:{_port}/");
            }
            catch (Exception ex)
            {
                LogProvider.Exception(cLogSection, Message.DvbvInitFailed, ex);
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch
                {
                    if (_running) Thread.Sleep(100);
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = 3000;
                    byte[] buf = new byte[2048];
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) return;

                    string req = Encoding.ASCII.GetString(buf, 0, n);
                    string path = ExtractPath(req);

                    // HTTP Basic Auth if configured
                    if (!CheckAuth(req))
                    {
                        SendStatus(stream, 401, "{\"error\":\"auth required\"}", "application/json",
                            "WWW-Authenticate: Basic realm=\"dvbapiNET\"\r\n");
                        return;
                    }

                    string body;
                    string contentType;
                    if (path.StartsWith("/api/status", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/reconnect", StringComparison.OrdinalIgnoreCase))
                    {
                        TriggerReconnect();
                        body = "{\"ok\":true}";
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/decrypt/reset", StringComparison.OrdinalIgnoreCase))
                    {
                        DecryptionMonitor.Instance.Reset();
                        body = "{\"ok\":true}";
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/decrypt/stats", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildDecryptStatsJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/ecm/recent", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildEcmRecentJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/ecm/export.csv", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildEcmCsv();
                        contentType = "text/csv; charset=utf-8";
                    }
                    else if (path.StartsWith("/api/ecm/latency-history", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildLatencyHistoryJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/heatmap/channels", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildHeatmapChannelsJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/heatmap/caid", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildHeatmapCaidJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/discovery/scan", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildDiscoveryJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/config", StringComparison.OrdinalIgnoreCase))
                    {
                        body = BuildConfigJson();
                        contentType = "application/json";
                    }
                    else if (path.StartsWith("/api/log/tail", StringComparison.OrdinalIgnoreCase))
                    {
                        int lines = 200;
                        int qIdx = path.IndexOf("n=");
                        if (qIdx >= 0) int.TryParse(path.Substring(qIdx + 2).Split('&')[0], out lines);
                        body = ReadLogTail(lines);
                        contentType = "text/plain; charset=utf-8";
                    }
                    else
                    {
                        body = BuildHtml();
                        contentType = "text/html; charset=utf-8";
                    }

                    byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                    string header =
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: " + contentType + "\r\n" +
                        "Content-Length: " + bodyBytes.Length + "\r\n" +
                        "Cache-Control: no-store\r\n" +
                        "Connection: close\r\n\r\n";
                    byte[] hdrBytes = Encoding.ASCII.GetBytes(header);
                    stream.Write(hdrBytes, 0, hdrBytes.Length);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }
            catch { }
        }

        private static bool CheckAuth(string req)
        {
            string user = "", pwd = "";
            try
            {
                Globals.Config.Get("web", "user", ref user);
                Globals.Config.Get("web", "password", ref pwd);
            }
            catch { }
            if (string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pwd)) return true;

            int idx = req.IndexOf("Authorization:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            int eol = req.IndexOf('\r', idx);
            if (eol < 0) eol = req.Length;
            string line = req.Substring(idx, eol - idx);
            int basicIdx = line.IndexOf("Basic ", StringComparison.OrdinalIgnoreCase);
            if (basicIdx < 0) return false;
            string b64 = line.Substring(basicIdx + 6).Trim();
            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                int sep = decoded.IndexOf(':');
                if (sep < 0) return false;
                return decoded.Substring(0, sep) == user && decoded.Substring(sep + 1) == pwd;
            }
            catch { return false; }
        }

        private static void SendStatus(NetworkStream s, int code, string body, string contentType, string extraHeaders = "")
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            string statusLine = code == 401 ? "401 Unauthorized" : code + " OK";
            string header =
                "HTTP/1.1 " + statusLine + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + bodyBytes.Length + "\r\n" +
                extraHeaders +
                "Connection: close\r\n\r\n";
            byte[] hdr = Encoding.ASCII.GetBytes(header);
            s.Write(hdr, 0, hdr.Length);
            s.Write(bodyBytes, 0, bodyBytes.Length);
        }

        private static string BuildLatencyHistoryJson()
        {
            var buckets = DecryptionMonitor.Instance.GetLatencyBuckets();
            var sb = new StringBuilder("[");
            for (int i = 0; i < buckets.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"t\":").Append(i).Append(",\"ms\":").Append(buckets[i]).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildConfigJson()
        {
            string srv = "127.0.0.1"; int port = 633, offset = 0, debug = 0, webPort = 8080;
            bool pretty = true, dump = false;
            string servers = "", whUrl = "";
            try { Globals.Config.Get("dvbapi", "server", ref srv); } catch { }
            try { Globals.Config.Get("dvbapi", "port", 1, 65535, ref port); } catch { }
            try { Globals.Config.Get("dvbapi", "offset", 0, 128, ref offset); } catch { }
            try { Globals.Config.Get("dvbapi", "servers", ref servers); } catch { }
            try { Globals.Config.Get("log", "debug", 0, int.MaxValue, ref debug); } catch { }
            try { Globals.Config.Get("log", "pretty", ref pretty); } catch { }
            try { Globals.Config.Get("debug", "streamdump", ref dump); } catch { }
            try { Globals.Config.Get("web", "port", 1, 65535, ref webPort); } catch { }
            try { Globals.Config.Get("webhook", "url", ref whUrl); } catch { }

            return "{" +
                "\"server\":\"" + JsonEscape(srv) + "\"," +
                "\"port\":" + port + "," +
                "\"offset\":" + offset + "," +
                "\"servers\":\"" + JsonEscape(servers) + "\"," +
                "\"debug\":" + debug + "," +
                "\"pretty\":" + (pretty ? "true" : "false") + "," +
                "\"streamdump\":" + (dump ? "true" : "false") + "," +
                "\"web_port\":" + webPort + "," +
                "\"webhook_url\":\"" + JsonEscape(whUrl) + "\"" +
                "}";
        }

        private static string ReadLogTail(int n)
        {
            try
            {
                var f = Globals.GetLogfile();
                if (!f.Exists) return "";
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    var lines = new System.Collections.Generic.Queue<string>(n);
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (lines.Count >= n) lines.Dequeue();
                        lines.Enqueue(line);
                    }
                    return string.Join("\n", lines.ToArray());
                }
            }
            catch (Exception ex) { return "ERROR: " + ex.Message; }
        }

        private static string ExtractPath(string req)
        {
            int sp1 = req.IndexOf(' ');
            if (sp1 < 0) return "/";
            int sp2 = req.IndexOf(' ', sp1 + 1);
            if (sp2 < 0) return "/";
            return req.Substring(sp1 + 1, sp2 - sp1 - 1);
        }

        private static DvbApiAdapter GetAdapter()
        {
            var m = MdApi.Plugin.Adapter;
            var d = DvbViewer.Plugin.Adapter;
            if (m != null && m.IsTuned) return m;
            if (d != null && d.IsTuned) return d;
            if (m != null && m.HasDvbApiClient) return m;
            if (d != null && d.HasDvbApiClient) return d;
            return m ?? d;
        }

        private static void TriggerReconnect()
        {
            try { GetAdapter()?.Tune(-1, -1, -1, -1); } catch { }
        }

        private static string BuildJson()
        {
            DvbApiAdapter a = GetAdapter();
            bool connected = a != null && a.HasDvbApiClient;
            bool tuned = a != null && a.IsTuned;
            int sid = a?.CurrentService ?? 0;
            int pmt = a?.CurrentPmtPid ?? 0;
            int tsid = a?.CurrentTransponder ?? 0;
            int nid = a?.CurrentNetwork ?? 0;
            string ver = Globals.Info?.Replace("\"", "\\\"") ?? "";

            return "{" +
                "\"version\":\"" + ver + "\"," +
                "\"connected\":" + (connected ? "true" : "false") + "," +
                "\"tuned\":" + (tuned ? "true" : "false") + "," +
                "\"sid\":" + sid + "," +
                "\"pmt_pid\":" + pmt + "," +
                "\"ts_id\":" + tsid + "," +
                "\"network_id\":" + nid +
                "}";
        }

        private static string BuildHtml()
        {
            DvbApiAdapter a = GetAdapter();
            bool connected = a != null && a.HasDvbApiClient;
            bool tuned = a != null && a.IsTuned;
            int sid = a?.CurrentService ?? 0;
            int pmt = a?.CurrentPmtPid ?? 0;
            int tsid = a?.CurrentTransponder ?? 0;
            int nid = a?.CurrentNetwork ?? 0;
            string ver = Globals.Info ?? "";

            string okCls(bool b) => b ? "ok" : "ko";
            string okTxt(bool b) => b ? "OUI" : "NON";

            var snap = DecryptionMonitor.Instance.GetSnapshot();

            var ecmRows = new StringBuilder();
            int shown = 0;
            for (int i = snap.RecentEcm.Length - 1; i >= 0 && shown < 20; i--, shown++)
            {
                var ev = snap.RecentEcm[i];
                int agoSec = (int)(DateTime.UtcNow - ev.When).TotalSeconds;
                ecmRows.Append("<tr><td>" + agoSec + "s</td><td>0x" + ev.CaId.ToString("X4") + "</td><td>0x" + ev.Pid.ToString("X4") + "</td><td>" + ev.EcmTimeMs + " ms</td><td>" + WebEscape(ev.Reader) + "</td><td>" + WebEscape(ev.Protocol) + "</td></tr>");
            }
            if (shown == 0) ecmRows.Append("<tr><td colspan='6' style='color:#666;text-align:center'>aucune ECM reçue</td></tr>");

            var upd = UpdateChecker.LatestRelease;
            string updBanner = upd == null ? "" :
                "<div style='background:#264f78;color:#fff;padding:10px 16px;border-radius:6px;margin-bottom:16px;max-width:720px'>" +
                "Nouvelle version <strong>" + WebEscape(upd.TagName) + "</strong> disponible — " +
                "<a href='" + WebEscape(upd.Url) + "' target='_blank' style='color:#9cdcfe'>voir sur GitHub</a></div>";

            return @"<!DOCTYPE html><html lang='fr'><head>
<meta charset='utf-8'>
<meta http-equiv='refresh' content='5'>
<title>dvbapiNET</title>
<style>
*{box-sizing:border-box}
body{font-family:'Segoe UI',sans-serif;background:#1e1e1e;color:#d4d4d4;margin:0;padding:24px;line-height:1.5}
h1{color:#4ec9b0;margin:0 0 24px;font-weight:300;font-size:28px}
h2{color:#9cdcfe;font-weight:400;font-size:16px;margin:24px 0 8px}
.card{background:#252526;border:1px solid #3e3e42;border-radius:8px;padding:20px;margin-bottom:16px;max-width:720px}
.row{display:flex;justify-content:space-between;padding:10px 0;border-bottom:1px solid #333}
.row:last-child{border:0}
.label{color:#9cdcfe}
.value{color:#dcdcaa;font-family:Consolas,monospace}
.ok{color:#4ec9b0}.ko{color:#f48771}
.actions{margin-top:8px;max-width:720px}
button{background:#0e639c;color:#fff;border:0;padding:8px 16px;border-radius:4px;cursor:pointer;font-size:14px;margin-right:8px}
button:hover{background:#1177bb}
.foot{color:#666;font-size:12px;margin-top:16px}
a{color:#569cd6}
.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}
.tile{background:#1e1e1e;border:1px solid #333;border-radius:6px;padding:12px;text-align:center}
.tile .n{font-size:24px;color:#dcdcaa;font-family:Consolas,monospace}
.tile .t{color:#9cdcfe;font-size:12px;text-transform:uppercase;letter-spacing:.5px}
table{width:100%;border-collapse:collapse;font-family:Consolas,monospace;font-size:13px}
th{text-align:left;color:#9cdcfe;border-bottom:1px solid #444;padding:6px}
td{padding:5px 6px;border-bottom:1px solid #2d2d2d}
.discovery-result{background:#252526;border:1px solid #3e3e42;border-radius:8px;padding:16px;margin-top:8px;max-width:720px;display:none}
.discovery-result.show{display:block}
.discovery-result table{margin-top:8px}
</style></head><body>
<h1>dvbapiNET</h1>
" + updBanner + @"
<div class='card'>
<div class='row'><span class='label'>Version</span><span class='value'>" + ver + @"</span></div>
<div class='row'><span class='label'>Connecté à Oscam</span><span class='value " + okCls(connected) + "'>" + okTxt(connected) + @"</span></div>
<div class='row'><span class='label'>Chaîne tunée</span><span class='value " + okCls(tuned) + "'>" + okTxt(tuned) + @"</span></div>
<div class='row'><span class='label'>Service ID</span><span class='value'>" + sid + @"</span></div>
<div class='row'><span class='label'>PMT PID</span><span class='value'>0x" + pmt.ToString("X4") + @"</span></div>
<div class='row'><span class='label'>Transport Stream</span><span class='value'>" + tsid + @"</span></div>
<div class='row'><span class='label'>Network ID</span><span class='value'>" + nid + @"</span></div>
</div>

<h2>Décryptage</h2>
<div class='card'>
<canvas id='lat' width='600' height='100' style='width:100%;height:100px;background:#1e1e1e;border:1px solid #333;border-radius:4px;margin-bottom:16px'></canvas>
<div class='grid'>
<div class='tile'><div class='n'>" + snap.CwTotal + @"</div><div class='t'>CW total</div></div>
<div class='tile'><div class='n'>" + snap.CwEven + @"</div><div class='t'>CW even</div></div>
<div class='tile'><div class='n'>" + snap.CwOdd + @"</div><div class='t'>CW odd</div></div>
<div class='tile'><div class='n'>" + snap.EcmTotal + @"</div><div class='t'>ECM total</div></div>
<div class='tile'><div class='n'>" + snap.LastEcmMs + @" <span style='font-size:12px;color:#9cdcfe'>ms</span></div><div class='t'>last ECM</div></div>
<div class='tile'><div class='n'>" + snap.AvgEcmMs + @" <span style='font-size:12px;color:#9cdcfe'>ms</span></div><div class='t'>avg ECM</div></div>
<div class='tile'><div class='n'>" + snap.MaxEcmMs + @" <span style='font-size:12px;color:#9cdcfe'>ms</span></div><div class='t'>max ECM</div></div>
<div class='tile'><div class='n' style='color:#569cd6'>" + (snap.LastCwAt == DateTime.MinValue ? "—" : ((int)(DateTime.UtcNow - snap.LastCwAt).TotalSeconds + "s")) + @"</div><div class='t'>last CW</div></div>
</div>
</div>

<h2>ECM récentes</h2>
<div class='card'>
<table><thead><tr><th>Quand</th><th>CAID</th><th>PID</th><th>Latence</th><th>Reader</th><th>Protocole</th></tr></thead><tbody>" + ecmRows.ToString() + @"</tbody></table>
</div>

<div class='actions'>
<button onclick=""fetch('/api/reconnect').then(()=>location.reload())"">Reconnecter</button>
<button onclick=""if(confirm('Reset stats?'))fetch('/api/decrypt/reset').then(()=>location.reload())"">Reset stats</button>
<button onclick=""runDiscovery()"">Détecter serveurs</button>
<a href='/api/status' style='margin-left:8px'>JSON</a>
</div>

<div id='disc' class='discovery-result'></div>

<p class='foot'>Auto-refresh 5s</p>

<script>
fetch('/api/ecm/latency-history').then(r=>r.json()).then(buckets=>{
  var c=document.getElementById('lat');if(!c)return;
  var ctx=c.getContext('2d');var w=c.width,h=c.height;
  ctx.clearRect(0,0,w,h);
  ctx.strokeStyle='#333';ctx.beginPath();ctx.moveTo(0,h/2);ctx.lineTo(w,h/2);ctx.stroke();
  var max=Math.max.apply(null,buckets.map(b=>b.ms).concat([100]));
  var bw=w/buckets.length;
  buckets.forEach((b,i)=>{
    var x=w-(i+1)*bw;
    var y=h-(b.ms/max)*h;
    var color=b.ms<500?'#4ec9b0':b.ms<1500?'#dcdcaa':'#f48771';
    ctx.fillStyle=color;ctx.fillRect(x,y,bw-1,h-y);
  });
  ctx.fillStyle='#666';ctx.font='10px Consolas';
  ctx.fillText('max '+max+'ms',4,12);ctx.fillText('60 min →',w-60,h-2);
}).catch(()=>{});
function runDiscovery(){
  var el=document.getElementById('disc');
  el.classList.add('show');
  el.innerHTML='<em>Scan en cours…</em>';
  fetch('/api/discovery/scan').then(r=>r.json()).then(list=>{
    if(!list.length){el.innerHTML='<em>Aucun serveur trouvé.</em>';return;}
    var h='<strong>'+list.length+' serveur(s) :</strong><table><thead><tr><th>IP</th><th>Port</th><th>Version</th></tr></thead><tbody>';
    list.forEach(s=>{h+='<tr><td>'+s.ip+'</td><td>'+s.port+'</td><td>'+s.version+'</td></tr>';});
    el.innerHTML=h+'</tbody></table>';
  }).catch(e=>{el.innerHTML='<em>Erreur : '+e+'</em>';});
}
</script>
</body></html>";
        }

        private static string WebEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string BuildDecryptStatsJson()
        {
            var s = DecryptionMonitor.Instance.GetSnapshot();
            var cc = CwCache.Instance;
            return "{" +
                "\"cw_total\":" + s.CwTotal + "," +
                "\"cw_even\":" + s.CwEven + "," +
                "\"cw_odd\":" + s.CwOdd + "," +
                "\"ecm_total\":" + s.EcmTotal + "," +
                "\"last_ms\":" + s.LastEcmMs + "," +
                "\"avg_ms\":" + s.AvgEcmMs + "," +
                "\"max_ms\":" + s.MaxEcmMs + "," +
                "\"last_cw_iso\":\"" + (s.LastCwAt == DateTime.MinValue ? "" : s.LastCwAt.ToString("o")) + "\"," +
                "\"cw_cache\":{\"enabled\":" + (cc.Enabled ? "true" : "false") +
                ",\"hits\":" + cc.Hits + ",\"misses\":" + cc.Misses +
                ",\"stores\":" + cc.Stores + ",\"size\":" + cc.Size + "}" +
                "}";
        }

        private static string BuildHeatmapChannelsJson()
        {
            var top = DecryptionMonitor.Instance.GetTopChannels(10);
            var sb = new StringBuilder("[");
            for (int i = 0; i < top.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"sid\":").Append(top[i].Key)
                  .Append(",\"watch_seconds\":").Append(top[i].Value).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildHeatmapCaidJson()
        {
            var stats = DecryptionMonitor.Instance.GetCaidEcm();
            var sb = new StringBuilder("[");
            for (int i = 0; i < stats.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"caid\":\"0x").Append(stats[i].Key.ToString("X4"))
                  .Append("\",\"ecm_count\":").Append(stats[i].Value).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildEcmCsv()
        {
            var s = DecryptionMonitor.Instance.GetSnapshot();
            var sb = new StringBuilder("time,caid,pid,ecm_ms,reader,protocol,hops\r\n");
            for (int i = s.RecentEcm.Length - 1; i >= 0; i--)
            {
                var ev = s.RecentEcm[i];
                sb.Append(ev.When.ToString("o")).Append(',')
                  .Append("0x").Append(ev.CaId.ToString("X4")).Append(',')
                  .Append("0x").Append(ev.Pid.ToString("X4")).Append(',')
                  .Append(ev.EcmTimeMs).Append(',')
                  .Append(CsvEscape(ev.Reader)).Append(',')
                  .Append(CsvEscape(ev.Protocol)).Append(',')
                  .Append(ev.Hops).Append("\r\n");
            }
            return sb.ToString();
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string BuildEcmRecentJson()
        {
            var s = DecryptionMonitor.Instance.GetSnapshot();
            var sb = new StringBuilder("[");
            for (int i = 0; i < s.RecentEcm.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var ev = s.RecentEcm[i];
                sb.Append("{\"time\":\"").Append(ev.When.ToString("o"))
                  .Append("\",\"caid\":").Append(ev.CaId)
                  .Append(",\"pid\":").Append(ev.Pid)
                  .Append(",\"ecm_ms\":").Append(ev.EcmTimeMs)
                  .Append(",\"reader\":\"").Append(JsonEscape(ev.Reader))
                  .Append("\",\"protocol\":\"").Append(JsonEscape(ev.Protocol))
                  .Append("\",\"hops\":").Append(ev.Hops).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string BuildDiscoveryJson()
        {
            int port = 633;
            try { Globals.Config.Get("dvbapi", "port", 1, 65535, ref port); } catch { }

            List<OscamDiscovery.DiscoveredServer> found;
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var t = OscamDiscovery.ScanLocalSubnetsAsync(port, 250, cts.Token);
                    found = t.GetAwaiter().GetResult();
                }
            }
            catch { found = new List<OscamDiscovery.DiscoveredServer>(); }

            var sb = new StringBuilder("[");
            for (int i = 0; i < found.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = found[i];
                sb.Append("{\"ip\":\"").Append(s.Ip)
                  .Append("\",\"port\":").Append(s.Port)
                  .Append(",\"version\":\"").Append(JsonEscape(s.Version))
                  .Append("\",\"proto\":").Append(s.ProtocolVersion).Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
        }
    }
}
