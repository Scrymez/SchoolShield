using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>Asks for the password of a Wi-Fi network: a big field, an eye to show the letters, two clear buttons.</summary>
    internal sealed class WifiPasswordDialog : Form
    {
        private readonly TextBox box = new TextBox();
        public string Password { get { return box.Text; } }

        public WifiPasswordDialog(string ssid)
        {
            Text = "Пароль от Wi-Fi";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Fx.FadeIn(this, 160);
            int w = Math.Min(Metrics.U(640), Screen.PrimaryScreen.WorkingArea.Width - Metrics.U(40));
            ClientSize = new Size(w, Metrics.U(300));
            int pad = Metrics.U(28);

            Label title = new Label();
            title.Text = "Сеть «" + ssid + "»";
            title.Font = Theme.UiFont(Metrics.Fz(17), FontStyle.Bold);
            title.ForeColor = Theme.Text;
            title.AutoEllipsis = true;
            title.SetBounds(pad, Metrics.U(22), w - pad * 2, Metrics.U(40));
            Controls.Add(title);

            Label hint = new Label();
            hint.Text = "Введите пароль. Обычно он написан на роутере или его скажет администратор.";
            hint.Font = Theme.UiFont(Metrics.Fz(11));
            hint.ForeColor = Theme.Muted;
            hint.SetBounds(pad, Metrics.U(64), w - pad * 2, Metrics.U(48));
            Controls.Add(hint);

            box.Font = Theme.UiFont(Metrics.Fz(15));
            box.UseSystemPasswordChar = true;
            box.Text = "";
            FieldHost host = new FieldHost(box, false);
            host.SetBounds(pad, Metrics.U(118), w - pad * 2, Metrics.U(52));
            box.Font = Theme.UiFont(Metrics.Fz(15));
            Controls.Add(host);
            box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); }
            };

            MotionCheck show = new MotionCheck();
            show.Text = "Показать пароль";
            show.Font = Theme.UiFont(Metrics.Fz(12));
            show.ForeColor = Theme.Text;
            show.Location = new Point(pad, Metrics.U(182));
            show.CheckedChanged += delegate { box.UseSystemPasswordChar = !show.Checked; };
            Controls.Add(show);

            MotionButton ok = (MotionButton)UiKit.Accent("Подключиться", delegate { Accept(); });
            ok.AutoSize = false;
            ok.Font = Fx.Ui(Metrics.Fz(13), FontStyle.Bold);
            ok.SetBounds(pad, Metrics.U(232), (w - pad * 2) / 2 - Metrics.U(8), Metrics.U(54));
            Controls.Add(ok);
            MotionButton cancel = (MotionButton)UiKit.Flat("Отмена", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.AutoSize = false;
            cancel.Font = Fx.Ui(Metrics.Fz(13));
            cancel.SetBounds(pad + (w - pad * 2) / 2 + Metrics.U(8), Metrics.U(232), (w - pad * 2) / 2 - Metrics.U(8), Metrics.U(54));
            Controls.Add(cancel);
            CancelButton = cancel;
            Shown += delegate { box.Focus(); };
        }

        private void Accept()
        {
            if (box.Text.Length < 8 && box.Text.Length > 0)
            {
                if (MessageBox.Show(this, "Пароль короче 8 знаков: у большинства сетей он длиннее. Всё равно попробовать?", "Пароль", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            }
            if (box.Text.Length == 0) { MessageBox.Show(this, "Введите пароль.", "Пароль"); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>One network in the list: its name, a few words about it, the signal, and one big button.</summary>
    internal sealed class WifiRow : Control, IEnterAnimated
    {
        public readonly WifiNetwork Net;
        public readonly MotionButton Button;
        private readonly string sub;
        private double enter = 1;
        private Tween tween;

        public WifiRow(WifiNetwork net, string sub, EventHandler click)
        {
            Net = net;
            this.sub = sub;
            Height = Metrics.U(82);
            Margin = new Padding(0, 0, 0, Metrics.U(10));
            Button = (MotionButton)(net.Connected ? UiKit.Flat("Отключиться", click) : UiKit.Accent("Подключиться", click));
            Button.AutoSize = false;
            Button.Font = Fx.Ui(Metrics.Fz(12), FontStyle.Bold);
            Controls.Add(Button);
            Resize += delegate { Button.SetBounds(Width - Metrics.U(210), (Height - Metrics.U(52)) / 2, Metrics.U(190), Metrics.U(52)); };
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void PlayEnter(int index)
        {
            if (tween != null) tween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            tween = Tween.Run(440, Easing.OutCubic, delegate (double t) { enter = t; Invalidate(); }, null, Math.Min(index, 10) * 40);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tween != null) tween.Cancel();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            if (en < 0.003) return;
            RectangleF r = new RectangleF(1, 1 + (float)((1 - en) * Metrics.U(14)), Width - 3, Height - 3);
            using (GraphicsPath p = Fx.Round(r, Metrics.U(16)))
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Net.Connected ? Fx.Blend(Theme.Card, Theme.Success, 0.10) : Theme.Card, en))) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Alpha(Net.Connected ? Theme.Success : Theme.Line, en), Net.Connected ? 2f : 1.3f)) g.DrawPath(pen, p);
            }
            // signal: four bars
            int bars = Net.Signal >= 75 ? 4 : Net.Signal >= 50 ? 3 : Net.Signal >= 25 ? 2 : 1;
            float bx = r.X + Metrics.U(24), by = r.Y + r.Height / 2f + Metrics.U(16), bw = Metrics.U(7), gap = Metrics.U(4);
            for (int i = 0; i < 4; i++)
            {
                float bh = Metrics.U(10) + i * Metrics.U(8);
                using (SolidBrush b = new SolidBrush(Fx.Alpha(i < bars ? Theme.Accent : Theme.Line, en)))
                    Fx.FillRound(g, b, new RectangleF(bx + i * (bw + gap), by - bh, bw, bh), 2);
            }
            float tx = bx + 4 * (bw + gap) + Metrics.U(22), tw = Width - tx - Metrics.U(230);
            Fx.Text(g, Net.Ssid, Fx.Ui(Metrics.Fz(14), FontStyle.Bold), Fx.Alpha(Theme.Text, en), new RectangleF(tx, r.Y + Metrics.U(12), tw, Metrics.U(30)), StringAlignment.Near, StringAlignment.Near, true);
            Fx.Text(g, sub, Fx.Ui(Metrics.Fz(10.5f)), Fx.Alpha(Net.Connected ? Theme.Success : Theme.Muted, en), new RectangleF(tx, r.Y + Metrics.U(44), tw, Metrics.U(26)), StringAlignment.Near, StringAlignment.Near, true);
        }
    }

    /// <summary>
    /// "Wi-Fi and network": what is connected, whether the internet works, every network around with a big Connect button
    /// (asks for the password when there is a padlock), a button that opens the browser. Scanning and connecting run in the background.
    /// </summary>
    internal sealed class TeacherNetworkSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();
        private Label statusLabel;
        private StatusChip internetChip, wiredChip;
        private FlowLayoutPanel listHost;
        private bool busy;
        private bool connecting;
        private System.Threading.Timer timer;

        public TeacherNetworkSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
            Resize += delegate { FitWidths(); };
        }

        public override string Id { get { return "network"; } }
        public override string Title { get { return "Wi-Fi и сеть"; } }
        public override string Glyph { get { return ""; } }

        private int RowWidth { get { return Math.Max(320, Math.Min(Metrics.U(900), root.ClientSize.Width - Metrics.U(34))); } }

        private void FitWidths()
        {
            if (listHost == null) return;
            int w = RowWidth;
            root.SuspendLayout();
            foreach (Control c in root.Controls) c.MaximumSize = new Size(Math.Max(320, root.ClientSize.Width - Metrics.U(34)), 0);
            foreach (Control c in listHost.Controls) c.Width = w;
            root.ResumeLayout();
        }

        public override void OnShown()
        {
            Build();
            Refresh(true);
            CheckInternet();
            if (timer == null) timer = new System.Threading.Timer(delegate { if (Parent != null && !connecting) { Refresh(false); CheckInternet(); } }, null, 9000, 9000);
        }

        private void Build()
        {
            root.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in root.Controls) old.Add(c);
            foreach (Control c in old) { root.Controls.Remove(c); c.Dispose(); }

            Label h1 = UiKit.Heading("Подключение к интернету");
            h1.Font = Theme.UiFont(Metrics.Fz(16), FontStyle.Bold);
            root.Controls.Add(h1);

            FlowLayoutPanel chips = UiKit.Flow();
            internetChip = new StatusChip();
            wiredChip = new StatusChip();
            internetChip.Set("Проверяю интернет…", Theme.Muted);
            wiredChip.Set("Проводная сеть: проверяю…", Theme.Muted);
            chips.Controls.Add(internetChip);
            chips.Controls.Add(wiredChip);
            root.Controls.Add(chips);

            FlowLayoutPanel actions = UiKit.Flow();
            MotionButton open = (MotionButton)UiKit.Accent("Открыть интернет", delegate { OpenBrowser(); });
            open.Glyph = "";
            MotionButton again = (MotionButton)UiKit.Flat("Обновить список сетей", delegate { Refresh(true); CheckInternet(); });
            again.Glyph = "";
            MotionButton win = (MotionButton)UiKit.Flat("Параметры сети Windows", delegate { WinTools.Settings("network"); });
            win.Glyph = "";
            foreach (MotionButton b in new MotionButton[] { open, again, win })
            {
                b.Font = Fx.Ui(Metrics.Fz(12), b == open ? FontStyle.Bold : FontStyle.Regular);
                b.AutoSize = false;
                b.Size = new Size(b.GetPreferredSize(Size.Empty).Width + Metrics.U(10), Metrics.U(54));
                b.Margin = new Padding(0, 0, Metrics.U(10), Metrics.U(10));
                actions.Controls.Add(b);
            }
            root.Controls.Add(actions);

            Label h2 = UiKit.Heading("Сети Wi-Fi рядом");
            h2.Font = Theme.UiFont(Metrics.Fz(16), FontStyle.Bold);
            root.Controls.Add(h2);

            statusLabel = UiKit.Hint(Wifi.Available ? "Ищу сети…" : "На этом компьютере нет адаптера Wi-Fi. Проводная сеть подключается кабелем и работает без настройки.");
            statusLabel.Font = Theme.UiFont(Metrics.Fz(12));
            root.Controls.Add(statusLabel);

            listHost = new FlowLayoutPanel();
            listHost.FlowDirection = FlowDirection.TopDown;
            listHost.WrapContents = false;
            listHost.AutoSize = true;
            listHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            listHost.BackColor = Theme.Background;
            listHost.Margin = new Padding(0);
            root.Controls.Add(listHost);
            FitWidths();
            root.ResumeLayout();
        }

        private void OpenBrowser()
        {
            string url = "https://ya.ru";
            try { string hp = Main.Cfg.Browser.HomePage; if (!string.IsNullOrEmpty(hp) && hp.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = hp; } catch { }
            WinTools.Open(url, null);
        }

        // ------------------------------------------------------------------ scanning

        private void Refresh(bool rescan)
        {
            if (busy || !Wifi.Available) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<WifiNetwork> nets = Wifi.Scan(rescan);
                if (rescan) { Thread.Sleep(1200); nets = Wifi.Scan(false); }     // the adapter needs a moment to find the networks
                try { BeginInvoke(new Action(delegate { Fill(nets); })); }
                catch { busy = false; }
            });
        }

        private static string About(WifiNetwork n)
        {
            if (n.Connected) return "Подключено" + (n.Secured ? ", сеть с паролем" : ", открытая сеть");
            if (n.HasProfile) return "Уже сохранена на этом компьютере";
            return n.Secured ? "Сеть с паролем" : "Открытая сеть (без пароля)";
        }

        private void Fill(List<WifiNetwork> nets)
        {
            busy = false;
            if (listHost == null || listHost.IsDisposed) return;
            listHost.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in listHost.Controls) old.Add(c);
            foreach (Control c in old) { listHost.Controls.Remove(c); c.Dispose(); }

            WifiNetwork cur = null;
            foreach (WifiNetwork n in nets) if (n.Connected) cur = n;
            statusLabel.Text = nets.Count == 0 ? "Сетей не найдено. Подойдите ближе к роутеру или нажмите «Обновить список сетей»."
                : cur != null ? "Вы подключены к сети «" + cur.Ssid + "»." : "Выберите свою сеть и нажмите «Подключиться».";

            // the connected one first, then saved ones, then the strongest
            nets.Sort(delegate (WifiNetwork a, WifiNetwork b)
            {
                int c = b.Connected.CompareTo(a.Connected);
                if (c != 0) return c;
                c = b.HasProfile.CompareTo(a.HasProfile);
                return c != 0 ? c : b.Signal.CompareTo(a.Signal);
            });
            int w = RowWidth, i = 0;
            foreach (WifiNetwork n in nets)
            {
                if (i >= 25) break;
                WifiNetwork net = n;
                WifiRow row = new WifiRow(net, About(net), delegate { Connect(net); });
                row.Width = w;
                listHost.Controls.Add(row);
                row.PlayEnter(i++);
            }
            listHost.ResumeLayout();
        }

        // ------------------------------------------------------------------ connecting

        private void Connect(WifiNetwork net)
        {
            if (connecting) { Ui.Toast("Подождите: подключение ещё идёт."); return; }
            if (net.Connected)
            {
                Wifi.Disconnect();
                statusLabel.Text = "Отключаюсь…";
                After(2500);
                return;
            }
            string password = null;
            if (!net.HasProfile && net.Secured)
            {
                using (WifiPasswordDialog d = new WifiPasswordDialog(net.Ssid))
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                    password = d.Password;
                }
            }
            connecting = true;
            statusLabel.Text = "Подключаюсь к «" + net.Ssid + "»… Это занимает до 15 секунд.";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message = null;
                try { ok = DoConnect(net, password, out message); }
                catch (Exception ex) { message = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        connecting = false;
                        statusLabel.Text = ok ? "Готово: подключено к «" + net.Ssid + "»." : message ?? "Не удалось подключиться.";
                        if (ok) Ui.Toast("Подключено к «" + net.Ssid + "».");
                        Refresh(false);
                        CheckInternet();
                    }));
                }
                catch { connecting = false; }
            });
        }

        private static bool WaitConnected(string ssid, int seconds)
        {
            for (int i = 0; i < seconds * 2; i++)
            {
                Thread.Sleep(500);
                WifiNetwork cur = Wifi.Current();
                if (cur != null && string.Equals(cur.Ssid, ssid, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool DoConnect(WifiNetwork net, string password, out string message)
        {
            message = null;
            string profile = net.Profile;
            if (!net.HasProfile)
            {
                string error;
                if (!Wifi.AddProfile(net.Ssid, password ?? "", net.Secured ? "WPA2" : "Open", out error)) { message = error; return false; }
                profile = net.Ssid;
            }
            if (!Wifi.Connect(profile)) { message = "Windows не смогла начать подключение. Попробуйте ещё раз."; return false; }
            if (WaitConnected(net.Ssid, 15)) return true;
            if (!net.HasProfile && net.Secured)
            {
                // some new routers use WPA3: try that once before giving up
                string error;
                if (Wifi.AddProfile(net.Ssid, password ?? "", "WPA3", out error) && Wifi.Connect(net.Ssid) && WaitConnected(net.Ssid, 15)) return true;
                try { Wifi.DeleteProfile(net.Ssid); } catch { }
                message = "Не получилось подключиться к «" + net.Ssid + "». Чаще всего неверный пароль: нажмите «Подключиться» и введите его ещё раз.";
            }
            else message = "Не получилось подключиться к «" + net.Ssid + "». Подойдите ближе к роутеру или попробуйте ещё раз.";
            return false;
        }

        private void After(int ms)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = ms;
            t.Tick += delegate { t.Stop(); t.Dispose(); Refresh(false); CheckInternet(); };
            t.Start();
        }

        // ------------------------------------------------------------------ is the internet really there

        private void CheckInternet()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string wired = "Проводная сеть не подключена";
                Color wiredColor = Theme.Muted;
                try
                {
                    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                        if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) { wired = "Проводная сеть подключена"; wiredColor = Theme.Success; break; }
                }
                catch { }
                string net; Color netColor;
                if (!NetworkInterface.GetIsNetworkAvailable()) { net = "Интернета нет: нет подключения к сети"; netColor = Theme.Warn; }
                else
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
                        req.Timeout = 4000;
                        req.AllowAutoRedirect = false;
                        using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                        {
                            if ((int)r.StatusCode == 200) { net = "Интернет работает"; netColor = Theme.Success; }
                            else { net = "Сеть есть, но нужен вход на странице сети"; netColor = Theme.Warn; }
                        }
                    }
                    catch { net = "Сеть есть, но интернет не отвечает"; netColor = Theme.Warn; }
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (internetChip == null || internetChip.IsDisposed) return;
                        internetChip.Set(net, netColor);
                        wiredChip.Set(wired, wiredColor);
                    }));
                }
                catch { }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && timer != null) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
