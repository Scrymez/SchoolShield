using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>One page of the launcher (Home, Programs, Documents...).</summary>
    internal abstract class Section : UserControl
    {
        protected readonly MainForm Main;

        protected Section(MainForm main)
        {
            Main = main;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            Padding = new Padding(Metrics.PagePad, Metrics.S(8), Metrics.PagePad, Metrics.S(8));
        }

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract string Glyph { get; }
        public virtual void OnShown() { }
        public virtual void OnSearch(string query) { }
        public virtual void OnAppsChanged() { }

        protected static PillButton Chip(string text, bool selected, EventHandler click)
        {
            PillButton b = new PillButton(text, selected);
            if (click != null) b.Click += click;
            return b;
        }
    }

    // ====================================================================== HOME

    internal sealed class HomeSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();

        public HomeSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
            Resize += delegate { FitWidths(); };
        }

        public override string Id { get { return "home"; } }
        public override string Title { get { return "Главная"; } }
        public override string Glyph { get { return "\uE80F"; } }

        public override void OnShown() { Rebuild(); }
        public override void OnAppsChanged() { Rebuild(); }

        private int RowWidth { get { return Math.Max(300, root.ClientSize.Width - Metrics.U(34)); } }

        private void FitWidths()
        {
            int w = RowWidth;
            root.SuspendLayout();
            foreach (Control c in root.Controls)
            {
                c.MaximumSize = new Size(w, 0);
                HeroCard hero = c as HeroCard;
                if (hero != null) hero.Width = w;
                TileGrid grid = c as TileGrid;
                if (grid != null) grid.Relayout(w);
            }
            root.ResumeLayout();
        }

        private void Rebuild()
        {
            root.SuspendLayout();
            foreach (Control c in new List<Control>(Controls2(root))) { root.Controls.Remove(c); c.Dispose(); }

            root.Controls.Add(new HeroCard());

            root.Controls.Add(UiKit.Heading("Быстрый доступ"));
            TileGrid quick = NewGrid();
            AddQuick(quick, "apps", "Программы", "\uE71D");
            AddQuick(quick, "docs", "Документы", "\uE8A5");
            AddQuick(quick, "media", "Фото, видео, музыка", "\uE714");
            AddQuick(quick, "files", "Мои файлы", "\uE8B7");
            AddQuick(quick, "usb", "Флешки", "\uE88E");
            AddQuick(quick, "web", "Интернет", "\uE774");
            root.Controls.Add(quick);

            root.Controls.Add(UiKit.Heading("Избранное"));
            TileGrid fav = NewGrid();
            foreach (string name in Main.User.Favorites)
            {
                AppEntry e = Main.FindApp(name);
                if (e != null) fav.Controls.Add(Main.MakeAppTile(e));
            }
            if (fav.Controls.Count == 0) { fav.Dispose(); root.Controls.Add(UiKit.Hint("Нажмите правой кнопкой мыши на программе и выберите «В избранное».")); }
            else root.Controls.Add(fav);

            List<AppEntry> recent = new List<AppEntry>();
            foreach (string name in Main.User.RecentApps)
            {
                AppEntry e = Main.FindApp(name);
                if (e != null) recent.Add(e);
            }
            if (recent.Count > 0)
            {
                root.Controls.Add(UiKit.Heading("Недавно открывали"));
                TileGrid rec = NewGrid();
                foreach (AppEntry e in recent) rec.Controls.Add(Main.MakeAppTile(e));
                root.Controls.Add(rec);
            }
            FitWidths();
            root.ResumeLayout();
        }

        private static TileGrid NewGrid()
        {
            TileGrid g = new TileGrid();
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Margin = new Padding(0);
            return g;
        }

        private static IEnumerable<Control> Controls2(Control c)
        {
            foreach (Control x in c.Controls) yield return x;
        }

        private void AddQuick(FlowLayoutPanel flow, string section, string title, string glyph)
        {
            if (!Main.Cfg.SectionEnabled(section)) return;
            TileControl t = new TileControl(title, glyph);
            t.Activated += delegate { Main.ShowSection(section); };
            flow.Controls.Add(t);
        }
    }

    /// <summary>The greeting card at the top of the home page: a gradient with moving light, a watermark shield, the greeting and the date.</summary>
    internal sealed class HeroCard : Control, IEnterAnimated
    {
        private double enter = 1;
        private double shine = -1;
        private Tween enterTween, shineTween;

        public HeroCard()
        {
            Height = Metrics.U(Metrics.Compact ? 124 : 148);
            Width = Metrics.U(900);
            Margin = new Padding(0, Metrics.U(6), 0, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void PlayEnter(int index)
        {
            if (enterTween != null) enterTween.Cancel();
            if (shineTween != null) shineTween.Cancel();
            if (!Motion.Enabled) { enter = 1; shine = -1; Invalidate(); return; }
            enter = 0;
            enterTween = Tween.Run(700, Easing.OutQuint, delegate (double t) { enter = t; Invalidate(); });
            shineTween = Tween.Run(1300, Easing.InOutCubic, delegate (double t) { shine = t; Invalidate(); }, delegate { shine = -1; Invalidate(); }, 350);
        }

        private static string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 6 ? "Доброй ночи!" : h < 12 ? "Доброе утро!" : h < 18 ? "Добрый день!" : "Добрый вечер!";
        }

        private static string DateText()
        {
            string d = DateTime.Now.ToString("dddd, d MMMM", new System.Globalization.CultureInfo("ru-RU"));
            return d.Length > 0 ? char.ToUpper(d[0]) + d.Substring(1) : d;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            double en = Math.Max(0, Math.Min(1, enter));
            RectangleF r = new RectangleF(1, 1 + (float)((1 - en) * Metrics.U(16)), Width - 3, Height - 3);
            float radius = Metrics.U(26);
            Color left = Theme.Dark ? Fx.Blend(Theme.Card, Theme.Accent2, 0.55) : Fx.Blend(Color.White, Theme.Accent2, 0.85);
            Color right = Theme.Dark ? Fx.Blend(Theme.Card, Theme.Accent, 0.40) : Fx.Blend(Color.White, Theme.Accent, 0.85);
            using (GraphicsPath path = Fx.Round(r, radius))
            {
                using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(left, en), Fx.Alpha(right, en), 20f)) g.FillPath(b, path);
                GraphicsState st = g.Save();
                g.SetClip(path);
                // soft lights
                Fx.Glow(g, new PointF(r.Right - r.Width * 0.12f, r.Y + r.Height * 0.2f), r.Height * 1.5f, Color.White, (int)(46 * en));
                Fx.Glow(g, new PointF(r.X + r.Width * 0.25f, r.Bottom), r.Height * 1.2f, Theme.Accent, (int)(60 * en));
                // watermark shield
                float ss = r.Height * 1.25f;
                using (GraphicsPath shield = BrandControl.ShieldPath(new RectangleF(r.Right - ss * 0.95f, r.Y - ss * 0.1f, ss, ss)))
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(34 * en), 255, 255, 255)))
                    g.FillPath(b, shield);
                // one sweep of light across the card
                if (shine >= 0 && shine <= 1)
                {
                    float x = (float)(r.X - r.Width * 0.3 + shine * r.Width * 1.6);
                    PointF[] band = new PointF[] { new PointF(x, r.Bottom), new PointF(x + r.Height * 0.55f, r.Y), new PointF(x + r.Height * 0.55f + r.Width * 0.12f, r.Y), new PointF(x + r.Width * 0.12f, r.Bottom) };
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPolygon(b, band);
                }
                g.Restore(st);
                using (Pen pen = new Pen(Color.FromArgb((int)(60 * en), 255, 255, 255), 1.4f)) g.DrawPath(pen, path);
            }

            float pad = Metrics.U(34);
            float slide = (float)((1 - Easing.OutCubic(en)) * Metrics.U(30));
            Color ink = Theme.Dark ? Color.White : Color.FromArgb(15, 23, 42);
            Fx.Text(g, Greeting(), Fx.Ui(Metrics.Fz(Metrics.Compact ? 24 : 30), FontStyle.Bold), Fx.Alpha(ink, en),
                new RectangleF(pad - slide, r.Y + r.Height * 0.2f, r.Width - pad * 2, Metrics.U(52)), StringAlignment.Near, StringAlignment.Near, true);
            Fx.Text(g, DateText(), Fx.Ui(Metrics.Fz(Metrics.Compact ? 12 : 14)), Fx.Alpha(ink, en * 0.78),
                new RectangleF(pad - slide * 1.4f, r.Y + r.Height * 0.2f + Metrics.U(Metrics.Compact ? 40 : 52), r.Width - pad * 2, Metrics.U(30)), StringAlignment.Near, StringAlignment.Near, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (enterTween != null) enterTween.Cancel();
                if (shineTween != null) shineTween.Cancel();
            }
            base.Dispose(disposing);
        }
    }

    // ====================================================================== PROGRAMS

    internal sealed class AppsSection : Section
    {
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        private readonly TileGrid grid = new TileGrid();
        private readonly Label empty = new Label();
        private string category = "all";
        private string query = "";
        private readonly List<TileControl> tiles = new List<TileControl>();

        public AppsSection(MainForm main) : base(main)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoScroll = true;
            grid.BackColor = Theme.Background;
            grid.Resize += delegate { grid.Relayout(grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Metrics.U(4)); };

            chips.Dock = DockStyle.Top;
            chips.AutoSize = true;
            chips.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            chips.BackColor = Theme.Background;
            chips.WrapContents = true;
            chips.Padding = new Padding(0, 6, 0, 0);

            empty.Dock = DockStyle.Top;
            empty.Height = 40;
            empty.ForeColor = Theme.Muted;
            empty.Font = Theme.UiFont(12);
            empty.Visible = false;
            empty.Text = "Ничего не найдено.";

            Controls.Add(grid);
            Controls.Add(empty);
            Controls.Add(chips);
        }

        public override string Id { get { return "apps"; } }
        public override string Title { get { return "Программы"; } }
        public override string Glyph { get { return ""; } }

        public override void OnShown() { if (tiles.Count == 0) Build(); else Apply(); }
        public override void OnAppsChanged() { Build(); }
        public override void OnSearch(string q) { query = q ?? ""; Apply(); }

        public void SelectCategory(string id) { category = id; Build(); }

        private void Build()
        {
            grid.SuspendLayout();
            foreach (TileControl t in tiles) { grid.Controls.Remove(t); t.Dispose(); }
            tiles.Clear();
            foreach (AppEntry e in Main.Apps)
            {
                TileControl t = Main.MakeAppTile(e);
                tiles.Add(t);
                grid.Controls.Add(t);
            }
            grid.ResumeLayout();
            grid.Relayout(grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Metrics.U(4));

            chips.SuspendLayout();
            foreach (Control c in new List<Control>(ChipControls())) { chips.Controls.Remove(c); c.Dispose(); }
            chips.Controls.Add(Chip("Все (" + Main.Apps.Count + ")", category == "all", delegate { SelectCategory("all"); }));
            List<string> ids = new List<string>();
            foreach (CategoryRule r in Main.Cfg.CategoryRules) ids.Add(r.Id);
            ids.Add(AppCatalog.OtherId);
            foreach (string id in ids)
            {
                int n = 0;
                foreach (AppEntry e in Main.Apps) if (e.Category == id) n++;
                if (n == 0) continue;
                string captured = id;
                chips.Controls.Add(Chip(Main.CategoryName(id) + " (" + n + ")", category == id, delegate { SelectCategory(captured); }));
            }
            chips.ResumeLayout();
            Apply();
        }

        private IEnumerable<Control> ChipControls()
        {
            foreach (Control c in chips.Controls) yield return c;
        }

        private void Apply()
        {
            grid.SuspendLayout();
            int visible = 0;
            foreach (TileControl t in tiles)
            {
                bool ok = (category == "all" || t.Entry.Category == category)
                    && (query.Length == 0 || t.Entry.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
                t.Visible = ok;
                if (ok) visible++;
            }
            grid.ResumeLayout();
            empty.Visible = visible == 0;
            empty.Text = Main.Apps.Count == 0 ? "Программы загружаются…" : "Ничего не найдено.";
        }

        public void RefreshIcons() { foreach (TileControl t in tiles) t.Invalidate(); }
    }

    // ====================================================================== DOCUMENTS

    internal sealed class DocsSection : Section
    {
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        private readonly ListView list = UiKit.List(true);
        private readonly Label status = new Label();
        private readonly ImageList icons = new ImageList();
        private List<FileItem> all = new List<FileItem>();
        private string tab = "all";
        private string query = "";
        private DateTime lastScan = DateTime.MinValue;
        private int scanId;

        private static readonly string[][] Tabs = new string[][] {
            new string[] { "all", "Все" },
            new string[] { "doc", "Документы" },
            new string[] { "ppt", "Презентации" },
            new string[] { "xls", "Таблицы" },
            new string[] { "book", "PDF и книги" },
            new string[] { "txt", "Текст" }
        };

        public DocsSection(MainForm main) : base(main)
        {
            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(32, 32);
            list.LargeImageList = icons;
            list.TileSize = new Size(Metrics.S(420), Metrics.S(56));
            list.Columns.Add("Папка", 200);
            list.Columns.Add("Изменён", 120);
            list.ItemActivate += delegate { OpenSelected(); };
            list.MouseUp += OnMouseUp;

            status.Dock = DockStyle.Top;
            status.Height = 30;
            status.ForeColor = Theme.Muted;
            status.Font = Theme.UiFont(10.5f);

            chips.Dock = DockStyle.Top;
            chips.AutoSize = true;
            chips.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            chips.BackColor = Theme.Background;
            chips.Padding = new Padding(0, 6, 0, 0);

            Controls.Add(list);
            Controls.Add(status);
            Controls.Add(chips);
        }

        public override string Id { get { return "docs"; } }
        public override string Title { get { return "Документы"; } }
        public override string Glyph { get { return ""; } }

        public override void OnShown()
        {
            if ((DateTime.Now - lastScan).TotalSeconds > 6) Scan();
            else Rebuild();
        }

        public override void OnSearch(string q) { query = q ?? ""; Rebuild(); }

        private static bool WantKind(FileKind k)
        {
            return k == FileKind.Document || k == FileKind.Presentation || k == FileKind.Table
                || k == FileKind.Book || k == FileKind.Text;
        }

        private void Scan()
        {
            lastScan = DateTime.Now;
            int id = ++scanId;
            status.Text = "Идёт поиск документов…";
            List<FileRoot> roots = FileRoots.Resolve(Main.Cfg);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<FileItem> found = FileIndex.Scan(roots, WantKind, 6000);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (id != scanId) return;
                        all = found;
                        Rebuild();
                    }));
                }
                catch { }
            });
        }

        private bool Match(FileItem f)
        {
            switch (tab)
            {
                case "doc": if (f.Kind != FileKind.Document) return false; break;
                case "ppt": if (f.Kind != FileKind.Presentation) return false; break;
                case "xls": if (f.Kind != FileKind.Table) return false; break;
                case "book": if (f.Kind != FileKind.Book) return false; break;
                case "txt": if (f.Kind != FileKind.Text) return false; break;
            }
            return query.Length == 0 || f.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private int CountOf(string t)
        {
            int n = 0;
            string saved = tab;
            tab = t;
            string q = query;
            query = "";
            foreach (FileItem f in all) if (Match(f)) n++;
            tab = saved;
            query = q;
            return n;
        }

        private void Rebuild()
        {
            chips.SuspendLayout();
            foreach (Control c in new List<Control>(ChipControls())) { chips.Controls.Remove(c); c.Dispose(); }
            foreach (string[] t in Tabs)
            {
                string id = t[0];
                chips.Controls.Add(Chip(t[1] + " (" + CountOf(id) + ")", tab == id, delegate { tab = id; Rebuild(); }));
            }
            chips.ResumeLayout();

            list.BeginUpdate();
            list.Items.Clear();
            int shown = 0;
            foreach (FileItem f in all)
            {
                if (!Match(f)) continue;
                if (shown++ >= 1500) break;
                string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                if (!icons.Images.ContainsKey(ext))
                {
                    Bitmap ic = ShellIcons.Get(ext, false);
                    if (ic != null) icons.Images.Add(ext, ic);
                }
                ListViewItem it = new ListViewItem(f.Name);
                it.ImageKey = icons.Images.ContainsKey(ext) ? ext : null;
                it.SubItems.Add(Path.GetFileName(f.Folder));
                it.SubItems.Add(f.Modified.ToString("dd.MM.yyyy") + "  •  " + FileIndex.Size(f.Size));
                it.Tag = f;
                list.Items.Add(it);
            }
            list.EndUpdate();
            status.Text = shown == 0 ? "Здесь пока ничего нет. Файлы появятся, когда вы сохраните их в «Мои документы»." : "Найдено файлов: " + shown;
        }

        private IEnumerable<Control> ChipControls()
        {
            foreach (Control c in chips.Controls) yield return c;
        }

        private FileItem Selected()
        {
            return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as FileItem;
        }

        private void OpenSelected()
        {
            FileItem f = Selected();
            if (f != null) Main.OpenFile(f.Path);
        }

        private void OnMouseUp(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem it = list.GetItemAt(e.X, e.Y);
            if (it == null) return;
            it.Selected = true;
            Menus.ForFile(Main, ((FileItem)it.Tag).Path, false, null);
        }
    }

    // ====================================================================== MEDIA

    internal sealed class MediaSection : Section
    {
        private readonly FlowLayoutPanel chips = new FlowLayoutPanel();
        private readonly ListView pictures = UiKit.List(false);
        private readonly ListView others = UiKit.List(true);
        private readonly Label status = new Label();
        private readonly ImageList thumbs = new ImageList();
        private readonly ImageList icons = new ImageList();
        private string tab = "img";
        private string query = "";
        private int loadId;
        private DateTime lastScan = DateTime.MinValue;

        public MediaSection(MainForm main) : base(main)
        {
            thumbs.ColorDepth = ColorDepth.Depth32Bit;
            thumbs.ImageSize = new Size(120, 120);
            pictures.LargeImageList = thumbs;
            pictures.ItemActivate += delegate { OpenFrom(pictures); };
            pictures.MouseUp += delegate (object s, MouseEventArgs e) { Popup(pictures, e); };

            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(32, 32);
            others.LargeImageList = icons;
            others.TileSize = new Size(Metrics.S(420), Metrics.S(56));
            others.Columns.Add("Папка", 200);
            others.Columns.Add("Изменён", 120);
            others.ItemActivate += delegate { OpenFrom(others); };
            others.MouseUp += delegate (object s, MouseEventArgs e) { Popup(others, e); };
            others.Visible = false;

            status.Dock = DockStyle.Top;
            status.Height = 30;
            status.ForeColor = Theme.Muted;
            status.Font = Theme.UiFont(10.5f);

            chips.Dock = DockStyle.Top;
            chips.AutoSize = true;
            chips.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            chips.BackColor = Theme.Background;
            chips.Padding = new Padding(0, 6, 0, 0);

            Controls.Add(pictures);
            Controls.Add(others);
            Controls.Add(status);
            Controls.Add(chips);
            BuildChips();
        }

        public override string Id { get { return "media"; } }
        public override string Title { get { return "Фото, видео, музыка"; } }
        public override string Glyph { get { return ""; } }

        public override void OnShown()
        {
            if ((DateTime.Now - lastScan).TotalSeconds > 10) LoadMedia();
        }

        public override void OnSearch(string q) { query = q ?? ""; LoadMedia(); }

        private void BuildChips()
        {
            chips.SuspendLayout();
            foreach (Control c in new List<Control>(ChipControls())) { chips.Controls.Remove(c); c.Dispose(); }
            chips.Controls.Add(Chip("Изображения", tab == "img", delegate { tab = "img"; BuildChips(); LoadMedia(); }));
            chips.Controls.Add(Chip("Видео", tab == "vid", delegate { tab = "vid"; BuildChips(); LoadMedia(); }));
            chips.Controls.Add(Chip("Музыка", tab == "aud", delegate { tab = "aud"; BuildChips(); LoadMedia(); }));
            chips.ResumeLayout();
        }

        private IEnumerable<Control> ChipControls()
        {
            foreach (Control c in chips.Controls) yield return c;
        }

        private FileKind WantedKind()
        {
            return tab == "img" ? FileKind.Image : tab == "vid" ? FileKind.Video : FileKind.Audio;
        }

        private void LoadMedia()
        {
            lastScan = DateTime.Now;
            int id = ++loadId;
            FileKind kind = WantedKind();
            pictures.Visible = kind == FileKind.Image;
            others.Visible = kind != FileKind.Image;
            pictures.Items.Clear();
            others.Items.Clear();
            status.Text = "Идёт поиск…";
            string q = query;
            List<FileRoot> roots = FileRoots.Resolve(Main.Cfg);

            Thread t = new Thread(delegate ()
            {
                List<FileItem> found = FileIndex.Scan(roots, delegate (FileKind k) { return k == kind; }, kind == FileKind.Image ? 400 : 3000);
                found = found.FindAll(delegate (FileItem f) { return q.Length == 0 || f.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0; });
                if (kind == FileKind.Image) LoadPictures(id, found);
                else Post(id, delegate { ShowList(found); });
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        private void Post(int id, Action a)
        {
            try
            {
                BeginInvoke(new Action(delegate { if (id == loadId) a(); }));
            }
            catch { }
        }

        private void ShowList(List<FileItem> found)
        {
            others.BeginUpdate();
            foreach (FileItem f in found)
            {
                string ext = Path.GetExtension(f.Name).ToLowerInvariant();
                if (!icons.Images.ContainsKey(ext))
                {
                    Bitmap ic = ShellIcons.Get(ext, false);
                    if (ic != null) icons.Images.Add(ext, ic);
                }
                ListViewItem it = new ListViewItem(f.Name);
                it.ImageKey = icons.Images.ContainsKey(ext) ? ext : null;
                it.SubItems.Add(Path.GetFileName(f.Folder));
                it.SubItems.Add(f.Modified.ToString("dd.MM.yyyy") + "  •  " + FileIndex.Size(f.Size));
                it.Tag = f;
                others.Items.Add(it);
            }
            others.EndUpdate();
            status.Text = found.Count == 0 ? "Здесь пока ничего нет." : "Найдено файлов: " + found.Count;
        }

        // Runs on a worker thread: builds thumbnails and asks the AI filter before showing each picture.
        private void LoadPictures(int id, List<FileItem> found)
        {
            Post(id, delegate
            {
                thumbs.Images.Clear();
                status.Text = found.Count == 0 ? "Здесь пока ничего нет." : "Загрузка изображений: " + found.Count;
            });
            NsfwEngine engine = Main.Cfg.Ai.Enabled && Main.Cfg.Ai.HideInGallery ? NsfwEngine.TryGet() : null;
            int hidden = 0;
            int index = 0;
            foreach (FileItem f in found)
            {
                if (id != loadId) return;
                Bitmap thumb = null;
                bool blocked = false;
                try
                {
                    using (FileStream fs = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (Image img = Image.FromStream(fs, false, false))
                    {
                        if (engine != null)
                        {
                            using (Bitmap bmp = new Bitmap(img))
                                blocked = Main.IsPictureBlocked(f.Path, engine, bmp);
                        }
                        thumb = blocked ? BlockedThumb() : Thumb(img);
                    }
                }
                catch { thumb = null; }
                if (thumb == null) continue;
                if (blocked) hidden++;
                FileItem item = f;
                Bitmap tb = thumb;
                bool isBlocked = blocked;
                int my = index++;
                Post(id, delegate
                {
                    thumbs.Images.Add(tb);
                    tb.Dispose();
                    ListViewItem it = new ListViewItem(isBlocked ? "Скрыто фильтром" : item.Name);
                    it.ImageIndex = thumbs.Images.Count - 1;
                    it.Tag = isBlocked ? null : item;
                    pictures.Items.Add(it);
                });
            }
            Post(id, delegate
            {
                status.Text = "Изображений: " + pictures.Items.Count + (hidden > 0 ? "   •   скрыто фильтром: " + hidden : "");
            });
        }

        private static Bitmap Thumb(Image img)
        {
            const int S = 120;
            Bitmap t = new Bitmap(S, S);
            using (Graphics g = Graphics.FromImage(t))
            {
                g.Clear(Theme.Card);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float k = Math.Min((float)S / img.Width, (float)S / img.Height);
                int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
                g.DrawImage(img, (S - w) / 2, (S - h) / 2, w, h);
            }
            return t;
        }

        private static Bitmap BlockedThumb()
        {
            const int S = 120;
            Bitmap t = new Bitmap(S, S);
            using (Graphics g = Graphics.FromImage(t))
            using (StringFormat sf = new StringFormat())
            {
                g.Clear(Theme.Card);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = Theme.IconFont(36))
                using (SolidBrush b = new SolidBrush(Theme.Danger))
                    g.DrawString("", f, b, new Rectangle(0, 10, S, 70), sf);
                using (Font f = Theme.UiFont(9))
                using (SolidBrush b = new SolidBrush(Theme.Muted))
                    g.DrawString("скрыто", f, b, new Rectangle(0, 80, S, 30), sf);
            }
            return t;
        }

        private void OpenFrom(ListView lv)
        {
            if (lv.SelectedItems.Count == 0) return;
            FileItem f = lv.SelectedItems[0].Tag as FileItem;
            if (f != null) Main.OpenFile(f.Path);
        }

        private void Popup(ListView lv, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem it = lv.GetItemAt(e.X, e.Y);
            if (it == null || !(it.Tag is FileItem)) return;
            it.Selected = true;
            Menus.ForFile(Main, ((FileItem)it.Tag).Path, false, null);
        }
    }

    // ====================================================================== FILE MANAGER

    /// <summary>
    /// The only way to browse files. Limited to the allowed folders (documents, desktop, downloads, pictures...);
    /// there is no path box, no drives list and no way up past a root.
    /// </summary>
    internal class FilesSection : Section
    {
        private sealed class Actions : FileManagerActions
        {
            private readonly FilesSection s;
            public Actions(FilesSection section) { s = section; }
            public override bool CanModify { get { return s.CanModifyHere(); } }
            public override void Enter(string folder) { s.Navigate(folder); }
            public override void Copy() { s.DoCopy(false); }
            public override void Cut() { s.DoCopy(true); }
            public override void Rename() { s.DoRename(); }
            public override void Delete() { s.DoDelete(); }
        }

        private readonly Actions actions;
        protected readonly ListView list = UiKit.List(true);
        private readonly ImageList icons = new ImageList();
        private readonly FlowLayoutPanel bar = new FlowLayoutPanel();
        private readonly Label where = new Label();
        private readonly Button binButton;
        private readonly Stack<string> back = new Stack<string>();
        protected List<FileRoot> roots = new List<FileRoot>();
        protected string current;                     // null = list of roots
        // shared by the "My files" and "Flash drives" sections: copy on a flash drive, paste into My files
        private static List<string> clip = new List<string>();
        private static bool clipCut;
        private string query = "";

        public FilesSection(MainForm main) : base(main)
        {
            actions = new Actions(this);
            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(32, 32);
            list.LargeImageList = icons;
            list.TileSize = new Size(Metrics.S(420), Metrics.S(56));
            list.MultiSelect = true;
            list.Columns.Add("Размер", 100);
            list.Columns.Add("Изменён", 120);
            list.ItemActivate += delegate { OpenSelected(); };
            list.MouseUp += OnMouseUp;
            list.KeyDown += OnKey;

            bar.Dock = DockStyle.Top;
            bar.Height = 52;
            bar.BackColor = Theme.Background;
            bar.Padding = new Padding(0, 6, 0, 0);
            AddTool("", "Назад", delegate { GoBack(); });
            AddTool("", "Вверх", delegate { GoUp(); });
            AddTool("", "Обновить", delegate { Reload(); });
            AddTool("", "Новая папка", delegate { DoNewFolder(); });
            AddTool("", "Копировать", delegate { DoCopy(false); });
            AddTool("", "Вырезать", delegate { DoCopy(true); });
            AddTool("", "Вставить", delegate { DoPaste(); });
            AddTool("", "Переименовать", delegate { DoRename(); });
            AddTool("", "Удалить", delegate { DoDelete(); });
            binButton = AddTool("\uE74D", "Очистить корзину (только администратор)", delegate { Main.EmptyRecycleBin(); });
            binButton.Text = "Очистить корзину";
            binButton.Font = Theme.UiFont(10.5f);
            binButton.BackColor = Theme.Danger;
            binButton.ForeColor = Color.White;
            binButton.Visible = false;

            where.Dock = DockStyle.Top;
            where.Height = 32;
            where.ForeColor = Theme.Muted;
            where.Font = Theme.UiFont(11);

            Controls.Add(list);
            Controls.Add(where);
            Controls.Add(bar);
        }

        public override string Id { get { return "files"; } }
        public override string Title { get { return "Мои файлы"; } }
        public override string Glyph { get { return ""; } }

        /// <summary>A text button on the tool bar (the sections derived from this one add their own).</summary>
        protected Button AddTextTool(string text, string tip, EventHandler click)
        {
            Button b = UiKit.Flat(text, click);
            b.Font = Theme.UiFont(10.5f);
            b.Padding = new Padding(10, 4, 10, 4);
            ToolTip t = new ToolTip();
            t.SetToolTip(b, tip);
            bar.Controls.Add(b);
            return b;
        }

        protected virtual List<FileRoot> ResolveRoots() { return FileRoots.Resolve(Main.Cfg); }
        protected virtual string RootsTitle { get { return "Выберите папку"; } }
        protected virtual string EmptyRootsText { get { return "Нет доступных папок."; } }

        /// <summary>Re-reads the roots (a drive came or went) and redraws; leaves a root that is gone.</summary>
        protected void RefreshRoots()
        {
            roots = ResolveRoots();
            if (current != null && FileRoots.RootOf(roots, current) == null) { current = null; Ui.Toast("Эта папка больше недоступна."); }
            Reload();
        }

        protected void ShowRoots()
        {
            current = null;
            Reload();
        }

        private Button AddTool(string glyph, string tip, EventHandler click)
        {
            Button b = UiKit.Flat(glyph, click);
            b.Font = Theme.IconFont(14);
            b.Padding = new Padding(8, 4, 8, 4);
            ToolTip t = new ToolTip();
            t.SetToolTip(b, tip);
            bar.Controls.Add(b);
            return b;
        }

        public override void OnShown()
        {
            binButton.Visible = State.Privileged;   // the recycle bin can be emptied by the administrator only
            roots = ResolveRoots();
            if (current != null && FileRoots.RootOf(roots, current) == null) current = null;
            Reload();
        }

        public override void OnSearch(string q) { query = q ?? ""; Reload(); }

        // ---------------------------------------------------------------- navigation

        public void Navigate(string folder)
        {
            if (folder != null)
            {
                roots = ResolveRoots();
                if (FileRoots.RootOf(roots, folder) == null) { Ui.Violation(ViolationKind.Folder, folder); return; }
            }
            if (current != null || folder != null) back.Push(current);
            current = folder;
            Reload();
        }

        /// <summary>Jump to a file's folder and select it (from the "Show in Files" menu item).</summary>
        public void Reveal(string file)
        {
            roots = ResolveRoots();
            string dir = Path.GetDirectoryName(file);
            if (FileRoots.RootOf(roots, dir) == null) return;
            current = dir;
            Reload();
            foreach (ListViewItem it in list.Items)
                if (string.Equals(it.Tag as string, file, StringComparison.OrdinalIgnoreCase)) { it.Selected = true; it.EnsureVisible(); break; }
        }

        private void GoBack()
        {
            if (back.Count == 0) return;
            current = back.Pop();
            Reload();
        }

        private void GoUp()
        {
            if (current == null) return;
            FileRoot r = FileRoots.RootOf(roots, current);
            if (r == null || string.Equals(FileRoots.Normalize(current), r.Path, StringComparison.OrdinalIgnoreCase)) current = null;
            else current = Path.GetDirectoryName(current);
            Reload();
        }

        private void AddIcon(string key, Bitmap bmp)
        {
            if (bmp != null && !icons.Images.ContainsKey(key)) icons.Images.Add(key, bmp);
        }

        protected void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            AddIcon("<dir>", ShellIcons.Get("", true));

            if (current == null)
            {
                where.Text = RootsTitle;
                if (roots.Count == 0)
                {
                    ListViewItem none = new ListViewItem(EmptyRootsText);
                    none.ForeColor = Theme.Muted;
                    none.SubItems.Add("");
                    none.SubItems.Add("");
                    list.Items.Add(none);        // Tag stays null: nothing to open
                }
                foreach (FileRoot r in roots)
                {
                    ListViewItem it = new ListViewItem(r.Name + (r.ReadOnly ? "  (только чтение)" : ""));
                    it.ImageKey = icons.Images.ContainsKey("<dir>") ? "<dir>" : null;
                    it.SubItems.Add("");
                    it.SubItems.Add("");
                    it.Tag = r.Path;
                    list.Items.Add(it);
                }
            }
            else
            {
                FileRoot root = FileRoots.RootOf(roots, current);
                string rel = root == null ? "" : current.Substring(Math.Min(current.Length, root.Path.Length));
                where.Text = (root == null ? "" : root.Name) + rel.Replace('\\', '›') + (root != null && root.ReadOnly ? "   (только чтение)" : "");
                try
                {
                    List<string> dirs = new List<string>(Directory.GetDirectories(current));
                    List<string> files = new List<string>(Directory.GetFiles(current));
                    dirs.Sort(StringComparer.CurrentCultureIgnoreCase);
                    files.Sort(StringComparer.CurrentCultureIgnoreCase);
                    foreach (string d in dirs) AddEntry(d, true);
                    foreach (string f in files) AddEntry(f, false);
                }
                catch (Exception ex) { where.Text += "   —   " + ex.Message; }
            }
            list.EndUpdate();
        }

        private void AddEntry(string path, bool isDir)
        {
            string name = Path.GetFileName(path);
            if (query.Length > 0 && name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) return;
            try
            {
                FileAttributes a = File.GetAttributes(path);
                if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) return;
            }
            catch { return; }
            if (!FileFilter.Visible(path, isDir, Main.Cfg)) return;   // students see educational files only (no installers, scripts, shortcuts)

            string key = "<dir>";
            if (!isDir)
            {
                key = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                AddIcon(key, ShellIcons.Get(key, false));
            }
            ListViewItem it = new ListViewItem(name);
            it.ImageKey = icons.Images.ContainsKey(key) ? key : null;
            if (isDir)
            {
                it.SubItems.Add("");
                it.SubItems.Add(Directory.GetLastWriteTime(path).ToString("dd.MM.yyyy"));
            }
            else
            {
                FileInfo fi = new FileInfo(path);
                it.SubItems.Add(FileIndex.Size(fi.Length));
                it.SubItems.Add(fi.LastWriteTime.ToString("dd.MM.yyyy"));
            }
            it.Tag = path;
            list.Items.Add(it);
        }

        // ---------------------------------------------------------------- selection and menu

        protected List<string> SelectedPaths()
        {
            List<string> r = new List<string>();
            foreach (ListViewItem it in list.SelectedItems) if (it.Tag is string) r.Add((string)it.Tag);
            return r;
        }

        private void OpenSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            string p = list.SelectedItems[0].Tag as string;
            if (p == null) return;
            if (Directory.Exists(p)) Navigate(p);
            else Main.OpenFile(p);
        }

        private void OnMouseUp(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem it = list.GetItemAt(e.X, e.Y);
            if (it == null || !(it.Tag is string)) return;
            if (!it.Selected) { list.SelectedItems.Clear(); it.Selected = true; }
            string p = (string)it.Tag;
            Menus.ForFile(Main, p, Directory.Exists(p), current == null ? null : actions);
        }

        private void OnKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back) { GoUp(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { DoRename(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DoDelete(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { DoCopy(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.X) { DoCopy(true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.V) { DoPaste(); e.Handled = true; }
        }

        // ---------------------------------------------------------------- operations (all stay inside allowed roots)

        private bool CanModifyHere()
        {
            if (current == null) return false;
            FileRoot r = FileRoots.RootOf(roots, current);
            return r != null && !r.ReadOnly;
        }

        private bool Forbidden(string name)
        {
            string ext = Path.GetExtension(name);
            return ext.Length > 0 && Main.Cfg.ForbiddenExtensions.Exists(delegate (string x)
            {
                return string.Equals(x, ext, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static bool ValidName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != "..";
        }

        private string Unique(string folder, string name)
        {
            string target = Path.Combine(folder, name);
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                target = Path.Combine(folder, stem + " (" + i + ")" + ext);
                if (!File.Exists(target) && !Directory.Exists(target)) return target;
            }
            return Path.Combine(folder, Guid.NewGuid().ToString("N") + ext);
        }

        private void DoCopy(bool cut)
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) return;
            if (cut && !CanModifyHere()) { Ui.Toast("Здесь нельзя перемещать файлы."); return; }
            clip = sel;
            clipCut = cut;
        }

        private void DoPaste()
        {
            if (!CanModifyHere()) { Ui.Toast("Сюда нельзя вставлять файлы."); return; }
            foreach (string src in clip)
            {
                try
                {
                    string name = Path.GetFileName(src);
                    bool isDir = Directory.Exists(src);
                    if (!isDir && Forbidden(name)) { Ui.Violation(ViolationKind.File, name); continue; }
                    if (!isDir && !FileFilter.Visible(src, false, Main.Cfg)) { Ui.Violation(ViolationKind.File, name); continue; }
                    string dst = Unique(current, name);
                    if (isDir)
                    {
                        if (dst.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase)) continue;   // into itself
                        if (!State.Privileged)
                        {
                            // A student's folder copy takes educational files only; a move deletes the source only when nothing was left out.
                            int skipped;
                            FileFilter.CopyDirectory(src, dst, Main.Cfg, false, out skipped);
                            if (clipCut && skipped == 0) Directory.Delete(src, true);
                            else if (skipped > 0) Ui.Toast("Скопированы только учебные файлы. Пропущено: " + skipped);
                        }
                        else if (clipCut) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(src, dst);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(src, dst);
                    }
                    else if (clipCut) File.Move(src, dst);
                    else File.Copy(src, dst);
                }
                catch (Exception ex) { Ui.Toast("Не удалось: " + ex.Message); }
            }
            if (clipCut) clip.Clear();
            Reload();
        }

        private void DoNewFolder()
        {
            if (!CanModifyHere()) { Ui.Toast("Здесь нельзя создавать папки."); return; }
            string name = Microsoft.VisualBasic.Interaction.InputBox("Название новой папки:", "Новая папка", "Новая папка");
            if (!ValidName(name)) return;
            try { Directory.CreateDirectory(Unique(current, name.Trim())); }
            catch (Exception ex) { Ui.Toast("Не удалось: " + ex.Message); }
            Reload();
        }

        private void DoRename()
        {
            List<string> sel = SelectedPaths();
            if (sel.Count != 1 || !CanModifyHere()) return;
            string src = sel[0];
            string old = Path.GetFileName(src);
            string name = Microsoft.VisualBasic.Interaction.InputBox("Новое имя:", "Переименовать", old);
            if (!ValidName(name) || name == old) return;
            bool isDir = Directory.Exists(src);
            if (!isDir && Forbidden(name)) { Ui.Violation(ViolationKind.File, name); return; }
            try
            {
                string dst = Path.Combine(Path.GetDirectoryName(src), name.Trim());
                if (File.Exists(dst) || Directory.Exists(dst)) { Ui.Toast("Такое имя уже есть."); return; }
                if (isDir) Directory.Move(src, dst); else File.Move(src, dst);
            }
            catch (Exception ex) { Ui.Toast("Не удалось: " + ex.Message); }
            Reload();
        }

        private void DoDelete()
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0 || !CanModifyHere()) return;
            if (!Main.Cfg.StudentCanDelete) { Ui.Toast("Удаление отключено администратором."); return; }
            string what = sel.Count == 1 ? "«" + Path.GetFileName(sel[0]) + "»" : "выбранные элементы (" + sel.Count + ")";
            if (MessageBox.Show(FindForm(), "Удалить " + what + " в корзину?", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (string p in sel)
            {
                try
                {
                    if (Directory.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    Log.Write("files", "Удалено в корзину: " + p);
                }
                catch (Exception ex) { Ui.Toast("Не удалось: " + ex.Message); }
            }
            Reload();
        }
    }

    // ====================================================================== INTERNET

    internal sealed class WebSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();
        private readonly BrowserPanel browser;

        public WebSection(MainForm main) : base(main)
        {
            if (main.Cfg.Browser.BuiltIn && WebView2Probe.Available)
            {
                // The school's own browser fills the whole page.
                Padding = new Padding(0);
                browser = new BrowserPanel(main);
                Controls.Add(browser);
                return;
            }

            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
            Resize += delegate { Fit(); };
        }

        public override string Id { get { return "web"; } }
        public override string Title { get { return "Интернет"; } }
        public override string Glyph { get { return "\uE774"; } }

        public bool HasBuiltInBrowser { get { return browser != null; } }

        public override void OnShown()
        {
            if (browser != null) browser.EnsureStarted();
            else Rebuild();
        }

        public override void OnAppsChanged() { if (browser == null) Rebuild(); }

        public void Navigate(string url) { if (browser != null) browser.Navigate(url); }
        public void Blank() { if (browser != null) browser.Blank(); }

        private void Fit()
        {
            int w = Math.Max(300, root.ClientSize.Width - 30);
            foreach (Control c in root.Controls) c.MaximumSize = new Size(w, 0);
        }

        // Fallback when the WebView2 runtime is missing: external browsers and the list of useful sites.
        private void Rebuild()
        {
            root.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in root.Controls) old.Add(c);
            foreach (Control c in old) { root.Controls.Remove(c); c.Dispose(); }

            root.Controls.Add(UiKit.Heading("Браузеры"));
            FlowLayoutPanel browsers = UiKit.Flow();
            foreach (AppEntry e in Main.Apps)
                if (e.Category == "internet") browsers.Controls.Add(Main.MakeAppTile(e));
            if (browsers.Controls.Count == 0) root.Controls.Add(UiKit.Hint("Браузер не найден. Попросите администратора установить его для всех пользователей."));
            else root.Controls.Add(browsers);

            if (Main.Cfg.Links.Count > 0)
            {
                root.Controls.Add(UiKit.Heading("Полезные сайты"));
                FlowLayoutPanel links = UiKit.Flow();
                foreach (LinkItem l in Main.Cfg.Links)
                {
                    if (l == null || string.IsNullOrEmpty(l.Url)) continue;
                    TileControl t = new TileControl(l.Name, "\uE774");
                    string url = l.Url;
                    t.Activated += delegate { Main.OpenUrl(url); };
                    links.Controls.Add(t);
                }
                root.Controls.Add(links);
            }
            Fit();
            root.ResumeLayout();
        }
    }

    // ====================================================================== SYSTEM

    internal sealed class SystemSection : Section
    {
        private readonly FlowLayoutPanel root = new FlowLayoutPanel();
        private Label wifiStatus;
        private FlowLayoutPanel wifiList;
        private bool wifiBusy;

        public SystemSection(MainForm main) : base(main)
        {
            root.Dock = DockStyle.Fill;
            root.FlowDirection = FlowDirection.TopDown;
            root.WrapContents = false;
            root.AutoScroll = true;
            root.BackColor = Theme.Background;
            Controls.Add(root);
        }

        public override string Id { get { return "system"; } }
        public override string Title { get { return "Компьютер"; } }
        public override string Glyph { get { return "\uE770"; } }

        public override void OnShown() { Rebuild(); }

        private void Rebuild()
        {
            root.SuspendLayout();
            List<Control> old = new List<Control>();
            foreach (Control c in root.Controls) old.Add(c);
            foreach (Control c in old) { root.Controls.Remove(c); c.Dispose(); }

            // ---- sound: a slider, not +/- buttons
            root.Controls.Add(UiKit.Heading("Звук"));
            if (AudioControl.Available)
            {
                FlowLayoutPanel row = UiKit.Flow();
                row.WrapContents = false;
                SliderBar slider = new SliderBar();
                slider.Width = Metrics.S(420);
                slider.BackColor = Theme.Background;
                slider.SetSilently((int)Math.Round(AudioControl.Volume * 100));
                Label pct = new Label();
                pct.AutoSize = false;
                pct.Size = new Size(Metrics.S(70), Metrics.S(34));
                pct.TextAlign = ContentAlignment.MiddleLeft;
                pct.Font = Theme.UiFont(12, FontStyle.Bold);
                pct.ForeColor = Theme.Text;
                pct.Text = slider.Value + "%";
                slider.ValueChanged += delegate
                {
                    AudioControl.Muted = false;
                    AudioControl.Volume = slider.Value / 100f;
                    pct.Text = slider.Value + "%";
                };
                row.Controls.Add(slider);
                row.Controls.Add(pct);
                root.Controls.Add(row);
                CheckBox mute = new CheckBox();
                mute.Text = "Выключить звук";
                mute.ForeColor = Theme.Text;
                mute.Font = Theme.UiFont(11);
                mute.AutoSize = true;
                mute.Checked = AudioControl.Muted;
                mute.CheckedChanged += delegate { AudioControl.Muted = mute.Checked; };
                root.Controls.Add(mute);
            }
            else root.Controls.Add(UiKit.Hint("Управление звуком недоступно на этом компьютере."));

            // ---- Wi-Fi
            BuildWifi();

            root.Controls.Add(UiKit.Heading("Оформление"));
            FlowLayoutPanel theme = UiKit.Flow();
            theme.Controls.Add(UiKit.Flat(Theme.Dark ? "Светлая тема" : "Тёмная тема", delegate { Main.ToggleTheme(); }));
            root.Controls.Add(theme);

            root.Controls.Add(UiKit.Heading("Об этом компьютере"));
            root.Controls.Add(UiKit.Hint("Компьютер: " + Environment.MachineName));
            root.Controls.Add(UiKit.Hint("Пользователь: " + Environment.UserName));
            root.Controls.Add(UiKit.Hint("SchoolShield " + AppInfo.VersionString + "  •  " + Release.Author));
            root.Controls.Add(UiKit.Hint("Параметры Windows и панель управления закрыты. Нужна помощь — обратитесь к учителю."));

            if (Main.Cfg.AdminButton && !State.Windowed)
            {
                root.Controls.Add(UiKit.Heading("Для администратора"));
                FlowLayoutPanel adm = UiKit.Flow();
                adm.Controls.Add(UiKit.Flat("Вход администратора", delegate { Main.ShowAdminConsole(); }));
                root.Controls.Add(adm);
                root.Controls.Add(UiKit.Hint("Нужен код администратора."));
            }

            if (Main.Cfg.AllowStudentShutdown || Main.Cfg.AllowStudentRestart)
            {
                root.Controls.Add(UiKit.Heading("Питание"));
                FlowLayoutPanel power = UiKit.Flow();
                if (Main.Cfg.AllowStudentShutdown)
                    power.Controls.Add(UiKit.Accent("Выключить компьютер", delegate { Main.Power(false); }));
                if (Main.Cfg.AllowStudentRestart)
                    power.Controls.Add(UiKit.Flat("Перезагрузить", delegate { Main.Power(true); }));
                root.Controls.Add(power);
            }
            root.ResumeLayout();
        }

        // ------------------------------------------------------------------ Wi-Fi

        private void BuildWifi()
        {
            root.Controls.Add(UiKit.Heading("Сеть Wi-Fi"));
            if (!Wifi.Available)
            {
                root.Controls.Add(UiKit.Hint("Адаптер Wi-Fi не найден. Проводная сеть работает без настройки."));
                return;
            }
            wifiStatus = UiKit.Hint("Поиск сетей…");
            wifiStatus.Font = Theme.UiFont(11);
            root.Controls.Add(wifiStatus);
            wifiList = new FlowLayoutPanel();
            wifiList.FlowDirection = FlowDirection.TopDown;
            wifiList.WrapContents = false;
            wifiList.AutoSize = true;
            wifiList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            wifiList.BackColor = Theme.Background;
            wifiList.Margin = new Padding(0);
            root.Controls.Add(wifiList);
            FlowLayoutPanel btns = UiKit.Flow();
            btns.Margin = new Padding(0, Metrics.S(6), 0, 0);
            btns.Controls.Add(UiKit.Flat("Обновить список", delegate { RefreshWifi(true); }));
            root.Controls.Add(btns);
            RefreshWifi(true);
        }

        private void RefreshWifi(bool rescan)
        {
            if (wifiBusy || wifiStatus == null) return;
            wifiBusy = true;
            wifiStatus.Text = "Поиск сетей…";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<WifiNetwork> nets = Wifi.Scan(rescan);
                WifiNetwork cur = Wifi.Current();
                try { BeginInvoke(new Action(delegate { FillWifi(nets, cur); })); }
                catch { }
            });
        }

        private static string Bars(int q)
        {
            return q >= 75 ? "▂▄▆█" : q >= 50 ? "▂▄▆" : q >= 25 ? "▂▄" : "▂";
        }

        private void FillWifi(List<WifiNetwork> nets, WifiNetwork cur)
        {
            wifiBusy = false;
            if (wifiList == null || wifiList.IsDisposed) return;
            List<Control> old = new List<Control>();
            foreach (Control c in wifiList.Controls) old.Add(c);
            foreach (Control c in old) { wifiList.Controls.Remove(c); c.Dispose(); }

            wifiStatus.Text = cur != null ? "Подключено: " + cur.Ssid + "   " + Bars(cur.Signal) : "Нет подключения к Wi-Fi";
            if (!Main.Cfg.Wifi.StudentCanConnect)
            {
                wifiList.Controls.Add(UiKit.Hint("Выбор сети отключён администратором."));
                return;
            }

            int shown = 0;
            foreach (WifiNetwork n in nets)
            {
                if (!n.HasProfile || !Wifi.IsAllowed(Main.Cfg, n.Ssid)) continue;
                shown++;
                WifiNetwork net = n;
                Panel row = new Panel();
                row.Size = new Size(Math.Min(Metrics.S(560), Math.Max(Metrics.S(300), root.ClientSize.Width - Metrics.S(40))), Metrics.S(56));
                row.BackColor = Theme.Card;
                row.Margin = new Padding(0, 0, 0, Metrics.S(8));
                Label name = new Label();
                name.Text = n.Ssid + (n.Secured ? "" : "   (открытая)");
                name.Font = Theme.UiFont(12, n.Connected ? FontStyle.Bold : FontStyle.Regular);
                name.ForeColor = Theme.Text;
                name.AutoEllipsis = true;
                name.SetBounds(Metrics.S(14), Metrics.S(8), row.Width - Metrics.S(250), Metrics.S(22));
                Label sub = new Label();
                sub.Text = (n.Connected ? "Подключено   " : "") + Bars(n.Signal);
                sub.Font = Theme.UiFont(9.5f);
                sub.ForeColor = n.Connected ? Color.FromArgb(74, 222, 128) : Theme.Muted;
                sub.SetBounds(Metrics.S(14), Metrics.S(30), row.Width - Metrics.S(250), Metrics.S(20));
                Button b = n.Connected ? UiKit.Flat("Отключиться", null) : UiKit.Accent("Подключиться", null);
                b.AutoSize = false;
                b.SetBounds(row.Width - Metrics.S(170), Metrics.S(9), Metrics.S(156), Metrics.S(38));
                b.Click += delegate
                {
                    if (net.Connected) Wifi.Disconnect();
                    else if (!Wifi.Connect(net.Profile)) Ui.Toast("Не удалось подключиться к сети.");
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = 3500;
                    t.Tick += delegate { t.Stop(); t.Dispose(); RefreshWifi(false); };
                    t.Start();
                    wifiStatus.Text = net.Connected ? "Отключаюсь…" : "Подключаюсь к «" + net.Ssid + "»…";
                };
                row.Controls.Add(name);
                row.Controls.Add(sub);
                row.Controls.Add(b);
                wifiList.Controls.Add(row);
            }
            if (shown == 0)
                wifiList.Controls.Add(UiKit.Hint("Сетей из списка школы поблизости нет. Нужна другая сеть — обратитесь к учителю."));
        }
    }
}
