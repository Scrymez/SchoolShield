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
    /// <summary>A disk or a folder in the "Places" list of the file manager.</summary>
    internal sealed class PlaceInfo
    {
        public string Title;
        public string Sub;
        public string Path;
        public string Glyph;
        public string Letter;        // "E:" for a drive
        public bool Removable;       // a flash drive or an external disk: can be ejected
        public bool Drive;

        public string Key { get { return (Path ?? "") + "|" + (Sub ?? "") + "|" + Title; } }
    }

    /// <summary>What the teacher sees on the left of the file manager: the usual folders and every disk, flash drives first.</summary>
    internal static class TeacherPlaces
    {
        public static List<PlaceInfo> Folders()
        {
            List<PlaceInfo> list = new List<PlaceInfo>();
            Add(list, "Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "");
            Add(list, "Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
            string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Add(list, "Загрузки", dl, "");
            Add(list, "Картинки", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "");
            Add(list, "Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "");
            Add(list, "Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "");
            return list;
        }

        private static void Add(List<PlaceInfo> list, string title, string path, string glyph)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            PlaceInfo p = new PlaceInfo();
            p.Title = title;
            p.Path = path;
            p.Glyph = glyph;
            list.Add(p);
        }

        /// <summary>The disks. May block for a moment (a disc that spins up, a network drive): call it from a worker thread.</summary>
        public static List<PlaceInfo> Disks()
        {
            List<PlaceInfo> removable = new List<PlaceInfo>();
            List<PlaceInfo> fixedDisks = new List<PlaceInfo>();
            HashSet<string> external = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (DriveEntry d in Drives.List()) if (d.Usb) external.Add(d.Letter); } catch { }

            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string letter = d.Name.Substring(0, 2);
                    PlaceInfo p = new PlaceInfo();
                    p.Drive = true;
                    p.Letter = letter;
                    p.Path = d.RootDirectory.FullName;
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel;
                    bool usb = d.DriveType == DriveType.Removable || external.Contains(letter);
                    p.Removable = usb && d.DriveType != DriveType.CDRom;
                    string kind = usb ? "Флешка" : d.DriveType == DriveType.CDRom ? "DVD-диск" : d.DriveType == DriveType.Network ? "Сетевой диск" : "Диск";
                    p.Title = (label.Length > 0 ? label : kind) + " (" + letter + ")";
                    p.Sub = "Свободно " + FileIndex.Size(d.AvailableFreeSpace) + " из " + FileIndex.Size(d.TotalSize);
                    p.Glyph = usb ? "" : d.DriveType == DriveType.CDRom ? "" : "";
                    if (usb) removable.Add(p); else fixedDisks.Add(p);
                }
                catch { }
            }
            removable.AddRange(fixedDisks);
            return removable;
        }
    }

    // =========================================================================================== small controls

    /// <summary>A row of the "Places" list: icon, name, free space; flash drives get an eject button. Accepts dropped files.</summary>
    internal sealed class PlaceButton : Control
    {
        public readonly PlaceInfo Info;
        private bool selected;
        private readonly AnimValue sel;
        private readonly AnimValue hover;
        private readonly AnimValue drop;
        private bool overEject;

        public event EventHandler Activated;
        public event EventHandler EjectClicked;
        /// <summary>Files were dropped on the place: paths and "move instead of copy".</summary>
        public event Action<PlaceInfo, string[], bool> FilesDropped;

        public PlaceButton(PlaceInfo info)
        {
            Info = info;
            Dock = DockStyle.Top;
            Height = Metrics.U(info.Sub == null ? 54 : 64);
            Cursor = Cursors.Hand;
            AllowDrop = true;
            sel = new AnimValue(this, 0);
            hover = new AnimValue(this, 0);
            drop = new AnimValue(this, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; sel.To(value ? 1 : 0); }
        }

        private Rectangle EjectRect { get { int s = Metrics.U(34); return new Rectangle(Width - s - Metrics.U(12), (Height - s) / 2, s, s); } }

        protected override void OnMouseEnter(EventArgs e) { hover.To(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover.To(0); if (overEject) { overEject = false; Invalidate(); } base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = Info.Removable && EjectRect.Contains(e.Location);
            if (over != overEject) { overEject = over; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            if (Info.Removable && EjectRect.Contains(e.Location)) { if (EjectClicked != null) EjectClicked(this, EventArgs.Empty); }
            else if (Activated != null) Activated(this, EventArgs.Empty);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = Effect(e); drop.To(1); }
            base.OnDragEnter(e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = Effect(e);
            base.OnDragOver(e);
        }

        protected override void OnDragLeave(EventArgs e) { drop.To(0); base.OnDragLeave(e); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            drop.To(0);
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            if (FilesDropped != null) FilesDropped(Info, paths, Effect(e) == DragDropEffects.Move);
            base.OnDragDrop(e);
        }

        private DragDropEffects Effect(DragEventArgs e)
        {
            if ((e.KeyState & 4) != 0) return DragDropEffects.Move & e.AllowedEffect;
            if ((e.KeyState & 8) != 0) return DragDropEffects.Copy & e.AllowedEffect;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            bool same = paths != null && paths.Length > 0 && FileJob.SameVolume(paths[0], Info.Path);
            return (same ? DragDropEffects.Move : DragDropEffects.Copy) & e.AllowedEffect;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Sidebar);
            double s = Math.Max(0, Math.Min(1, sel.Value)), h = Math.Max(0, Math.Min(1, hover.Value)), d = Math.Max(0, Math.Min(1, drop.Value));
            RectangleF r = new RectangleF(Metrics.U(6), Metrics.U(3), Width - Metrics.U(12), Height - Metrics.U(6));
            float radius = Metrics.U(14);
            using (GraphicsPath p = Fx.Round(r, radius))
            {
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.CardHover, h * (1 - s * 0.5) * 0.7))) g.FillPath(b, p);
                if (s > 0.01)
                    using (LinearGradientBrush b = Fx.Gradient(r, Fx.Alpha(Theme.BrandA, 0.20 * s), Fx.Alpha(Theme.BrandB, 0.12 * s), 0f)) g.FillPath(b, p);
                if (d > 0.01)
                {
                    using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Accent, 0.18 * d))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Fx.Alpha(Theme.Accent, d), 2.4f)) g.DrawPath(pen, p);
                }
            }
            if (s > 0.01)
                using (SolidBrush b = new SolidBrush(Fx.Alpha(Theme.Accent, s)))
                    Fx.FillRound(g, b, new RectangleF(Metrics.U(2), Height * 0.25f, Metrics.U(4), Height * 0.5f), 2);

            float ic = Metrics.U(36);
            RectangleF circle = new RectangleF(Metrics.U(18), (Height - ic) / 2f, ic, ic);
            using (LinearGradientBrush b = Fx.Gradient(circle, Fx.Alpha(Theme.BrandA, 0.20 + 0.25 * s), Fx.Alpha(Theme.BrandB, 0.14 + 0.2 * s), 60f)) g.FillEllipse(b, circle);
            Fx.Text(g, Info.Glyph, Fx.Icons(Metrics.Fz(14)), Fx.Blend(Theme.Accent, Theme.Text, 0.0), circle, StringAlignment.Center, StringAlignment.Center, false);

            float tx = circle.Right + Metrics.U(12);
            float tw = Width - tx - Metrics.U(Info.Removable ? 60 : 16);
            if (Info.Sub == null)
                Fx.Text(g, Info.Title, Fx.Ui(Metrics.Fz(11.5f), s > 0.5 ? FontStyle.Bold : FontStyle.Regular), Theme.Text, new RectangleF(tx, 0, tw, Height), StringAlignment.Near, StringAlignment.Center, true);
            else
            {
                Fx.Text(g, Info.Title, Fx.Ui(Metrics.Fz(11f), FontStyle.Bold), Theme.Text, new RectangleF(tx, Metrics.U(9), tw, Metrics.U(24)), StringAlignment.Near, StringAlignment.Near, true);
                Fx.Text(g, Info.Sub, Fx.Ui(Metrics.Fz(9f)), Theme.Muted, new RectangleF(tx, Metrics.U(33), tw, Metrics.U(22)), StringAlignment.Near, StringAlignment.Near, true);
            }

            if (Info.Removable)
            {
                Rectangle er = EjectRect;
                using (GraphicsPath p = Fx.Round(er, er.Height / 2))
                {
                    using (SolidBrush b = new SolidBrush(overEject ? Fx.Alpha(Theme.Danger, 0.22) : Fx.Alpha(Theme.Line, 0.7))) g.FillPath(b, p);
                }
                // the eject sign: a triangle over a bar
                float cx = er.X + er.Width / 2f, cy = er.Y + er.Height / 2f, u = er.Width * 0.22f;
                Color ink = overEject ? Theme.Danger : Theme.Muted;
                using (SolidBrush b = new SolidBrush(ink))
                {
                    g.FillPolygon(b, new PointF[] { new PointF(cx - u * 1.2f, cy + u * 0.25f), new PointF(cx + u * 1.2f, cy + u * 0.25f), new PointF(cx, cy - u * 1.1f) });
                    g.FillRectangle(b, cx - u * 1.2f, cy + u * 0.7f, u * 2.4f, u * 0.5f);
                }
            }
        }
    }

    /// <summary>The path as clickable pieces: "Диск C: › Users › Документы". Long paths drop their beginning.</summary>
    internal sealed class Breadcrumb : Control
    {
        private sealed class Seg { public string Text; public string Path; public RectangleF Box; }

        private readonly List<Seg> segs = new List<Seg>();
        private int hot = -1;
        public event Action<string> Navigate;

        public Breadcrumb()
        {
            Height = Metrics.U(44);
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetPath(string path)
        {
            segs.Clear();
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string root = Path.GetPathRoot(full);
                string rest = full.Length > root.Length ? full.Substring(root.Length) : "";
                string acc = root;
                segs.Add(new Seg { Text = "Диск " + root.TrimEnd('\\'), Path = root });
                if (rest.Length > 0)
                    foreach (string part in rest.Split('\\'))
                    {
                        if (part.Length == 0) continue;
                        acc = Path.Combine(acc, part);
                        segs.Add(new Seg { Text = part, Path = acc });
                    }
            }
            catch { }
            Invalidate();
        }

        private Font F(bool last) { return Fx.Ui(Metrics.Fz(11.5f), last ? FontStyle.Bold : FontStyle.Regular); }

        private void Layout(Graphics g)
        {
            float pad = Metrics.U(14), gap = Metrics.U(26);
            float[] w = new float[segs.Count];
            for (int i = 0; i < segs.Count; i++) w[i] = g.MeasureString(segs[i].Text, F(i == segs.Count - 1), 100000, StringFormat.GenericTypographic).Width + Metrics.U(22);
            int start = 0;
            float total = pad;
            for (int i = 0; i < segs.Count; i++) total += w[i] + gap;
            while (start < segs.Count - 1 && total > Width - pad)
            {
                total -= w[start] + gap;
                start++;
            }
            float x = pad;
            for (int i = 0; i < segs.Count; i++)
            {
                if (i < start) { segs[i].Box = RectangleF.Empty; continue; }
                segs[i].Box = new RectangleF(x, (Height - Metrics.U(34)) / 2f, w[i], Metrics.U(34));
                x += w[i] + gap;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = -1;
            for (int i = 0; i < segs.Count; i++) if (!segs[i].Box.IsEmpty && segs[i].Box.Contains(e.Location)) idx = i;
            if (idx != hot) { hot = idx; Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (hot != -1) { hot = -1; Cursor = Cursors.Default; Invalidate(); } base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (hot >= 0 && hot < segs.Count && Navigate != null) Navigate(segs[hot].Path);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            if (segs.Count == 0) return;
            Layout(g);
            bool cut = segs.Count > 0 && segs[0].Box.IsEmpty;
            if (cut) Fx.Text(g, "…", F(false), Theme.Muted, new RectangleF(0, 0, Metrics.U(14), Height), StringAlignment.Center, StringAlignment.Center, false);
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                if (s.Box.IsEmpty) continue;
                bool last = i == segs.Count - 1;
                using (GraphicsPath p = Fx.Round(s.Box, s.Box.Height / 2))
                {
                    Color fill = last ? Fx.Alpha(Theme.BrandA, 0.18) : (hot == i ? Theme.CardHover : Theme.Card);
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                    using (Pen pen = new Pen(last ? Fx.Alpha(Theme.Accent, 0.6) : Theme.Line, 1.2f)) g.DrawPath(pen, p);
                }
                Fx.Text(g, s.Text, F(last), last ? Theme.Accent : Theme.Text, s.Box, StringAlignment.Center, StringAlignment.Center, true);
                if (!last)
                    Fx.Text(g, "›", Fx.Ui(Metrics.Fz(14), FontStyle.Bold), Theme.Muted, new RectangleF(s.Box.Right, 0, Metrics.U(26), Height), StringAlignment.Center, StringAlignment.Center, false);
            }
        }
    }

    /// <summary>The strip at the bottom of the file manager while files are being copied: what, how far, and a Cancel button.</summary>
    internal sealed class JobBar : Control
    {
        private string text = "";
        private double progress;
        private Color tone;
        private readonly MotionButton cancel;
        public event EventHandler CancelClicked;

        public JobBar()
        {
            Height = Metrics.U(78);
            Visible = false;
            tone = Theme.Accent;
            cancel = (MotionButton)UiKit.Flat("Отмена", delegate { if (CancelClicked != null) CancelClicked(this, EventArgs.Empty); });
            cancel.AutoSize = false;
            cancel.Font = Fx.Ui(Metrics.Fz(11), FontStyle.Bold);
            Controls.Add(cancel);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Resize += delegate { cancel.SetBounds(Width - Metrics.U(130), (Height - Metrics.U(44)) / 2, Metrics.U(116), Metrics.U(44)); };
        }

        public void Show(string what, double p, bool cancellable, Color color)
        {
            text = what;
            progress = p;
            tone = color;
            cancel.Visible = cancellable;
            if (!Visible) Visible = true;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Fx.Quality(g);
            g.Clear(Parent != null ? Parent.BackColor : Theme.Background);
            RectangleF r = new RectangleF(1, Metrics.U(6), Width - 3, Height - Metrics.U(10));
            using (GraphicsPath p = Fx.Round(r, Metrics.U(16)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Fx.Alpha(tone, 0.7), 1.6f)) g.DrawPath(pen, p);
            }
            float right = cancel.Visible ? Metrics.U(150) : Metrics.U(24);
            Fx.Text(g, text, Fx.Ui(Metrics.Fz(11.5f), FontStyle.Bold), Theme.Text, new RectangleF(Metrics.U(22), r.Y + Metrics.U(8), Width - Metrics.U(22) - right, Metrics.U(26)),
                StringAlignment.Near, StringAlignment.Near, true);
            RectangleF bar = new RectangleF(Metrics.U(22), r.Bottom - Metrics.U(22), Width - Metrics.U(22) - right, Metrics.U(10));
            using (SolidBrush b = new SolidBrush(Theme.Line)) Fx.FillRound(g, b, bar, bar.Height / 2);
            float w = (float)Math.Max(0, Math.Min(1, progress)) * bar.Width;
            if (w > 1)
                using (LinearGradientBrush b = Fx.Gradient(new RectangleF(bar.X, bar.Y, Math.Max(w, 2), bar.Height), tone, Theme.BrandB, 0f))
                    Fx.FillRound(g, b, new RectangleF(bar.X, bar.Y, w, bar.Height), bar.Height / 2);
        }
    }

    // =========================================================================================== the file manager

    /// <summary>
    /// "Files and flash drive" for the teacher's centre: the usual folders and all disks on the left, the folder in big readable rows,
    /// drag and drop between folders and onto a flash drive, copying in the background with a progress bar (the window never freezes),
    /// one-button "to the flash drive" and "eject". No restrictions at all: only 18+ is filtered, by the guard.
    /// </summary>
    internal sealed class TeacherFilesSection : Section
    {
        private sealed class Row
        {
            public string Name;
            public string Path;
            public bool Dir;
            public long Size;
            public DateTime Modified;
        }

        private readonly Panel placesPanel = new Panel();
        private readonly Panel mainPanel = new Panel();
        private readonly Label placesTitle = new Label();
        private readonly FlowLayoutPanel tools = new FlowLayoutPanel();
        private readonly Breadcrumb crumbs = new Breadcrumb();
        private readonly ListView list = new ListView();
        private readonly Label status = new Label();
        private readonly JobBar jobBar = new JobBar();
        private readonly ImageList icons = new ImageList();
        private readonly Stack<string> back = new Stack<string>();
        private readonly List<PlaceButton> placeButtons = new List<PlaceButton>();
        private List<PlaceInfo> disks = new List<PlaceInfo>();
        private string disksKey = "";
        private List<Row> rows = new List<Row>();
        private string current;
        private string query = "";
        private int generation;
        private bool placesBuilt;
        private ListViewItem dropHot;
        private System.Threading.Timer diskTimer;
        private MotionButton toFlash, fromFlash;

        private FileJob job;
        private System.Windows.Forms.Timer jobTimer;
        private Tween barHide;

        private static List<string> clip = new List<string>();
        private static bool clipCut;

        public TeacherFilesSection(MainForm main) : base(main)
        {
            // ---- left: places
            placesPanel.Dock = DockStyle.Left;
            placesPanel.Width = Metrics.U(Metrics.Tiny ? 250 : 290);
            placesPanel.BackColor = Theme.Background;
            placesPanel.AutoScroll = true;
            placesPanel.Padding = new Padding(0, 0, Metrics.U(8), 0);

            // ---- right: tools, path, list, progress, status
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.BackColor = Theme.Background;

            tools.Dock = DockStyle.Top;
            tools.AutoSize = true;
            tools.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tools.WrapContents = true;
            tools.BackColor = Theme.Background;
            tools.Padding = new Padding(0, 0, 0, Metrics.U(4));

            AddTool("Назад", "", delegate { GoBack(); }, false);
            AddTool("Вверх", "", delegate { GoUp(); }, false);
            AddTool("Копировать", "", delegate { DoCopy(false); }, false);
            AddTool("Вырезать", "", delegate { DoCopy(true); }, false);
            AddTool("Вставить", "", delegate { DoPaste(); }, false);
            AddTool("Новая папка", "", delegate { DoNewFolder(); }, false);
            AddTool("Переименовать", "", delegate { DoRename(); }, false);
            AddTool("Удалить", "", delegate { DoDelete(); }, false);
            toFlash = AddTool("На флешку", "", delegate { DoToFlash(); }, true);
            fromFlash = AddTool("С флешки в «Документы»", "", delegate { DoFromFlash(); }, true);
            fromFlash.Visible = false;
            AddTool("В Проводнике", "", delegate { DoExplorer(); }, false);

            crumbs.Dock = DockStyle.Top;
            crumbs.Navigate += delegate (string p) { Navigate(p, true); };

            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(32, 32);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = true;
            list.HideSelection = false;
            list.LabelEdit = true;
            list.AllowDrop = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Card;
            list.ForeColor = Theme.Text;
            list.Font = Theme.UiFont(Metrics.Fz(12));
            list.SmallImageList = icons;
            list.Columns.Add("Имя", Metrics.U(420));
            list.Columns.Add("Размер", Metrics.U(110));
            list.Columns.Add("Изменён", Metrics.U(170));
            list.ItemActivate += delegate { OpenSelected(); };
            list.MouseUp += OnListMouseUp;
            list.KeyDown += OnListKey;
            list.ItemDrag += OnItemDrag;
            list.DragEnter += OnListDrag;
            list.DragOver += OnListDrag;
            list.DragLeave += delegate { ClearDropHot(); };
            list.DragDrop += OnListDrop;
            list.AfterLabelEdit += OnAfterEdit;
            list.SelectedIndexChanged += delegate { UpdateStatus(); };
            list.Resize += delegate { FitColumns(); };

            status.Dock = DockStyle.Bottom;
            status.Height = Metrics.U(34);
            status.ForeColor = Theme.Muted;
            status.Font = Theme.UiFont(Metrics.Fz(10.5f));
            status.TextAlign = ContentAlignment.MiddleLeft;

            jobBar.Dock = DockStyle.Bottom;
            jobBar.CancelClicked += delegate { if (job != null) job.Cancelled = true; };

            mainPanel.Controls.Add(list);
            mainPanel.Controls.Add(jobBar);
            mainPanel.Controls.Add(status);
            mainPanel.Controls.Add(crumbs);
            mainPanel.Controls.Add(tools);

            placesTitle.Dock = DockStyle.Top;
            placesTitle.Height = Metrics.U(40);
            placesTitle.Text = "Места";
            placesTitle.Font = Fx.Ui(Metrics.Fz(12), FontStyle.Bold);
            placesTitle.ForeColor = Theme.Muted;
            placesTitle.TextAlign = ContentAlignment.MiddleLeft;
            placesTitle.Padding = new Padding(Metrics.U(14), 0, 0, 0);
            placesPanel.Controls.Add(placesTitle);

            Controls.Add(mainPanel);
            Controls.Add(placesPanel);
        }

        public override string Id { get { return "files"; } }
        public override string Title { get { return "Файлы и флешка"; } }
        public override string Glyph { get { return ""; } }

        private MotionButton AddTool(string text, string glyph, EventHandler click, bool primary)
        {
            MotionButton b = (MotionButton)(primary ? UiKit.Accent(text, click) : UiKit.Flat(text, click));
            b.Glyph = glyph;
            b.Font = Fx.Ui(Metrics.Fz(11), primary ? FontStyle.Bold : FontStyle.Regular);
            b.AutoSize = false;
            b.Size = new Size(b.GetPreferredSize(Size.Empty).Width + Metrics.U(6), Metrics.U(50));
            b.Margin = new Padding(0, 0, Metrics.U(8), Metrics.U(8));
            tools.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ show / places

        public override void OnShown()
        {
            if (!placesBuilt) BuildPlaces(null);
            if (current == null) Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false);
            else LoadFolder();
            RefreshDisks();
            if (diskTimer == null) diskTimer = new System.Threading.Timer(delegate { RefreshDisks(); }, null, 3500, 3500);
        }

        public override void OnSearch(string q)
        {
            query = q ?? "";
            ShowRows();
        }

        private string revealName;

        /// <summary>"Show in Files" from the document lists: opens the folder of the file and selects it.</summary>
        public void Reveal(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                revealName = Path.GetFileName(path);
                if (string.Equals(current, dir, StringComparison.OrdinalIgnoreCase)) LoadFolder(); else Navigate(dir, true);
            }
            catch { }
        }

        /// <summary>Opens a place from outside (the "Flash drive" tile of the home page): the first flash drive, or the documents.</summary>
        public void OpenFlashOrDocuments()
        {
            foreach (PlaceInfo d in disks) if (d.Removable) { Navigate(d.Path, true); return; }
            Ui.Toast("Флешка не найдена. Вставьте её в компьютер: она появится в списке слева.");
        }

        private void RefreshDisks()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<PlaceInfo> found;
                try { found = TeacherPlaces.Disks(); }
                catch { return; }
                try { BeginInvoke(new Action(delegate { ApplyDisks(found); })); }
                catch { }
            });
        }

        private void ApplyDisks(List<PlaceInfo> found)
        {
            if (IsDisposed) return;
            StringBuilderKey key = new StringBuilderKey();
            foreach (PlaceInfo p in found) key.Add(p.Letter + p.Title + (p.Removable ? "!" : ""));      // free space changes should not rebuild the list
            if (key.Text == disksKey) { return; }
            string before = disksKey;
            disksKey = key.Text;
            List<PlaceInfo> old = disks;
            disks = found;
            BuildPlaces(old);
            if (before.Length > 0 && Parent != null)
            {
                foreach (PlaceInfo p in found)
                {
                    bool isNew = true;
                    foreach (PlaceInfo o in old) if (o.Letter == p.Letter) isNew = false;
                    if (isNew && p.Removable) Ui.Toast("Подключена флешка: " + p.Title + ". Она в списке слева.");
                }
                // the drive we were looking at has been pulled out
                if (current != null && !Directory.Exists(current))
                {
                    Ui.Toast("Флешку вынули. Открываю «Документы».");
                    Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false);
                }
            }
        }

        private sealed class StringBuilderKey
        {
            private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();
            public void Add(string s) { sb.Append(s).Append(';'); }
            public string Text { get { return sb.ToString(); } }
        }

        private void BuildPlaces(List<PlaceInfo> previous)
        {
            placesBuilt = true;
            placesPanel.SuspendLayout();
            foreach (PlaceButton b in placeButtons) { placesPanel.Controls.Remove(b); b.Dispose(); }
            placeButtons.Clear();

            List<PlaceInfo> all = new List<PlaceInfo>();
            all.AddRange(TeacherPlaces.Folders());
            all.AddRange(disks);
            // Dock.Top stacks in reverse order: add the last one first
            for (int i = all.Count - 1; i >= 0; i--)
            {
                PlaceInfo info = all[i];
                PlaceButton b = new PlaceButton(info);
                b.Activated += delegate { Navigate(info.Path, true); };
                b.EjectClicked += delegate { Eject(info); };
                b.FilesDropped += OnPlaceDrop;
                placeButtons.Insert(0, b);
                placesPanel.Controls.Add(b);
            }
            placesPanel.Controls.SetChildIndex(placesTitle, placesPanel.Controls.Count - 1);
            placesPanel.ResumeLayout();
            MarkPlace();
        }

        private void MarkPlace()
        {
            PlaceButton best = null;
            int bestLen = -1;
            foreach (PlaceButton b in placeButtons)
            {
                b.Selected = false;
                if (current != null && FileJob.IsInside(current, b.Info.Path) && b.Info.Path.Length > bestLen) { best = b; bestLen = b.Info.Path.Length; }
            }
            if (best != null) best.Selected = true;
            if (fromFlash != null)
            {
                bool onFlash = false;
                foreach (PlaceInfo d in disks) if (d.Removable && current != null && FileJob.IsInside(current, d.Path)) onFlash = true;
                fromFlash.Visible = onFlash;
            }
        }

        // ------------------------------------------------------------------ navigation and listing

        public void Navigate(string path, bool push)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) { Ui.Toast("Эта папка недоступна."); return; }
                if (push && current != null && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase)) back.Push(current);
                current = path;
                crumbs.SetPath(path);
                MarkPlace();
                LoadFolder();
            }
            catch (Exception ex) { Ui.Toast("Не удалось открыть: " + ex.Message); }
        }

        private void GoBack()
        {
            while (back.Count > 0)
            {
                string p = back.Pop();
                if (Directory.Exists(p)) { Navigate(p, false); return; }
            }
        }

        private void GoUp()
        {
            if (current == null) return;
            try
            {
                DirectoryInfo parent = Directory.GetParent(current);
                if (parent != null) Navigate(parent.FullName, true);
            }
            catch { }
        }

        private void LoadFolder()
        {
            int gen = ++generation;
            string dir = current;
            list.BeginUpdate();
            list.Items.Clear();
            list.EndUpdate();
            rows = new List<Row>();
            status.Text = "Загрузка…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Row> found = new List<Row>();
                string error = null;
                try
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    List<Row> dirs = new List<Row>(), files = new List<Row>();
                    foreach (DirectoryInfo d in di.GetDirectories())
                    {
                        if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        dirs.Add(new Row { Name = d.Name, Path = d.FullName, Dir = true, Modified = d.LastWriteTime });
                    }
                    foreach (FileInfo f in di.GetFiles())
                    {
                        if ((f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        files.Add(new Row { Name = f.Name, Path = f.FullName, Size = f.Length, Modified = f.LastWriteTime });
                    }
                    Comparison<Row> byName = delegate (Row a, Row b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); };
                    dirs.Sort(byName);
                    files.Sort(byName);
                    found.AddRange(dirs);
                    found.AddRange(files);
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (gen != generation || IsDisposed) return;
                        rows = found;
                        if (error != null) status.Text = "Не удалось прочитать папку: " + error;
                        ShowRows();
                    }));
                }
                catch { }
            });
        }

        /// <summary>Fills the list from <see cref="rows"/> (filtered by the search box) in small portions, so a folder with thousands of files never freezes the window.</summary>
        private void ShowRows()
        {
            int gen = ++generation;
            List<Row> view = new List<Row>();
            foreach (Row r in rows)
                if (query.Length == 0 || r.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) view.Add(r);
            list.BeginUpdate();
            list.Items.Clear();
            list.EndUpdate();
            if (view.Count == 0)
            {
                status.Text = query.Length > 0 ? "Ничего не найдено." : "Здесь пусто. Перетащите сюда файлы.";
                return;
            }
            FillBatch(view, 0, gen);
        }

        private void FillBatch(List<Row> view, int start, int gen)
        {
            if (gen != generation || IsDisposed) return;
            int end = Math.Min(view.Count, start + 250);
            List<ListViewItem> items = new List<ListViewItem>(end - start);
            for (int i = start; i < end; i++)
            {
                Row r = view[i];
                string key = r.Dir ? "<dir>" : IconKey(r.Name);
                if (!icons.Images.ContainsKey(key))
                {
                    Bitmap bmp = ShellIcons.Get(key == "<dir>" ? "" : key, r.Dir);
                    if (bmp != null) icons.Images.Add(key, bmp);
                }
                ListViewItem it = new ListViewItem(r.Name);
                it.ImageKey = icons.Images.ContainsKey(key) ? key : null;
                it.SubItems.Add(r.Dir ? "" : FileIndex.Size(r.Size));
                it.SubItems.Add(r.Modified.ToString("dd.MM.yyyy  HH:mm"));
                it.Tag = r;
                items.Add(it);
            }
            list.BeginUpdate();
            list.Items.AddRange(items.ToArray());
            list.EndUpdate();
            if (end < view.Count)
            {
                try { BeginInvoke(new Action(delegate { FillBatch(view, end, gen); })); } catch { }
            }
            else
            {
                UpdateStatus();
                if (revealName != null)
                {
                    string want = revealName;
                    revealName = null;
                    foreach (ListViewItem it in list.Items)
                        if (it.Text == want) { it.Selected = true; it.EnsureVisible(); break; }
                }
            }
        }

        private static string IconKey(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            // programs and shortcuts have their own pictures, ask the shell per file name: too slow for long lists, so one shared picture
            return ext.Length == 0 ? "file" : ext;
        }

        private void FitColumns()
        {
            if (list.Columns.Count < 3 || list.ClientSize.Width < 200) return;
            int rest = list.ClientSize.Width - list.Columns[1].Width - list.Columns[2].Width - Metrics.U(6);
            list.Columns[0].Width = Math.Max(Metrics.U(200), rest);
        }

        private void UpdateStatus()
        {
            int sel = list.SelectedItems.Count;
            if (sel == 0)
            {
                int dirs = 0, files = 0;
                foreach (Row r in rows) { if (r.Dir) dirs++; else files++; }
                string free = "";
                try
                {
                    DriveInfo d = new DriveInfo(Path.GetPathRoot(current));
                    free = "   •   Свободно на диске: " + FileIndex.Size(d.AvailableFreeSpace);
                }
                catch { }
                status.Text = "Папок: " + dirs + ", файлов: " + files + free;
                return;
            }
            long size = 0;
            foreach (ListViewItem it in list.SelectedItems) { Row r = it.Tag as Row; if (r != null && !r.Dir) size += r.Size; }
            status.Text = "Выбрано: " + sel + (size > 0 ? "   •   " + FileIndex.Size(size) : "");
        }

        // ------------------------------------------------------------------ selection helpers

        private List<string> SelectedPaths()
        {
            List<string> res = new List<string>();
            foreach (ListViewItem it in list.SelectedItems) { Row r = it.Tag as Row; if (r != null) res.Add(r.Path); }
            return res;
        }

        private void OpenSelected()
        {
            if (list.SelectedItems.Count == 0) return;
            Row r = list.SelectedItems[0].Tag as Row;
            if (r == null) return;
            if (r.Dir) Navigate(r.Path, true); else Main.OpenFile(r.Path);
        }

        // ------------------------------------------------------------------ actions

        private void DoCopy(bool cut)
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) { Ui.Toast("Сначала выберите файлы или папки: нажмите на них."); return; }
            clip = sel;
            clipCut = cut;
            Ui.Toast((cut ? "Вырезано: " : "Скопировано: ") + sel.Count + ". Откройте нужную папку и нажмите «Вставить».");
        }

        private void DoPaste()
        {
            if (clip.Count == 0) { Ui.Toast("Нечего вставлять: сначала нажмите «Копировать» или «Вырезать»."); return; }
            if (current == null) return;
            StartJob(clip, current, clipCut);
            if (clipCut) clip = new List<string>();
        }

        private void DoNewFolder()
        {
            if (current == null) return;
            try
            {
                string path = FileJob.UniquePath(current, "Новая папка");
                Directory.CreateDirectory(path);
                LoadFolder();
                string name = Path.GetFileName(path);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 250;
                t.Tick += delegate
                {
                    t.Stop(); t.Dispose();
                    foreach (ListViewItem it in list.Items)
                        if (it.Text == name) { it.Selected = true; it.EnsureVisible(); it.BeginEdit(); break; }
                };
                t.Start();
            }
            catch (Exception ex) { Ui.Toast("Не удалось создать папку: " + ex.Message); }
        }

        private void DoRename()
        {
            if (list.SelectedItems.Count != 1) { Ui.Toast("Выберите один файл или одну папку, чтобы переименовать."); return; }
            list.SelectedItems[0].BeginEdit();
        }

        private void OnAfterEdit(object sender, LabelEditEventArgs e)
        {
            if (e.Label == null) return;                      // cancelled
            ListViewItem it = list.Items[e.Item];
            Row r = it.Tag as Row;
            string name = e.Label.Trim();
            e.CancelEdit = true;
            if (r == null || name.Length == 0 || name == r.Name) return;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Ui.Toast("В имени нельзя использовать знаки \\ / : * ? \" < > |"); return; }
            try
            {
                string dst = Path.Combine(Path.GetDirectoryName(r.Path), name);
                if (File.Exists(dst) || Directory.Exists(dst)) { Ui.Toast("Такое имя уже есть. Выберите другое."); return; }
                if (r.Dir) Directory.Move(r.Path, dst); else File.Move(r.Path, dst);
                BeginInvoke(new Action(LoadFolder));
            }
            catch (Exception ex) { Ui.Toast("Не удалось переименовать: " + ex.Message); }
        }

        private void DoDelete()
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) { Ui.Toast("Сначала выберите, что удалить."); return; }
            bool permanent = false;
            foreach (PlaceInfo d in disks) if (d.Removable && current != null && FileJob.IsInside(current, d.Path)) permanent = true;
            string what = sel.Count == 1 ? "«" + Path.GetFileName(sel[0]) + "»" : sel.Count + " объектов";
            string text = permanent
                ? "Удалить " + what + " с флешки?\n\nС флешки файлы удаляются навсегда: корзины там нет."
                : "Убрать " + what + " в корзину?\n\nПотом их можно вернуть из корзины.";
            if (MessageBox.Show(this, text, "Удаление", MessageBoxButtons.YesNo, permanent ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            jobBar.Show("Удаляю…", 0, false, Theme.Warn);
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                string error = null;
                foreach (string p in sel)
                {
                    try
                    {
                        if (Directory.Exists(p))
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                permanent ? Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently : Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        else
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                permanent ? Microsoft.VisualBasic.FileIO.RecycleOption.DeletePermanently : Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        ok++;
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { error = ex.Message; }
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        FinishBar(error == null ? "Готово: удалено " + ok : "Удалено " + ok + ". Ошибка: " + error, error == null ? Theme.Success : Theme.Danger);
                        LoadFolder();
                    }));
                }
                catch { }
            });
        }

        private void DoExplorer()
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + (current ?? "") + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Ui.Toast("Не удалось открыть Проводник: " + ex.Message); }
        }

        // ------------------------------------------------------------------ flash drives

        private List<PlaceInfo> Flash()
        {
            List<PlaceInfo> res = new List<PlaceInfo>();
            foreach (PlaceInfo d in disks) if (d.Removable) res.Add(d);
            return res;
        }

        private void DoToFlash()
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) { Ui.Toast("Выберите файлы или папки, которые нужно записать на флешку."); return; }
            List<PlaceInfo> flash = Flash();
            if (flash.Count == 0) { MessageBox.Show(this, "Флешка не найдена.\n\nВставьте флешку в компьютер и подождите несколько секунд.", "Флешка"); return; }
            if (flash.Count == 1) { StartJob(sel, flash[0].Path, false); return; }
            ContextMenuStrip m = Menus.Create();
            m.Font = Theme.UiFont(Metrics.Fz(12));
            foreach (PlaceInfo f in flash)
            {
                PlaceInfo target = f;
                Menus.Add(m, "Записать на: " + target.Title, delegate { StartJob(sel, target.Path, false); });
            }
            m.Show(toFlash, new Point(0, toFlash.Height));
        }

        private void DoFromFlash()
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0)
            {
                foreach (Row r in rows) sel.Add(r.Path);
                if (sel.Count == 0) { Ui.Toast("На флешке в этой папке ничего нет."); return; }
            }
            string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "С флешки");
            StartJob(sel, target, false);
        }

        private void Eject(PlaceInfo info)
        {
            string letter = info.Letter;
            if (current != null && FileJob.IsInside(current, info.Path))
                Navigate(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false);      // do not hold the drive open
            jobBar.Show("Извлекаю " + info.Title + "…", 0.3, false, Theme.Accent);
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Drives.Eject(letter);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        FinishBar(ok ? "Флешку " + letter + " можно вынимать." : "Не удалось извлечь " + letter + ". Закройте файлы с флешки (Word, проигрыватель) и попробуйте ещё раз.",
                            ok ? Theme.Success : Theme.Danger);
                        RefreshDisks();
                    }));
                }
                catch { }
            });
        }

        // ------------------------------------------------------------------ background copy / move

        private void StartJob(IEnumerable<string> sources, string target, bool move)
        {
            if (job != null && !job.Finished) { Ui.Toast("Подождите: ещё идёт предыдущее копирование."); return; }
            FileJob j = new FileJob(sources, target, move);
            job = j;
            if (barHide != null) barHide.Cancel();
            jobBar.Show((move ? "Перемещаю" : "Копирую") + "…", 0, true, Theme.Accent);
            ThreadPool.QueueUserWorkItem(delegate { j.Run(); });
            if (jobTimer == null)
            {
                jobTimer = new System.Windows.Forms.Timer();
                jobTimer.Interval = 150;
                jobTimer.Tick += delegate { JobTick(); };
            }
            jobTimer.Start();
        }

        private void JobTick()
        {
            FileJob j = job;
            if (j == null) { jobTimer.Stop(); return; }
            if (!j.Finished)
            {
                string verb = j.Move ? "Перемещаю" : "Копирую";
                string name = j.Current ?? "";
                if (name.Length > 40) name = name.Substring(0, 39) + "…";
                jobBar.Show(verb + " «" + name + "»   " + Math.Min(j.Done + 1, Math.Max(1, j.Total)) + " из " + Math.Max(1, j.Total) + "   (" + j.Percent + "%)", j.Percent / 100.0, true, Theme.Accent);
                return;
            }
            jobTimer.Stop();
            job = null;
            string text;
            Color tone;
            if (j.Cancelled) { text = "Отменено. Скопированное до этого момента осталось на месте."; tone = Theme.Warn; }
            else if (j.Errors.Count > 0)
            {
                text = (j.Done > 0 ? "Готово: " + j.Done + ". " : "") + "Не получилось: " + j.Errors[0] + (j.Errors.Count > 1 ? " (и ещё " + (j.Errors.Count - 1) + ")" : "");
                tone = Theme.Danger;
            }
            else { text = (j.Move ? "Готово: перемещено " : "Готово: скопировано ") + j.Done + " (" + FileIndex.Size(j.TotalBytes) + ")"; tone = Theme.Success; }
            FinishBar(text, tone);
            LoadFolder();
            RefreshDisks();
        }

        private void FinishBar(string text, Color tone)
        {
            jobBar.Show(text, 1, false, tone);
            if (barHide != null) barHide.Cancel();
            barHide = Tween.Run(5200, Easing.Linear, delegate (double t) { }, delegate { if (!jobBar.IsDisposed && job == null) jobBar.Visible = false; });
        }

        // ------------------------------------------------------------------ drag and drop

        private void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) return;
            DataObject data = new DataObject(DataFormats.FileDrop, sel.ToArray());
            list.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private string DropTarget(DragEventArgs e, out ListViewItem hit)
        {
            Point pt = list.PointToClient(new Point(e.X, e.Y));
            hit = list.GetItemAt(Math.Max(0, pt.X), pt.Y);
            Row r = hit == null ? null : hit.Tag as Row;
            if (r != null && r.Dir) return r.Path;
            hit = null;
            return current;
        }

        private void OnListDrag(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.None; return; }
            ListViewItem hit;
            string target = DropTarget(e, out hit);
            ClearDropHot();
            if (hit != null) { dropHot = hit; hit.BackColor = Theme.CardHover; }
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            DragDropEffects fx;
            if ((e.KeyState & 4) != 0) fx = DragDropEffects.Move;
            else if ((e.KeyState & 8) != 0) fx = DragDropEffects.Copy;
            else fx = paths != null && paths.Length > 0 && target != null && FileJob.SameVolume(paths[0], target) ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Effect = fx & e.AllowedEffect;
        }

        private void ClearDropHot()
        {
            if (dropHot != null) { try { dropHot.BackColor = list.BackColor; } catch { } dropHot = null; }
        }

        private void OnListDrop(object sender, DragEventArgs e)
        {
            ListViewItem hit;
            string target = DropTarget(e, out hit);
            ClearDropHot();
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0 || target == null) return;
            StartJob(paths, target, e.Effect == DragDropEffects.Move);
        }

        private void OnPlaceDrop(PlaceInfo place, string[] paths, bool move)
        {
            StartJob(paths, place.Path, move);
        }

        // ------------------------------------------------------------------ mouse and keys

        private void OnListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem hit = list.GetItemAt(e.X, e.Y);
            ContextMenuStrip m = Menus.Create();
            m.Font = Theme.UiFont(Metrics.Fz(12));
            if (hit != null)
            {
                if (!hit.Selected) { list.SelectedItems.Clear(); hit.Selected = true; }
                Row r = hit.Tag as Row;
                Menus.Add(m, "Открыть", delegate { OpenSelected(); });
                Menus.Separator(m);
                Menus.Add(m, "Копировать", delegate { DoCopy(false); });
                Menus.Add(m, "Вырезать", delegate { DoCopy(true); });
                Menus.Add(m, "Вставить", delegate { DoPaste(); });
                Menus.Separator(m);
                Menus.Add(m, "Переименовать", delegate { DoRename(); });
                Menus.Add(m, "Удалить", delegate { DoDelete(); });
                Menus.Separator(m);
                Menus.Add(m, "Записать на флешку", delegate { DoToFlash(); });
                if (r != null && !r.Dir) Menus.Add(m, "Свойства", delegate { try { new FilePropertiesForm(Main, r.Path).ShowDialog(); } catch { } });
            }
            else
            {
                Menus.Add(m, "Вставить", delegate { DoPaste(); });
                Menus.Add(m, "Новая папка", delegate { DoNewFolder(); });
                Menus.Add(m, "Обновить", delegate { LoadFolder(); });
            }
            m.Show(Cursor.Position);
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { DoDelete(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { DoRename(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { LoadFolder(); e.Handled = true; }
            else if (e.KeyCode == Keys.Back) { GoUp(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { DoCopy(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.X) { DoCopy(true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.V) { DoPaste(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in list.Items) it.Selected = true; e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (diskTimer != null) diskTimer.Dispose();
                if (jobTimer != null) jobTimer.Dispose();
                if (barHide != null) barHide.Cancel();
            }
            base.Dispose(disposing);
        }
    }
}
