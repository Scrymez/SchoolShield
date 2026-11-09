using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// A card that floats down from the top of the screen, stays for a few seconds (the clock stops while the mouse is over it)
    /// and floats away. Never takes the focus. The base of the toast, the violation message and similar notices.
    /// </summary>
    internal abstract class FloatCard : Form, IAnimated
    {
        private double clock;
        private double held;
        private double exitAt = -1;
        private int finalY;
        private bool started;

        protected bool Hovered;
        protected double HoldSeconds = 4.5;
        protected double EnterSeconds = 0.55;
        protected const double ExitSeconds = 0.30;

        protected double Clock { get { return clock; } }
        protected bool Exiting { get { return exitAt >= 0; } }
        protected double HoldLeft { get { return Math.Max(0, 1 - held / Math.Max(0.1, HoldSeconds)); } }

        protected FloatCard()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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

        protected static int Radius { get { return Metrics.S(18); } }

        /// <summary>Sets the size, rounds the corners and puts the card above the screen, ready to slide in.</summary>
        protected void Fit(Size size)
        {
            Size = size;
            using (GraphicsPath p = Fx.Round(new RectangleF(0, 0, size.Width, size.Height), Radius))
                Region = new Region(p);
            Rectangle s = Screen.PrimaryScreen.Bounds;
            int top = State.Windowed ? Metrics.S(14) : Metrics.DockHeight + Metrics.S(14);
            finalY = s.Y + top;
            Location = new Point(s.X + (s.Width - size.Width) / 2, finalY - size.Height - Metrics.S(40));
        }

        public void Present()
        {
            Opacity = 0;
            started = true;
            Show();
            Motion.Add(this);
        }

        /// <summary>Starts the way out now.</summary>
        public void Dismiss()
        {
            if (exitAt < 0) exitAt = clock;
        }

        protected virtual void OnFrame(double seconds) { }

        public bool Step(double seconds)
        {
            if (IsDisposed || !started) return false;
            clock += seconds;
            bool anim = Motion.Enabled;
            if (exitAt < 0)
            {
                if (!Hovered && clock >= (anim ? EnterSeconds : 0.05)) held += seconds;
                if (held >= HoldSeconds) exitAt = clock;
            }

            double ep = anim ? Easing.Clamp(clock / EnterSeconds) : 1;
            double y = finalY - (1 - Easing.OutBack(ep)) * (Height + Metrics.S(30));
            double op = anim ? Math.Min(1, clock / 0.16) : 1;
            if (exitAt >= 0)
            {
                double xp = Math.Min(1, (clock - exitAt) / ExitSeconds);
                if (!anim) xp = 1;
                op *= 1 - Easing.InCubic(xp);
                y -= Easing.InCubic(xp) * Metrics.S(18);
                if (xp >= 1) { Close(); return false; }
            }
            int yi = (int)Math.Round(y);
            if (Top != yi) Top = yi;
            op = Math.Max(0, Math.Min(1, op));
            if (Math.Abs(Opacity - op) > 0.004) Opacity = op;
            OnFrame(seconds);
            Invalidate();
            return true;
        }

        protected override void OnMouseEnter(EventArgs e) { Hovered = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hovered = false; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Dismiss(); base.OnMouseDown(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>Width of the card: the screen is narrow on some laptops.</summary>
        protected static int CardWidth(int wanted)
        {
            return Math.Min(wanted, Screen.PrimaryScreen.Bounds.Width - Metrics.S(32));
        }

        /// <summary>Breaks a text into lines for a given width (own wrapping, so the typewriter never makes a word jump to the next line).</summary>
        protected static List<string> Wrap(Graphics g, string text, Font f, float width)
        {
            List<string> lines = new List<string>();
            string cur = "";
            foreach (string word in (text ?? "").Split(' '))
            {
                string test = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && g.MeasureString(test, f, 100000, StringFormat.GenericTypographic).Width > width)
                {
                    lines.Add(cur);
                    cur = word;
                }
                else cur = test;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        protected static void Lines(Graphics g, List<string> lines, Font f, Color c, float x, float y, int visibleChars)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                float lh = f.GetHeight(g);
                int left = visibleChars;
                for (int i = 0; i < lines.Count; i++)
                {
                    string s = lines[i];
                    if (left <= 0) break;
                    if (left < s.Length) s = s.Substring(0, left);
                    left -= lines[i].Length + 1;
                    g.DrawString(s, f, b, x, y + i * lh, StringFormat.GenericTypographic);
                }
            }
        }
    }

    /// <summary>A short notice: a slim card with a coloured bar on the left and a progress line at the bottom.</summary>
    internal sealed class ToastForm : FloatCard
    {
        private readonly Color accent;
        private readonly List<string> lines;
        private readonly Font font = Fx.Ui(Metrics.Compact ? 11f : 12f, FontStyle.Regular);
        private readonly int pad = Metrics.S(20);

        private static ToastForm current;

        public static void Info(string text)
        {
            if (current != null && !current.IsDisposed) { try { current.Close(); } catch { } }
            ToastForm t = new ToastForm(text);
            current = t;
            t.Present();
        }

        private ToastForm(string text)
        {
            accent = Theme.Accent;
            int width = CardWidth(Metrics.S(520));
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                lines = Wrap(g, text, font, width - pad * 2 - Metrics.S(18));
                int h = (int)(lines.Count * font.GetHeight(g)) + pad * 2 + Metrics.S(6);
                Fit(new Size(width, h));
            }
            HoldSeconds = Math.Max(3.2, text.Length * 0.05);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush b = Fx.Gradient(r, Theme.Lighten(Theme.Card, Theme.Dark ? 10 : 0), Theme.Card, 90f))
                g.FillRectangle(b, r);
            using (SolidBrush b = new SolidBrush(Fx.WithAlpha(accent, 255)))
                g.FillRectangle(b, 0, 0, Metrics.S(6), Height);
            Fx.StrokeRound(g, Fx.Alpha(Theme.Line, 1), 2f, new RectangleF(1, 1, Width - 2, Height - 2), Radius);

            float x = pad + Metrics.S(12);
            Lines(g, lines, font, Theme.Text, x, pad, 100000);

            float w = (Width - Metrics.S(12)) * (float)HoldLeft;
            using (SolidBrush b = new SolidBrush(Fx.WithAlpha(accent, 150)))
                g.FillRectangle(b, Metrics.S(6), Height - Metrics.S(3), w, Metrics.S(3));
        }
    }

    /// <summary>
    /// The message for a forbidden action: a card with an animated picture (pulsing rings, a shake), a headline, one more line that
    /// appears letter by letter, the thing that was caught and, when it keeps happening, a remark about the number of attempts.
    /// </summary>
    internal sealed class ViolationForm : FloatCard
    {
        private static ViolationForm current;

        private readonly ViolationKind kind;
        private readonly Color accent;
        private readonly Joke joke;
        private readonly string detail;
        private readonly int attempt;
        private readonly List<string> titleLines;
        private readonly List<string> textLines;
        private readonly List<string> remarkLines;
        private readonly Font titleFont = Fx.Ui(Metrics.Compact ? 15.5f : 17.5f, FontStyle.Bold);
        private readonly Font textFont = Fx.Ui(Metrics.Compact ? 11f : 12f, FontStyle.Regular);
        private readonly Font smallFont = Fx.Ui(Metrics.Compact ? 9.5f : 10f, FontStyle.Regular);
        private readonly Font remarkFont = Fx.Ui(Metrics.Compact ? 9.5f : 10.5f, FontStyle.Italic);
        private readonly int pad = Metrics.S(24);
        private readonly int badge = Metrics.S(76);
        private int totalChars;
        private float textX;
        private float titleY, textY, remarkY, chipY;

        public static Color AccentOf(ViolationKind k)
        {
            switch (k)
            {
                case ViolationKind.Adult: return Color.FromArgb(251, 113, 133);
                case ViolationKind.Game: return Color.FromArgb(251, 191, 36);
                case ViolationKind.Download: return Color.FromArgb(167, 139, 250);
                case ViolationKind.Site: return Color.FromArgb(251, 146, 60);
                case ViolationKind.Program: return Color.FromArgb(232, 121, 249);
                case ViolationKind.File: return Color.FromArgb(45, 212, 191);
                case ViolationKind.Folder: return Color.FromArgb(129, 140, 248);
                case ViolationKind.Wifi: return Color.FromArgb(56, 189, 248);
                default: return Color.FromArgb(251, 113, 133);
            }
        }

        public static void Show(ViolationKind kind, string detail, Joke joke, int attempt)
        {
            if (current != null && !current.IsDisposed) { try { current.Close(); } catch { } }
            ViolationForm f = new ViolationForm(kind, detail, joke, attempt);
            current = f;
            f.Present();
        }

        private ViolationForm(ViolationKind kind, string detail, Joke joke, int attempt)
        {
            this.kind = kind;
            this.joke = joke;
            this.detail = detail ?? "";
            this.attempt = attempt;
            accent = AccentOf(kind);

            int width = CardWidth(Metrics.S(700));
            textX = pad + badge + Metrics.S(22);
            float textW = width - textX - pad;
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                titleLines = Wrap(g, joke.Title, titleFont, textW);
                textLines = Wrap(g, joke.Line, textFont, textW);
                remarkLines = joke.Remark == null ? new List<string>() : Wrap(g, joke.Remark, remarkFont, textW);
                float y = pad;
                titleY = y;
                y += titleLines.Count * titleFont.GetHeight(g) + Metrics.S(6);
                textY = y;
                y += textLines.Count * textFont.GetHeight(g) + Metrics.S(10);
                remarkY = y;
                if (remarkLines.Count > 0) y += remarkLines.Count * remarkFont.GetHeight(g) + Metrics.S(10);
                chipY = y;
                bool chipRow = this.detail.Length > 0 || attempt >= 2;
                if (chipRow) y += Metrics.S(28);
                y += pad - Metrics.S(6);
                int h = (int)Math.Max(y, pad * 2 + badge) + Metrics.S(4);
                Fit(new Size(width, h));
            }
            totalChars = 0;
            foreach (string s in textLines) totalChars += s.Length + 1;
            HoldSeconds = 5.5 + Math.Min(3.0, (joke.Line.Length + (joke.Remark ?? "").Length) / 40.0);
            EnterSeconds = 0.6;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            double t = Clock;
            Rectangle full = new Rectangle(0, 0, Width, Height);

            // card body
            using (LinearGradientBrush b = Fx.Gradient(full, Theme.Lighten(Theme.Card, Theme.Dark ? 12 : 0), Theme.Card, 90f))
                g.FillRectangle(b, full);
            // a warm glow behind the picture, a short flash when the card arrives
            float cx = pad + badge / 2f, cy = pad + badge / 2f;
            Fx.Glow(g, new PointF(cx, cy), Width * 0.42f, accent, (int)(Theme.Dark ? 60 : 38));
            double flash = Math.Max(0, 1 - t / 0.35);
            if (flash > 0)
                using (SolidBrush b = new SolidBrush(Fx.Alpha(accent, flash * 0.22))) g.FillRectangle(b, full);

            // rings
            for (int k = 0; k < 2; k++)
            {
                double phase = (t * 0.85 + k * 0.5) % 1.0;
                float rad = badge / 2f + (float)phase * Metrics.S(20);
                using (Pen p = new Pen(Fx.Alpha(accent, (1 - phase) * 0.45), 2f))
                    g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
            }

            // badge
            RectangleF br = new RectangleF(pad, pad, badge, badge);
            using (LinearGradientBrush b = Fx.Gradient(br, Fx.Alpha(accent, 0.34), Fx.Alpha(accent, 0.12), 60f))
                Fx.FillRound(g, b, br, Metrics.S(22));
            Fx.StrokeRound(g, Fx.Alpha(accent, 0.55), 1.6f, br, Metrics.S(22));

            double pop = Easing.OutBack((t - 0.10) / 0.55);
            double wob = Easing.Wobble((t - 0.55) / 0.8) * 14;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform((float)wob);
            float sc = (float)Math.Max(0.01, pop);
            g.ScaleTransform(sc, sc);
            float isz = badge * 0.58f;
            ViolationIcons.Draw(g, kind, new RectangleF(-isz / 2, -isz / 2, isz, isz), accent, Math.Max(2.4f, badge / 22f), Theme.Card);
            g.Restore(st);

            // texts
            double tp = Easing.OutCubic((t - 0.18) / 0.4);
            float dx = (float)((1 - tp) * Metrics.S(18));
            Color tc = Fx.Alpha(Theme.Text, tp);
            Lines(g, titleLines, titleFont, tc, textX + dx, titleY, 100000);

            int chars = (int)Math.Max(0, (t - 0.45) * 52);
            Color lc = Fx.Blend(Theme.Muted, Theme.Text, 0.55);
            Lines(g, textLines, textFont, lc, textX, textY, Math.Min(chars, totalChars));
            if (chars < totalChars && ((int)(t * 2.4)) % 2 == 0 && textLines.Count > 0)
            {
                // blinking caret at the end of what has been typed
                int left = chars, li = 0;
                while (li < textLines.Count - 1 && left > textLines[li].Length) { left -= textLines[li].Length + 1; li++; }
                string typed = textLines[li].Substring(0, Math.Min(left, textLines[li].Length));
                float w = g.MeasureString(typed, textFont, 100000, StringFormat.GenericTypographic).Width;
                float lh = textFont.GetHeight(g);
                using (SolidBrush b = new SolidBrush(accent))
                    g.FillRectangle(b, textX + w + 2, textY + li * lh + lh * 0.12f, Metrics.S(2), lh * 0.78f);
            }
            if (remarkLines.Count > 0 && chars >= totalChars)
            {
                double rp = Easing.OutCubic((t - 0.45 - totalChars / 52.0) / 0.4);
                Lines(g, remarkLines, remarkFont, Fx.Alpha(accent, rp), textX, remarkY, 100000);
            }

            // what was caught + attempt counter
            float chipX = textX;
            if (detail.Length > 0)
            {
                string d = detail.Length > 46 ? detail.Substring(0, 45) + "…" : detail;
                SizeF sz = g.MeasureString(d, smallFont, 100000, StringFormat.GenericTypographic);
                RectangleF cr = new RectangleF(chipX, chipY, Math.Min(sz.Width + Metrics.S(24), Width - textX - pad), Metrics.S(24));
                double cp = Easing.OutCubic((t - 0.6) / 0.4);
                using (SolidBrush b = new SolidBrush(Fx.Alpha(accent, 0.16 * cp))) Fx.FillRound(g, b, cr, cr.Height / 2);
                Fx.Text(g, d, smallFont, Fx.Alpha(accent, cp), new RectangleF(cr.X + Metrics.S(12), cr.Y, cr.Width - Metrics.S(12), cr.Height),
                    StringAlignment.Near, StringAlignment.Center, true);
            }
            if (attempt >= 2)
            {
                string s = "Попытка №" + attempt;
                Fx.Text(g, s, smallFont, Theme.Muted, new RectangleF(textX, chipY, Width - textX - pad, Metrics.S(24)),
                    StringAlignment.Far, StringAlignment.Center, false);
            }

            // border and the time left
            Fx.StrokeRound(g, Fx.Alpha(accent, 0.5), 2f, new RectangleF(1, 1, Width - 2, Height - 2), Radius);
            float pw = Width * (float)HoldLeft;
            using (SolidBrush b = new SolidBrush(Fx.Alpha(accent, 0.85)))
                g.FillRectangle(b, 0, Height - Metrics.S(4), pw, Metrics.S(4));
        }
    }

    /// <summary>Small vector pictures for the kinds of violation. Drawn with paths, so they do not depend on an icon font.</summary>
    internal static class ViolationIcons
    {
        public static void Draw(Graphics g, ViolationKind kind, RectangleF r, Color color, float stroke, Color knockout)
        {
            using (Pen pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                PointF P(float u, float v) { return new PointF(x + u * w, y + v * h); }

                switch (kind)
                {
                    case ViolationKind.Site:
                        g.DrawEllipse(pen, x + 0.08f * w, y + 0.08f * h, 0.84f * w, 0.84f * h);
                        g.DrawEllipse(pen, x + 0.32f * w, y + 0.08f * h, 0.36f * w, 0.84f * h);
                        g.DrawLine(pen, P(0.08f, 0.5f), P(0.92f, 0.5f));
                        Slash(g, r, color, stroke, knockout);
                        break;
                    case ViolationKind.Download:
                        g.DrawLine(pen, P(0.5f, 0.08f), P(0.5f, 0.62f));
                        g.DrawLine(pen, P(0.5f, 0.64f), P(0.27f, 0.41f));
                        g.DrawLine(pen, P(0.5f, 0.64f), P(0.73f, 0.41f));
                        g.DrawLines(pen, new PointF[] { P(0.16f, 0.7f), P(0.16f, 0.9f), P(0.84f, 0.9f), P(0.84f, 0.7f) });
                        Slash(g, r, color, stroke, knockout);
                        break;
                    case ViolationKind.Game:
                        using (GraphicsPath gp = Fx.Round(new RectangleF(x + 0.04f * w, y + 0.26f * h, 0.92f * w, 0.5f * h), 0.2f * h))
                            g.DrawPath(pen, gp);
                        g.DrawLine(pen, P(0.26f, 0.42f), P(0.26f, 0.6f));
                        g.DrawLine(pen, P(0.17f, 0.51f), P(0.35f, 0.51f));
                        using (SolidBrush b = new SolidBrush(color))
                        {
                            g.FillEllipse(b, x + 0.62f * w, y + 0.4f * h, 0.1f * w, 0.1f * h);
                            g.FillEllipse(b, x + 0.74f * w, y + 0.52f * h, 0.1f * w, 0.1f * h);
                        }
                        Slash(g, r, color, stroke, knockout);
                        break;
                    case ViolationKind.Adult:
                        g.DrawEllipse(pen, x + 0.04f * w, y + 0.04f * h, 0.92f * w, 0.92f * h);
                        Fx.Text(g, "18+", Fx.Ui(Math.Max(8f, h * 0.27f), FontStyle.Bold), color, new RectangleF(x, y, w, h), StringAlignment.Center, StringAlignment.Center, false);
                        Slash(g, r, color, stroke, knockout);
                        break;
                    case ViolationKind.Program:
                        using (GraphicsPath gp = Fx.Round(new RectangleF(x + 0.06f * w, y + 0.14f * h, 0.88f * w, 0.72f * h), 0.1f * h))
                            g.DrawPath(pen, gp);
                        g.DrawLine(pen, P(0.06f, 0.34f), P(0.94f, 0.34f));
                        g.DrawLine(pen, P(0.34f, 0.5f), P(0.66f, 0.76f));
                        g.DrawLine(pen, P(0.66f, 0.5f), P(0.34f, 0.76f));
                        break;
                    case ViolationKind.File:
                        g.DrawPolygon(pen, new PointF[] { P(0.2f, 0.06f), P(0.6f, 0.06f), P(0.82f, 0.28f), P(0.82f, 0.94f), P(0.2f, 0.94f) });
                        g.DrawLines(pen, new PointF[] { P(0.6f, 0.06f), P(0.6f, 0.28f), P(0.82f, 0.28f) });
                        g.DrawLine(pen, P(0.34f, 0.52f), P(0.68f, 0.52f));
                        g.DrawLine(pen, P(0.34f, 0.7f), P(0.6f, 0.7f));
                        Slash(g, r, color, stroke, knockout);
                        break;
                    case ViolationKind.Folder:
                        g.DrawPolygon(pen, new PointF[] { P(0.06f, 0.22f), P(0.4f, 0.22f), P(0.5f, 0.36f), P(0.94f, 0.36f), P(0.94f, 0.82f), P(0.06f, 0.82f) });
                        g.DrawEllipse(pen, x + 0.4f * w, y + 0.5f * h, 0.2f * w, 0.2f * h);
                        break;
                    case ViolationKind.Wifi:
                        for (int i = 1; i <= 3; i++)
                        {
                            float rad = i * 0.17f;
                            g.DrawArc(pen, x + (0.5f - rad) * w, y + (0.78f - rad) * h, rad * 2 * w, rad * 2 * h, 225, 90);
                        }
                        using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, x + 0.45f * w, y + 0.73f * h, 0.1f * w, 0.1f * h);
                        Slash(g, r, color, stroke, knockout);
                        break;
                    default:
                        g.DrawEllipse(pen, x + 0.06f * w, y + 0.06f * h, 0.88f * w, 0.88f * h);
                        g.DrawLine(pen, P(0.5f, 0.28f), P(0.5f, 0.56f));
                        using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, x + 0.45f * w, y + 0.68f * h, 0.1f * w, 0.1f * h);
                        break;
                }
            }
        }

        private static void Slash(Graphics g, RectangleF r, Color color, float stroke, Color knockout)
        {
            PointF a = new PointF(r.X + 0.1f * r.Width, r.Y + 0.1f * r.Height);
            PointF b = new PointF(r.X + 0.9f * r.Width, r.Y + 0.9f * r.Height);
            using (Pen under = new Pen(knockout, stroke * 2.8f)) { under.StartCap = LineCap.Round; under.EndCap = LineCap.Round; g.DrawLine(under, a, b); }
            using (Pen top = new Pen(color, stroke * 1.15f)) { top.StartCap = LineCap.Round; top.EndCap = LineCap.Round; g.DrawLine(top, a, b); }
        }
    }

    /// <summary>"Hold the keys…" while the administrator chord is down: a ring that fills up.</summary>
    internal sealed class HoldOverlay : Form
    {
        private readonly AnimValue shown;
        private double target;

        public HoldOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(14, 11, 34);
            Size = new Size(Metrics.S(380), Metrics.S(84));
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Rectangle s = Screen.PrimaryScreen.Bounds;
            Location = new Point(s.X + (s.Width - Width) / 2, s.Y + Metrics.DockHeight + Metrics.S(16));
            using (GraphicsPath p = Fx.Round(new RectangleF(0, 0, Width, Height), Metrics.S(20)))
                Region = new Region(p);
            shown = new AnimValue(this, 0);
            shown.Stiffness = 520;
            shown.Damping = 40;
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
            target = Math.Max(0, Math.Min(1, p));
            if (!Visible) Show();
            shown.To(target);
            Invalidate();
        }

        public void HideNow()
        {
            if (Visible) Hide();
            shown.Jump(0);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            Rectangle full = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush b = Fx.Gradient(full, Color.FromArgb(43, 35, 96), Color.FromArgb(14, 11, 34), 90f))
                g.FillRectangle(b, full);
            Fx.StrokeRound(g, Color.FromArgb(150, 192, 132, 252), 2f, new RectangleF(1, 1, Width - 2, Height - 2), Metrics.S(20));

            float d = Metrics.S(52);
            RectangleF ring = new RectangleF(Metrics.S(20), (Height - d) / 2, d, d);
            using (Pen back = new Pen(Color.FromArgb(50, 192, 132, 252), Metrics.S(6))) g.DrawEllipse(back, ring);
            double v = Math.Max(0, Math.Min(1, shown.Value));
            if (v > 0.002)
                using (Pen fill = new Pen(Color.FromArgb(192, 132, 252), Metrics.S(6)))
                {
                    fill.StartCap = LineCap.Round;
                    fill.EndCap = LineCap.Round;
                    g.DrawArc(fill, ring, -90, (float)(360 * v));
                }
            Fx.Text(g, ((int)Math.Round(v * 100)) + "%", Fx.Ui(10f, FontStyle.Bold), Color.White, ring, StringAlignment.Center, StringAlignment.Center, false);
            Fx.Text(g, "Удерживайте клавиши…", Fx.Ui(13f, FontStyle.Bold), Color.White,
                new RectangleF(ring.Right + Metrics.S(18), 0, Width - ring.Right - Metrics.S(30), Height), StringAlignment.Near, StringAlignment.Center, true);
        }
    }
}
