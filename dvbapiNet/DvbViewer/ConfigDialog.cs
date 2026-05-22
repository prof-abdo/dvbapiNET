using dvbapiNet.Utils;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace dvbapiNet.DvbViewer
{
    internal class ConfigDialog : Form
    {
        // Configuration tab
        private TextBox _TxtHost;
        private TextBox _TxtPort;
        private TextBox _TxtOffset;
        private TextBox _TxtDebug;
        private TextBox _TxtWebPort;
        private TextBox _TxtWebUser;
        private TextBox _TxtWebPwd;
        private TextBox _TxtServers;
        private TextBox _TxtWebhookUrl;
        private CheckBox _ChkPretty;
        private CheckBox _ChkDump;
        private CheckBox _ChkDark;
        private CheckBox _ChkCwCache;

        // Status tab
        private Label _LblConn;
        private Label _LblTuned;
        private Label _LblService;
        private Label _LblPmt;
        private LinkLabel _LnkWeb;
        private Timer _RefreshTimer;

        // Debug tab
        private Label _LblCwTotal;
        private Label _LblCwEven;
        private Label _LblCwOdd;
        private Label _LblEcmTotal;
        private Label _LblLat;
        private ListView _LstEcm;

        // Common
        private Button _BtnOk;
        private Button _BtnCancel;
        private Label _LblStatus;

        // Theme
        private static readonly Color _DarkBg = Color.FromArgb(30, 30, 30);
        private static readonly Color _DarkBg2 = Color.FromArgb(45, 45, 48);
        private static readonly Color _DarkFg = Color.FromArgb(220, 220, 220);
        private static readonly Color _DarkAccent = Color.FromArgb(78, 201, 176);
        private bool _DarkMode;

        public ConfigDialog()
        {
            try { Globals.Config.Get("ui", "dark", ref _DarkMode); } catch { }
            BuildUi();
            LoadValues();
            if (_DarkMode) ApplyDarkTheme(this);
            RefreshStatus(null, null);
        }

        private static void ApplyDarkTheme(Control root)
        {
            if (root is Form) { root.BackColor = _DarkBg; root.ForeColor = _DarkFg; }
            foreach (Control c in root.Controls)
            {
                if (c is GroupBox)
                {
                    c.BackColor = _DarkBg; c.ForeColor = _DarkAccent;
                }
                else if (c is TabControl tc)
                {
                    foreach (TabPage tp in tc.TabPages)
                    {
                        tp.BackColor = _DarkBg; tp.ForeColor = _DarkFg;
                        ApplyDarkTheme(tp);
                    }
                    continue;
                }
                else if (c is TextBox)
                {
                    c.BackColor = _DarkBg2; c.ForeColor = _DarkFg;
                    (c as TextBox).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ListView lv)
                {
                    lv.BackColor = _DarkBg2; lv.ForeColor = _DarkFg;
                }
                else if (c is Button)
                {
                    c.BackColor = Color.FromArgb(60, 60, 65); c.ForeColor = _DarkFg;
                    (c as Button).FlatStyle = FlatStyle.Flat;
                    (c as Button).FlatAppearance.BorderColor = Color.FromArgb(80, 80, 85);
                }
                else if (c is CheckBox || c is Label)
                {
                    c.BackColor = Color.Transparent; c.ForeColor = _DarkFg;
                }
                else if (c is LinkLabel ll)
                {
                    ll.BackColor = Color.Transparent;
                    ll.LinkColor = Color.FromArgb(86, 156, 214);
                    ll.ActiveLinkColor = _DarkAccent;
                }
                if (c.HasChildren) ApplyDarkTheme(c);
            }
        }

        private void BuildUi()
        {
            Text = "dvbapiNET";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 520);
            Font = new Font("Segoe UI", 9f);

            var tabs = new TabControl { Left = 8, Top = 8, Width = 464, Height = 460 };

            var tabConfig = new TabPage("Configuration");
            BuildConfigTab(tabConfig);
            tabs.TabPages.Add(tabConfig);

            var tabActions = new TabPage("Statut / Actions");
            BuildActionsTab(tabActions);
            tabs.TabPages.Add(tabActions);

            var tabDebug = new TabPage("Debug");
            BuildDebugTab(tabDebug);
            tabs.TabPages.Add(tabDebug);

            var tabAbout = new TabPage("À propos");
            BuildAboutTab(tabAbout);
            tabs.TabPages.Add(tabAbout);

            Controls.Add(tabs);

            _LblStatus = new Label { Left = 12, Top = 472, Width = 280, Height = 20, ForeColor = Color.DarkRed };
            Controls.Add(_LblStatus);

            _BtnOk = new Button { Text = "OK", Left = 304, Top = 488, Width = 80, Height = 26 };
            _BtnOk.Click += BtnOk_Click;
            _BtnCancel = new Button { Text = "Annuler", Left = 392, Top = 488, Width = 80, Height = 26 };
            _BtnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_BtnOk);
            Controls.Add(_BtnCancel);
            AcceptButton = _BtnOk;
            CancelButton = _BtnCancel;

            _RefreshTimer = new Timer { Interval = 1500 };
            _RefreshTimer.Tick += RefreshStatus;
            _RefreshTimer.Start();
            FormClosed += (s, e) => _RefreshTimer.Stop();
        }

        private void BuildConfigTab(TabPage p)
        {
            int labelW = 110;
            int ctrlX = 130;
            int rowH = 28;

            var grpOscam = new GroupBox { Text = "Oscam", Left = 8, Top = 8, Width = 440, Height = 110 };
            grpOscam.Controls.Add(MakeLabel("Serveur :", labelW, 22));
            _TxtHost = new TextBox { Left = ctrlX, Top = 20, Width = 200 };
            grpOscam.Controls.Add(_TxtHost);

            var btnDetect = new Button { Text = "Détecter…", Left = ctrlX + 210, Top = 18, Width = 90, Height = 24 };
            btnDetect.Click += BtnDetect_Click;
            grpOscam.Controls.Add(btnDetect);

            grpOscam.Controls.Add(MakeLabel("Port :", labelW, 22 + rowH));
            _TxtPort = new TextBox { Left = ctrlX, Top = 20 + rowH, Width = 60 };
            grpOscam.Controls.Add(_TxtPort);

            grpOscam.Controls.Add(MakeLabel("Offset adapt. :", labelW, 22 + rowH * 2));
            _TxtOffset = new TextBox { Left = ctrlX, Top = 20 + rowH * 2, Width = 60 };
            grpOscam.Controls.Add(_TxtOffset);
            p.Controls.Add(grpOscam);

            var grpLog = new GroupBox { Text = "Journalisation", Left = 8, Top = 124, Width = 440, Height = 110 };
            grpLog.Controls.Add(MakeLabel("Niveau debug :", labelW, 22));
            _TxtDebug = new TextBox { Left = ctrlX, Top = 20, Width = 60 };
            grpLog.Controls.Add(_TxtDebug);
            grpLog.Controls.Add(new Label { Text = "(0 = désactivé)", Left = ctrlX + 70, Top = 22, AutoSize = true });

            _ChkPretty = new CheckBox { Text = "Logs formatés (pretty)", Left = ctrlX, Top = 20 + rowH, AutoSize = true };
            grpLog.Controls.Add(_ChkPretty);
            _ChkDump = new CheckBox { Text = "Dump stream TS", Left = ctrlX, Top = 20 + rowH * 2, AutoSize = true };
            grpLog.Controls.Add(_ChkDump);
            _ChkDark = new CheckBox { Text = "Dark mode (redémarrage requis)", Left = ctrlX + 130, Top = 20 + rowH * 2, AutoSize = true };
            grpLog.Controls.Add(_ChkDark);
            p.Controls.Add(grpLog);

            var grpWeb = new GroupBox { Text = "Interface web", Left = 8, Top = 240, Width = 440, Height = 120 };
            grpWeb.Controls.Add(MakeLabel("Port :", labelW, 18));
            _TxtWebPort = new TextBox { Left = ctrlX, Top = 16, Width = 60 };
            grpWeb.Controls.Add(_TxtWebPort);
            grpWeb.Controls.Add(new Label { Text = "(redémarrage requis)", Left = ctrlX + 70, Top = 18, AutoSize = true, ForeColor = Color.Gray });

            grpWeb.Controls.Add(MakeLabel("User (auth) :", labelW, 18 + rowH));
            _TxtWebUser = new TextBox { Left = ctrlX, Top = 16 + rowH, Width = 120 };
            grpWeb.Controls.Add(_TxtWebUser);

            grpWeb.Controls.Add(MakeLabel("Password :", labelW, 18 + rowH * 2));
            _TxtWebPwd = new TextBox { Left = ctrlX, Top = 16 + rowH * 2, Width = 120, UseSystemPasswordChar = true };
            grpWeb.Controls.Add(_TxtWebPwd);
            grpWeb.Controls.Add(new Label { Text = "(vide = pas d'auth)", Left = ctrlX + 130, Top = 18 + rowH * 2, AutoSize = true, ForeColor = Color.Gray });
            p.Controls.Add(grpWeb);

            var grpAdv = new GroupBox { Text = "Avancé", Left = 8, Top = 366, Width = 440, Height = 88 };
            grpAdv.Controls.Add(MakeLabel("Serveurs failover :", labelW, 18));
            _TxtServers = new TextBox { Left = ctrlX, Top = 16, Width = 290 };
            grpAdv.Controls.Add(_TxtServers);
            grpAdv.Controls.Add(new Label { Text = "host:port,host:port", Left = ctrlX, Top = 30, AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f) });
            grpAdv.Controls.Add(MakeLabel("Webhook URL :", labelW, 48));
            _TxtWebhookUrl = new TextBox { Left = ctrlX, Top = 46, Width = 290 };
            grpAdv.Controls.Add(_TxtWebhookUrl);
            _ChkCwCache = new CheckBox { Text = "CW cache (zapping rapide — expérimental)", Left = ctrlX, Top = 70, AutoSize = true };
            grpAdv.Controls.Add(_ChkCwCache);
            grpAdv.Height = 105;
            p.Controls.Add(grpAdv);
        }

        private void BuildActionsTab(TabPage p)
        {
            var grpStat = new GroupBox { Text = "Statut", Left = 8, Top = 8, Width = 440, Height = 160 };
            int y = 22;
            grpStat.Controls.Add(new Label { Text = "Connexion Oscam :", Left = 12, Top = y, AutoSize = true });
            _LblConn = new Label { Left = 160, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStat.Controls.Add(_LblConn);
            y += 24;
            grpStat.Controls.Add(new Label { Text = "Chaîne tunée :", Left = 12, Top = y, AutoSize = true });
            _LblTuned = new Label { Left = 160, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStat.Controls.Add(_LblTuned);
            y += 24;
            grpStat.Controls.Add(new Label { Text = "Service ID :", Left = 12, Top = y, AutoSize = true });
            _LblService = new Label { Left = 160, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStat.Controls.Add(_LblService);
            y += 24;
            grpStat.Controls.Add(new Label { Text = "PMT PID :", Left = 12, Top = y, AutoSize = true });
            _LblPmt = new Label { Left = 160, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStat.Controls.Add(_LblPmt);
            y += 24;
            grpStat.Controls.Add(new Label { Text = "Interface web :", Left = 12, Top = y, AutoSize = true });
            _LnkWeb = new LinkLabel { Left = 160, Top = y, AutoSize = true };
            _LnkWeb.LinkClicked += (s, e) => { try { Process.Start(_LnkWeb.Text); } catch { } };
            grpStat.Controls.Add(_LnkWeb);
            p.Controls.Add(grpStat);

            var grpAct = new GroupBox { Text = "Actions", Left = 8, Top = 176, Width = 440, Height = 110 };
            var bRec = new Button { Text = "Reconnecter", Left = 12, Top = 24, Width = 160, Height = 28 };
            bRec.Click += (s, e) =>
            {
                try { MdApi.Plugin.Adapter?.Tune(-1, -1, -1, -1); } catch { }
                try { DvbViewer.Plugin.Adapter?.Tune(-1, -1, -1, -1); } catch { }
            };
            grpAct.Controls.Add(bRec);

            var bLogs = new Button { Text = "Ouvrir dossier logs", Left = 180, Top = 24, Width = 180, Height = 28 };
            bLogs.Click += (s, e) =>
            {
                try { Process.Start("explorer.exe", Globals.HomeDirectory.FullName); } catch { }
            };
            grpAct.Controls.Add(bLogs);

            var bClear = new Button { Text = "Vider le log", Left = 12, Top = 60, Width = 160, Height = 28 };
            bClear.Click += (s, e) =>
            {
                try
                {
                    var f = Globals.GetLogfile();
                    if (f.Exists) File.WriteAllText(f.FullName, "");
                }
                catch { }
            };
            grpAct.Controls.Add(bClear);

            var bDiag = new Button { Text = "Collecter diagnostic…", Left = 180, Top = 60, Width = 180, Height = 28 };
            bDiag.Click += (s, e) => CollectDiagnostic();
            grpAct.Controls.Add(bDiag);

            p.Controls.Add(grpAct);
        }

        private void CollectDiagnostic()
        {
            try
            {
                string ts = DateTime.Now.ToString("yyyyMMdd-HHmm");
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string zipPath = Path.Combine(desktop, "dvbapiNET-diag-" + ts + ".zip");

                using (var fs = new FileStream(zipPath, FileMode.Create))
                using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var log = Globals.GetLogfile();
                    if (log.Exists)
                    {
                        var entry = zip.CreateEntry("dvbapiNET.log");
                        using (var es = entry.Open())
                        using (var src = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            src.CopyTo(es);
                    }
                    var cfg = Globals.ConfigFilePath;
                    if (cfg.Exists)
                    {
                        var entry = zip.CreateEntry("dvbapiNET.ini");
                        using (var es = entry.Open())
                        using (var sw = new StreamWriter(es))
                            sw.Write(MaskPasswords(File.ReadAllText(cfg.FullName)));
                    }
                    var snapEntry = zip.CreateEntry("snapshot.json");
                    using (var sw = new StreamWriter(snapEntry.Open()))
                    {
                        var snap = Oscam.DecryptionMonitor.Instance.GetSnapshot();
                        sw.Write("{\"version\":\"" + Globals.Info + "\"," +
                                 "\"os\":\"" + Environment.OSVersion + "\"," +
                                 "\"machine\":\"" + Environment.MachineName + "\"," +
                                 "\"cw_total\":" + snap.CwTotal + "," +
                                 "\"ecm_total\":" + snap.EcmTotal + "," +
                                 "\"last_ms\":" + snap.LastEcmMs + "," +
                                 "\"avg_ms\":" + snap.AvgEcmMs + "}");
                    }
                }

                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zipPath + "\"");
                _LblStatus.Text = "Diagnostic exporté : " + Path.GetFileName(zipPath);
                _LblStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _LblStatus.Text = "Erreur diag : " + ex.Message;
                _LblStatus.ForeColor = Color.DarkRed;
            }
        }

        private void ExportCsv()
        {
            try
            {
                string ts = DateTime.Now.ToString("yyyyMMdd-HHmm");
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string csvPath = Path.Combine(desktop, "dvbapiNET-ecm-" + ts + ".csv");
                var s = Oscam.DecryptionMonitor.Instance.GetSnapshot();
                using (var sw = new StreamWriter(csvPath))
                {
                    sw.WriteLine("time,caid,pid,ecm_ms,reader,protocol,hops");
                    for (int i = s.RecentEcm.Length - 1; i >= 0; i--)
                    {
                        var ev = s.RecentEcm[i];
                        sw.Write(ev.When.ToString("o")); sw.Write(',');
                        sw.Write("0x" + ev.CaId.ToString("X4")); sw.Write(',');
                        sw.Write("0x" + ev.Pid.ToString("X4")); sw.Write(',');
                        sw.Write(ev.EcmTimeMs); sw.Write(',');
                        sw.Write(ev.Reader ?? ""); sw.Write(',');
                        sw.Write(ev.Protocol ?? ""); sw.Write(',');
                        sw.WriteLine(ev.Hops);
                    }
                }
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + csvPath + "\"");
                _LblStatus.Text = "CSV exporté : " + Path.GetFileName(csvPath);
                _LblStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _LblStatus.Text = "Erreur export : " + ex.Message;
                _LblStatus.ForeColor = Color.DarkRed;
            }
        }

        private static string MaskPasswords(string ini)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in ini.Split('\n'))
            {
                if (line.StartsWith("password", StringComparison.OrdinalIgnoreCase)
                    || line.IndexOf("password", StringComparison.OrdinalIgnoreCase) == 0)
                    sb.AppendLine("password=***");
                else
                    sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        private void BtnDetect_Click(object sender, EventArgs e)
        {
            int port = 633;
            if (!int.TryParse(_TxtPort.Text.Trim(), out port) || port < 1 || port > 65535) port = 633;

            using (var dlg = new DiscoveryDialog { InitialPort = port })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedServer != null)
                {
                    _TxtHost.Text = dlg.SelectedServer.Ip.ToString();
                    _TxtPort.Text = dlg.SelectedServer.Port.ToString();
                }
            }
        }

        private void BuildDebugTab(TabPage p)
        {
            var grpStats = new GroupBox { Text = "Statistiques décryptage", Left = 8, Top = 8, Width = 440, Height = 130 };
            int y = 22;
            grpStats.Controls.Add(new Label { Text = "CW total :", Left = 12, Top = y, AutoSize = true });
            _LblCwTotal = new Label { Left = 130, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStats.Controls.Add(_LblCwTotal);

            grpStats.Controls.Add(new Label { Text = "Even / Odd :", Left = 230, Top = y, AutoSize = true });
            _LblCwEven = new Label { Left = 305, Top = y, AutoSize = true, Font = new Font("Consolas", 9f), ForeColor = Color.SteelBlue };
            _LblCwOdd = new Label { Left = 365, Top = y, AutoSize = true, Font = new Font("Consolas", 9f), ForeColor = Color.DarkOrange };
            grpStats.Controls.Add(_LblCwEven);
            grpStats.Controls.Add(_LblCwOdd);

            y += 26;
            grpStats.Controls.Add(new Label { Text = "ECM total :", Left = 12, Top = y, AutoSize = true });
            _LblEcmTotal = new Label { Left = 130, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStats.Controls.Add(_LblEcmTotal);

            y += 26;
            grpStats.Controls.Add(new Label { Text = "Latence (last/avg/max) :", Left = 12, Top = y, AutoSize = true });
            _LblLat = new Label { Left = 150, Top = y, AutoSize = true, Font = new Font("Consolas", 9f) };
            grpStats.Controls.Add(_LblLat);

            y += 30;
            var bReset = new Button { Text = "Reset stats", Left = 12, Top = y, Width = 120, Height = 26 };
            bReset.Click += (s, ev) => { try { Oscam.DecryptionMonitor.Instance.Reset(); RefreshStatus(null, null); } catch { } };
            grpStats.Controls.Add(bReset);
            var bExport = new Button { Text = "Exporter CSV…", Left = 140, Top = y, Width = 130, Height = 26 };
            bExport.Click += (s, ev) => ExportCsv();
            grpStats.Controls.Add(bExport);
            p.Controls.Add(grpStats);

            var grpEcm = new GroupBox { Text = "ECM récentes (les plus récentes en haut)", Left = 8, Top = 144, Width = 440, Height = 200 };
            _LstEcm = new ListView
            {
                Left = 8,
                Top = 18,
                Width = 424,
                Height = 174,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new Font("Consolas", 8f)
            };
            _LstEcm.Columns.Add("Heure", 70);
            _LstEcm.Columns.Add("CAID", 60);
            _LstEcm.Columns.Add("PID", 60);
            _LstEcm.Columns.Add("Lat.", 50);
            _LstEcm.Columns.Add("Reader", 90);
            _LstEcm.Columns.Add("Protocole", 80);
            grpEcm.Controls.Add(_LstEcm);
            p.Controls.Add(grpEcm);
        }

        private void BuildAboutTab(TabPage p)
        {
            var lblTitle = new Label
            {
                Text = "dvbapiNET",
                Left = 16,
                Top = 20,
                AutoSize = true,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 128, 128)
            };
            p.Controls.Add(lblTitle);

            var lblVer = new Label
            {
                Text = Globals.Info,
                Left = 16,
                Top = 64,
                AutoSize = true,
                Font = new Font("Consolas", 9f)
            };
            p.Controls.Add(lblVer);

            var lblCopy = new Label
            {
                Text = Globals.PluginInfo?.LegalCopyright ?? "",
                Left = 16,
                Top = 92,
                AutoSize = true,
                ForeColor = Color.Gray
            };
            p.Controls.Add(lblCopy);

            var lblDesc = new Label
            {
                Text = "Plugin de décryptage DVB pour DVBViewer / MDAPI\nvia serveur Oscam (dvbapi).",
                Left = 16,
                Top = 130,
                Width = 360,
                Height = 50
            };
            p.Controls.Add(lblDesc);

            var lblCfg = new Label
            {
                Text = "Fichier config :",
                Left = 16,
                Top = 200,
                AutoSize = true,
                ForeColor = Color.Gray
            };
            p.Controls.Add(lblCfg);

            var lblCfgPath = new Label
            {
                Text = Globals.ConfigFilePath.FullName,
                Left = 16,
                Top = 220,
                AutoSize = true,
                Font = new Font("Consolas", 8f),
                ForeColor = Color.DimGray
            };
            p.Controls.Add(lblCfgPath);

            var upd = Oscam.UpdateChecker.LatestRelease;
            if (upd != null)
            {
                var lblUpd = new Label
                {
                    Text = "★ Mise à jour disponible : " + upd.TagName,
                    Left = 16,
                    Top = 260,
                    AutoSize = true,
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(38, 79, 120)
                };
                p.Controls.Add(lblUpd);

                var lnkUpd = new LinkLabel
                {
                    Text = upd.Url,
                    Left = 16,
                    Top = 285,
                    AutoSize = true
                };
                lnkUpd.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(upd.Url); } catch { } };
                p.Controls.Add(lnkUpd);
            }
        }

        private static dvbapiNet.Oscam.DvbApiAdapter PickAdapter()
        {
            var m = MdApi.Plugin.Adapter;
            var d = DvbViewer.Plugin.Adapter;
            if (m != null && m.IsTuned) return m;
            if (d != null && d.IsTuned) return d;
            if (m != null && m.HasDvbApiClient) return m;
            if (d != null && d.HasDvbApiClient) return d;
            return m ?? d;
        }

        private void RefreshStatus(object sender, EventArgs e)
        {
            try
            {
                var a = PickAdapter();
                bool conn = a != null && a.HasDvbApiClient;
                bool tuned = a != null && a.IsTuned;
                if (_LblConn != null)
                {
                    _LblConn.Text = conn ? "Connecté" : "Déconnecté";
                    _LblConn.ForeColor = conn ? Color.Green : Color.Red;
                }
                if (_LblTuned != null)
                {
                    _LblTuned.Text = tuned ? "Oui" : "Non";
                    _LblTuned.ForeColor = tuned ? Color.Green : Color.Gray;
                }
                if (_LblService != null) _LblService.Text = (a?.CurrentService ?? 0).ToString();
                if (_LblPmt != null) _LblPmt.Text = "0x" + (a?.CurrentPmtPid ?? 0).ToString("X4");
                if (_LnkWeb != null)
                {
                    var w = Globals.WebInterface;
                    if (w != null && w.IsRunning)
                    {
                        string url = $"http://127.0.0.1:{w.Port}/";
                        _LnkWeb.Text = url;
                        _LnkWeb.LinkArea = new LinkArea(0, url.Length);
                    }
                    else
                    {
                        _LnkWeb.Text = "(non démarré)";
                        _LnkWeb.LinkArea = new LinkArea(0, 0);
                    }
                }

                if (_LblCwTotal != null)
                {
                    var snap = Oscam.DecryptionMonitor.Instance.GetSnapshot();
                    _LblCwTotal.Text = snap.CwTotal.ToString();
                    _LblCwEven.Text = "E " + snap.CwEven;
                    _LblCwOdd.Text = "O " + snap.CwOdd;
                    _LblEcmTotal.Text = snap.EcmTotal.ToString();
                    _LblLat.Text = $"{snap.LastEcmMs} / {snap.AvgEcmMs} / {snap.MaxEcmMs} ms";

                    if (_LstEcm != null)
                    {
                        _LstEcm.BeginUpdate();
                        _LstEcm.Items.Clear();
                        for (int i = snap.RecentEcm.Length - 1; i >= 0; i--)
                        {
                            var ev = snap.RecentEcm[i];
                            var it = new ListViewItem(ev.When.ToLocalTime().ToString("HH:mm:ss"));
                            it.SubItems.Add("0x" + ev.CaId.ToString("X4"));
                            it.SubItems.Add("0x" + ev.Pid.ToString("X4"));
                            it.SubItems.Add(ev.EcmTimeMs + "ms");
                            it.SubItems.Add(ev.Reader ?? "");
                            it.SubItems.Add(ev.Protocol ?? "");
                            _LstEcm.Items.Add(it);
                        }
                        _LstEcm.EndUpdate();
                    }
                }
            }
            catch { }
        }

        private void LoadValues()
        {
            string host = "127.0.0.1";
            int port = 633, offset = 0, debug = 0, webPort = 8080;
            bool pretty = true, dump = false;
            string servers = "", webUser = "", webPwd = "", whUrl = "";

            Globals.Config.Get("dvbapi", "server", ref host);
            Globals.Config.Get("dvbapi", "port", 1, 65535, ref port);
            Globals.Config.Get("dvbapi", "offset", 0, 128, ref offset);
            Globals.Config.Get("dvbapi", "servers", ref servers);
            Globals.Config.Get("log", "debug", 0, int.MaxValue, ref debug);
            Globals.Config.Get("log", "pretty", ref pretty);
            Globals.Config.Get("debug", "streamdump", ref dump);
            Globals.Config.Get("web", "port", 1, 65535, ref webPort);
            Globals.Config.Get("web", "user", ref webUser);
            Globals.Config.Get("web", "password", ref webPwd);
            Globals.Config.Get("webhook", "url", ref whUrl);

            _TxtHost.Text = host;
            _TxtPort.Text = port.ToString();
            _TxtOffset.Text = offset.ToString();
            _TxtDebug.Text = debug.ToString();
            _ChkPretty.Checked = pretty;
            _ChkDump.Checked = dump;
            _TxtWebPort.Text = webPort.ToString();
            _TxtServers.Text = servers;
            _TxtWebUser.Text = webUser;
            _TxtWebPwd.Text = webPwd;
            _TxtWebhookUrl.Text = whUrl;
            _ChkDark.Checked = _DarkMode;
            bool cwCache = false;
            try { Globals.Config.Get("cache", "cw", ref cwCache); } catch { }
            _ChkCwCache.Checked = cwCache;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(_TxtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
            { _LblStatus.Text = "Port Oscam invalide."; return; }
            if (!int.TryParse(_TxtOffset.Text.Trim(), out int offset) || offset < 0 || offset > 128)
            { _LblStatus.Text = "Offset invalide (0-128)."; return; }
            if (!int.TryParse(_TxtDebug.Text.Trim(), out int debug) || debug < 0)
            { _LblStatus.Text = "Niveau debug invalide."; return; }
            if (!int.TryParse(_TxtWebPort.Text.Trim(), out int webPort) || webPort < 1 || webPort > 65535)
            { _LblStatus.Text = "Port web invalide."; return; }
            string host = _TxtHost.Text.Trim();
            if (string.IsNullOrEmpty(host))
            { _LblStatus.Text = "Serveur invalide."; return; }

            try
            {
                IniFile ini = new IniFile(Globals.ConfigFilePath);
                ini.SetValue("dvbapi", "server", host);
                ini.SetValue("dvbapi", "port", port.ToString());
                ini.SetValue("dvbapi", "offset", offset.ToString());
                ini.SetValue("log", "debug", debug.ToString());
                ini.SetValue("log", "pretty", _ChkPretty.Checked ? "1" : "0");
                ini.SetValue("debug", "streamdump", _ChkDump.Checked ? "1" : "0");
                ini.SetValue("web", "port", webPort.ToString());
                ini.SetValue("web", "user", _TxtWebUser.Text.Trim());
                ini.SetValue("web", "password", _TxtWebPwd.Text);
                ini.SetValue("dvbapi", "servers", _TxtServers.Text.Trim());
                ini.SetValue("webhook", "url", _TxtWebhookUrl.Text.Trim());
                ini.SetValue("ui", "dark", _ChkDark.Checked ? "1" : "0");
                ini.SetValue("cache", "cw", _ChkCwCache.Checked ? "1" : "0");
                Oscam.CwCache.Instance.Enabled = _ChkCwCache.Checked;
                ini.Save();
                Globals.ReloadConfig();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _LblStatus.Text = "Erreur sauvegarde : " + ex.Message;
            }
        }

        private static Label MakeLabel(string text, int width, int top)
        {
            return new Label
            {
                Text = text,
                Left = 10,
                Top = top + 3,
                Width = width,
                TextAlign = ContentAlignment.MiddleRight
            };
        }
    }
}
