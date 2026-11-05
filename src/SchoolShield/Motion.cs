using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>Easing curves, all of them take and return a value in 0..1 (overshoot curves may leave that range on the way).</summary>
    internal static class Easing
    {
        public static double Clamp(double t) { return t < 0 ? 0 : t > 1 ? 1 : t; }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        public static double Linear(double t) { return Clamp(t); }
        public static double OutCubic(double t) { t = Clamp(t); return 1 - Math.Pow(1 - t, 3); }
        public static double OutQuint(double t) { t = Clamp(t); return 1 - Math.Pow(1 - t, 5); }
        public static double InCubic(double t) { t = Clamp(t); return t * t * t; }
        public static double InOutCubic(double t) { t = Clamp(t); return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }

        /// <summary>Goes a little past the target and settles back: the "springy" entrance.</summary>
        public static double OutBack(double t)
        {
            t = Clamp(t);
            const double c1 = 1.70158, c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }

        public static double OutElastic(double t)
        {
            t = Clamp(t);
            if (t <= 0 || t >= 1) return t;
            const double c4 = 2 * Math.PI / 3;
            return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
        }

        /// <summary>A short damped wobble from 0 back to 0: used for shaking an icon.</summary>
        public static double Wobble(double t)
        {
            t = Clamp(t);
            return Math.Sin(t * Math.PI * 6) * Math.Pow(1 - t, 2);
        }
    }

    /// <summary>Something the motion clock advances every frame. Step returns false when it has finished.</summary>
    internal interface IAnimated
    {
        bool Step(double seconds);
    }

    /// <summary>
    /// One shared 60 fps clock for every animation in the program. It runs only while something moves, so an idle launcher costs
    /// nothing. Animations are time based: a slow computer skips frames instead of slowing the motion down.
    /// </summary>
    internal static class Motion
    {
        public static bool Enabled = true;

        private static readonly List<IAnimated> active = new List<IAnimated>();
        private static readonly List<IAnimated> waiting = new List<IAnimated>();
        private static System.Windows.Forms.Timer timer;
        private static Stopwatch clock;
        private static long last;
        private static bool stepping;

        /// <summary>Turns animations on or off. They are also off when Windows has "animation effects" disabled or in a remote session.</summary>
        public static void Configure(bool wanted)
        {
            bool system = true;
            try { system = SystemInformation.UIEffectsEnabled && !SystemInformation.TerminalServerSession; } catch { }
            Enabled = wanted && system;
        }

        public static void Add(IAnimated a)
        {
            if (stepping) { if (!waiting.Contains(a)) waiting.Add(a); return; }
            if (!active.Contains(a)) active.Add(a);
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 15;
                timer.Tick += delegate { Tick(); };
            }
            if (!timer.Enabled)
            {
                if (clock == null) clock = Stopwatch.StartNew();
                last = clock.ElapsedTicks;
                timer.Start();
            }
        }

        private static void Tick()
        {
            long now = clock.ElapsedTicks;
            double dt = (now - last) / (double)Stopwatch.Frequency;
            last = now;
            if (dt > 0.05) dt = 0.05;          // after a stall do not jump
            if (dt < 0.0005) dt = 0.0005;

            stepping = true;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                bool keep;
                try { keep = active[i].Step(dt); }
                catch { keep = false; }
                if (!keep) active.RemoveAt(i);
            }
            stepping = false;

            if (waiting.Count > 0)
            {
                foreach (IAnimated w in waiting) if (!active.Contains(w)) active.Add(w);
                waiting.Clear();
            }
            if (active.Count == 0 && timer != null) timer.Stop();
        }
    }

    /// <summary>A timed animation: step(eased progress 0..1) every frame, then done().</summary>
    internal sealed class Tween : IAnimated
    {
        private readonly double duration;
        private readonly double delay;
        private readonly Func<double, double> ease;
        private readonly Action<double> step;
        private readonly Action done;
        private double elapsed;
        private bool cancelled;

        private Tween(double ms, double delayMs, Func<double, double> ease, Action<double> step, Action done)
        {
            duration = Math.Max(1, ms) / 1000.0;
            delay = Math.Max(0, delayMs) / 1000.0;
            this.ease = ease ?? Easing.OutCubic;
            this.step = step;
            this.done = done;
        }

        public static Tween Run(double ms, Func<double, double> ease, Action<double> step, Action done, double delayMs)
        {
            Tween t = new Tween(ms, delayMs, ease, step, done);
            if (!Motion.Enabled)
            {
                try { step(1); if (done != null) done(); } catch { }
                return t;
            }
            Motion.Add(t);
            return t;
        }

        public static Tween Run(double ms, Func<double, double> ease, Action<double> step, Action done)
        {
            return Run(ms, ease, step, done, 0);
        }

        public static Tween Run(double ms, Func<double, double> ease, Action<double> step)
        {
            return Run(ms, ease, step, null, 0);
        }

        public void Cancel() { cancelled = true; }

        public bool Step(double seconds)
        {
            if (cancelled) return false;
            elapsed += seconds;
            if (elapsed < delay) return true;
            double p = (elapsed - delay) / duration;
            if (p >= 1)
            {
                step(1);
                if (done != null) done();
                return false;
            }
            step(ease(p));
            return true;
        }
    }

    /// <summary>
    /// A number that chases its target like a spring. Hover, press and selection states use it, so an interrupted motion turns around
    /// smoothly instead of jumping. Invalidates its control on every frame.
    /// </summary>
    internal sealed class AnimValue : IAnimated
    {
        public double Value;
        public double Target;
        public double Stiffness = 340;
        public double Damping = 27;
        public Action Changed;

        private double velocity;
        private readonly Control owner;

        public AnimValue(Control owner, double initial)
        {
            this.owner = owner;
            Value = Target = initial;
        }

        public void To(double target)
        {
            Target = target;
            if (!Motion.Enabled) { Value = target; velocity = 0; Raise(); return; }
            Motion.Add(this);
        }

        public void Jump(double v) { Value = Target = v; velocity = 0; Raise(); }

        /// <summary>Push: gives the spring a kick (a small bounce when something is pressed or dropped).</summary>
        public void Kick(double speed)
        {
            velocity += speed;
            if (Motion.Enabled) Motion.Add(this);
        }

        public bool Step(double seconds)
        {
            if (owner != null && (owner.IsDisposed || !owner.IsHandleCreated)) { Value = Target; velocity = 0; return false; }
            double remaining = seconds;
            while (remaining > 0)
            {
                double h = Math.Min(remaining, 1.0 / 120);
                double a = -Stiffness * (Value - Target) - Damping * velocity;
                velocity += a * h;
                Value += velocity * h;
                remaining -= h;
            }
            bool settled = Math.Abs(Value - Target) < 0.0005 && Math.Abs(velocity) < 0.005;
            if (settled) { Value = Target; velocity = 0; }
            Raise();
            return !settled;
        }

        private void Raise()
        {
            if (owner != null && !owner.IsDisposed) owner.Invalidate();
            if (Changed != null) Changed();
        }
    }

    /// <summary>Drawing helpers shared by every custom control.</summary>
    internal static class Fx
    {
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        /// <summary>Cached font (never dispose it): painting must not create a font per frame.</summary>
        public static Font Ui(float pt, FontStyle style)
        {
            string key = pt.ToString("0.0") + "|" + (int)style;
            Font f;
            lock (fonts)
            {
                if (!fonts.TryGetValue(key, out f)) { f = new Font("Segoe UI", pt, style); fonts[key] = f; }
            }
            return f;
        }

        public static Font Ui(float pt) { return Ui(pt, FontStyle.Regular); }

        private static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        public static Font Icons(float pt)
        {
            Font f;
            lock (iconFonts)
            {
                if (!iconFonts.TryGetValue(pt, out f)) { f = new Font("Segoe MDL2 Assets", pt); iconFonts[pt] = f; }
            }
            return f;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            t = Easing.Clamp(t);
            return Color.FromArgb((int)Math.Round(a.A + (b.A - a.A) * t), (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, double a)
        {
            return Color.FromArgb((int)Math.Round(255 * Easing.Clamp(a)), c.R, c.G, c.B);
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c.R, c.G, c.B);
        }

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Brush b, RectangleF r, float radius)
        {
            using (GraphicsPath p = Round(r, radius)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, float width, RectangleF r, float radius)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static LinearGradientBrush Gradient(RectangleF r, Color a, Color b, float angle)
        {
            if (r.Width < 1) r.Width = 1;
            if (r.Height < 1) r.Height = 1;
            return new LinearGradientBrush(r, a, b, angle);
        }

        /// <summary>Soft shadow: a few rounded rectangles that grow and fade. Cheap enough to repaint every frame for one card.</summary>
        public static void Shadow(Graphics g, RectangleF r, float radius, int spread, int maxAlpha, Color color)
        {
            for (int i = spread; i >= 1; i--)
            {
                double k = 1.0 - (i - 1) / (double)spread;
                int a = (int)(maxAlpha * k * k / 3.0);
                if (a <= 0) continue;
                RectangleF q = RectangleF.Inflate(r, i, i);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(255, a), color)))
                    FillRound(g, b, q, radius + i);
            }
        }

        /// <summary>Soft round glow (a radial gradient that fades to nothing).</summary>
        public static void Glow(Graphics g, PointF center, float radius, Color color, int alpha)
        {
            if (radius < 2 || alpha <= 0) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
                using (PathGradientBrush pg = new PathGradientBrush(path))
                {
                    pg.CenterPoint = center;
                    pg.CenterColor = Color.FromArgb(Math.Min(255, alpha), color);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillEllipse(pg, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                }
            }
        }

        public static void Text(Graphics g, string text, Font font, Color color, RectangleF r, StringAlignment h, StringAlignment v, bool ellipsis)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(color))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                if (ellipsis) sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, b, r, sf);
            }
        }

        public static Bitmap Snapshot(Control c)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            return bmp;
        }

        /// <summary>Plays the entrance of everything on a page that knows how, one after another.</summary>
        public static void PlayEnter(Control page)
        {
            int i = 0;
            PlayEnter(page, ref i);
        }

        private static void PlayEnter(Control root, ref int i)
        {
            foreach (Control c in root.Controls)
            {
                IEnterAnimated a = c as IEnterAnimated;
                if (a != null) { if (c.Visible) a.PlayEnter(i++); }
                else if (c.HasChildren) PlayEnter(c, ref i);
            }
        }

        /// <summary>Window opens with a quick fade (dialogs, wizard, panel).</summary>
        public static void FadeIn(Form f, int ms)
        {
            if (!Motion.Enabled) return;
            f.Opacity = 0;
            f.Shown += delegate
            {
                Tween.Run(ms, Easing.OutCubic, delegate (double t) { if (!f.IsDisposed) f.Opacity = t; });
            };
        }

        /// <summary>
        /// Swaps the content of a region with a cross-fade: a picture of the old content is laid over the region in a click-through
        /// window and fades out, showing the new content underneath. Falls back to a plain swap whenever a picture is impossible.
        /// </summary>
        public static void Crossfade(Control region, Action swap, int ms)
        {
            if (!Motion.Enabled || region == null || !region.IsHandleCreated || !region.Visible || region.Width < 60 || region.Height < 60)
            {
                swap();
                return;
            }
            Bitmap snap = null;
            try { snap = Snapshot(region); }
            catch { swap(); return; }
            swap();
            try
            {
                region.Update();
                SnapshotVeil veil = new SnapshotVeil(snap, region.RectangleToScreen(region.ClientRectangle));
                veil.Show();
                Tween.Run(ms, Easing.OutCubic,
                    delegate (double t) { if (!veil.IsDisposed) veil.Opacity = 1 - t; },
                    delegate { if (!veil.IsDisposed) veil.Close(); });
            }
            catch { if (snap != null) snap.Dispose(); }
        }
    }

    /// <summary>The picture of the previous page, fading away on top of the new one. Click-through, never takes focus.</summary>
    internal sealed class SnapshotVeil : Form
    {
        private readonly Bitmap picture;

        public SnapshotVeil(Bitmap picture, Rectangle screenBounds)
        {
            this.picture = picture;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            Opacity = 1;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // tool window, no activation, click-through
                cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(picture, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picture != null) picture.Dispose();
            base.Dispose(disposing);
        }
    }
}
