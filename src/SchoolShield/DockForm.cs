using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>Volume slider that opens from the dock (instead of "+" and "-" buttons).</summary>
    internal sealed class VolumeFlyout : Form
    {
        private readonly SliderBar slider = new SliderBar();
        private readonly Button mute = new Button();
        private readonly Label percent = new Label();

        public VolumeFlyout()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            Size = new Size(Metrics.S(330), Metrics.S(62));
            Padding = new Padding(1);

            mute.FlatStyle = FlatStyle.Flat;
            mute.FlatAppearance.BorderSize = 0;
            mute.Font = Theme.IconFont(15);
            mute.ForeColor = Theme.Text;
            mute.BackColor = Theme.Card;
            mute.Cursor = Cursors.Hand;
            mute.SetBounds(Metrics.S(8), Metrics.S(10), Metrics.S(44), Metrics.S(42));
            mute.Click += delegate { AudioControl.Muted = !AudioControl.Muted; Sync(); };

            slider.SetBounds(Metrics.S(58), Metrics.S(14), Width - Metrics.S(58) - Metrics.S(66), Metrics.S(34));
            slider.BackColor = Theme.Card;
            slider.ValueChanged += delegate
            {
                AudioControl.Muted = false;
                AudioControl.Volume = slider.Value / 100f;
                UpdateLabels();
            };

            percent.SetBounds(Width - Metrics.S(62), Metrics.S(10), Metrics.S(54), Metrics.S(42));
            percent.TextAlign = ContentAlignment.MiddleCenter;
            percent.Font = Theme.UiFont(12, FontStyle.Bold);
            percent.ForeColor = Theme.Text;

            Controls.Add(mute);
            Controls.Add(slider);
            Controls.Add(percent);
            Deactivate += delegate { Close(); };
            Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
        }

        private void Sync()
        {
            slider.SetSilently((int)Math.Round(AudioControl.Volume * 100));
            UpdateLabels();
        }

        private void UpdateLabels()
        {
            bool m = AudioControl.Muted;
            mute.Text = m || slider.Value == 0 ? "" : "";
            percent.Text = m ? "—" : slider.Value + "%";
        }

        public void ShowNear(Control anchor)
        {
            Sync();
            Rectangle a = anchor.RectangleToScreen(anchor.ClientRectangle);
            Rectangle scr = Screen.PrimaryScreen.Bounds;
            int x = Math.Max(scr.X, Math.Min(scr.Right - Width, a.Right - Width));
            Location = new Point(x, scr.Y + Metrics.DockHeight);
            Show();
            Activate();
        }
    }

    /// <summary>
    /// Thin always-on-top strip at the top of the screen. A shell without Explorer has no taskbar or tray,
    /// so this provides what a student still needs: open windows, language, volume, battery, Wi-Fi, clock.
    /// </summary>
    internal sealed class DockForm : Form
    {
        public static int BarHeight { get { return Metrics.DockHeight; } }

        private readonly MainForm main;
        private readonly Button home = new Button();
        private readonly FlowLayoutPanel tasks = new FlowLayoutPanel();
        private readonly FlowLayoutPanel right = new FlowLayoutPanel();
        private readonly Label admin = new Label();
        private readonly Button lang = new Button();
        private readonly Button volume = new Button();
        private readonly Label battery = new Label();
        private readonly Button wifi = new Button();
        private readonly Label clock = new Label();
        private readonly ToolTip tip = new ToolTip();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private string lastSignature = "";
        private int wifiTick;
        private VolumeFlyout flyout;

        public DockForm(MainForm main)
        {
            this.main = main;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            Rectangle s = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(s.X, s.Y, s.Width, BarHeight);

            home.Text = "   " + (Metrics.Tiny ? "" : "Меню");
            home.FlatStyle = FlatStyle.Flat;
            home.FlatAppearance.BorderSize = 0;
            home.Dock = DockStyle.Left;
            home.Width = Metrics.Tiny ? Metrics.S(56) : Metrics.S(118);
            home.Click += delegate { main.ShowMenu(); };

            tasks.WrapContents = false;
            tasks.FlowDirection = FlowDirection.LeftToRight;
            tasks.Dock = DockStyle.Fill;

            right.Dock = DockStyle.Right;
            right.AutoSize = true;
            right.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            right.WrapContents = false;
            right.FlowDirection = FlowDirection.LeftToRight;
            right.Padding = new Padding(0, 0, Metrics.S(8), 0);

            int h = BarHeight - Metrics.S(10);
            Style(lang, "RU", Metrics.S(46), h);
            lang.Click += delegate { InputLang.Toggle(); };
            Style(volume, "", Metrics.S(42), h);
            volume.Font = Theme.IconFont(12);
            volume.MouseUp += delegate (object o, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) { AudioControl.Muted = !AudioControl.Muted; Tick(); return; }
                if (flyout != null && !flyout.IsDisposed) { flyout.Close(); flyout = null; return; }
                flyout = new VolumeFlyout();
                flyout.FormClosed += delegate { flyout = null; };
                flyout.ShowNear(volume);
            };
            Style(wifi, "", Metrics.S(42), h);
            wifi.Font = Theme.IconFont(12);
            wifi.Click += delegate { main.ShowSection("system"); };

            battery.AutoSize = false;
            battery.Size = new Size(Metrics.S(70), BarHeight);
            battery.TextAlign = ContentAlignment.MiddleCenter;
            battery.Font = Theme.UiFont(10);
            battery.Margin = new Padding(0);

            clock.AutoSize = false;
            clock.Size = new Size(Metrics.S(64), BarHeight);
            clock.TextAlign = ContentAlignment.MiddleCenter;
            clock.Font = Theme.UiFont(11, FontStyle.Bold);
            clock.Margin = new Padding(0);

            admin.AutoSize = false;
            admin.Size = new Size(Metrics.S(84), h);
            admin.TextAlign = ContentAlignment.MiddleCenter;
            admin.Font = Theme.UiFont(8.5f, FontStyle.Bold);
            admin.ForeColor = Color.White;
            admin.BackColor = Theme.Danger;
            admin.Margin = new Padding(0, Metrics.S(5), Metrics.S(6), Metrics.S(5));
            admin.Visible = false;

            right.Controls.Add(admin);
            right.Controls.Add(lang);
            right.Controls.Add(volume);
            right.Controls.Add(battery);
            right.Controls.Add(wifi);
            right.Controls.Add(clock);

            // Fill goes first, edges after (WinForms docks the last control first).
            Controls.Add(tasks);
            Controls.Add(right);
            Controls.Add(home);

            ApplyTheme();
            tip.SetToolTip(lang, "Раскладка клавиатуры");
            tip.SetToolTip(volume, "Громкость (правая кнопка — выключить звук)");
            tip.SetToolTip(wifi, "Сеть Wi-Fi");

            timer.Interval = 700;
            timer.Tick += delegate { Tick(); };
            Shown += delegate { timer.Start(); Tick(); };
        }

        private void Style(Button b, string text, int w, int h)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Size = new Size(w, h);
            b.Margin = new Padding(0, Metrics.S(5), Metrics.S(6), Metrics.S(5));
            b.Cursor = Cursors.Hand;
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Sidebar;
            tasks.BackColor = Theme.Sidebar;
            right.BackColor = Theme.Sidebar;
            home.BackColor = Theme.Accent;
            home.ForeColor = Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White;
            home.Font = Theme.UiFont(10.5f, FontStyle.Bold);
            foreach (Button b in new Button[] { lang, volume, wifi })
            {
                b.BackColor = Theme.Card;
                b.ForeColor = Theme.Text;
            }
            lang.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            foreach (Label l in new Label[] { battery, clock })
            {
                l.BackColor = Theme.Sidebar;
                l.ForeColor = Theme.Text;
            }
            lastSignature = "";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        private void Tick()
        {
            try
            {
                clock.Text = DateTime.Now.ToString("HH:mm");
                lang.Text = InputLang.Current();

                if (State.AdminMode)
                {
                    TimeSpan left = State.AdminLeft;
                    admin.Text = "АДМИН\n" + ((int)left.TotalMinutes).ToString("00") + ":" + left.Seconds.ToString("00");
                    admin.Visible = true;
                }
                else admin.Visible = false;

                PowerStatus ps = SystemInformation.PowerStatus;
                bool noBattery = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0;
                battery.Visible = !noBattery;
                if (!noBattery) battery.Text = (ps.PowerLineStatus == PowerLineStatus.Online ? "⚡ " : "") + (int)(ps.BatteryLifePercent * 100) + "%";

                volume.Text = AudioControl.Available && (AudioControl.Muted || AudioControl.Volume <= 0.001f) ? "" : "";

                if ((wifiTick++ % 4) == 0) UpdateWifi();

                // Stay above full-screen windows.
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

                RebuildTasks();
            }
            catch { }
        }

        private void UpdateWifi()
        {
            bool online = NetworkInterface.GetIsNetworkAvailable();
            WifiNetwork cur = Wifi.Available ? Wifi.Current() : null;
            wifi.Text = cur != null ? "" : (online ? "" : "");   // wifi / wired / no network
            wifi.ForeColor = online ? Color.FromArgb(74, 222, 128) : Theme.Muted;
            tip.SetToolTip(wifi, cur != null ? "Wi-Fi: " + cur.Ssid : (online ? "Сеть подключена" : "Нет сети. Нажмите, чтобы подключиться"));
        }

        private void RebuildTasks()
        {
            uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr fg = Native.GetForegroundWindow();
            List<WinInfo> wins = new List<WinInfo>();
            foreach (WinInfo w in WindowList.Enumerate())
                if (w.Pid != ownPid) wins.Add(w);

            StringBuilder sig = new StringBuilder();
            foreach (WinInfo w in wins)
                sig.Append(w.Handle.ToInt64()).Append('|').Append(w.Title).Append('|').Append(w.Handle == fg ? "*" : "").Append(';');
            if (sig.ToString() == lastSignature) return;
            lastSignature = sig.ToString();

            tasks.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in tasks.Controls) old.Add(c);
            foreach (Control c in old) { tasks.Controls.Remove(c); c.Dispose(); }
            int width = Metrics.Tiny ? Metrics.S(130) : Metrics.Compact ? Metrics.S(160) : Metrics.S(190);
            foreach (WinInfo w in wins)
            {
                IntPtr handle = w.Handle;
                Button b = new Button();
                b.Text = w.Title;
                b.AutoEllipsis = true;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = handle == fg ? Theme.CardHover : Theme.Card;
                b.ForeColor = Theme.Text;
                b.Font = Theme.UiFont(9.5f);
                b.Size = new Size(width, BarHeight - Metrics.S(10));
                b.Margin = new Padding(Metrics.S(6), Metrics.S(5), 0, Metrics.S(5));
                b.Click += delegate { Activate(handle); };
                tasks.Controls.Add(b);
            }
            tasks.ResumeLayout();
        }

        private static void Activate(IntPtr h)
        {
            if (!Native.IsWindow(h)) return;
            if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetForegroundWindow(h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
