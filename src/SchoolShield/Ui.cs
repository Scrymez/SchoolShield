using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Screen-dependent sizes. The launcher must look right on a 1366x768 laptop at 125% as well as on a 1920x1080 monitor,
    /// so every pixel size goes through S() (DPI scale) and the layout switches to Compact / Tiny on small screens.
    /// </summary>
    internal static class Metrics
    {
        public static float Dpi = 1f;
        public static int LogicalWidth = 1920;
        public static int LogicalHeight = 1080;

        /// <summary>Logical width below 1250: smaller tiles, narrower sidebar.</summary>
        public static bool Compact { get { return LogicalWidth < 1250; } }
        /// <summary>Logical width below 1000: icon-only sidebar, stacked header.</summary>
        public static bool Tiny { get { return LogicalWidth < 1000; } }

        public static void Init()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Dpi = Math.Max(1f, g.DpiX / 96f);
                Rectangle b = Screen.PrimaryScreen.Bounds;
                LogicalWidth = (int)(b.Width / Dpi);
                LogicalHeight = (int)(b.Height / Dpi);
            }
            catch { }
        }

        public static int S(int px) { return (int)Math.Round(px * Dpi); }

        public static int SidebarWidth { get { return Tiny ? S(76) : Compact ? S(204) : S(236); } }
        public static int DockHeight { get { return S(40); } }
        public static int HeaderHeight { get { return Tiny ? S(104) : S(96); } }
        public static int PagePad { get { return Compact ? S(20) : S(32); } }
        public static Size TileSize { get { return Compact ? new Size(S(128), S(124)) : new Size(S(150), S(142)); } }
        public static int TileIcon { get { return Compact ? S(50) : S(60); } }
        public static float TitlePt { get { return Tiny ? 19f : Compact ? 21f : 25f; } }
    }

    internal static class Theme
    {
        public static bool Dark = true;
        public static Color Background, Header, Sidebar, Card, CardHover, Plate, Accent, Text, Muted, Danger, Line;

        static Theme() { Apply(true); }

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Background = Color.FromArgb(11, 18, 32);
                Header = Color.FromArgb(13, 22, 40);
                Sidebar = Color.FromArgb(7, 12, 24);
                Card = Color.FromArgb(17, 28, 51);
                CardHover = Color.FromArgb(30, 48, 84);
                Plate = Color.FromArgb(26, 255, 255, 255);
                Accent = Color.FromArgb(56, 189, 248);
                Text = Color.FromArgb(226, 232, 240);
                Muted = Color.FromArgb(148, 163, 184);
                Line = Color.FromArgb(30, 41, 59);
            }
            else
            {
                Background = Color.FromArgb(241, 245, 249);
                Header = Color.FromArgb(248, 250, 252);
                Sidebar = Color.FromArgb(226, 232, 240);
                Card = Color.White;
                CardHover = Color.FromArgb(224, 242, 254);
                Plate = Color.FromArgb(18, 15, 23, 42);
                Accent = Color.FromArgb(2, 132, 199);
                Text = Color.FromArgb(15, 23, 42);
                Muted = Color.FromArgb(100, 116, 139);
                Line = Color.FromArgb(203, 213, 225);
            }
            Danger = Color.FromArgb(185, 28, 28);
        }

        public static Color Lighten(Color c, int by)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + by), Math.Min(255, c.G + by), Math.Min(255, c.B + by));
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Segoe MDL2 Assets ships with Windows 10 and holds the system icon glyphs.</summary>
        public static Font IconFont(float size) { return new Font("Segoe MDL2 Assets", size); }
        public static Font UiFont(float size, FontStyle style) { return new Font("Segoe UI", size, style); }
        public static Font UiFont(float size) { return new Font("Segoe UI", size); }
    }

    /// <summary>Small factory methods so every screen looks the same.</summary>
    internal static class UiKit
    {
        public static Button Flat(string text, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Theme.Card;
            b.ForeColor = Theme.Text;
            b.Font = Theme.UiFont(10.5f);
            b.Cursor = Cursors.Hand;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(Metrics.S(10), Metrics.S(4), Metrics.S(10), Metrics.S(4));
            b.Margin = new Padding(0, 0, Metrics.S(8), 0);
            b.UseVisualStyleBackColor = false;
            if (click != null) b.Click += click;
            return b;
        }

        public static Button Accent(string text, EventHandler click)
        {
            Button b = Flat(text, click);
            b.BackColor = Theme.Accent;
            b.ForeColor = Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White;
            b.Font = Theme.UiFont(10.5f, FontStyle.Bold);
            return b;
        }

        public static Label Heading(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.UiFont(Metrics.Compact ? 13 : 15, FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.AutoSize = true;
            l.Margin = new Padding(0, Metrics.S(14), 0, Metrics.S(8));
            return l;
        }

        public static Label Hint(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.UiFont(10.5f);
            l.ForeColor = Theme.Muted;
            l.AutoSize = true;
            l.Margin = new Padding(0, 4, 0, 4);
            return l;
        }

        public static FlowLayoutPanel Flow()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.WrapContents = true;
            f.BackColor = Theme.Background;
            f.Margin = new Padding(0);
            return f;
        }

        public static ListView List(bool tile)
        {
            ListView lv = new ListView();
            lv.BackColor = Theme.Background;
            lv.ForeColor = Theme.Text;
            lv.BorderStyle = BorderStyle.None;
            lv.Font = Theme.UiFont(11);
            lv.Dock = DockStyle.Fill;
            lv.HideSelection = false;
            lv.MultiSelect = false;
            lv.View = tile ? View.Tile : View.LargeIcon;
            return lv;
        }
    }

    /// <summary>Thread-safe access to the UI thread for background components.</summary>
    internal static class Ui
    {
        public static Control Sync;

        public static void Toast(string message)
        {
            Control c = Sync;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            try
            {
                c.BeginInvoke(new Action(delegate
                {
                    try { new ToastForm(message).Show(); } catch { }
                }));
            }
            catch { }
        }
    }

    /// <summary>A program (or shortcut) tile in the launcher grid.</summary>
    internal sealed class TileControl : Control
    {
        public readonly AppEntry Entry;
        public string Title;
        public string Glyph;
        public bool Star;
        private bool hover;

        public TileControl(AppEntry entry)
        {
            Entry = entry;
            Title = entry.Name;
            Init();
        }

        /// <summary>A tile that is not a program (section shortcut, website).</summary>
        public TileControl(string title, string glyph)
        {
            Title = title;
            Glyph = glyph;
            Init();
        }

        private void Init()
        {
            Size = Metrics.TileSize;
            Margin = new Padding(Metrics.S(7));
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Left click.</summary>
        public event EventHandler Activated;
        /// <summary>Right click (own limited menu).</summary>
        public event EventHandler ContextRequested;

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!ClientRectangle.Contains(e.Location)) return;
            if (e.Button == MouseButtons.Left && Activated != null) Activated(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right && ContextRequested != null) ContextRequested(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Background);

            int radius = Metrics.S(16);
            Rectangle card = new Rectangle(1, 1, Width - 3, Height - 3);
            Color top = hover ? Theme.CardHover : (Theme.Dark ? Theme.Lighten(Theme.Card, 9) : Theme.Card);
            Color bottom = hover ? Theme.CardHover : Theme.Card;
            using (GraphicsPath path = Theme.Rounded(card, radius))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(card, top, bottom, 90f))
                    g.FillPath(b, path);
                using (Pen p = new Pen(hover ? Theme.Accent : Theme.Line, hover ? 2f : 1f))
                    g.DrawPath(p, path);
            }

            int icon = Metrics.TileIcon;
            int plate = icon + Metrics.S(14);
            Rectangle plateRect = new Rectangle((Width - plate) / 2, Metrics.S(12), plate, plate);
            using (GraphicsPath pp = Theme.Rounded(plateRect, Metrics.S(14)))
            using (SolidBrush b = new SolidBrush(Theme.Plate))
                g.FillPath(b, pp);
            Rectangle iconRect = new Rectangle(plateRect.X + (plate - icon) / 2, plateRect.Y + (plate - icon) / 2, icon, icon);

            if (Entry != null && Entry.Icon != null)
            {
                g.DrawImage(Entry.Icon, iconRect);
            }
            else if (Glyph != null)
            {
                using (Font f = Theme.IconFont(Metrics.Compact ? 26 : 32))
                using (StringFormat sf = new StringFormat())
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(Glyph, f, b, iconRect, sf);
                }
            }
            else
            {
                int hue = Math.Abs(Title.GetHashCode()) % 6;
                Color[] palette = new Color[] {
                    Color.FromArgb(14, 116, 144), Color.FromArgb(79, 70, 229), Color.FromArgb(5, 150, 105),
                    Color.FromArgb(217, 119, 6), Color.FromArgb(190, 24, 93), Color.FromArgb(71, 85, 105)
                };
                using (SolidBrush b = new SolidBrush(palette[hue]))
                    g.FillEllipse(b, iconRect);
                string letter = Title.Length > 0 ? Title.Substring(0, 1).ToUpper() : "?";
                using (Font f = new Font("Segoe UI", Metrics.Compact ? 20 : 24, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(letter, f, Brushes.White, iconRect, sf);
                }
            }

            if (Star)
                using (Font f = Theme.IconFont(11))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 204, 21)))
                    g.DrawString("", f, b, Width - Metrics.S(26), Metrics.S(8));

            Rectangle textRect = new Rectangle(Metrics.S(8), plateRect.Bottom + Metrics.S(6), Width - Metrics.S(16), Height - plateRect.Bottom - Metrics.S(10));
            using (Font f = new Font("Segoe UI", Metrics.Compact ? 9.5f : 10.5f))
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(Theme.Text))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(Title, f, b, textRect, sf);
            }
        }
    }

    /// <summary>Entry of the left navigation bar. In Tiny mode only the icon is drawn.</summary>
    internal sealed class SidebarButton : Control
    {
        public string Glyph;
        public string Caption;
        public bool Selected;
        private bool hover;

        public SidebarButton(string glyph, string caption)
        {
            Glyph = glyph;
            Caption = caption;
            Height = Metrics.Compact ? Metrics.S(46) : Metrics.S(52);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Sidebar);
            if (Selected || hover)
            {
                Rectangle r = new Rectangle(Metrics.S(8), Metrics.S(3), Width - Metrics.S(16), Height - Metrics.S(6));
                using (GraphicsPath p = Theme.Rounded(r, Metrics.S(10)))
                using (SolidBrush b = new SolidBrush(Selected ? Theme.Card : Theme.CardHover))
                    g.FillPath(b, p);
            }
            if (Selected)
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, 0, Metrics.S(12), Metrics.S(4), Height - Metrics.S(24));

            float iconPt = Metrics.Tiny ? 17 : 15;
            using (Font f = Theme.IconFont(iconPt))
            using (SolidBrush b = new SolidBrush(Selected ? Theme.Accent : Theme.Muted))
            {
                if (Metrics.Tiny)
                {
                    SizeF sz = g.MeasureString(Glyph, f);
                    g.DrawString(Glyph, f, b, (Width - sz.Width) / 2, (Height - sz.Height) / 2);
                }
                else g.DrawString(Glyph, f, b, Metrics.S(22), (Height - g.MeasureString(Glyph, f).Height) / 2);
            }
            if (!Metrics.Tiny)
                using (Font f = Theme.UiFont(Metrics.Compact ? 10.5f : 12, Selected ? FontStyle.Bold : FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(Theme.Text))
                using (StringFormat sf = new StringFormat())
                {
                    sf.LineAlignment = StringAlignment.Center;
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(Caption, f, b, new RectangleF(Metrics.S(58), 0, Width - Metrics.S(62), Height), sf);
                }
        }
    }

    /// <summary>Rounded "pill" button for category tabs.</summary>
    internal sealed class PillButton : Control
    {
        public bool Selected;
        private bool hover;
        private readonly string caption;

        public PillButton(string text, bool selected)
        {
            caption = text;
            Selected = selected;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, Metrics.S(8), Metrics.S(8));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (Font f = Theme.UiFont(Metrics.Compact ? 10f : 10.5f))
                Size = new Size(TextRenderer.MeasureText(text, f).Width + Metrics.S(30), Metrics.S(34));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Background);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Selected ? Theme.Accent : (hover ? Theme.CardHover : Theme.Card)))
                    g.FillPath(b, p);
                if (!Selected)
                    using (Pen pen = new Pen(Theme.Line)) g.DrawPath(pen, p);
            }
            using (Font f = Theme.UiFont(Metrics.Compact ? 10f : 10.5f, Selected ? FontStyle.Bold : FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(Selected ? (Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White) : Theme.Text))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(caption, f, b, new RectangleF(0, 0, Width, Height), sf);
            }
        }
    }

    /// <summary>Search field with a magnifier and a placeholder that stays readable on the dark theme.</summary>
    internal sealed class SearchBox : Panel
    {
        private readonly TextBox box = new TextBox();
        private readonly string placeholder;
        private bool showingPlaceholder;
        private bool silent;

        public event EventHandler QueryChanged;

        public SearchBox(string placeholder)
        {
            this.placeholder = placeholder;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Metrics.S(38);
            box.BorderStyle = BorderStyle.None;
            box.Font = Theme.UiFont(Metrics.Compact ? 11 : 12);
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            Controls.Add(box);
            box.GotFocus += delegate { if (showingPlaceholder) SetPlaceholder(false); Invalidate(); };
            box.LostFocus += delegate { if (box.Text.Length == 0) SetPlaceholder(true); Invalidate(); };
            box.TextChanged += delegate { if (!silent && !showingPlaceholder && QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            SetPlaceholder(true);
            Resize += delegate { LayoutBox(); };
            LayoutBox();
        }

        private void LayoutBox()
        {
            int h = box.PreferredHeight;
            box.SetBounds(Metrics.S(40), (Height - h) / 2, Math.Max(20, Width - Metrics.S(52)), h);
        }

        private void SetPlaceholder(bool on)
        {
            silent = true;
            showingPlaceholder = on;
            box.Text = on ? placeholder : "";
            box.ForeColor = on ? Theme.Muted : Theme.Text;
            silent = false;
        }

        public string Query { get { return showingPlaceholder ? "" : box.Text.Trim(); } }

        public void ClearQuiet()
        {
            silent = true;
            if (!box.Focused) SetPlaceholder(true); else box.Text = "";
            silent = false;
        }

        public void SetQuery(string q)
        {
            silent = true;
            showingPlaceholder = false;
            box.ForeColor = Theme.Text;
            box.Text = q;
            box.SelectionStart = q.Length;
            silent = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Header);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(box.Focused ? Theme.Accent : Theme.Line, box.Focused ? 2f : 1f)) g.DrawPath(pen, p);
            }
            using (Font f = Theme.IconFont(12))
            using (SolidBrush b = new SolidBrush(Theme.Muted))
                g.DrawString("", f, b, Metrics.S(14), (Height - g.MeasureString("", f).Height) / 2);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); box.Focus(); }
    }

    /// <summary>Horizontal slider (volume). A TrackBar would ignore the dark theme.</summary>
    internal sealed class SliderBar : Control
    {
        private int value;
        private bool dragging;

        public int Minimum = 0;
        public int Maximum = 100;
        public event EventHandler ValueChanged;

        public SliderBar()
        {
            Height = Metrics.S(34);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetSilently(int v) { value = Math.Max(Minimum, Math.Min(Maximum, v)); Invalidate(); }

        private int Pad { get { return Metrics.S(12); } }

        private void FromMouse(int x)
        {
            int w = Math.Max(1, Width - 2 * Pad);
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * (double)(x - Pad) / w);
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Capture = true; FromMouse(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) FromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            int track = Metrics.S(6);
            int y = (Height - track) / 2;
            int w = Width - 2 * Pad;
            int fill = (int)(w * (value - Minimum) / (double)Math.Max(1, Maximum - Minimum));
            using (GraphicsPath p = Theme.Rounded(new Rectangle(Pad, y, w, track), track / 2))
            using (SolidBrush b = new SolidBrush(Theme.Line))
                g.FillPath(b, p);
            if (fill > 0)
                using (GraphicsPath p = Theme.Rounded(new Rectangle(Pad, y, fill, track), track / 2))
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillPath(b, p);
            int d = Metrics.S(20);
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, Pad + fill - d / 2, (Height - d) / 2, d, d);
            using (Pen pen = new Pen(Theme.Accent, 2))
                g.DrawEllipse(pen, Pad + fill - d / 2, (Height - d) / 2, d, d);
        }
    }

    /// <summary>Red banner that appears briefly (blocked content, deleted download...).</summary>
    internal sealed class ToastForm : Form
    {
        private readonly Timer timer = new Timer();

        public ToastForm(string text)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Danger;

            Label l = new Label();
            l.Text = text;
            l.ForeColor = Color.White;
            l.Font = new Font("Segoe UI", Metrics.Compact ? 12 : 14, FontStyle.Bold);
            l.AutoSize = true;
            l.MaximumSize = new Size(Math.Max(300, Screen.PrimaryScreen.Bounds.Width - Metrics.S(120)), 0);
            l.Padding = new Padding(Metrics.S(24), Metrics.S(12), Metrics.S(24), Metrics.S(12));
            Controls.Add(l);
            Size = l.PreferredSize;

            Rectangle s = Screen.PrimaryScreen.Bounds;
            Location = new Point(s.X + (s.Width - Width) / 2, s.Y + Metrics.DockHeight + Metrics.S(12));

            timer.Interval = 4000;
            timer.Tick += delegate { timer.Stop(); Close(); };
            Shown += delegate { timer.Start(); };
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>"Hold the keys…" progress, shown while the admin chord is down so the administrator sees it is detected.</summary>
    internal sealed class HoldOverlay : Form
    {
        private double progress;

        public HoldOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(7, 12, 24);
            Size = new Size(Metrics.S(360), Metrics.S(64));
            DoubleBuffered = true;
            Rectangle s = Screen.PrimaryScreen.Bounds;
            Location = new Point(s.X + (s.Width - Width) / 2, s.Y + Metrics.DockHeight + Metrics.S(16));
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

        public void SetProgress(double p)
        {
            progress = p;
            if (!Visible) Show();
            Invalidate();
        }

        public void HideNow()
        {
            if (Visible) Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (Pen pen = new Pen(Color.FromArgb(56, 189, 248), 2))
                g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            using (Font f = new Font("Segoe UI", 11, FontStyle.Bold))
                g.DrawString("Удерживайте клавиши…", f, Brushes.White, Metrics.S(16), Metrics.S(10));
            Rectangle bar = new Rectangle(Metrics.S(16), Metrics.S(40), Width - Metrics.S(32), Metrics.S(10));
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 41, 59)))
                g.FillRectangle(b, bar);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(56, 189, 248)))
                g.FillRectangle(b, bar.X, bar.Y, (int)(bar.Width * Math.Max(0, Math.Min(1, progress))), bar.Height);
        }
    }
}
