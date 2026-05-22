using dvbapiNet.Log;
using dvbapiNet.Log.Locale;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Checks the latest release on GitHub once a day. Notification only — does not download.
    /// Result exposed via Globals.LatestRelease (null if no update available or check failed).
    /// </summary>
    public static class UpdateChecker
    {
        private const string cLogSection = "updchk";
        private const string cDefaultRepo = "dvbapiNET"; // overridable via config update.repo
        private const string cDefaultOwner = "";          // empty until set via config update.owner
        private const int cCacheTtlHours = 24;

        public sealed class ReleaseInfo
        {
            public string TagName;
            public string Url;
            public DateTime PublishedAt;
        }

        public static ReleaseInfo LatestRelease { get; private set; }

        public static void StartAsync()
        {
            Task.Run(() =>
            {
                try { CheckOnce(); }
                catch (Exception ex) { LogProvider.Exception(cLogSection, Message.DvbvInitFailed, ex); }
            });
        }

        private static void CheckOnce()
        {
            string owner = cDefaultOwner;
            string repo = cDefaultRepo;
            try
            {
                Globals.Config.Get("update", "owner", ref owner);
                Globals.Config.Get("update", "repo", ref repo);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
                return; // updater disabled until owner/repo configured

            var cacheFile = new FileInfo(Path.Combine(Globals.HomeDirectory.FullName, "update.cache.json"));
            if (cacheFile.Exists && (DateTime.UtcNow - cacheFile.LastWriteTimeUtc).TotalHours < cCacheTtlHours)
            {
                LatestRelease = TryReadCache(cacheFile);
                return;
            }

            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            string json;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "dvbapiNET-update-check";
                req.Timeout = 8000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
            }
            catch
            {
                return;
            }

            string tag = ExtractJsonString(json, "tag_name");
            string htmlUrl = ExtractJsonString(json, "html_url");
            string published = ExtractJsonString(json, "published_at");

            if (string.IsNullOrEmpty(tag)) return;

            if (IsNewerThanCurrent(tag))
            {
                LatestRelease = new ReleaseInfo
                {
                    TagName = tag,
                    Url = htmlUrl,
                    PublishedAt = DateTime.TryParse(published, out var d) ? d : DateTime.UtcNow
                };
                try { File.WriteAllText(cacheFile.FullName, json); } catch { }
            }
            else
            {
                try { File.WriteAllText(cacheFile.FullName, "{}"); } catch { }
            }
        }

        private static ReleaseInfo TryReadCache(FileInfo f)
        {
            try
            {
                string json = File.ReadAllText(f.FullName);
                string tag = ExtractJsonString(json, "tag_name");
                if (string.IsNullOrEmpty(tag)) return null;
                if (!IsNewerThanCurrent(tag)) return null;
                return new ReleaseInfo
                {
                    TagName = tag,
                    Url = ExtractJsonString(json, "html_url"),
                    PublishedAt = DateTime.TryParse(ExtractJsonString(json, "published_at"), out var d) ? d : DateTime.UtcNow
                };
            }
            catch { return null; }
        }

        private static bool IsNewerThanCurrent(string tagName)
        {
            // tag like "v2.1.0" or "2.1.0"
            string clean = tagName.TrimStart('v', 'V').Trim();
            if (!Version.TryParse(clean, out var remote)) return false;
            Version local = Globals.PluginAssembly.GetName().Version;
            // compare only major.minor for our format
            return remote.Major > local.Major
                || (remote.Major == local.Major && remote.Minor > local.Minor);
        }

        // Minimal JSON string extractor — avoids pulling in a JSON library.
        private static string ExtractJsonString(string json, string key)
        {
            if (json == null) return null;
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;
            i = json.IndexOf('"', i);
            if (i < 0) return null;
            int end = i + 1;
            var sb = new System.Text.StringBuilder();
            while (end < json.Length)
            {
                char c = json[end];
                if (c == '\\' && end + 1 < json.Length) { sb.Append(json[end + 1]); end += 2; continue; }
                if (c == '"') break;
                sb.Append(c);
                end++;
            }
            return sb.ToString();
        }
    }
}
