using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Screen-dependent sizes. The launcher must look right on a 1366x768 laptop at 125% as well as on a 1920x1080 monitor:
    /// every pixel size goes through S() (DPI) or U() (DPI and the size of the screen), fonts of the launcher through Fz(),
    /// and the layout switches to Compact / Tiny on small screens.
    /// </summary>
    internal static class Metrics
    {
        public static float Dpi = 1f;
        public static float UiScale = 1f;
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
                // 1600x900 (logical) is the reference screen: bigger screens get bigger controls, small laptops get smaller ones.
                float k = Math.Min(LogicalWidth / 1600f, LogicalHeight / 900f);
                UiScale = Math.Max(0.82f, Math.Min(1.25f, k));
            }
            catch { }
        }

        /// <summary>Pixels for DPI only (admin panel, dialogs).</summary>
        public static int S(int px) { return (int)Math.Round(px * Dpi); }

        /// <summary>Pixels for DPI and for the size of the screen (the launcher).</summary>
        public static int U(int px) { return (int)Math.Round(px * Dpi * UiScale); }

        /// <summary>Font size in points for the launcher: a little smaller on small screens, a little bigger on large ones.</summary>
        public static float Fz(float pt) { return pt * Math.Max(0.9f, Math.Min(1.15f, UiScale)); }

        public static int SidebarWidth { get { return Tiny ? U(86) : Compact ? U(228) : U(256); } }
        public static int DockHeight { get { return S(40); } }
        public static int HeaderHeight { get { return Tiny ? U(132) : U(112); } }
        public static int PagePad { get { return Compact ? U(24) : U(40); } }
        public static Size TileSize { get { return new Size(U(166), U(156)); } }
        public static int TileIcon { get { return U(60); } }
        public static float TitlePt { get { return Fz(Tiny ? 22f : 27f); } }
    }

    internal static class Theme
    {
        public static bool Dark = true;
        public static Color Background, Header, Sidebar, Card, CardHover, Plate, Accent, Accent2, OnAccent, Text, Muted, Danger, Line, Success, Warn;

        static Theme() { Apply(true); }

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Background = Color.FromArgb(10, 14, 28);
                Header = Color.FromArgb(10, 14, 28);
                Sidebar = Color.FromArgb(7, 10, 21);
                Card = Color.FromArgb(19, 26, 48);
                CardHover = Color.FromArgb(29, 40, 72);
                Plate = Color.FromArgb(22, 255, 255, 255);
                Accent = Color.FromArgb(34, 211, 238);
                Accent2 = Color.FromArgb(129, 140, 248);
                OnAccent = Color.FromArgb(6, 16, 31);
                Text = Color.FromArgb(232, 237, 248);
                Muted = Color.FromArgb(140, 152, 178);
                Line = Color.FromArgb(34, 45, 74);
                Danger = Color.FromArgb(244, 63, 94);
                Success = Color.FromArgb(52, 211, 153);
                Warn = Color.FromArgb(251, 191, 36);
            }
            else
            {
                Background = Color.FromArgb(243, 246, 252);
                Header = Color.FromArgb(243, 246, 252);
                Sidebar = Color.FromArgb(232, 237, 247);
                Card = Color.White;
                CardHover = Color.FromArgb(232, 240, 255);
                Plate = Color.FromArgb(14, 15, 23, 42);
                Accent = Color.FromArgb(8, 145, 178);
                Accent2 = Color.FromArgb(79, 70, 229);
                OnAccent = Color.White;
                Text = Color.FromArgb(15, 23, 42);
                Muted = Color.FromArgb(95, 110, 135);
                Line = Color.FromArgb(214, 222, 236);
                Danger = Color.FromArgb(225, 29, 72);
                Success = Color.FromArgb(5, 150, 105);
                Warn = Color.FromArgb(217, 119, 6);
            }
        }

        public static Color Lighten(Color c, int by)
        {
            return Color.FromArgb(c.A, Math.Min(255, c.R + by), Math.Min(255, c.G + by), Math.Min(255, c.B + by));
        }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            return Fx.Round(r, radius);
        }

        /// <summary>Segoe MDL2 Assets ships with Windows 10 and holds the system icon glyphs.</summary>
        public static Font IconFont(float size) { return new Font("Segoe MDL2 Assets", size); }
        public static Font UiFont(float size, FontStyle style) { return new Font("Segoe UI", size, style); }
        public static Font UiFont(float size) { return new Font("Segoe UI", size); }
    }

    internal enum BtnKind { Secondary, Primary, Danger, Ghost }

    /// <summary>Small factory methods so every screen looks the same.</summary>
    internal static class UiKit
    {
        public static Button Flat(string text, EventHandler click)
        {
            return new MotionButton(BtnKind.Secondary, text, click);
        }

        public static Button Accent(string text, EventHandler click)
        {
            return new MotionButton(BtnKind.Primary, text, click);
        }

        public static Button Danger(string text, EventHandler click)
        {
            return new MotionButton(BtnKind.Danger, text, click);
        }

        public static Label Heading(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.UiFont(Metrics.Fz(Metrics.Compact ? 13 : 15), FontStyle.Bold);
            l.ForeColor = Theme.Text;
            l.AutoSize = true;
            l.Margin = new Padding(0, Metrics.U(18), 0, Metrics.U(8));
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

        /// <summary>Set from the configuration: jokes in the messages for the student, or plain sentences.</summary>
        public static bool Sarcastic = true;

        /// <summary>Violations in this session (the number shown on the card and used for the remark about repeated attempts).</summary>
        public static int Attempts;
        private static string lastKey = "";
        private static int lastTick;

        /// <summary>A neutral notice (information, confirmations).</summary>
        public static void Toast(string message)
        {
            Control c = Sync;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            try
            {
                c.BeginInvoke(new Action(delegate
                {
                    try { ToastForm.Info(message); } catch { }
                }));
            }
            catch { }
        }

        /// <summary>A forbidden action: the card with the joke and the animation. <paramref name="detail"/> is what was caught (a site, a file).</summary>
        public static void Violation(ViolationKind kind, string detail)
        {
            Control c = Sync;
            if (c == null || c.IsDisposed || !c.IsHandleCreated) return;
            try
            {
                c.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        // the same thing caught again within a few seconds (a program that keeps restarting) is one violation
                        string key = kind + "|" + detail;
                        if (key == lastKey && Environment.TickCount - lastTick < 4000) return;
                        lastKey = key;
                        lastTick = Environment.TickCount;
                        Attempts++;
                        Joke j = Sarcasm.Pick(kind, Attempts, Sarcastic);
                        ViolationForm.Show(kind, detail, j, Attempts);
                    }
                    catch { }
                }));
            }
            catch { }
        }

        /// <summary>For places that only have the old text of the message.</summary>
        public static void Violation(string message)
        {
            Violation(Sarcasm.GuessKind(message), "");
        }
    }

    // =========================================================================================== buttons and switches

    /// <summary>Button with a spring hover, a press squeeze and a ripple. Same API as Button, so every old call site keeps working.</summary>
    internal sealed class MotionButton : Button
    {
        public BtnKind Kind;
        /// <summary>Text alignment: centred by default, left for list-like buttons (the task buttons of the dock).</summary>
        public StringAlignment HAlign = StringAlignment.Center;
        private readonly AnimValue hover;
        private readonly AnimValue press;
        private double ripple = -1;
        private PointF rippleAt;
        private bool customFill, customText;
        private Tween rippleTween;

        public MotionButton(BtnKind kind, string text, EventHandler click)
        {
            Kind = kind;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(10.5f, kind == BtnKind.Primary ? FontStyle.Bold : FontStyle.Regular);
            Cursor = Cursors.Hand;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Metrics.S(10), Metrics.S(4), Metrics.S(10), Metrics.S(4));
            Margin = new Padding(0, 0, Metrics.S(8), 0);
            hover = new AnimValue(this, 0);
            press = new AnimValue(this, 0);
            press.Stiffness = 600;
            press.Damping = 34;
            if (click != null) Click += click;
        }

        // Only an explicit assignment counts as a custom colour: the "changed" events also fire when the parent's colour is inherited.
        public override Color BackColor
        {
            get { return base.BackColor; }
            set { customFill = true; base.BackColor = value; }
        }

        public override Color ForeColor
        {
            get { return base.ForeColor; }
            set { customText = true; base.ForeColor = value; }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(t.Width + Padding.Horizontal + Metrics.S(26), Math.Max(t.Height + Padding.Vertical + Metrics.S(14), Metrics.S(38)));
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); press.To(0); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                press.To(1);
                rippleAt = e.Location;
                if (rippleTween != null) rippleTween.Cancel();
                ripple = 0;
                rippleTween = Tween.Run(560, Easing.OutCubic, delegate (double t) { ripple = t; Invalidate(); }, delegate { ripple = -1; Invalidate(); });
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { press.To(0); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);

            double h = Math.Max(0, Math.Min(1, hover.Value));
            double p = Math.Max(0, Math.Min(1, press.Value));
            float inset = (float)(1 + p * Metrics.S(2));
            RectangleF r = new RectangleF(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
            float radius = Math.Min(Metrics.S(12), r.Height / 2f);
            bool enabled = Enabled;
            double dim = enabled ? 1 : 0.45;

            Color textColor;
            using (GraphicsPath path = Fx.Round(r, radius))
            {
                if (customFill)
                {
                    Color fill = Fx.Blend(BackColor, Color.White, h * 0.14);
                    using (SolidBrush b = new SolidBrush(Fx.Alpha(fill, dim))) g.FillPath(b, path);
                    textColor = customText ? ForeColor : Color.White;
                }
                else switch (Kind)
                {
                    case BtnKind.Primary:
                        Color a = Fx.Blend(Theme.Accent, Color.White, h * 0.16), c2 = Fx.Blend(Theme.Accent2, Color.White, h * 0.16);
                        if (h > 0.01 && enabled) Fx.Shadow(g, new RectangleF(r.X, r.Y + Metrics.S(2), r.Width, r.Height), radius, Metrics.S(8), (int)(110 * h), Theme.Accent);
                        using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(a, dim), Fx.Alpha(c2, dim), 0f)) g.FillPath(b, path);
                        textColor = customText ? ForeColor : Theme.OnAccent;
                        break;
                    case BtnKind.Danger:
                        using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Danger, Color.White, h * 0.14), dim))) g.FillPath(b, path);
                        textColor = Color.White;
                        break;
                    case BtnKind.Ghost:
                        using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Card, h))) g.FillPath(b, path);
                        textColor = Fx.Blend(Theme.Muted, Theme.Text, h);
                        break;
                    default:
                        using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Card, Theme.CardHover, h), dim))) g.FillPath(b, path);
                        using (Pen pen = new Pen(Fx.Alpha(Fx.Blend(Theme.Line, Theme.Accent, h), dim), 1.4f)) g.DrawPath(pen, path);
                        textColor = customText ? ForeColor : Theme.Text;
                        break;
                }

                if (ripple >= 0 && enabled)
                {
                    GraphicsState st = g.Save();
                    g.SetClip(path);
                    float maxR = (float)Math.Sqrt(Width * Width + Height * Height);
                    float rr = (float)(ripple * maxR);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)((1 - ripple) * 70), 255, 255, 255)))
                        g.FillEllipse(b, rippleAt.X - rr, rippleAt.Y - rr, rr * 2, rr * 2);
                    g.Restore(st);
                }
                if (Focused && ShowFocusCues)
                    using (Pen pen = new Pen(Fx.Alpha(Theme.Accent, 0.9), 2f)) g.DrawPath(pen, path);
            }

            float left = HAlign == StringAlignment.Near ? Metrics.S(12) : 0;
            Fx.Text(g, Text, Font, Fx.Alpha(textColor, enabled ? 1 : 0.55), new RectangleF(left, 0, Width - left - (left > 0 ? Metrics.S(8) : 0), Height - (float)(p * 1)),
                HAlign, StringAlignment.Center, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && rippleTween != null) rippleTween.Cancel();
            base.Dispose(disposing);
        }
    }

    /// <summary>A CheckBox drawn as a switch: the knob slides, the track changes colour.</summary>
    internal sealed class MotionCheck : CheckBox
    {
        private readonly AnimValue on;
        private readonly AnimValue hover;

        public MotionCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AutoSize = true;
            ForeColor = Theme.Text;
            on = new AnimValue(this, 0);
            on.Stiffness = 420;
            on.Damping = 24;
            hover = new AnimValue(this, 0);
        }

        private int TrackW { get { return Metrics.S(44); } }
        private int TrackH { get { return Metrics.S(24); } }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size t = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? " " : Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(TrackW + Metrics.S(12) + t.Width + Metrics.S(6), Math.Max(TrackH + Metrics.S(6), t.Height + Metrics.S(6)));
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            on.To(Checked ? 1 : 0);
            base.OnCheckedChanged(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double v = Math.Max(0, Math.Min(1.15, on.Value));
            double vc = Math.Min(1, v);
            double h = Math.Max(0, Math.Min(1, hover.Value));
            double dim = Enabled ? 1 : 0.45;

            RectangleF track = new RectangleF(1, (Height - TrackH) / 2f, TrackW, TrackH);
            using (GraphicsPath p = Fx.Round(track, TrackH / 2f))
            {
                Color off = Fx.Blend(Theme.Line, Theme.Muted, 0.25 + h * 0.2);
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(off, Theme.Accent, vc), dim))) g.FillPath(b, p);
                if (vc > 0.01)
                    using (LinearGradientBrush b = Fx.Gradient(track, Fx.Alpha(Theme.Accent, vc * dim), Fx.Alpha(Theme.Accent2, vc * dim), 0f)) g.FillPath(b, p);
            }
            float d = TrackH - Metrics.S(8);
            float x = track.X + Metrics.S(4) + (float)(v * (TrackW - TrackH));
            RectangleF knob = new RectangleF(x, track.Y + Metrics.S(4), d, d);
            Fx.Shadow(g, new RectangleF(knob.X, knob.Y + 1, knob.Width, knob.Height), d / 2, Metrics.S(3), 90, Color.Black);
            using (SolidBrush b = new SolidBrush(Fx.Alpha(Color.White, dim))) g.FillEllipse(b, knob);

            if (Focused && ShowFocusCues)
                Fx.StrokeRound(g, Fx.Alpha(Theme.Accent, 0.9), 2f, RectangleF.Inflate(track, 2, 2), (TrackH + 4) / 2f);

            Fx.Text(g, Text, Font, Fx.Alpha(ForeColor, dim), new RectangleF(TrackW + Metrics.S(14), 0, Width - TrackW - Metrics.S(14), Height),
                StringAlignment.Near, StringAlignment.Center, false);
        }
    }

    // =========================================================================================== tiles

    /// <summary>Something on a page that plays an entrance when the page opens.</summary>
    internal interface IEnterAnimated
    {
        void PlayEnter(int index);
    }

    /// <summary>A program (or shortcut) tile in the launcher grid.</summary>
    internal sealed class TileControl : Control, IEnterAnimated
    {
        public readonly AppEntry Entry;
        public string Title;
        public string Glyph;
        public bool Star;

        private readonly AnimValue hover;
        private readonly AnimValue press;
        private double enter = 1;
        private Tween enterTween;

        public TileControl(AppEntry entry)
        {
            Entry = entry;
            Title = entry.Name;
            hover = new AnimValue(this, 0);
            press = new AnimValue(this, 0);
            press.Stiffness = 700;
            press.Damping = 38;
            Init();
        }

        /// <summary>A tile that is not a program (section shortcut, website).</summary>
        public TileControl(string title, string glyph)
        {
            Title = title;
            Glyph = glyph;
            hover = new AnimValue(this, 0);
            press = new AnimValue(this, 0);
            press.Stiffness = 700;
            press.Damping = 38;
            Init();
        }

        private void Init()
        {
            Size = Metrics.TileSize;
            Margin = new Padding(Metrics.U(4));
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Left click.</summary>
        public event EventHandler Activated;
        /// <summary>Right click (own limited menu).</summary>
        public event EventHandler ContextRequested;

        /// <summary>The tile pops in: it rises, grows and fades in. The delay makes a wave across the grid.</summary>
        public void PlayEnter(int index)
        {
            if (enterTween != null) enterTween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            enterTween = Tween.Run(560, Easing.OutBack, delegate (double t) { enter = Math.Max(0, t); Invalidate(); }, delegate { enter = 1; Invalidate(); },
                Math.Min(index, 24) * 32 + 40);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); press.To(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) press.To(1); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            press.To(0);
            base.OnMouseUp(e);
            if (!ClientRectangle.Contains(e.Location)) return;
            if (e.Button == MouseButtons.Left && Activated != null) Activated(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right && ContextRequested != null) ContextRequested(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && enterTween != null) enterTween.Cancel();
            base.Dispose(disposing);
        }

        private static readonly Color[][] Palette = new Color[][] {
            new Color[] { Color.FromArgb(34, 211, 238), Color.FromArgb(14, 116, 144) },
            new Color[] { Color.FromArgb(129, 140, 248), Color.FromArgb(67, 56, 202) },
            new Color[] { Color.FromArgb(52, 211, 153), Color.FromArgb(4, 120, 87) },
            new Color[] { Color.FromArgb(251, 191, 36), Color.FromArgb(180, 83, 9) },
            new Color[] { Color.FromArgb(244, 114, 182), Color.FromArgb(157, 23, 77) },
            new Color[] { Color.FromArgb(148, 163, 184), Color.FromArgb(51, 65, 85) }
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            if (en <= 0.003) return;

            double h = Math.Max(0, Math.Min(1.15, hover.Value));
            double pr = Math.Max(0, Math.Min(1, press.Value));
            int margin = Metrics.U(9);
            float lift = (float)(h * Metrics.U(5));
            float slide = (float)((1 - en) * Metrics.U(26));
            RectangleF card = new RectangleF(margin, margin - lift + slide, Width - margin * 2, Height - margin * 2);
            float radius = Metrics.U(20);
            float cx = card.X + card.Width / 2, cy = card.Y + card.Height / 2;
            float sc = (float)((0.9 + 0.1 * en) * (1 - 0.045 * pr) * (1 + 0.012 * h));

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(-cx, -cy);

            // soft shadow that grows with the lift
            Fx.Shadow(g, new RectangleF(card.X, card.Y + Metrics.U(5) + (float)(h * Metrics.U(3)), card.Width, card.Height), radius, Metrics.U(11),
                (int)((Theme.Dark ? 170 : 60) * (0.45 + 0.55 * h) * en), Theme.Dark ? Color.Black : Color.FromArgb(30, 41, 90));

            Color top = Fx.Blend(Theme.Lighten(Theme.Card, Theme.Dark ? 9 : 0), Theme.CardHover, h);
            Color bottom = Fx.Blend(Theme.Card, Theme.CardHover, h * 0.8);
            using (GraphicsPath path = Fx.Round(card, radius))
            {
                using (LinearGradientBrush b = Fx.Gradient(card, Fx.Alpha(top, en), Fx.Alpha(bottom, en), 90f)) g.FillPath(b, path);

                // light that follows the hover: a glow from above the card
                if (h > 0.02)
                {
                    GraphicsState clip = g.Save();
                    g.SetClip(path);
                    Fx.Glow(g, new PointF(cx, card.Y + card.Height * 0.12f), card.Width * 0.95f, Theme.Accent, (int)(Math.Min(1, h) * (Theme.Dark ? 60 : 40)));
                    g.Restore(clip);
                }

                Color edge = Fx.Blend(Theme.Line, Theme.Accent, Math.Min(1, h));
                using (Pen pen = new Pen(Fx.Alpha(edge, en), (float)(1.2 + h * 1.0))) g.DrawPath(pen, path);
            }

            // icon on a tinted plate
            int icon = Metrics.TileIcon;
            int plate = icon + Metrics.U(18);
            float iconScale = (float)(1 + 0.09 * h);
            RectangleF plateRect = new RectangleF(cx - plate / 2f, card.Y + Metrics.U(16), plate, plate);
            GraphicsState ist = g.Save();
            g.TranslateTransform(cx, plateRect.Y + plate / 2f);
            g.ScaleTransform(iconScale, iconScale);
            g.TranslateTransform(-cx, -(plateRect.Y + plate / 2f));
            using (LinearGradientBrush b = Fx.Gradient(plateRect, Fx.Alpha(Theme.Accent, (0.20 + 0.14 * h) * en), Fx.Alpha(Theme.Accent2, (0.08 + 0.1 * h) * en), 60f))
                Fx.FillRound(g, b, plateRect, Metrics.U(18));
            RectangleF iconRect = new RectangleF(plateRect.X + (plate - icon) / 2f, plateRect.Y + (plate - icon) / 2f, icon, icon);

            if (Entry != null && Entry.Icon != null)
            {
                if (en > 0.99) g.DrawImage(Entry.Icon, iconRect);
                else using (ImageAttributes ia = FadeAttributes(en))
                    g.DrawImage(Entry.Icon, Rectangle.Round(iconRect), 0, 0, Entry.Icon.Width, Entry.Icon.Height, GraphicsUnit.Pixel, ia);
            }
            else if (Glyph != null)
            {
                Font f = Fx.Icons(Metrics.Fz(Metrics.Compact ? 24 : 30));
                using (LinearGradientBrush b = Fx.Gradient(iconRect, Fx.Alpha(Theme.Accent, en), Fx.Alpha(Theme.Accent2, en), 90f))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(Glyph, f, b, iconRect, sf);
                }
            }
            else
            {
                Color[] pal = Palette[Math.Abs(Title.GetHashCode()) % Palette.Length];
                RectangleF circle = RectangleF.Inflate(iconRect, -2, -2);
                using (LinearGradientBrush b = Fx.Gradient(circle, Fx.Alpha(pal[0], en), Fx.Alpha(pal[1], en), 60f)) g.FillEllipse(b, circle);
                string letter = Title.Length > 0 ? Title.Substring(0, 1).ToUpper() : "?";
                Fx.Text(g, letter, Fx.Ui(Metrics.Fz(Metrics.Compact ? 19 : 23), FontStyle.Bold), Fx.Alpha(Color.White, en), iconRect,
                    StringAlignment.Center, StringAlignment.Center, false);
            }
            g.Restore(ist);

            if (Star)
                Fx.Text(g, "", Fx.Icons(11), Fx.Alpha(Color.FromArgb(250, 204, 21), en), new RectangleF(card.Right - Metrics.U(28), card.Y + Metrics.U(8), Metrics.U(22), Metrics.U(22)),
                    StringAlignment.Center, StringAlignment.Center, false);

            RectangleF textRect = new RectangleF(card.X + Metrics.U(8), plateRect.Bottom + Metrics.U(8), card.Width - Metrics.U(16), card.Bottom - plateRect.Bottom - Metrics.U(12));
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Text, Theme.Text, h), en)))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(Title, Fx.Ui(Metrics.Fz(Metrics.Compact ? 9.5f : 10.5f), FontStyle.Regular), b, textRect, sf);
            }
            g.Restore(st);
        }

        private static ImageAttributes FadeAttributes(double alpha)
        {
            ColorMatrix m = new ColorMatrix();
            m.Matrix33 = (float)alpha;
            ImageAttributes ia = new ImageAttributes();
            ia.SetColorMatrix(m, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            return ia;
        }
    }

    /// <summary>
    /// A flow of tiles that fills the width: the number of columns follows the width of the window and the tiles stretch a little
    /// to leave no ragged edge. Works from a small laptop to a wide monitor.
    /// </summary>
    internal sealed class TileGrid : FlowLayoutPanel
    {
        public int MinTile = Metrics.U(150);
        public int MaxTile = Metrics.U(215);

        public TileGrid()
        {
            WrapContents = true;
            FlowDirection = FlowDirection.LeftToRight;
            BackColor = Theme.Background;
            DoubleBuffered = true;
        }

        /// <summary>Resizes the tiles for a row width. Call it when the width changes or tiles are added.</summary>
        public void Relayout(int availableWidth)
        {
            if (availableWidth < 50) return;
            int gap = Metrics.U(8);
            int cols = Math.Max(1, (availableWidth + gap) / (MinTile + gap));
            int w = Math.Min(MaxTile, (availableWidth - gap * cols) / cols);
            int hgt = (int)(w * 0.96);
            SuspendLayout();
            foreach (Control c in Controls)
            {
                TileControl t = c as TileControl;
                if (t == null) continue;
                t.Margin = new Padding(gap / 2);
                t.Size = new Size(w, hgt);
            }
            ResumeLayout();
        }

        /// <summary>Plays the entrance of all visible tiles, one after another.</summary>
        public void PlayEnter()
        {
            int i = 0;
            foreach (Control c in Controls)
            {
                TileControl t = c as TileControl;
                if (t != null && t.Visible) t.PlayEnter(i++);
            }
        }
    }

    // =========================================================================================== navigation

    /// <summary>Entry of the left navigation bar. In Tiny mode only the icon is drawn.</summary>
    internal sealed class SidebarButton : Control
    {
        public string Glyph;
        public string Caption;
        /// <summary>Show the caption even on a narrow screen (the administrator's panel has a fixed wide menu).</summary>
        public bool ForceLabels;
        private bool selected;
        private readonly AnimValue sel;
        private readonly AnimValue hover;

        public SidebarButton(string glyph, string caption)
        {
            Glyph = glyph;
            Caption = caption;
            Height = Metrics.U(Metrics.Compact ? 50 : 56);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            sel = new AnimValue(this, 0);
            sel.Stiffness = 300;
            sel.Damping = 22;
            hover = new AnimValue(this, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public bool Selected
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                sel.To(value ? 1 : 0);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Theme.Sidebar);
            double s = Math.Max(0, Math.Min(1.1, sel.Value));
            double sc = Math.Min(1, s);
            double h = Math.Max(0, Math.Min(1, hover.Value));

            RectangleF r = new RectangleF(Metrics.U(10), Metrics.U(3), Width - Metrics.U(20), Height - Metrics.U(6));
            float radius = Metrics.U(14);
            if (sc > 0.01 || h > 0.01)
            {
                using (GraphicsPath p = Fx.Round(r, radius))
                {
                    using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.CardHover, h * (1 - sc * 0.6) * 0.55))) g.FillPath(b, p);
                    if (sc > 0.01)
                        using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(Theme.Accent, 0.20 * sc), Fx.Alpha(Theme.Accent2, 0.10 * sc), 0f)) g.FillPath(b, p);
                }
            }
            // the bar on the left grows from the middle
            if (sc > 0.01)
            {
                float bh = (float)((Height - Metrics.U(22)) * s);
                using (LinearGradientBrush b = Fx.Gradient(new RectangleF(0, (Height - bh) / 2, Metrics.U(5), Math.Max(2, bh)), Theme.Accent, Theme.Accent2, 90f))
                    Fx.FillRound(g, b, new RectangleF(-Metrics.U(3), (Height - bh) / 2, Metrics.U(8), Math.Max(2, bh)), Metrics.U(4));
            }

            Color ink = Fx.Blend(Theme.Muted, Theme.Text, Math.Max(h * 0.7, sc));
            Color glyphInk = Fx.Blend(Fx.Blend(Theme.Muted, Theme.Text, h * 0.6), Theme.Accent, sc);
            bool tiny = Metrics.Tiny && !ForceLabels;
            float iconPt = Metrics.Fz(tiny ? 17 : 15);
            float bump = (float)(1 + 0.08 * sc + 0.05 * h);
            Font icon = Fx.Icons(iconPt);
            SizeF isz = g.MeasureString(Glyph, icon);
            float ix = tiny ? (Width - isz.Width) / 2 : Metrics.U(24);
            GraphicsState st = g.Save();
            g.TranslateTransform(ix + isz.Width / 2, Height / 2f);
            g.ScaleTransform(bump, bump);
            g.TranslateTransform(-(ix + isz.Width / 2), -Height / 2f);
            using (SolidBrush b = new SolidBrush(glyphInk)) g.DrawString(Glyph, icon, b, ix, (Height - isz.Height) / 2);
            g.Restore(st);

            if (!tiny)
                Fx.Text(g, Caption, Fx.Ui(Metrics.Fz(Metrics.Compact ? 10.5f : 11.5f), sc > 0.5 ? FontStyle.Bold : FontStyle.Regular), ink,
                    new RectangleF(Metrics.U(62), 0, Width - Metrics.U(70), Height), StringAlignment.Near, StringAlignment.Center, true);
        }
    }

    /// <summary>The logo (a shield with a check mark) and the name at the top of the sidebar.</summary>
    internal sealed class BrandControl : Control
    {
        public string Caption = "";
        public bool ForceLabels;
        private readonly AnimValue hover;

        public BrandControl()
        {
            hover = new AnimValue(this, 0);
            Dock = DockStyle.Top;
            Height = Metrics.U(Metrics.Tiny ? 70 : 92);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        public static GraphicsPath ShieldPath(RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            PointF A(float u, float v) { return new PointF(x + u * w, y + v * h); }
            p.AddLine(A(0.5f, 0.02f), A(0.9f, 0.17f));
            p.AddBezier(A(0.9f, 0.17f), A(0.9f, 0.62f), A(0.68f, 0.86f), A(0.5f, 0.98f));
            p.AddBezier(A(0.5f, 0.98f), A(0.32f, 0.86f), A(0.1f, 0.62f), A(0.1f, 0.17f));
            p.CloseFigure();
            return p;
        }

        /// <summary>The logo itself: gradient shield, a check mark drawn up to <paramref name="check"/> (0..1).</summary>
        public static void DrawLogo(Graphics g, RectangleF r, double check, double glow)
        {
            if (glow > 0.01) Fx.Glow(g, new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width * 1.1f, Theme.Accent, (int)(90 * glow));
            using (GraphicsPath p = ShieldPath(r))
            {
                using (LinearGradientBrush b = Fx.Gradient(r, Theme.Accent, Theme.Accent2, 60f)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, r.Width / 28f))) g.DrawPath(pen, p);
            }
            if (check > 0)
            {
                PointF a = new PointF(r.X + 0.3f * r.Width, r.Y + 0.5f * r.Height), m = new PointF(r.X + 0.45f * r.Width, r.Y + 0.65f * r.Height), c = new PointF(r.X + 0.72f * r.Width, r.Y + 0.36f * r.Height);
                using (Pen pen = new Pen(Theme.OnAccent, Math.Max(2f, r.Width / 9f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    double c1 = Math.Min(1, check / 0.4), c2 = Math.Max(0, Math.Min(1, (check - 0.4) / 0.6));
                    PointF mid = new PointF(a.X + (m.X - a.X) * (float)c1, a.Y + (m.Y - a.Y) * (float)c1);
                    g.DrawLine(pen, a, mid);
                    if (c2 > 0) g.DrawLine(pen, m, new PointF(m.X + (c.X - m.X) * (float)c2, m.Y + (c.Y - m.Y) * (float)c2));
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Theme.Sidebar);
            double h = Math.Max(0, Math.Min(1, hover.Value));
            bool tiny = Metrics.Tiny && !ForceLabels;
            int size = Metrics.U(tiny ? 40 : 44);
            float x = tiny ? (Width - size) / 2f : Metrics.U(22);
            float y = (Height - size) / 2f;
            GraphicsState st = g.Save();
            float sc = (float)(1 + 0.06 * h);
            g.TranslateTransform(x + size / 2f, y + size / 2f);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(-(x + size / 2f), -(y + size / 2f));
            DrawLogo(g, new RectangleF(x, y, size, size), 1, 0.35 + 0.65 * h);
            g.Restore(st);
            if (!tiny)
                Fx.Text(g, Caption, Fx.Ui(Metrics.Fz(Metrics.Compact ? 12.5f : 14f), FontStyle.Bold), Theme.Text,
                    new RectangleF(x + size + Metrics.U(14), 0, Width - x - size - Metrics.U(20), Height), StringAlignment.Near, StringAlignment.Center, true);
        }
    }

    /// <summary>Rounded "pill" button for category tabs.</summary>
    internal sealed class PillButton : Control
    {
        private bool selected;
        private readonly AnimValue hover;
        private readonly AnimValue sel;
        private readonly string caption;

        public PillButton(string text, bool selected)
        {
            caption = text;
            this.selected = selected;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, Metrics.U(8), Metrics.U(8));
            hover = new AnimValue(this, 0);
            sel = new AnimValue(this, selected ? 1 : 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(TextRenderer.MeasureText(text, Fx.Ui(Metrics.Fz(Metrics.Compact ? 10f : 10.5f), FontStyle.Bold)).Width + Metrics.U(34), Metrics.U(38));
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; sel.To(value ? 1 : 0); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double h = Math.Max(0, Math.Min(1, hover.Value));
            double s = Math.Max(0, Math.Min(1, sel.Value));
            RectangleF r = new RectangleF(1, 1, Width - 2, Height - 2);
            using (GraphicsPath p = Fx.Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Fx.Blend(Theme.Card, Theme.CardHover, h))) g.FillPath(b, p);
                if (s > 0.01)
                    using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(Theme.Accent, s), Fx.Alpha(Theme.Accent2, s), 0f)) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Alpha(Fx.Blend(Theme.Line, Theme.Accent, h), 1 - s * 0.9), 1.3f)) g.DrawPath(pen, p);
            }
            Fx.Text(g, caption, Fx.Ui(Metrics.Fz(Metrics.Compact ? 10f : 10.5f), FontStyle.Bold), Fx.Blend(Theme.Text, Theme.OnAccent, s), new RectangleF(0, 0, Width, Height),
                StringAlignment.Center, StringAlignment.Center, false);
        }
    }

    /// <summary>Search field with a magnifier and a placeholder that stays readable on the dark theme.</summary>
    internal sealed class SearchBox : Panel
    {
        private readonly TextBox box = new TextBox();
        private readonly string placeholder;
        private readonly AnimValue focus;
        private bool showingPlaceholder;
        private bool silent;

        public event EventHandler QueryChanged;

        public SearchBox(string placeholder)
        {
            this.placeholder = placeholder;
            focus = new AnimValue(this, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Metrics.U(44);
            box.BorderStyle = BorderStyle.None;
            box.Font = Theme.UiFont(Metrics.Fz(Metrics.Compact ? 11 : 12));
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            Controls.Add(box);
            box.GotFocus += delegate { if (showingPlaceholder) SetPlaceholder(false); focus.To(1); };
            box.LostFocus += delegate { if (box.Text.Length == 0) SetPlaceholder(true); focus.To(0); };
            box.TextChanged += delegate { if (!silent && !showingPlaceholder && QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            SetPlaceholder(true);
            Resize += delegate { LayoutBox(); };
            LayoutBox();
        }

        private void LayoutBox()
        {
            int h = box.PreferredHeight;
            box.SetBounds(Metrics.U(46), (Height - h) / 2, Math.Max(20, Width - Metrics.U(62)), h);
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

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Header);
            double f = Math.Max(0, Math.Min(1, focus.Value));
            RectangleF r = new RectangleF(1, 1, Width - 2, Height - 2);
            if (f > 0.02) Fx.Shadow(g, r, r.Height / 2, Metrics.U(7), (int)(95 * f), Theme.Accent);
            using (GraphicsPath p = Fx.Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Blend(Theme.Line, Theme.Accent, f), (float)(1.3 + f * 0.9))) g.DrawPath(pen, p);
            }
            // magnifier, drawn by hand
            float s = Metrics.U(18);
            float mx = Metrics.U(18), my = (Height - s) / 2f;
            using (Pen pen = new Pen(Fx.Blend(Theme.Muted, Theme.Accent, f), Math.Max(1.8f, Metrics.U(2))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, mx, my, s * 0.72f, s * 0.72f);
                g.DrawLine(pen, mx + s * 0.64f, my + s * 0.64f, mx + s, my + s);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); box.Focus(); }
    }

    /// <summary>Horizontal slider (volume). A TrackBar would ignore the dark theme.</summary>
    internal sealed class SliderBar : Control
    {
        private int value;
        private bool dragging;
        private readonly AnimValue grow;

        public int Minimum = 0;
        public int Maximum = 100;
        public event EventHandler ValueChanged;

        public SliderBar()
        {
            Height = Metrics.S(34);
            Cursor = Cursors.Hand;
            grow = new AnimValue(this, 0);
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

        protected override void OnMouseEnter(EventArgs e) { grow.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { if (!dragging) grow.To(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Capture = true; grow.To(1.35); FromMouse(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) FromMouse(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; grow.To(ClientRectangle.Contains(e.Location) ? 1 : 0); base.OnMouseUp(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            double gr = Math.Max(0, Math.Min(1.4, grow.Value));
            int track = Metrics.S(6);
            int y = (Height - track) / 2;
            int w = Width - 2 * Pad;
            int fill = (int)(w * (value - Minimum) / (double)Math.Max(1, Maximum - Minimum));
            using (GraphicsPath p = Fx.Round(new RectangleF(Pad, y, w, track), track / 2f))
            using (SolidBrush b = new SolidBrush(Theme.Line))
                g.FillPath(b, p);
            if (fill > 0)
            {
                RectangleF fr = new RectangleF(Pad, y, fill, track);
                using (GraphicsPath p = Fx.Round(fr, track / 2f))
                using (LinearGradientBrush b = Fx.Gradient(new RectangleF(Pad, y, Math.Max(fill, 2), track), Theme.Accent, Theme.Accent2, 0f))
                    g.FillPath(b, p);
            }
            float d = (float)(Metrics.S(18) * (1 + 0.18 * gr));
            RectangleF knob = new RectangleF(Pad + fill - d / 2, (Height - d) / 2, d, d);
            Fx.Shadow(g, new RectangleF(knob.X, knob.Y + 1, knob.Width, knob.Height), d / 2, Metrics.S(4), 110, Color.Black);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, knob);
            using (Pen pen = new Pen(Theme.Accent, 2.2f)) g.DrawEllipse(pen, knob);
        }
    }

    // =========================================================================================== header

    /// <summary>
    /// Top of a page: the title (slides when the section changes), a line below it, the clock in a pill and the search box.
    /// Everything is placed from the real width, so nothing overlaps on a narrow screen.
    /// </summary>
    internal sealed class HeaderBar : Control
    {
        private string title = "";
        private string oldTitle = "";
        private double titleP = 1;
        private Tween titleTween;
        private string subtitle = "";
        private string clock = "";
        private bool clockHover;
        private Rectangle clockRect;

        public readonly SearchBox Search;
        public event EventHandler ClockClicked;

        public HeaderBar(SearchBox search)
        {
            Search = search;
            Dock = DockStyle.Top;
            Height = Metrics.HeaderHeight;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Controls.Add(search);
            Resize += delegate { Place(); };
        }

        public string Title
        {
            get { return title; }
            set
            {
                if (title == value) return;
                oldTitle = title;
                title = value ?? "";
                if (titleTween != null) titleTween.Cancel();
                if (!Motion.Enabled || oldTitle.Length == 0) { titleP = 1; Invalidate(); return; }
                titleP = 0;
                titleTween = Tween.Run(380, Easing.OutCubic, delegate (double t) { titleP = t; Invalidate(); });
            }
        }

        public string Subtitle
        {
            get { return subtitle; }
            set { if (subtitle == value) return; subtitle = value ?? ""; Invalidate(); }
        }

        public string ClockText
        {
            get { return clock; }
            set { if (clock == value) return; clock = value ?? ""; Place(); Invalidate(); }
        }

        public void Place()
        {
            if (Width <= 0) return;
            int pad = Metrics.PagePad;
            Size cs = TextRenderer.MeasureText(string.IsNullOrEmpty(clock) ? "00:00" : clock, Fx.Ui(Metrics.Fz(Metrics.Compact ? 17 : 20), FontStyle.Bold));
            int cw = cs.Width + Metrics.U(34), ch = Metrics.U(44);
            clockRect = new Rectangle(Width - pad - cw, Metrics.U(18), cw, ch);
            if (Metrics.Tiny)
                Search.SetBounds(pad, Metrics.U(80), Width - 2 * pad, Metrics.U(44));
            else
            {
                int sw = Math.Max(Metrics.U(190), Math.Min(Metrics.U(380), (int)(Width * 0.32)));
                Search.SetBounds(Width - pad - sw, Height - Metrics.U(44) - Metrics.U(14), sw, Metrics.U(44));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = clockRect.Contains(e.Location);
            if (over != clockHover) { clockHover = over; Cursor = over ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (clockHover) { clockHover = false; Cursor = Cursors.Default; Invalidate(); } base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (clockRect.Contains(e.Location) && ClockClicked != null) ClockClicked(this, EventArgs.Empty);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Theme.Header);
            int pad = Metrics.PagePad;
            // a soft light in the corner behind the title
            Fx.Glow(g, new PointF(pad + Metrics.U(120), Metrics.U(10)), Metrics.U(380), Theme.Accent, Theme.Dark ? 22 : 16);

            float titleX = pad, titleY = Metrics.U(14);
            float tw = Math.Max(100, clockRect.Left - pad - Metrics.U(16));
            Font tf = Fx.Ui(Metrics.TitlePt, FontStyle.Bold);
            float slide = Metrics.U(16);
            double p = Easing.OutCubic(titleP);
            if (titleP < 1 && oldTitle.Length > 0)
                Fx.Text(g, oldTitle, tf, Fx.Alpha(Theme.Text, 1 - p), new RectangleF(titleX, titleY - (float)(p * slide), tw, Metrics.U(50)), StringAlignment.Near, StringAlignment.Near, true);
            Fx.Text(g, title, tf, Fx.Alpha(Theme.Text, p), new RectangleF(titleX, titleY + (float)((1 - p) * slide), tw, Metrics.U(50)), StringAlignment.Near, StringAlignment.Near, true);

            if (!Metrics.Tiny)
                Fx.Text(g, subtitle, Fx.Ui(Metrics.Fz(Metrics.Compact ? 10.5f : 11.5f)), Theme.Muted,
                    new RectangleF(titleX + 2, titleY + Metrics.U(48), Math.Max(100, Search.Left - pad - Metrics.U(16)), Metrics.U(28)), StringAlignment.Near, StringAlignment.Near, true);
            else
                Fx.Text(g, subtitle, Fx.Ui(Metrics.Fz(10f)), Theme.Muted, new RectangleF(titleX + 2, titleY + Metrics.U(42), tw, Metrics.U(24)),
                    StringAlignment.Near, StringAlignment.Near, true);

            // clock pill
            RectangleF cr = clockRect;
            using (GraphicsPath cp = Fx.Round(cr, cr.Height / 2f))
            {
                using (SolidBrush b = new SolidBrush(clockHover ? Theme.CardHover : Theme.Card)) g.FillPath(b, cp);
                using (Pen pen = new Pen(Fx.Blend(Theme.Line, Theme.Accent, clockHover ? 1 : 0), 1.3f)) g.DrawPath(pen, cp);
            }
            using (LinearGradientBrush b = Fx.Gradient(cr, Theme.Accent, Theme.Accent2, 0f))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(clock, Fx.Ui(Metrics.Fz(Metrics.Compact ? 17 : 20), FontStyle.Bold), b, cr, sf);
            }

            // thin line under the header that fades to the right
            using (LinearGradientBrush b = Fx.Gradient(new RectangleF(0, Height - 2, Width, 2), Fx.Alpha(Theme.Accent, 0.55), Fx.Alpha(Theme.Accent2, 0.0), 0f))
                g.FillRectangle(b, 0, Height - 2, Width, 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && titleTween != null) titleTween.Cancel();
            base.Dispose(disposing);
        }
    }

    // =========================================================================================== panel controls

    /// <summary>A text box in a rounded frame whose border lights up on focus. The box inside is what callers read and write.</summary>
    internal sealed class FieldHost : Panel
    {
        public readonly TextBox Box;
        private readonly AnimValue focus;

        public FieldHost(TextBox box, bool multiline)
        {
            Box = box;
            focus = new AnimValue(this, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            Controls.Add(box);
            box.GotFocus += delegate { focus.To(1); };
            box.LostFocus += delegate { focus.To(0); };
            Resize += delegate { Fit(multiline); };
            Fit(multiline);
        }

        private void Fit(bool multiline)
        {
            if (multiline) Box.SetBounds(Metrics.S(12), Metrics.S(10), Math.Max(10, Width - Metrics.S(24)), Math.Max(10, Height - Metrics.S(20)));
            else Box.SetBounds(Metrics.S(14), Math.Max(2, (Height - Box.PreferredHeight) / 2), Math.Max(10, Width - Metrics.S(28)), Box.PreferredHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double f = Math.Max(0, Math.Min(1, focus.Value));
            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (f > 0.02) Fx.Shadow(g, r, Metrics.S(12), Metrics.S(6), (int)(80 * f), Theme.Accent);
            using (GraphicsPath p = Fx.Round(r, Metrics.S(12)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Blend(Theme.Line, Theme.Accent, f), (float)(1.3 + f * 0.9))) g.DrawPath(pen, p);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }
    }

    /// <summary>One line of the status page: a coloured dot, the name and the value, in a rounded card that slides in.</summary>
    internal sealed class StatusRow : Control, IEnterAnimated
    {
        private readonly string caption;
        private readonly string value;
        private readonly bool ok;
        private double enter = 1;
        private Tween tween;
        private readonly AnimValue hover;

        public StatusRow(string caption, string value, bool ok)
        {
            this.caption = caption;
            this.value = value;
            this.ok = ok;
            hover = new AnimValue(this, 0);
            Height = Metrics.S(40);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void PlayEnter(int index)
        {
            if (tween != null) tween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            tween = Tween.Run(480, Easing.OutCubic, delegate (double t) { enter = t; Invalidate(); }, null, Math.Min(index, 14) * 45);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); base.OnMouseLeave(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            if (en < 0.003) return;
            double h = Math.Max(0, Math.Min(1, hover.Value));
            float dx = (float)((1 - en) * Metrics.S(28));
            RectangleF r = new RectangleF(1 + dx, 1, Width - 3 - dx, Height - 3);
            using (GraphicsPath p = Fx.Round(r, Metrics.S(12)))
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Fx.Blend(Theme.Card, Theme.CardHover, h * 0.7), en))) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Alpha(Theme.Line, en), 1.2f)) g.DrawPath(pen, p);
            }
            Color dot = ok ? Theme.Success : Theme.Warn;
            float d = Metrics.S(10);
            float cy = r.Y + r.Height / 2;
            Fx.Glow(g, new PointF(r.X + Metrics.S(20), cy), Metrics.S(14), dot, (int)(70 * en));
            using (SolidBrush b = new SolidBrush(Fx.Alpha(dot, en))) g.FillEllipse(b, r.X + Metrics.S(20) - d / 2, cy - d / 2, d, d);
            Font f = Fx.Ui(10.5f);
            Fx.Text(g, caption, f, Fx.Alpha(Theme.Muted, en), new RectangleF(r.X + Metrics.S(40), r.Y, Metrics.S(250), r.Height), StringAlignment.Near, StringAlignment.Center, true);
            Fx.Text(g, value, Fx.Ui(10.5f, FontStyle.Bold), Fx.Alpha(ok ? Theme.Text : Theme.Warn, en),
                new RectangleF(r.X + Metrics.S(300), r.Y, r.Width - Metrics.S(312), r.Height), StringAlignment.Near, StringAlignment.Center, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tween != null) tween.Cancel();
            base.Dispose(disposing);
        }
    }

    /// <summary>A highlighted message at the top of a page: a gradient strip with text.</summary>
    internal sealed class Banner : Control, IEnterAnimated
    {
        private double enter = 1;
        private Tween tween;

        public Banner(string text)
        {
            Text = text;
            Height = Metrics.S(48);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void PlayEnter(int index)
        {
            if (tween != null) tween.Cancel();
            if (!Motion.Enabled) { enter = 1; Invalidate(); return; }
            enter = 0;
            tween = Tween.Run(600, Easing.OutBack, delegate (double t) { enter = Math.Max(0, t); Invalidate(); });
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            RectangleF r = new RectangleF(1, 1 + (float)((1 - en) * Metrics.S(10)), Width - 3, Height - 3);
            using (GraphicsPath p = Fx.Round(r, Metrics.S(14)))
            using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(Theme.Accent, en), Fx.Alpha(Theme.Accent2, en), 0f))
                g.FillPath(b, p);
            Fx.Text(g, Text, Fx.Ui(10.5f, FontStyle.Bold), Fx.Alpha(Theme.OnAccent, en), new RectangleF(r.X + Metrics.S(18), r.Y, r.Width - Metrics.S(30), r.Height),
                StringAlignment.Near, StringAlignment.Center, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && tween != null) tween.Cancel();
            base.Dispose(disposing);
        }
    }
}
