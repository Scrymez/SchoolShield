using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Windows' own right-click menus (Properties, Open with, Send to, Open file location, Security tab...)
    /// are not reachable inside the launcher. These are the only menus a student gets.
    /// </summary>
    internal static class Menus
    {
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.CardHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
            public override Color MenuItemBorder { get { return Theme.Accent; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Line; } }
            public override Color SeparatorLight { get { return Theme.Line; } }
        }

        /// <summary>Rounded hover pill, thin border, no system look.</summary>
        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors()) { }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                Graphics g = e.Graphics;
                Fx.Quality(g);
                Fx.StrokeRound(g, Theme.Line, 1.6f, new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 2, e.ToolStrip.Height - 2), Metrics.S(12));
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                Graphics g = e.Graphics;
                Fx.Quality(g);
                RectangleF r = new RectangleF(Metrics.S(5), 2, e.Item.Width - Metrics.S(10), e.Item.Height - 4);
                using (SolidBrush b = new SolidBrush(Theme.CardHover)) Fx.FillRound(g, b, r, Metrics.S(9));
                using (LinearGradientBrush lg = Fx.Gradient(new RectangleF(r.X, r.Y + 6, Metrics.S(3), Math.Max(4, r.Height - 12)), Theme.Accent, Theme.Accent2, 90f))
                    Fx.FillRound(g, lg, new RectangleF(r.X + 2, r.Y + 6, Metrics.S(3), Math.Max(4, r.Height - 12)), 1.5f);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (Pen p = new Pen(Theme.Line))
                    e.Graphics.DrawLine(p, Metrics.S(12), e.Item.Height / 2, e.Item.Width - Metrics.S(12), e.Item.Height / 2);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (!e.Item.Enabled) e.TextColor = Fx.Alpha(Theme.Muted, 0.6);
                else if (e.Item.ForeColor == Theme.Danger) e.TextColor = Theme.Danger;
                else e.TextColor = Theme.Text;
                base.OnRenderItemText(e);
            }
        }

        public static ContextMenuStrip Create()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new MenuRenderer();
            m.BackColor = Theme.Card;
            m.ForeColor = Theme.Text;
            m.Font = Theme.UiFont(11);
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.Padding = new Padding(Metrics.S(4));
            m.Opening += delegate
            {
                if (!Motion.Enabled) return;
                m.Opacity = 0;
                Tween.Run(150, Easing.OutCubic, delegate (double t) { if (!m.IsDisposed) m.Opacity = t; });
            };
            m.Opened += delegate
            {
                try
                {
                    using (System.Drawing.Drawing2D.GraphicsPath gp = Fx.Round(new RectangleF(0, 0, m.Width, m.Height), Metrics.S(12)))
                        m.Region = new Region(gp);
                }
                catch { }
            };
            return m;
        }

        public static ToolStripItem Add(ContextMenuStrip m, string text, Action action)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.ForeColor = Theme.Text;
            it.Padding = new Padding(6, 6, 6, 6);
            if (action != null) it.Click += delegate { action(); };
            m.Items.Add(it);
            return it;
        }

        public static void Separator(ContextMenuStrip m) { m.Items.Add(new ToolStripSeparator()); }

        /// <summary>Menu of a program tile: open and favorites only.</summary>
        public static void ForApp(MainForm main, AppEntry app, Control at)
        {
            ContextMenuStrip m = Create();
            Add(m, "Открыть", delegate { main.LaunchApp(app); });
            bool fav = main.User.IsFavorite(app.Name);
            Add(m, fav ? "Убрать из избранного" : "В избранное", delegate { main.ToggleFavorite(app); });
            if (State.Privileged)
            {
                // Only after the admin code was entered (admin mode) and still behind the Windows admin password prompt.
                Separator(m);
                ToolStripItem rm = Add(m, "Удалить программу из системы…", delegate { main.UninstallApp(app); });
                rm.ForeColor = Theme.Danger;
            }
            m.Show(Cursor.Position);
        }

        /// <summary>Menu of a file or folder. <paramref name="fm"/> is non-null inside the file manager (extra editing items).</summary>
        public static void ForFile(MainForm main, string path, bool isFolder, FileManagerActions fm)
        {
            ContextMenuStrip m = Create();
            Add(m, "Открыть", delegate { if (fm != null && isFolder) fm.Enter(path); else main.OpenFile(path); });

            if (!isFolder)
            {
                if (fm == null)
                    Add(m, "Показать в «Файлах»", delegate { main.ShowInFiles(path); });
                Add(m, "Свойства", delegate { new FilePropertiesForm(main, path).ShowDialog(); });
            }

            if (fm != null)
            {
                Separator(m);
                ToolStripItem copy = Add(m, "Копировать", delegate { fm.Copy(); });
                ToolStripItem cut = Add(m, "Вырезать", delegate { fm.Cut(); });
                ToolStripItem rename = Add(m, "Переименовать", delegate { fm.Rename(); });
                ToolStripItem del = Add(m, "Удалить", delegate { fm.Delete(); });
                bool canWrite = fm.CanModify;
                cut.Enabled = canWrite;
                rename.Enabled = canWrite;
                del.Enabled = canWrite && main.Cfg.StudentCanDelete;
            }
            m.Show(Cursor.Position);
        }
    }

    /// <summary>What the file manager lets its context menu do (kept as an interface to avoid a hard dependency).</summary>
    internal abstract class FileManagerActions
    {
        public abstract bool CanModify { get; }
        public abstract void Enter(string folder);
        public abstract void Copy();
        public abstract void Cut();
        public abstract void Rename();
        public abstract void Delete();
    }

    internal static class FileSecurity
    {
        // Word, Excel, PowerPoint documents without macros. Macro-enabled formats are never unblocked.
        private static readonly HashSet<string> Unblockable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".doc", ".docx", ".dot", ".dotx",
            ".xls", ".xlsx", ".xlt", ".xltx",
            ".ppt", ".pptx", ".pps", ".ppsx", ".pot", ".potx"
        };

        public static bool IsOfficeFile(string path)
        {
            return Unblockable.Contains(Path.GetExtension(path) ?? "");
        }

        private static string ZoneStream(string path) { return path + ":Zone.Identifier"; }

        /// <summary>True when Windows marked the file as downloaded from the internet (Mark of the Web).</summary>
        public static bool IsBlocked(string path)
        {
            return Native.GetFileAttributes(ZoneStream(path)) != 0xFFFFFFFF;
        }

        public static bool Unblock(string path)
        {
            if (!IsOfficeFile(path)) return false;
            if (!IsBlocked(path)) return true;
            bool ok = Native.DeleteFile(ZoneStream(path));
            Log.Write("files", "Разблокировка " + Path.GetFileName(path) + ": " + (ok ? "ок" : "не удалось"));
            return ok;
        }
    }

    /// <summary>
    /// Our replacement for the Windows "Properties" window: name, type, size, date. No security tab, no attributes,
    /// no "Open with". The only action is Unblock, and only for Word / Excel / PowerPoint files.
    /// </summary>
    internal sealed class FilePropertiesForm : Form
    {
        private readonly string path;
        private readonly Label state = new Label();
        private readonly Button unblock;

        public FilePropertiesForm(MainForm main, string path)
        {
            this.path = path;
            Text = "Свойства";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11);
            ClientSize = new Size(520, 330);

            FileInfo fi = new FileInfo(path);
            FileRoot root = FileRoots.RootOf(FileRoots.Resolve(main.Cfg), path);
            string where = root == null ? "—" : root.Name + (fi.DirectoryName.Length > root.Path.Length ? fi.DirectoryName.Substring(root.Path.Length) : "");

            AddRow(0, "Имя", fi.Name);
            AddRow(1, "Тип", KindName(FileKinds.Of(path)) + " (" + fi.Extension.ToLowerInvariant() + ")");
            AddRow(2, "Размер", FileIndex.Size(fi.Length));
            AddRow(3, "Изменён", fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm"));
            AddRow(4, "Папка", where);

            state.SetBounds(20, 200, 480, 48);
            state.ForeColor = Theme.Muted;
            Controls.Add(state);

            unblock = UiKit.Accent("Разблокировать", delegate { DoUnblock(); });
            unblock.AutoSize = false;
            unblock.SetBounds(20, 270, 200, 42);
            Controls.Add(unblock);

            Button close = UiKit.Flat("Закрыть", delegate { Close(); });
            close.AutoSize = false;
            close.SetBounds(300, 270, 200, 42);
            Controls.Add(close);
            CancelButton = close;

            Refresh2();
        }

        private void AddRow(int i, string caption, string value)
        {
            Label c = new Label();
            c.Text = caption;
            c.ForeColor = Theme.Muted;
            c.SetBounds(20, 20 + i * 34, 110, 26);
            Label v = new Label();
            v.Text = value;
            v.AutoEllipsis = true;
            v.SetBounds(140, 20 + i * 34, 370, 26);
            Controls.Add(c);
            Controls.Add(v);
        }

        private void Refresh2()
        {
            bool office = FileSecurity.IsOfficeFile(path);
            bool blocked = office && FileSecurity.IsBlocked(path);
            if (!office) state.Text = "Другие свойства файла недоступны.";
            else if (blocked) state.Text = "Файл получен из интернета, поэтому Word / Excel / PowerPoint открывают его в защищённом режиме без редактирования. Нажмите «Разблокировать».";
            else state.Text = "Файл разблокирован.";
            unblock.Visible = office;
            unblock.Enabled = blocked;
        }

        private void DoUnblock()
        {
            if (!FileSecurity.Unblock(path)) MessageBox.Show(this, "Не удалось разблокировать файл.", "Свойства");
            Refresh2();
        }

        private static string KindName(FileKind k)
        {
            switch (k)
            {
                case FileKind.Document: return "Документ";
                case FileKind.Presentation: return "Презентация";
                case FileKind.Table: return "Таблица";
                case FileKind.Book: return "Книга / PDF";
                case FileKind.Text: return "Текст";
                case FileKind.Image: return "Изображение";
                case FileKind.Video: return "Видео";
                case FileKind.Audio: return "Музыка";
                default: return "Файл";
            }
        }
    }
}
