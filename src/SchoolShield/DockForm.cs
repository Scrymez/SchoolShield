using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Thin always-on-top strip at the top of the screen. A shell without Explorer has no taskbar or tray,
    /// so this provides what a student still needs: open windows, language, volume, battery, network, clock.
    /// </summary>
    internal sealed class DockForm : Form
    {
        public const int BarHeight = 40;

        private readonly MainForm main;
        private readonly Button home = new Button();
        private readonly FlowLayoutPanel tasks = new FlowLayoutPanel();
        private readonly Panel right = new Panel();
        private readonly Label admin = new Label();
        private readonly Button lang = new Button();
        private readonly Button volDown = new Button();
        private readonly Button volMute = new Button();
        private readonly Button volUp = new Button();
        private readonly Label battery = new Label();
        private readonly Label net = new Label();
        private readonly Label clock = new Label();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private string lastSignature = "";

        public DockForm(MainForm main)
        {
            this.main = main;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            Rectangle s = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(s.X, s.Y, s.Width, BarHeight);

            home.Text = "  Меню";
            home.FlatStyle = FlatStyle.Flat;
            home.FlatAppearance.BorderSize = 0;
            home.Click += delegate { main.ShowMenu(); };

            tasks.WrapContents = false;
            tasks.FlowDirection = FlowDirection.LeftToRight;
            tasks.Dock = DockStyle.Fill;

            right.Dock = DockStyle.Right;
            right.Width = 470;

            Configure(lang, "RU", 6, 5, 44);
            lang.Click += delegate { InputLang.Toggle(); };
            Configure(volDown, "", 56, 5, 34);
            volDown.Click += delegate { Volume(-0.05f); };
            Configure(volMute, "", 92, 5, 34);
            volMute.Click += delegate { Mute(); };
            Configure(volUp, "", 128, 5, 34);
            volUp.Click += delegate { Volume(0.05f); };
            volDown.Font = volMute.Font = volUp.Font = Theme.IconFont(12);

            battery.SetBounds(172, 0, 90, BarHeight);
            net.SetBounds(262, 0, 34, BarHeight);
            clock.SetBounds(300, 0, 76, BarHeight);
            admin.SetBounds(380, 6, 86, BarHeight - 12);
            foreach (Label l in new Label[] { battery, net, clock })
            {
                l.AutoSize = false;
                l.TextAlign = ContentAlignment.MiddleCenter;
            }
            net.Font = Theme.IconFont(13);
            battery.Font = Theme.UiFont(10);
            clock.Font = Theme.UiFont(11, FontStyle.Bold);
            admin.AutoSize = false;
            admin.TextAlign = ContentAlignment.MiddleCenter;
            admin.Font = Theme.UiFont(8.5f, FontStyle.Bold);
            admin.ForeColor = Color.White;
            admin.BackColor = Theme.Danger;
            admin.Visible = false;

            right.Controls.AddRange(new Control[] { lang, volDown, volMute, volUp, battery, net, clock, admin });

            // Fill goes first, edges after (WinForms docks the last control first).
            Controls.Add(tasks);
            Controls.Add(right);
            Controls.Add(home);
            home.Dock = DockStyle.Left;
            home.Width = 124;

            ApplyTheme();
            ToolTip tip = new ToolTip();
            tip.SetToolTip(lang, "Раскладка клавиатуры");
            tip.SetToolTip(volDown, "Тише");
            tip.SetToolTip(volMute, "Выключить звук");
            tip.SetToolTip(volUp, "Громче");

            timer.Interval = 700;
            timer.Tick += delegate { Tick(); };
            Shown += delegate { timer.Start(); Tick(); };
        }

        private void Configure(Button b, string text, int x, int y, int w)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.SetBounds(x, y, w, BarHeight - 10);
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
            foreach (Button b in new Button[] { lang, volDown, volMute, volUp })
            {
                b.BackColor = Theme.Card;
                b.ForeColor = Theme.Text;
                if (b == lang) b.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            }
            foreach (Label l in new Label[] { battery, net, clock })
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

        private static void Volume(float delta)
        {
            if (!AudioControl.Available) return;
            AudioControl.Muted = false;
            AudioControl.Volume = AudioControl.Volume + delta;
        }

        private static void Mute()
        {
            if (AudioControl.Available) AudioControl.Muted = !AudioControl.Muted;
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
                if ((ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0) battery.Text = "";
                else battery.Text = (ps.PowerLineStatus == PowerLineStatus.Online ? "⚡ " : "") + (int)(ps.BatteryLifePercent * 100) + "%";

                bool online = NetworkInterface.GetIsNetworkAvailable();
                net.Text = online ? "" : "";
                net.ForeColor = online ? Color.FromArgb(74, 222, 128) : Theme.Muted;

                volMute.Text = AudioControl.Available && AudioControl.Muted ? "" : "";

                // Stay above full-screen windows.
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

                RebuildTasks();
            }
            catch { }
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
                b.Size = new Size(190, BarHeight - 10);
                b.Margin = new Padding(6, 5, 0, 5);
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
