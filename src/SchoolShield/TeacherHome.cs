using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>Opens Windows pages and tools by their plain names (the teacher's centre never sends anybody hunting through the Start menu).</summary>
    internal static class WinTools
    {
        public static void Open(string target, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(target, args ?? "");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Ui.Toast("Не удалось открыть: " + ex.Message); }
        }

        public static void Settings(string page) { Open("ms-settings:" + page, null); }

        /// <summary>DisplaySwitch.exe: /internal (this screen only), /clone (the same on both), /extend (two screens), /external (projector only).</summary>
        public static void Display(string mode)
        {
            string exe = System.IO.Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe");
            Open(exe, "/" + mode);
        }
    }

    /// <summary>Screen brightness of a laptop through WMI (a desktop monitor has none: then the slider is not shown).</summary>
    internal static class Brightness
    {
        public static int Get()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness"))
                    foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }

        public static void Set(int percent)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                        foreach (ManagementObject o in s.Get())
                            o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Max(0, Math.Min(100, percent)) });
                }
                catch { }
            });
        }
    }

    // =========================================================================================== controls

    /// <summary>A big button of the home page: a round icon, a headline and one line of explanation, all in plain words.</summary>
    internal sealed class BigTile : Control, IEnterAnimated
    {
        public string Title, Subtitle, Glyph;
        public event EventHandler Activated;
        private readonly AnimValue hover;
        private readonly AnimValue press;
        private double enter = 1;
        private Tween tween;

        public BigTile(string title, string subtitle, string glyph)
        {
            Title = title;
            Subtitle = subtitle;
            Glyph = glyph;
            Cursor = Cursors.Hand;
            hover = new AnimValue(this, 0);
            press = new AnimValue(this, 0);
            press.Stiffness = 700;
            press.Damping = 38;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void PlayEnter(int index)
        {
            if (tween != null) tween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            tween = Tween.Run(520, Easing.OutBack, delegate (double t) { enter = Math.Max(0, t); Invalidate(); }, delegate { enter = 1; Invalidate(); }, Math.Min(index, 12) * 40 + 30);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); press.To(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) press.To(1); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            press.To(0);
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && Activated != null) Activated(this, EventArgs.Empty);
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
            double h = Math.Max(0, Math.Min(1.1, hover.Value)), pr = Math.Max(0, Math.Min(1, press.Value));
            int m = Metrics.U(8);
            float lift = (float)(h * Metrics.U(4)), slide = (float)((1 - en) * Metrics.U(22));
            RectangleF card = new RectangleF(m, m - lift + slide, Width - m * 2, Height - m * 2);
            float radius = Metrics.U(20);
            float cx = card.X + card.Width / 2, cy = card.Y + card.Height / 2;
            float sc = (float)((0.92 + 0.08 * en) * (1 - 0.04 * pr));
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(-cx, -cy);

            Fx.Shadow(g, new RectangleF(card.X, card.Y + Metrics.U(4) + (float)(h * Metrics.U(3)), card.Width, card.Height), radius, Metrics.U(10),
                (int)((Theme.Dark ? 150 : 55) * (0.45 + 0.55 * h) * en), Theme.Dark ? Color.Black : Color.FromArgb(76, 29, 149));
            using (GraphicsPath path = Fx.Round(card, radius))
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Card, Theme.CardHover, h * 0.7), en))) g.FillPath(b, path);
                using (Pen pen = new Pen(Fx.Alpha(Fx.Blend(Theme.Line, Theme.Accent, Math.Min(1, h)), en), (float)(1.3 + h))) g.DrawPath(pen, path);
            }

            // round icon with the brand gradient
            float d = Math.Min(card.Height - Metrics.U(28), Metrics.U(76));
            RectangleF ic = new RectangleF(card.X + Metrics.U(20), card.Y + (card.Height - d) / 2f, d, d);
            using (LinearGradientBrush b = Fx.Gradient(ic, Fx.Alpha(Theme.BrandA, en), Fx.Alpha(Theme.BrandB, en), 50f)) g.FillEllipse(b, ic);
            float gs = (float)(1 + 0.08 * h);
            GraphicsState ist = g.Save();
            g.TranslateTransform(ic.X + d / 2, ic.Y + d / 2);
            g.ScaleTransform(gs, gs);
            g.TranslateTransform(-(ic.X + d / 2), -(ic.Y + d / 2));
            Fx.Text(g, Glyph, Fx.Icons(Metrics.Fz(24)), Fx.Alpha(Color.White, en), ic, StringAlignment.Center, StringAlignment.Center, false);
            g.Restore(ist);

            float tx = ic.Right + Metrics.U(18), tw = card.Right - tx - Metrics.U(16);
            Fx.Text(g, Title, Fx.Ui(Metrics.Fz(15), FontStyle.Bold), Fx.Alpha(Theme.Text, en), new RectangleF(tx, card.Y + Metrics.U(18), tw, Metrics.U(34)), StringAlignment.Near, StringAlignment.Near, true);
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Muted, en)))
            {
                sf.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(Subtitle ?? "", Fx.Ui(Metrics.Fz(10.5f)), b, new RectangleF(tx, card.Y + Metrics.U(54), tw, card.Bottom - card.Y - Metrics.U(60)), sf);
            }
            g.Restore(st);
        }
    }

    /// <summary>A small pill with a coloured dot: "Wi-Fi: Школа", "Флешка: E:".</summary>
    internal sealed class StatusChip : Control
    {
        private string text = "";
        private Color dot;

        public StatusChip()
        {
            Height = Metrics.U(40);
            Margin = new Padding(0, 0, Metrics.U(10), Metrics.U(8));
            dot = Theme.Muted;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Set(string t, Color c)
        {
            if (text == t && dot == c) return;
            text = t;
            dot = c;
            int w;
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
                w = (int)g.MeasureString(text, Fx.Ui(Metrics.Fz(11), FontStyle.Bold), 100000, StringFormat.GenericTypographic).Width;
            Width = w + Metrics.U(52);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (GraphicsPath p = Fx.Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Line, 1.3f)) g.DrawPath(pen, p);
            }
            float d = Metrics.U(12);
            using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, Metrics.U(16), (Height - d) / 2f, d, d);
            Fx.Text(g, text, Fx.Ui(Metrics.Fz(11), FontStyle.Bold), Theme.Text, new RectangleF(Metrics.U(36), 0, Width - Metrics.U(40), Height), StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    /// <summary>A flow of big tiles: one, two or three in a row depending on the width of the window.</summary>
    internal sealed class BigTileGrid : FlowLayoutPanel
    {
        public BigTileGrid()
        {
            WrapContents = true;
            FlowDirection = FlowDirection.LeftToRight;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = new Padding(0);
        }

        public void Relayout(int width)
        {
            int cols = width >= Metrics.U(1060) ? 3 : width >= Metrics.U(660) ? 2 : 1;
            int w = (width - 2) / cols;
            SuspendLayout();
            foreach (Control c in Controls) { c.Margin = new Padding(0); c.Size = new Size(w, Metrics.U(140)); }
            ResumeLayout();
        }
    }

    // =========================================================================================== home

    /// <summary>
    /// The first page of the teacher's centre: a greeting, what is connected (Wi-Fi, flash drive, sound), and nine big buttons with plain names.
    /// </summary>
    internal sealed class TeacherHomeSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();
        private StatusChip wifi, flash, sound, filter;
        private System.Threading.Timer poll;
        private BigTileGrid grid;

        public TeacherHomeSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
            Resize += delegate { Fit(); };
        }

        public override string Id { get { return "home"; } }
        public override string Title { get { return "Главная"; } }
        public override string Glyph { get { return ""; } }

        private int RowWidth { get { return Math.Max(320, root.ClientSize.Width - Metrics.U(30)); } }

        private void Fit()
        {
            if (root.Controls.Count == 0) return;
            int w = RowWidth;
            root.SuspendLayout();
            foreach (Control c in root.Controls)
            {
                c.MaximumSize = new Size(w, 0);
                HeroCard hero = c as HeroCard;
                if (hero != null) hero.Width = w;
                BigTileGrid g = c as BigTileGrid;
                if (g != null) g.Relayout(w);
            }
            root.ResumeLayout();
        }

        public override void OnShown()
        {
            Rebuild();
            if (poll == null) poll = new System.Threading.Timer(delegate { Poll(); }, null, 300, 5000);
        }

        private void Rebuild()
        {
            root.SuspendLayout();
            foreach (Control c in new List<Control>(Controls2(root))) { root.Controls.Remove(c); c.Dispose(); }

            HeroCard hero = new HeroCard();
            hero.Subtitle = "Что вы хотите сделать?";
            root.Controls.Add(hero);

            FlowLayoutPanel chips = UiKit.Flow();
            chips.Margin = new Padding(0, Metrics.U(14), 0, 0);
            wifi = new StatusChip(); flash = new StatusChip(); sound = new StatusChip(); filter = new StatusChip();
            wifi.Set("Wi-Fi: проверяю…", Theme.Muted);
            flash.Set("Флешка: проверяю…", Theme.Muted);
            sound.Set("Звук", Theme.Muted);
            filter.Set("Защита 18+ включена", Theme.Success);
            chips.Controls.Add(wifi); chips.Controls.Add(flash); chips.Controls.Add(sound); chips.Controls.Add(filter);
            root.Controls.Add(chips);

            root.Controls.Add(UiKit.Heading("Выберите, что нужно"));
            grid = new BigTileGrid();
            AddTile("Файлы и флешка", "Папки, документы, запись на флешку", "", delegate { Main.ShowSection("files"); });
            AddTile("Интернет", "Открыть браузер и сайты", "", delegate { OpenInternet(); });
            AddTile("Wi-Fi и сеть", "Подключиться к интернету", "", delegate { Main.ShowSection("network"); });
            AddTile("Документы", "Word, Excel, презентации, PDF", "", delegate { Main.ShowSection("docs"); });
            AddTile("Фото, видео, музыка", "Картинки, ролики и песни", "", delegate { Main.ShowSection("media"); });
            AddTile("Программы", "Все программы на компьютере", "", delegate { Main.ShowSection("apps"); });
            AddTile("Экран и проектор", "Показать на проекторе, яркость", "", delegate { Main.ShowSection("settings"); });
            AddTile("Звук", "Громче, тише, колонки, наушники", "", delegate { Main.ShowSection("settings"); });
            AddTile("Помощь", "Простые подсказки, как это делается", "", delegate { Main.ShowSection("help"); });
            root.Controls.Add(grid);
            Fit();
            root.ResumeLayout();
            Poll();
        }

        private void AddTile(string title, string sub, string glyph, EventHandler click)
        {
            BigTile t = new BigTile(title, sub, glyph);
            t.Activated += click;
            grid.Controls.Add(t);
        }

        private static IEnumerable<Control> Controls2(Control c)
        {
            foreach (Control x in c.Controls) yield return x;
        }

        private void OpenInternet()
        {
            string url = "https://ya.ru";
            try { string hp = Main.Cfg.Browser.HomePage; if (!string.IsNullOrEmpty(hp) && hp.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = hp; } catch { }
            WinTools.Open(url, null);
        }

        /// <summary>Looks at what is connected, off the window thread, and updates the pills.</summary>
        private void Poll()
        {
            if (IsDisposed || Parent == null) return;
            string wifiText; Color wifiColor;
            string flashText; Color flashColor;
            string soundText = "Звук";
            try
            {
                WifiNetwork cur = Wifi.Available ? Wifi.Current() : null;
                if (cur != null) { wifiText = "Wi-Fi: " + cur.Ssid; wifiColor = Theme.Success; }
                else if (NetworkInterface.GetIsNetworkAvailable()) { wifiText = "Сеть подключена"; wifiColor = Theme.Success; }
                else { wifiText = "Нет подключения к сети"; wifiColor = Theme.Warn; }
            }
            catch { wifiText = "Сеть"; wifiColor = Theme.Muted; }
            try
            {
                int n = 0; string first = "";
                foreach (PlaceInfo d in TeacherPlaces.Disks()) if (d.Removable) { n++; if (first.Length == 0) first = d.Title; }
                if (n == 0) { flashText = "Флешка не вставлена"; flashColor = Theme.Muted; }
                else { flashText = n == 1 ? "Флешка: " + first : "Флешек: " + n; flashColor = Theme.Success; }
            }
            catch { flashText = "Флешка"; flashColor = Theme.Muted; }
            try { if (AudioControl.Available) soundText = AudioControl.Muted ? "Звук выключен" : "Громкость " + (int)Math.Round(AudioControl.Volume * 100) + "%"; } catch { }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (wifi == null || wifi.IsDisposed) return;
                    wifi.Set(wifiText, wifiColor);
                    flash.Set(flashText, flashColor);
                    sound.Set(soundText, Theme.Accent);
                }));
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && poll != null) poll.Dispose();
            base.Dispose(disposing);
        }
    }

    // =========================================================================================== settings

    /// <summary>
    /// "Settings" in plain words: sound, brightness, the projector, text size, theme, printers and the Windows pages a teacher may need,
    /// power. Everything is a big button or a slider; nothing needs the Windows Settings app unless the teacher asks for it.
    /// </summary>
    internal sealed class TeacherSettingsSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();

        public TeacherSettingsSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
        }

        public override string Id { get { return "settings"; } }
        public override string Title { get { return "Настройки"; } }
        public override string Glyph { get { return ""; } }

        public override void OnShown() { Rebuild(); }

        private static MotionButton Big(string text, string glyph, bool primary, EventHandler click)
        {
            MotionButton b = (MotionButton)(primary ? UiKit.Accent(text, click) : UiKit.Flat(text, click));
            b.Glyph = glyph;
            b.Font = Fx.Ui(Metrics.Fz(12), primary ? FontStyle.Bold : FontStyle.Regular);
            b.AutoSize = false;
            b.Size = new Size(b.GetPreferredSize(Size.Empty).Width + Metrics.U(10), Metrics.U(54));
            b.Margin = new Padding(0, 0, Metrics.U(10), Metrics.U(10));
            return b;
        }

        private FlowLayoutPanel Row()
        {
            FlowLayoutPanel f = UiKit.Flow();
            f.MaximumSize = new Size(Math.Max(320, root.ClientSize.Width - Metrics.U(40)), 0);
            return f;
        }

        private Label Hint(string text)
        {
            Label l = UiKit.Hint(text);
            l.Font = Theme.UiFont(Metrics.Fz(11));
            l.MaximumSize = new Size(Math.Max(320, root.ClientSize.Width - Metrics.U(60)), 0);
            return l;
        }

        private static Label Heading(string text)
        {
            Label l = UiKit.Heading(text);
            l.Font = Theme.UiFont(Metrics.Fz(16), FontStyle.Bold);
            return l;
        }

        private void Slider(FlowLayoutPanel row, int value, Action<int> changed, string suffix)
        {
            SliderBar s = new SliderBar();
            s.Width = Math.Min(Metrics.U(520), Math.Max(Metrics.U(240), root.ClientSize.Width - Metrics.U(200)));
            s.Height = Metrics.U(44);
            s.BackColor = Theme.Background;
            s.SetSilently(value);
            Label pct = new Label();
            pct.AutoSize = false;
            pct.Size = new Size(Metrics.U(90), Metrics.U(44));
            pct.TextAlign = ContentAlignment.MiddleLeft;
            pct.Font = Theme.UiFont(Metrics.Fz(14), FontStyle.Bold);
            pct.ForeColor = Theme.Text;
            pct.Text = value + suffix;
            s.ValueChanged += delegate { pct.Text = s.Value + suffix; changed(s.Value); };
            row.Controls.Add(s);
            row.Controls.Add(pct);
        }

        private void Rebuild()
        {
            root.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in root.Controls) old.Add(c);
            foreach (Control c in old) { root.Controls.Remove(c); c.Dispose(); }

            // ---- sound
            root.Controls.Add(Heading("Звук"));
            if (AudioControl.Available)
            {
                FlowLayoutPanel row = Row();
                Slider(row, (int)Math.Round(AudioControl.Volume * 100), delegate (int v) { AudioControl.Muted = false; AudioControl.Volume = v / 100f; }, "%");
                root.Controls.Add(row);
                MotionCheck mute = new MotionCheck();
                mute.Text = "Выключить звук";
                mute.Font = Theme.UiFont(Metrics.Fz(12));
                mute.ForeColor = Theme.Text;
                mute.Checked = AudioControl.Muted;
                mute.CheckedChanged += delegate { AudioControl.Muted = mute.Checked; };
                root.Controls.Add(mute);
            }
            else root.Controls.Add(Hint("Управление громкостью недоступно на этом компьютере."));
            FlowLayoutPanel sr = Row();
            sr.Controls.Add(Big("Выбрать колонки или наушники", "", false, delegate { WinTools.Settings("sound"); }));
            root.Controls.Add(sr);

            // ---- brightness
            int br = Brightness.Get();
            if (br >= 0)
            {
                root.Controls.Add(Heading("Яркость экрана"));
                FlowLayoutPanel row = Row();
                Slider(row, br, delegate (int v) { Brightness.Set(v); }, "%");
                root.Controls.Add(row);
            }

            // ---- projector
            root.Controls.Add(Heading("Экран и проектор"));
            root.Controls.Add(Hint("Подключите проектор кабелем, затем нажмите нужную кнопку."));
            FlowLayoutPanel pr = Row();
            pr.Controls.Add(Big("Повторить на проекторе", "", true, delegate { WinTools.Display("clone"); }));
            pr.Controls.Add(Big("Расширить на два экрана", "", false, delegate { WinTools.Display("extend"); }));
            pr.Controls.Add(Big("Только проектор", "", false, delegate { WinTools.Display("external"); }));
            pr.Controls.Add(Big("Только этот экран", "", false, delegate { WinTools.Display("internal"); }));
            root.Controls.Add(pr);

            // ---- text size
            root.Controls.Add(Heading("Размер текста"));
            root.Controls.Add(Hint("Если буквы мелкие, выберите «Крупный» или «Очень крупный»: весь центр станет крупнее."));
            FlowLayoutPanel tr = Row();
            string now = Main.TextSizeNow;
            tr.Controls.Add(Big("Обычный", "", now == "normal", delegate { Main.SetTextSize("normal"); }));
            tr.Controls.Add(Big("Крупный", "", now == "large", delegate { Main.SetTextSize("large"); }));
            tr.Controls.Add(Big("Очень крупный", "", now == "huge", delegate { Main.SetTextSize("huge"); }));
            root.Controls.Add(tr);

            // ---- appearance
            root.Controls.Add(Heading("Оформление"));
            FlowLayoutPanel ar = Row();
            ar.Controls.Add(Big("Светлая тема", "", !Theme.Dark, delegate { if (Theme.Dark) Main.ToggleTheme(); }));
            ar.Controls.Add(Big("Тёмная тема", "", Theme.Dark, delegate { if (!Theme.Dark) Main.ToggleTheme(); }));
            root.Controls.Add(ar);

            // ---- devices and Windows pages
            root.Controls.Add(Heading("Принтеры и устройства"));
            FlowLayoutPanel dr = Row();
            dr.Controls.Add(Big("Принтеры", "", false, delegate { WinTools.Settings("printers"); }));
            dr.Controls.Add(Big("Bluetooth", "", false, delegate { WinTools.Settings("bluetooth"); }));
            dr.Controls.Add(Big("Мышь и клавиатура", "", false, delegate { WinTools.Settings("mousetouchpad"); }));
            dr.Controls.Add(Big("Дата и время", "", false, delegate { WinTools.Settings("dateandtime"); }));
            root.Controls.Add(dr);

            root.Controls.Add(Heading("Все возможности Windows"));
            root.Controls.Add(Hint("Здесь ничего не закрыто: фильтр защищает только от материалов 18+."));
            FlowLayoutPanel wr = Row();
            wr.Controls.Add(Big("Параметры Windows", "", false, delegate { WinTools.Settings(""); }));
            wr.Controls.Add(Big("Панель управления", "", false, delegate { WinTools.Open("control.exe", null); }));
            wr.Controls.Add(Big("Установка и удаление программ", "", false, delegate { WinTools.Open("appwiz.cpl", null); }));
            wr.Controls.Add(Big("Обновление Windows", "", false, delegate { WinTools.Settings("windowsupdate"); }));
            root.Controls.Add(wr);

            // ---- power
            root.Controls.Add(Heading("Питание"));
            FlowLayoutPanel pw = Row();
            pw.Controls.Add(Big("Выключить компьютер", "", false, delegate { Main.Power(false); }));
            pw.Controls.Add(Big("Перезагрузить", "", false, delegate { Main.Power(true); }));
            root.Controls.Add(pw);

            root.Controls.Add(Heading("О компьютере"));
            root.Controls.Add(Hint("Компьютер: " + Environment.MachineName + "   •   Пользователь: " + Environment.UserName));
            root.Controls.Add(Hint("SchoolShield " + AppInfo.VersionString + "   •   " + Release.Author));
            root.ResumeLayout();
        }
    }

    // =========================================================================================== help

    /// <summary>One answer that opens and closes: "How do I write files to a flash drive?".</summary>
    internal sealed class GuideCard : Control, IEnterAnimated
    {
        private readonly string title;
        private readonly string body;
        private bool open;
        private double grow;                 // 0 closed .. 1 open
        private Tween tween;
        private double enter = 1;
        private Tween enterTween;
        private readonly AnimValue hover;
        private int bodyHeight;

        public GuideCard(string title, string body)
        {
            this.title = title;
            this.body = body;
            Cursor = Cursors.Hand;
            hover = new AnimValue(this, 0);
            Margin = new Padding(0, 0, 0, Metrics.U(10));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private int HeadHeight { get { return Metrics.U(64); } }

        private void Measure()
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
                bodyHeight = (int)g.MeasureString(body, Fx.Ui(Metrics.Fz(12)), Math.Max(100, Width - Metrics.U(60))).Height + Metrics.U(28);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
        }

        public void SetWidth(int w)
        {
            Width = w;
            Measure();
            Height = HeadHeight + (int)(bodyHeight * grow);
        }

        public void PlayEnter(int index)
        {
            if (enterTween != null) enterTween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            enterTween = Tween.Run(460, Easing.OutCubic, delegate (double t) { enter = t; Invalidate(); }, null, Math.Min(index, 10) * 45);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            open = !open;
            if (tween != null) tween.Cancel();
            double from = grow, to = open ? 1 : 0;
            tween = Tween.Run(260, Easing.OutCubic, delegate (double t)
            {
                grow = from + (to - from) * t;
                Height = HeadHeight + (int)(bodyHeight * grow);
                Invalidate();
            });
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (tween != null) tween.Cancel(); if (enterTween != null) enterTween.Cancel(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            if (en < 0.003) return;
            double h = Math.Max(0, Math.Min(1, hover.Value));
            RectangleF r = new RectangleF(1, 1 + (float)((1 - en) * Metrics.U(14)), Width - 3, Height - 3);
            using (GraphicsPath p = Fx.Round(r, Metrics.U(16)))
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Card, Theme.CardHover, h * 0.6), en))) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Alpha(Fx.Blend(Theme.Line, Theme.Accent, Math.Max(h, grow)), en), 1.4f)) g.DrawPath(pen, p);
            }
            Fx.Text(g, title, Fx.Ui(Metrics.Fz(13), FontStyle.Bold), Fx.Alpha(Theme.Text, en), new RectangleF(r.X + Metrics.U(22), r.Y, r.Width - Metrics.U(80), HeadHeight - 2), StringAlignment.Near, StringAlignment.Center, true);
            // chevron that turns
            float cx = r.Right - Metrics.U(34), cy = r.Y + (HeadHeight - 2) / 2f, u = Metrics.U(7);
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform((float)(grow * 180));
            using (Pen pen = new Pen(Fx.Alpha(Theme.Accent, en), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new PointF[] { new PointF(-u, -u * 0.5f), new PointF(0, u * 0.5f), new PointF(u, -u * 0.5f) });
            g.Restore(st);
            if (grow > 0.02)
            {
                GraphicsState clip = g.Save();
                g.SetClip(new RectangleF(r.X, r.Y + HeadHeight - 2, r.Width, Math.Max(0, r.Height - HeadHeight + 2)));
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Text, en * Math.Min(1, grow * 1.5))))
                    g.DrawString(body, Fx.Ui(Metrics.Fz(12)), b, new RectangleF(r.X + Metrics.U(22), r.Y + HeadHeight - 2, r.Width - Metrics.U(44), bodyHeight));
                g.Restore(clip);
            }
        }
    }

    /// <summary>Short answers in plain words for the questions a teacher asks most.</summary>
    internal sealed class TeacherHelpSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();

        private static readonly string[][] Guides = new string[][] {
            new string[] { "Как записать файлы на флешку",
                "1. Вставьте флешку в компьютер и подождите несколько секунд.\n2. Откройте «Файлы и флешка».\n3. Найдите нужные файлы и нажмите на них. Чтобы выбрать несколько, держите кнопку Ctrl.\n4. Нажмите зелёную кнопку «На флешку».\n5. Внизу пройдёт полоска. Когда она станет зелёной, всё записано." },
            new string[] { "Как безопасно достать флешку",
                "В списке «Места» слева найдите флешку и нажмите на значок ⏏ справа от её названия. Когда появится сообщение «Флешку можно вынимать», вытащите её из компьютера. Если пишет, что не получилось: закройте файлы с флешки (Word, проигрыватель) и нажмите ещё раз." },
            new string[] { "Как перенести файлы в другую папку",
                "Самый простой способ: нажмите на файл, не отпуская кнопку мыши перетащите его на нужную папку (в списке или слева в «Местах») и отпустите.\nДругой способ: «Вырезать» → откройте нужную папку → «Вставить». Если файл с таким именем уже есть, новый получит номер (2), старый не пропадёт." },
            new string[] { "Как подключиться к Wi-Fi",
                "Откройте «Wi-Fi и сеть». Выберите свою сеть в списке и нажмите «Подключиться». Если сеть с замком, введите пароль (глазок показывает буквы). Через несколько секунд появится «Подключено»." },
            new string[] { "Как показать на проекторе",
                "Подключите проектор кабелем. Откройте «Настройки» → «Экран и проектор». «Повторить на проекторе» показывает то же, что на компьютере. «Расширить» делает проектор вторым экраном. Чтобы вернуть всё как было, нажмите «Только этот экран»." },
            new string[] { "Как сделать буквы крупнее",
                "«Настройки» → «Размер текста» → «Крупный» или «Очень крупный». Весь центр станет крупнее сразу." },
            new string[] { "Куда пропал мой файл",
                "Откройте «Файлы и флешка» и посмотрите в «Документах», «Загрузках» и на «Рабочем столе». Вверху есть поле поиска: оно ищет в той папке, которая открыта. Недавние документы видны в разделе «Документы»." },
            new string[] { "Что делает защита 18+",
                "На этом компьютере единственное ограничение: закрываются сайты и окна с материалами 18+ (порно, эротика). Всё остальное работает как обычно. Если урок биологии или искусства случайно закрылся, откройте его снова и скажите администратору: защиту можно подстроить." },
            new string[] { "Что делать, если всё зависло",
                "Подождите одну минуту: иногда компьютер просто думает. Если программа не отвечает, нажмите вместе Ctrl, Shift и Esc, выберите программу в списке и нажмите «Снять задачу». Потом откройте её заново." }
        };

        public TeacherHelpSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
            Resize += delegate { Fit(); };
        }

        public override string Id { get { return "help"; } }
        public override string Title { get { return "Помощь"; } }
        public override string Glyph { get { return ""; } }

        private void Fit()
        {
            int w = Math.Max(320, root.ClientSize.Width - Metrics.U(30));
            root.SuspendLayout();
            foreach (Control c in root.Controls)
            {
                GuideCard g = c as GuideCard;
                if (g != null) g.SetWidth(w);
                else c.MaximumSize = new Size(w, 0);
            }
            root.ResumeLayout();
        }

        public override void OnShown()
        {
            root.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in root.Controls) old.Add(c);
            foreach (Control c in old) { root.Controls.Remove(c); c.Dispose(); }
            Label intro = UiKit.Hint("Нажмите на вопрос, чтобы увидеть ответ.");
            intro.Font = Theme.UiFont(Metrics.Fz(12));
            intro.Margin = new Padding(0, Metrics.U(6), 0, Metrics.U(14));
            root.Controls.Add(intro);
            foreach (string[] g in Guides) root.Controls.Add(new GuideCard(g[0], g[1]));
            Fit();
            root.ResumeLayout();
        }
    }
}
