using dvbapiNet.Oscam;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace dvbapiNet.DvbViewer
{
    internal class DiscoveryDialog : Form
    {
        private ListView _List;
        private Button _BtnScan;
        private Button _BtnUse;
        private Button _BtnCancel;
        private ProgressBar _Progress;
        private Label _LblStatus;
        private CancellationTokenSource _Cts;

        public int InitialPort { get; set; } = 633;
        public OscamDiscovery.DiscoveredServer SelectedServer { get; private set; }

        public DiscoveryDialog()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Détection serveurs Oscam";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 320);
            Font = new Font("Segoe UI", 9f);

            _List = new ListView
            {
                Left = 12,
                Top = 12,
                Width = 416,
                Height = 220,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false
            };
            _List.Columns.Add("Adresse", 140);
            _List.Columns.Add("Port", 60);
            _List.Columns.Add("Version Oscam", 200);
            _List.DoubleClick += (s, e) => { if (_List.SelectedItems.Count > 0) BtnUse_Click(null, null); };
            Controls.Add(_List);

            _Progress = new ProgressBar
            {
                Left = 12,
                Top = 240,
                Width = 416,
                Height = 14,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 0,
                Visible = false
            };
            Controls.Add(_Progress);

            _LblStatus = new Label { Left = 12, Top = 258, Width = 280, Height = 20, ForeColor = Color.Gray };
            Controls.Add(_LblStatus);

            _BtnScan = new Button { Text = "Scanner", Left = 12, Top = 282, Width = 100, Height = 26 };
            _BtnScan.Click += BtnScan_Click;
            Controls.Add(_BtnScan);

            _BtnUse = new Button { Text = "Utiliser", Left = 260, Top = 282, Width = 80, Height = 26 };
            _BtnUse.Click += BtnUse_Click;
            Controls.Add(_BtnUse);

            _BtnCancel = new Button { Text = "Annuler", Left = 348, Top = 282, Width = 80, Height = 26 };
            _BtnCancel.Click += (s, e) => { try { _Cts?.Cancel(); } catch { } DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_BtnCancel);

            AcceptButton = _BtnUse;
            CancelButton = _BtnCancel;
        }

        private async void BtnScan_Click(object sender, EventArgs e)
        {
            try { _Cts?.Cancel(); } catch { }
            _Cts = new CancellationTokenSource();

            _List.Items.Clear();
            _BtnScan.Enabled = false;
            _LblStatus.Text = "Scan en cours…";
            _Progress.Visible = true;
            _Progress.MarqueeAnimationSpeed = 30;

            try
            {
                var found = await Task.Run(() =>
                    OscamDiscovery.ScanLocalSubnetsAsync(InitialPort, 250, _Cts.Token))
                    .ConfigureAwait(true);

                foreach (var srv in found)
                {
                    var item = new ListViewItem(srv.Ip.ToString());
                    item.SubItems.Add(srv.Port.ToString());
                    item.SubItems.Add(srv.Version);
                    item.Tag = srv;
                    _List.Items.Add(item);
                }

                _LblStatus.Text = found.Count == 0
                    ? "Aucun serveur trouvé."
                    : $"{found.Count} serveur(s) trouvé(s).";
            }
            catch (Exception ex)
            {
                _LblStatus.Text = "Erreur : " + ex.Message;
            }
            finally
            {
                _Progress.MarqueeAnimationSpeed = 0;
                _Progress.Visible = false;
                _BtnScan.Enabled = true;
            }
        }

        private void BtnUse_Click(object sender, EventArgs e)
        {
            if (_List.SelectedItems.Count == 0)
            {
                _LblStatus.Text = "Sélectionnez un serveur.";
                return;
            }
            SelectedServer = _List.SelectedItems[0].Tag as OscamDiscovery.DiscoveredServer;
            if (SelectedServer == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _Cts?.Cancel(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
