using dvbapiNet.Log;
using dvbapiNet.Utils;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace dvbapiNet
{
    /// <summary>
    /// Stellt Global benötigte Eigenschaften bereit
    /// </summary>
    public static class Globals
    {
        private const string cName = "dvbapiNET";
        private static Configuration _Config;
        private static DirectoryInfo _HomeDir;

        private static string _Info;
        private static string _PipeName;
        private static Oscam.WebInterface _WebInterface;
        public static Oscam.WebInterface WebInterface => _WebInterface;
        private static Oscam.MqttPublisher _Mqtt;
        public static Oscam.MqttPublisher Mqtt => _Mqtt;
        private static System.Threading.Timer _MqttStateTimer;

        public delegate void ExternalLog(string line);

        public static event ExternalLog LoggedLine;

        /// <summary>
        /// Gibt die Plugin-Konfiguration zurück
        /// </summary>
        public static Configuration Config
        {
            get
            {
                LoadConfig();

                return _Config;
            }
        }

        /// <summary>
        /// Gibt die PLugin-Information zurück, z.b. dvbapiNET x.y (#01234abcd)
        /// </summary>
        public static string Info
        {
            get
            {
                return _Info;
            }
        }

        /// <summary>
        /// Gibt den Pipe-Namen an, der für die Mitteilung der Internen Kommunikationsparameter dient.
        /// </summary>
        public static string PipeName
        {
            get
            {
                return _PipeName;
            }
        }

        /// <summary>
        /// Gibt die lokale Assembly zurück
        /// </summary>
        public static Assembly PluginAssembly
        {
            get
            {
                return typeof(Globals).Assembly;
            }
        }

        /// <summary>
        /// Gibt die FileVersionInfo dieses Plugins zurück.
        /// </summary>
        public static FileVersionInfo PluginInfo
        {
            get
            {
                return FileVersionInfo.GetVersionInfo(PluginAssembly.Location);
            }
        }

        /// <summary>
        /// Gibt das Konfigurationsverzeichnis für dieses Plugin an
        /// </summary>
        public static DirectoryInfo HomeDirectory
        {
            get
            {
                return _HomeDir;
            }
        }

        /// <summary>
        /// Initialisiert die globalen Variablen
        /// </summary>
        static Globals()
        {
            _HomeDir = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), cName));

            try
            {
                if (!_HomeDir.Exists)
                    _HomeDir.Create();
            }
            catch { }

            Assembly asm = PluginAssembly;
            Version ver = asm.GetName().Version;

            string hexBuild = ver.Build.ToString("x4") + ver.Revision.ToString("x4");

            _PipeName = $"{cName}.{hexBuild}";

            _Info = $"{cName} v{ver.Major}.{ver.Minor} 2026";

            try
            {
                int port = 8080;
                Config.Get("web", "port", 1, 65535, ref port);
                _WebInterface = new Oscam.WebInterface(port);
                _WebInterface.Start();
            }
            catch { }

            try
            {
                bool checkUpd = true;
                Config.Get("update", "check", ref checkUpd);
                if (checkUpd) Oscam.UpdateChecker.StartAsync();
            }
            catch { }

            try
            {
                bool mqttOn = false;
                Config.Get("mqtt", "enabled", ref mqttOn);
                if (mqttOn)
                {
                    string host = "127.0.0.1"; int port = 1883;
                    string user = "", pwd = "", baseT = "dvbapinet";
                    bool ha = true;
                    Config.Get("mqtt", "host", ref host);
                    Config.Get("mqtt", "port", 1, 65535, ref port);
                    Config.Get("mqtt", "user", ref user);
                    Config.Get("mqtt", "password", ref pwd);
                    Config.Get("mqtt", "topic", ref baseT);
                    Config.Get("mqtt", "ha_discovery", ref ha);

                    _Mqtt = new Oscam.MqttPublisher(host, port, user, pwd, null, baseT, ha);
                    _Mqtt.Start();

                    _MqttStateTimer = new System.Threading.Timer(_ => PublishMqttState(), null,
                        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                }
            }
            catch { }
        }

        private static void PublishMqttState()
        {
            if (_Mqtt == null || !_Mqtt.IsConnected) return;
            try
            {
                var a = dvbapiNet.MdApi.Plugin.Adapter ?? dvbapiNet.DvbViewer.Plugin.Adapter;
                if (a != null && a.HasDvbApiClient && !a.IsTuned)
                {
                    // pick the first one that has IsTuned if available
                    var m = dvbapiNet.MdApi.Plugin.Adapter;
                    var d = dvbapiNet.DvbViewer.Plugin.Adapter;
                    if (m != null && m.IsTuned) a = m;
                    else if (d != null && d.IsTuned) a = d;
                }
                var snap = dvbapiNet.Oscam.DecryptionMonitor.Instance.GetSnapshot();
                string json =
                    "{\"connected\":" + (a != null && a.HasDvbApiClient ? "true" : "false") +
                    ",\"tuned\":" + (a != null && a.IsTuned ? "true" : "false") +
                    ",\"sid\":" + (a?.CurrentService ?? 0) +
                    ",\"pmt_pid\":" + (a?.CurrentPmtPid ?? 0) +
                    ",\"cw_total\":" + snap.CwTotal +
                    ",\"ecm_total\":" + snap.EcmTotal +
                    ",\"last_ms\":" + snap.LastEcmMs +
                    ",\"avg_ms\":" + snap.AvgEcmMs + "}";
                _Mqtt.Publish("status", json, true);
            }
            catch { }
        }

        /// <summary>
        /// Gibt den Pfad für die Logdatei zurück.
        /// </summary>
        /// <returns></returns>
        public static FileInfo GetLogfile()
        {
            FileInfo f = new FileInfo(Path.Combine(_HomeDir.FullName, cName + ".log"));

            return f;
        }

        /// <summary>
        /// Lädt die Konfiguration, sofern vorhanden.
        /// </summary>
        private static void LoadConfig()
        {
            if (_Config != null)
                return;

            FileInfo f = new FileInfo(Path.Combine(_HomeDir.FullName, cName + ".ini"));

            IniFile cfg = new IniFile(f);

            IniFile def = new IniFile(null);

            /*
             * [dvbapi]
             * server=127.0.0.1
             * port=633
             * offset=0
             *
             * [log]
             * debug=0
             * pretty=1
             *
             * [debug]
             * streamdump=0
             */

            def.SetValue("dvbapi", "server", "127.0.0.1");
            def.SetValue("dvbapi", "port", "633");
            def.SetValue("dvbapi", "offset", "0");

            def.SetValue("log", "debug", "0");
            def.SetValue("log", "pretty", "1");
            def.SetValue("debug", "streamdump", "0");
            def.SetValue("web", "port", "8080");
            def.SetValue("web", "user", "");
            def.SetValue("web", "password", "");
            def.SetValue("dvbapi", "servers", "");
            def.SetValue("webhook", "url", "");
            def.SetValue("ui", "tray", "1");
            def.SetValue("update", "owner", "");
            def.SetValue("update", "repo", "dvbapiNET");
            def.SetValue("cache", "cw", "0");
            def.SetValue("mqtt", "enabled", "0");
            def.SetValue("mqtt", "host", "127.0.0.1");
            def.SetValue("mqtt", "port", "1883");
            def.SetValue("mqtt", "user", "");
            def.SetValue("mqtt", "password", "");
            def.SetValue("mqtt", "topic", "dvbapinet");
            def.SetValue("mqtt", "ha_discovery", "1");

            _Config = new Configuration(cfg, def);
        }

        /// <summary>
        /// Ruft das LoggedLine Ereignis auf, wird vom LogProvider verwendet
        /// </summary>
        /// <param name="s"></param>
        internal static void ExternalLogHandler(string s)
        {
            LoggedLine?.Invoke(s);
        }

        /// <summary>
        /// Pfad zur Konfigurationsdatei
        /// </summary>
        public static FileInfo ConfigFilePath
        {
            get { return new FileInfo(Path.Combine(_HomeDir.FullName, cName + ".ini")); }
        }

        /// <summary>
        /// Erzwingt Neuladen der Konfiguration beim nächsten Zugriff
        /// </summary>
        public static void ReloadConfig()
        {
            _Config = null;
        }

        public static void Dispose()
        {
            try { _MqttStateTimer?.Dispose(); } catch { }
            try { _Mqtt?.Dispose(); } catch { }
            try { _WebInterface?.Dispose(); } catch { }
            LogProvider.Dispose();
        }
    }
}
