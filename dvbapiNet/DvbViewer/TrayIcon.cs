using dvbapiNet.Oscam;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace dvbapiNet.DvbViewer
{
    /// <summary>
    /// Windows system tray icon + native toast notifications.
    /// Lives in a dedicated STA thread with a hidden ApplicationContext message pump.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private NotifyIcon _Icon;
        private ContextMenuStrip _Menu;
        private System.Windows.Forms.Timer _Timer;
        private TrayState _LastState = TrayState.Unknown;
        private DateTime _LastDownNotify = DateTime.MinValue;
        private DateTime _LastUpNotify = DateTime.MinValue;
        private DateTime _LastTimeoutNotify = DateTime.MinValue;
        private long _LastSeenCwTotal = -1;
        private DateTime _LastCwGrowth = DateTime.UtcNow;

        private enum TrayState { Unknown, Disconnected, Idle, Tuned }

        public void Initialize()
        {
            _Icon = new NotifyIcon
            {
                Visible = true,
                Text = "dvbapiNET",
                Icon = MakeIcon(Color.Gray)
            };

            _Menu = new ContextMenuStrip();
            _Menu.Items.Add("Configuration…", null, (s, e) => OpenConfig());
            _Menu.Items.Add("Statut (Web)", null, (s, e) => OpenWeb());
            _Menu.Items.Add("Reconnecter", null, (s, e) => Reconnect());
            _Menu.Items.Add(new ToolStripSeparator());
            _Menu.Items.Add("Quitter le tray", null, (s, e) => Dispose());
            _Icon.ContextMenuStrip = _Menu;
            _Icon.DoubleClick += (s, e) => OpenConfig();

            _Timer = new System.Windows.Forms.Timer { Interval = 5000 };
            _Timer.Tick += (s, e) => UpdateState();
            _Timer.Start();
            UpdateState();
        }

        public void Notify(string title, string msg, ToolTipIcon icon)
        {
            try { _Icon?.ShowBalloonTip(5000, title, msg, icon); } catch { }
        }

        private void UpdateState()
        {
            try
            {
                var a = PickAdapter();
                var st = TrayState.Disconnected;
                if (a != null)
                {
                    if (a.IsTuned) st = TrayState.Tuned;
                    else if (a.HasDvbApiClient) st = TrayState.Idle;
                }

                if (st != _LastState)
                {
                    _Icon.Icon = MakeIcon(StateColor(st));
                    _Icon.Text = "dvbapiNET — " + StateLabel(st);

                    // notification on state transitions (with throttle)
                    if (_LastState != TrayState.Unknown)
                    {
                        if (st == TrayState.Disconnected && (DateTime.UtcNow - _LastDownNotify).TotalSeconds > 60)
                        {
                            Notify("dvbapiNET", "Oscam déconnecté.", ToolTipIcon.Warning);
                            _LastDownNotify = DateTime.UtcNow;
                            try { WebhookDispatcher.Post("oscam_down", "{}"); } catch { }
                        }
                        else if ((st == TrayState.Idle || st == TrayState.Tuned) && _LastState == TrayState.Disconnected
                                 && (DateTime.UtcNow - _LastUpNotify).TotalSeconds > 60)
                        {
                            Notify("dvbapiNET", "Oscam reconnecté.", ToolTipIcon.Info);
                            _LastUpNotify = DateTime.UtcNow;
                            try { WebhookDispatcher.Post("oscam_up", "{}"); } catch { }
                        }
                    }
                    _LastState = st;
                }

                // ECM timeout detection: tuned but CW counter hasn't grown in > 15s
                if (st == TrayState.Tuned)
                {
                    var snap = DecryptionMonitor.Instance.GetSnapshot();
                    if (snap.CwTotal != _LastSeenCwTotal)
                    {
                        _LastSeenCwTotal = snap.CwTotal;
                        _LastCwGrowth = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - _LastCwGrowth).TotalSeconds > 15
                             && (DateTime.UtcNow - _LastTimeoutNotify).TotalSeconds > 60)
                    {
                        Notify("dvbapiNET", "Aucun CW reçu depuis 15s. CAM ou ECM bloqué ?", ToolTipIcon.Warning);
                        _LastTimeoutNotify = DateTime.UtcNow;
                        try { WebhookDispatcher.Post("ecm_timeout", "{}"); } catch { }
                    }
                }
            }
            catch { }
        }

        private static DvbApiAdapter PickAdapter()
        {
            var m = MdApi.Plugin.Adapter;
            var d = Plugin.Adapter;
            if (m != null && m.IsTuned) return m;
            if (d != null && d.IsTuned) return d;
            if (m != null && m.HasDvbApiClient) return m;
            if (d != null && d.HasDvbApiClient) return d;
            return m ?? d;
        }

        private static Color StateColor(TrayState st)
        {
            switch (st)
            {
                case TrayState.Tuned: return Color.FromArgb(76, 175, 80);   // green
                case TrayState.Idle: return Color.FromArgb(255, 193, 7);    // amber
                case TrayState.Disconnected: return Color.FromArgb(244, 67, 54); // red
                default: return Color.Gray;
            }
        }

        private static string StateLabel(TrayState st)
        {
            switch (st)
            {
                case TrayState.Tuned: return "Chaîne tunée";
                case TrayState.Idle: return "Connecté, en attente";
                case TrayState.Disconnected: return "Déconnecté";
                default: return "État inconnu";
            }
        }

        // Generate a 16x16 colored disc icon dynamically (no resource needed).
        private static Icon MakeIcon(Color color)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(color))
                    g.FillEllipse(b, 1, 1, 14, 14);
                using (var p = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                    g.DrawEllipse(p, 1, 1, 14, 14);
                IntPtr h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        private void OpenConfig() => Plugin.OpenConfig();

        private void OpenWeb()
        {
            try
            {
                var w = Globals.WebInterface;
                if (w != null && w.IsRunning)
                    Process.Start($"http://127.0.0.1:{w.Port}/");
            }
            catch { }
        }

        private void Reconnect()
        {
            try { MdApi.Plugin.Adapter?.Tune(-1, -1, -1, -1); } catch { }
            try { Plugin.Adapter?.Tune(-1, -1, -1, -1); } catch { }
        }

        public void Dispose()
        {
            try { _Timer?.Stop(); } catch { }
            try { _Icon?.Dispose(); } catch { }
            try { _Menu?.Dispose(); } catch { }
        }
    }
}
