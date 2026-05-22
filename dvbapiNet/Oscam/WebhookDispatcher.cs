using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Fire-and-forget outgoing webhook delivery. Reads URLs from config key "webhook.url"
    /// (CSV list). On each event, posts JSON {event, ts, data} to each URL.
    /// Failures are silently swallowed — webhooks are not critical for operation.
    /// </summary>
    public static class WebhookDispatcher
    {
        public static void Post(string eventName, string dataJson)
        {
            string urls = "";
            try { Globals.Config.Get("webhook", "url", ref urls); } catch { }
            if (string.IsNullOrWhiteSpace(urls)) return;

            string payload = "{\"event\":\"" + eventName +
                             "\",\"ts\":\"" + DateTime.UtcNow.ToString("o") +
                             "\",\"data\":" + (dataJson ?? "null") + "}";

            foreach (var raw in urls.Split(','))
            {
                string url = raw.Trim();
                if (url.Length == 0) continue;
                FireAndForget(url, payload);
            }
        }

        private static void FireAndForget(string url, string payload)
        {
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.UserAgent = "dvbapiNET";
                    req.Timeout = 4000;
                    var body = Encoding.UTF8.GetBytes(payload);
                    req.ContentLength = body.Length;
                    using (var s = req.GetRequestStream())
                        s.Write(body, 0, body.Length);
                    using (var r = (HttpWebResponse)req.GetResponse()) { /* ignore body */ }
                }
                catch { }
            });
        }
    }
}
