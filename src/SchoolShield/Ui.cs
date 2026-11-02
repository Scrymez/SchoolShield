using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SchoolShield
{
    internal static class Theme
    {
        public static bool Dark = true;
        public static Color Background, Sidebar, Card, CardHover, Accent, Text, Muted, Danger, Line;

        static Theme() { Apply(true); }

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Background = Color.FromArgb(11, 18, 32);
                Sidebar = Color.FromArgb(7, 12, 24);
                Card = Color.FromArgb(17, 28, 51);
                CardHover = Color.FromArgb(30, 48, 84);
                Accent = Color.FromArgb(56, 189, 248);
                Text = Color.FromArgb(226, 232, 240);
                Muted = Color.FromArgb(148, 163, 184);
                Line = Color.FromArgb(30, 41, 59);
            }
            else
            {
                Background = Color.FromArgb(241, 245, 249);
                Sidebar = Color.FromArgb(226, 232, 240);
                Card = Color.White;
                CardHover = Color.FromArgb(224, 242, 254);
                Accent = Color.FromArgb(2, 132, 199);
                Text = Color.FromArgb(15, 23, 42);
                Muted = Color.FromArgb(100, 116, 139);
                Line = Color.FromArgb(203, 213, 225);
            }
            Danger = Color.FromArgb(185, 28, 28);
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetPlaceholder(TextBox box, string text)
        {
            try { SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, text); } catch { }
        }

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
            b.Padding = new Padding(10, 4, 10, 4);
            b.Margin = new Padding(0, 0, 8, 0);
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
            l.Font = Theme.UiFont(15, FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.AutoSize = true;
            l.Margin = new Padding(0, 14, 0, 8);
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
            Size = new Size(150, 142);
            Margin = new Padding(8);
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

            Rectangle card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(card, 14))
            using (SolidBrush b = new SolidBrush(hover ? Theme.CardHover : Theme.Card))
                g.FillPath(b, path);
            if (hover)
                using (GraphicsPath path = Theme.Rounded(card, 14))
                using (Pen p = new Pen(Theme.Accent, 2))
                    g.DrawPath(p, path);

            Rectangle iconRect = new Rectangle((Width - 64) / 2, 16, 64, 64);
            if (Entry != null && Entry.Icon != null)
            {
                g.DrawImage(Entry.Icon, iconRect);
            }
            else if (Glyph != null)
            {
                using (Font f = Theme.IconFont(34))
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
                using (Font f = new Font("Segoe UI", 26, FontStyle.Bold))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(letter, f, Brushes.White, iconRect, sf);
                }
            }

            if (Star)
                using (Font f = Theme.IconFont(12))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(250, 204, 21)))
                    g.DrawString("", f, b, Width - 26, 8);

            Rectangle textRect = new Rectangle(8, 88, Width - 16, Height - 96);
            using (Font f = new Font("Segoe UI", 10.5f))
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

    /// <summary>Entry of the left navigation bar.</summary>
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
            Height = 52;
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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Sidebar);
            if (Selected || hover)
            {
                Rectangle r = new Rectangle(10, 4, Width - 20, Height - 8);
                using (GraphicsPath p = Theme.Rounded(r, 10))
                using (SolidBrush b = new SolidBrush(Selected ? Theme.Card : Theme.CardHover))
                    g.FillPath(b, p);
            }
            if (Selected)
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, 0, 12, 4, Height - 24);

            using (Font f = Theme.IconFont(16))
            using (SolidBrush b = new SolidBrush(Selected ? Theme.Accent : Theme.Muted))
                g.DrawString(Glyph, f, b, 26, 14);
            using (Font f = Theme.UiFont(12, Selected ? FontStyle.Bold : FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(Theme.Text))
                g.DrawString(Caption, f, b, 64, 13);
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
            l.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            l.AutoSize = true;
            l.Padding = new Padding(24, 12, 24, 12);
            Controls.Add(l);
            Size = l.PreferredSize;

            Rectangle s = Screen.PrimaryScreen.Bounds;
            Location = new Point(s.X + (s.Width - Width) / 2, s.Y + 52);

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
}
