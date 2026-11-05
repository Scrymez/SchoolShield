using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// The start screen: the shield pops in with a burst of light, the check mark draws itself, the name rises, then everything
    /// fades and the launcher is there. About two seconds; a click or a key skips it. Layered window, covers the whole screen.
    /// </summary>
    internal sealed class IntroForm : Form, IAnimated
    {
        private const double Total = 2.35;
        private const double FadeStart = 1.85;

        private readonly string title;
        private readonly string subtitle;
        private double t;
        private bool skipped;

        public static void Play(string title, string subtitle)
        {
            if (!Motion.Enabled) return;
            IntroForm f = new IntroForm(title, subtitle);
            f.Show();
            f.Activate();
            Motion.Add(f);
        }

        private IntroForm(string title, string subtitle)
        {
            this.title = title ?? "SchoolShield";
            this.subtitle = subtitle ?? "";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
            BackColor = Theme.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            KeyPreview = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnMouseDown(MouseEventArgs e) { skipped = true; base.OnMouseDown(e); }
        protected override void OnKeyDown(KeyEventArgs e) { skipped = true; base.OnKeyDown(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        public bool Step(double seconds)
        {
            if (IsDisposed) return false;
            t += seconds;
            if (skipped && t < FadeStart) t = FadeStart;
            if (t >= Total) { Close(); return false; }
            if (t >= FadeStart) Opacity = Math.Max(0, 1 - Easing.InCubic((t - FadeStart) / (Total - FadeStart)));
            Invalidate();
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Theme.Background);
            float cx = Width / 2f, cy = Height / 2f - Height * 0.04f;
            float size = Math.Min(Width, Height) * 0.17f;

            double pop = Easing.OutBack((t - 0.1) / 0.75);
            double check = Easing.OutCubic((t - 0.55) / 0.55);
            double ex = t >= FadeStart ? Easing.InCubic((t - FadeStart) / (Total - FadeStart)) : 0;
            float scale = (float)(Math.Max(0.001, pop) * (1 + 0.14 * ex));

            // light behind the logo, grows with the pop
            double glow = Easing.OutCubic((t - 0.05) / 0.9);
            Fx.Glow(g, new PointF(cx, cy), size * 3.2f * (float)(0.6 + 0.4 * glow), Theme.Accent, (int)(Theme.Dark ? 80 * glow : 50 * glow));
            Fx.Glow(g, new PointF(cx + size * 0.8f, cy + size * 0.5f), size * 2.4f, Theme.Accent2, (int)(Theme.Dark ? 55 * glow : 36 * glow));

            // a ring that spreads out from the logo
            double ring = Easing.OutCubic((t - 0.25) / 1.0);
            if (ring > 0 && ring < 1)
                using (Pen p = new Pen(Fx.Alpha(Theme.Accent, (1 - ring) * 0.6), 2.4f))
                {
                    float rr = size * (0.7f + (float)ring * 2.1f);
                    g.DrawEllipse(p, cx - rr, cy - rr, rr * 2, rr * 2);
                }

            // sparks flying out
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI * 2 / 16 + (i % 3) * 0.17;
                double sp = Easing.OutCubic((t - 0.3 - (i % 4) * 0.04) / 1.1);
                if (sp <= 0 || sp >= 1) continue;
                double dist = size * (0.9 + 1.7 * sp + (i % 5) * 0.12);
                float px = cx + (float)(Math.Cos(a) * dist), py = cy + (float)(Math.Sin(a) * dist);
                float ps = (float)((1 - sp) * (3 + (i % 3) * 2));
                using (SolidBrush b = new SolidBrush(Fx.Alpha(i % 2 == 0 ? Theme.Accent : Theme.Accent2, (1 - sp) * 0.9)))
                    g.FillEllipse(b, px - ps, py - ps, ps * 2, ps * 2);
            }

            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(scale, scale);
            g.RotateTransform((float)((1 - Math.Min(1, pop)) * -18));
            BrandControl.DrawLogo(g, new RectangleF(-size / 2, -size / 2, size, size), check, 0.0);
            g.Restore(st);

            // name and a line under it
            double nm = Easing.OutCubic((t - 0.75) / 0.6);
            double sb = Easing.OutCubic((t - 0.95) / 0.6);
            float nameY = cy + size * 0.78f;
            Fx.Text(g, title, Fx.Ui(Math.Max(22f, size * 0.22f), FontStyle.Bold), Fx.Alpha(Theme.Text, nm * (1 - ex)),
                new RectangleF(0, nameY + (float)((1 - nm) * 24), Width, size * 0.4f), StringAlignment.Center, StringAlignment.Near, false);
            Fx.Text(g, subtitle, Fx.Ui(Math.Max(12f, size * 0.1f)), Fx.Alpha(Theme.Muted, sb * (1 - ex)),
                new RectangleF(0, nameY + size * 0.42f + (float)((1 - sb) * 18), Width, size * 0.3f), StringAlignment.Center, StringAlignment.Near, false);

            // thin loading line
            double ln = Math.Min(1, t / FadeStart);
            float lw = Math.Min(Width * 0.3f, 420f);
            RectangleF lr = new RectangleF(cx - lw / 2, Height * 0.88f, lw, 3);
            using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Line, 1 - ex))) Fx.FillRound(g, b, lr, 1.5f);
            using (LinearGradientBrush b = Fx.Gradient(new RectangleF(lr.X, lr.Y, Math.Max(2, lr.Width * (float)Easing.InOutCubic(ln)), lr.Height), Theme.Accent, Theme.Accent2, 0f))
                Fx.FillRound(g, b, new RectangleF(lr.X, lr.Y, Math.Max(2, lr.Width * (float)Easing.InOutCubic(ln)), lr.Height), 1.5f);
        }
    }
}
