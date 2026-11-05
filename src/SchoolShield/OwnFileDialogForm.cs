using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SchoolShield
{
    internal sealed class FileDialogResult
    {
        public List<string> Paths = new List<string>();
        public int TypeIndex = -1;
        public string Folder;
    }

    /// <summary>
    /// The launcher's own "Open" / "Save as" window: the same look as the launcher, only the allowed folders and flash drives, educational files
    /// only (no installers), a student cannot leave the allowed folders and cannot save a file of a forbidden type.
    /// </summary>
    internal sealed class OwnFileDialogForm : Form
    {
        private readonly bool save;
        private readonly Config cfg;
        private readonly ListBox rootsBox = new ListBox();
        private readonly ListView list = new ListView();
        private readonly Label where = new Label();
        private readonly TextBox nameBox = new TextBox();
        private readonly ComboBox typeBox = new ComboBox();
        private readonly ImageList icons = new ImageList();
        private List<FileRoot> roots = new List<FileRoot>();
        private List<string> exts;                         // null = all educational files
        private string current;                            // null = the list of roots
        private bool fillingName;
        public FileDialogResult Result;

        public static FileDialogResult Pick(DialogLogic.Mode mode, string app, List<string> typeTexts, int typeIndex, string initialName, string startFolder, Config cfg)
        {
            using (OwnFileDialogForm f = new OwnFileDialogForm(mode == DialogLogic.Mode.Save, app, typeTexts, typeIndex, initialName, startFolder, cfg))
            {
                f.Shown += delegate { f.BringUp(); };
                return f.ShowDialog() == DialogResult.OK ? f.Result : null;
            }
        }

        private OwnFileDialogForm(bool save, string app, List<string> typeTexts, int typeIndex, string initialName, string startFolder, Config cfg)
        {
            this.save = save;
            this.cfg = cfg;

            Text = save ? "Сохранить файл" : "Открыть файл";
            Fx.FadeIn(this, 160);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(Metrics.S(960), Metrics.S(600));
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10.5f);

            int pad = Metrics.S(16);
            Label title = new Label();
            title.Text = save ? "Сохранить файл" : "Открыть файл";
            title.Font = Theme.UiFont(18, FontStyle.Bold);
            title.ForeColor = Theme.Accent;
            title.SetBounds(pad, Metrics.S(10), Metrics.S(600), Metrics.S(38));
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = string.IsNullOrEmpty(app) ? "" : "Программа: " + app;
            sub.ForeColor = Theme.Muted;
            sub.SetBounds(pad, Metrics.S(48), Metrics.S(600), Metrics.S(22));
            Controls.Add(sub);

            rootsBox.SetBounds(pad, Metrics.S(82), Metrics.S(230), Metrics.S(400));
            rootsBox.BackColor = Theme.Sidebar;
            rootsBox.ForeColor = Theme.Text;
            rootsBox.BorderStyle = BorderStyle.None;
            rootsBox.Font = Theme.UiFont(11);
            rootsBox.ItemHeight = Metrics.S(30);
            rootsBox.DrawMode = DrawMode.OwnerDrawFixed;
            rootsBox.DrawItem += DrawRoot;
            rootsBox.SelectedIndexChanged += delegate
            {
                FileRoot r = rootsBox.SelectedItem as FileRoot;
                if (r != null) Navigate(r.Path);
            };
            Controls.Add(rootsBox);

            int cx = pad + Metrics.S(230) + Metrics.S(12), cw = ClientSize.Width - cx - pad;
            Button up = UiKit.Flat("Вверх", delegate { GoUp(); });
            up.AutoSize = false;
            up.SetBounds(cx, Metrics.S(82), Metrics.S(90), Metrics.S(34));
            Controls.Add(up);
            Button newFolder = UiKit.Flat("Новая папка", delegate { NewFolder(); });
            newFolder.AutoSize = false;
            newFolder.SetBounds(cx + Metrics.S(98), Metrics.S(82), Metrics.S(140), Metrics.S(34));
            newFolder.Visible = save;
            Controls.Add(newFolder);
            where.SetBounds(cx + (save ? Metrics.S(250) : Metrics.S(98)), Metrics.S(86), cw - Metrics.S(260), Metrics.S(28));
            where.ForeColor = Theme.Muted;
            where.AutoEllipsis = true;
            Controls.Add(where);

            icons.ColorDepth = ColorDepth.Depth32Bit;
            icons.ImageSize = new Size(24, 24);
            list.SetBounds(cx, Metrics.S(122), cw, Metrics.S(360));
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = !save;
            list.HideSelection = false;
            list.BackColor = Theme.Background;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.Font = Theme.UiFont(11);
            list.SmallImageList = icons;
            list.Columns.Add("Имя", cw - Metrics.S(240));
            list.Columns.Add("Размер", Metrics.S(100));
            list.Columns.Add("Изменён", Metrics.S(120));
            list.ItemActivate += delegate { Activated(); };
            list.SelectedIndexChanged += delegate { SelectionToName(); };
            Controls.Add(list);

            Label nameLabel = new Label();
            nameLabel.Text = "Имя файла:";
            nameLabel.SetBounds(pad, Metrics.S(500), Metrics.S(110), Metrics.S(28));
            Controls.Add(nameLabel);
            nameBox.SetBounds(pad + Metrics.S(112), Metrics.S(498), Metrics.S(520), Metrics.S(28));
            nameBox.BackColor = Theme.Card;
            nameBox.ForeColor = Theme.Text;
            nameBox.BorderStyle = BorderStyle.FixedSingle;
            nameBox.ReadOnly = !save;
            string initial = save ? Path.GetFileName(initialName ?? "") : "";
            nameBox.Text = initial;
            nameBox.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Accept(); } };
            Controls.Add(nameBox);

            typeBox.SetBounds(pad + Metrics.S(112) + Metrics.S(530), Metrics.S(498), ClientSize.Width - (pad + Metrics.S(112) + Metrics.S(530)) - pad, Metrics.S(28));
            typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string t in typeTexts) typeBox.Items.Add(t);
            if (typeBox.Items.Count > 0) typeBox.SelectedIndex = Math.Max(0, Math.Min(typeBox.Items.Count - 1, typeIndex));
            typeBox.Visible = typeBox.Items.Count > 0;
            typeBox.SelectedIndexChanged += delegate { ApplyType(); Reload(); };
            Controls.Add(typeBox);
            ApplyType();

            Button ok = UiKit.Accent(save ? "Сохранить" : "Открыть", delegate { Accept(); });
            ok.AutoSize = false;
            ok.SetBounds(ClientSize.Width - pad - Metrics.S(320), Metrics.S(544), Metrics.S(150), Metrics.S(42));
            Controls.Add(ok);
            Button cancel = UiKit.Flat("Отмена", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.AutoSize = false;
            cancel.SetBounds(ClientSize.Width - pad - Metrics.S(160), Metrics.S(544), Metrics.S(160), Metrics.S(42));
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = null;

            Label hint = new Label();
            hint.Text = save ? "Сохранять можно только в свои папки и на флешку (если разрешено)." : "Показаны папки и учебные файлы.";
            hint.ForeColor = Theme.Muted;
            hint.SetBounds(pad, Metrics.S(552), Metrics.S(500), Metrics.S(26));
            Controls.Add(hint);

            roots = FileRoots.ResolveAll(cfg);
            foreach (FileRoot r in roots) rootsBox.Items.Add(r);
            // the folder last used with this program, else "My documents" (or the first folder we have)
            string folder = null;
            if (!string.IsNullOrEmpty(startFolder) && FileRoots.RootOf(roots, startFolder) != null && Directory.Exists(startFolder)) folder = startFolder;
            else if (roots.Count > 0) folder = roots[0].Path;
            if (folder != null) Navigate(folder); else Reload();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = Theme.Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(Handle, 20, ref on, 4);
                Native.DwmSetWindowAttribute(Handle, 19, ref on, 4);
            }
            catch { }
        }

        /// <summary>Forces the window to the front even though another program is active.</summary>
        private void BringUp()
        {
            Native.keybd_event((byte)Native.VK_MENU, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Native.VK_MENU, 0, 2, UIntPtr.Zero);
            Activate();
            Native.SetForegroundWindow(Handle);
            if (save) { nameBox.Focus(); nameBox.SelectAll(); } else list.Focus();
        }

        private void DrawRoot(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            FileRoot r = (FileRoot)rootsBox.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush bg = new SolidBrush(sel ? Theme.Accent : Theme.Sidebar))
                e.Graphics.FillRectangle(bg, e.Bounds);
            string text = r.Name.Split(new string[] { "  —  " }, StringSplitOptions.None)[0] + (r.ReadOnly ? "  (чтение)" : "");
            using (SolidBrush fg = new SolidBrush(sel ? (Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White) : Theme.Text))
                e.Graphics.DrawString(text, Font, fg, e.Bounds.X + 8, e.Bounds.Y + (e.Bounds.Height - Font.Height) / 2);
        }

        // ---------------------------------------------------------------- browsing

        private void ApplyType()
        {
            exts = typeBox.SelectedIndex >= 0 ? DialogLogic.ExtensionsOf((string)typeBox.SelectedItem) : null;
        }

        private FileRoot RootOf(string folder) { return folder == null ? null : FileRoots.RootOf(roots, folder); }

        private void Navigate(string folder)
        {
            if (folder != null && RootOf(folder) == null) return;     // never outside the allowed folders
            current = folder;
            Reload();
        }

        private void GoUp()
        {
            if (current == null) return;
            FileRoot r = RootOf(current);
            if (r == null || string.Equals(FileRoots.Normalize(current), r.Path, StringComparison.OrdinalIgnoreCase)) { current = null; rootsBox.ClearSelected(); }
            else current = Path.GetDirectoryName(current);
            Reload();
        }

        private void AddIcon(string key, Bitmap bmp)
        {
            if (bmp != null && !icons.Images.ContainsKey(key)) icons.Images.Add(key, bmp);
        }

        private void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            AddIcon("<dir>", ShellIcons.Get("", true));
            if (current == null)
            {
                where.Text = roots.Count == 0 ? "Нет доступных папок." : "Выберите папку слева.";
                list.EndUpdate();
                return;
            }
            FileRoot root = RootOf(current);
            where.Text = (root == null ? "" : root.Name.Split(new string[] { "  —  " }, StringSplitOptions.None)[0]) + current.Substring(Math.Min(current.Length, root == null ? 0 : root.Path.Length)).Replace('\\', '›')
                + (root != null && root.ReadOnly ? "   (только чтение)" : "");
            try
            {
                List<string> dirs = new List<string>(Directory.GetDirectories(current));
                List<string> files = new List<string>(Directory.GetFiles(current));
                dirs.Sort(StringComparer.CurrentCultureIgnoreCase);
                files.Sort(StringComparer.CurrentCultureIgnoreCase);
                foreach (string d in dirs)
                {
                    if (FileFilter.IsHidden(d)) continue;
                    ListViewItem it = new ListViewItem(Path.GetFileName(d));
                    it.ImageKey = icons.Images.ContainsKey("<dir>") ? "<dir>" : null;
                    it.SubItems.Add("");
                    it.SubItems.Add(Directory.GetLastWriteTime(d).ToString("dd.MM.yyyy"));
                    it.Tag = d;
                    list.Items.Add(it);
                }
                foreach (string f in files)
                {
                    if (FileFilter.IsHidden(f) || !FileFilter.Allowed(f, cfg) || !DialogLogic.MatchesFilter(f, exts)) continue;
                    string ext = FileFilter.ExtensionOf(f);
                    AddIcon(ext, ShellIcons.Get(ext, false));
                    FileInfo fi = new FileInfo(f);
                    ListViewItem it = new ListViewItem(fi.Name);
                    it.ImageKey = icons.Images.ContainsKey(ext) ? ext : null;
                    it.SubItems.Add(FileIndex.Size(fi.Length));
                    it.SubItems.Add(fi.LastWriteTime.ToString("dd.MM.yyyy"));
                    it.Tag = f;
                    list.Items.Add(it);
                }
            }
            catch (Exception ex) { where.Text += "   —   " + ex.Message; }
            list.EndUpdate();
        }

        private void SelectionToName()
        {
            if (fillingName || list.SelectedItems.Count == 0) return;
            List<string> files = new List<string>();
            foreach (ListViewItem it in list.SelectedItems)
            {
                string p = it.Tag as string;
                if (p != null && File.Exists(p)) files.Add(Path.GetFileName(p));
            }
            if (files.Count == 0) return;
            fillingName = true;
            nameBox.Text = save || files.Count == 1 ? files[0] : DialogLogic.QuoteNames(files);
            fillingName = false;
        }

        private void Activated()
        {
            if (list.SelectedItems.Count == 0) return;
            string p = list.SelectedItems[0].Tag as string;
            if (p == null) return;
            if (Directory.Exists(p)) Navigate(p);
            else Accept();
        }

        private void NewFolder()
        {
            FileRoot r = RootOf(current);
            if (r == null || r.ReadOnly) { MessageBox.Show(this, "Здесь нельзя создавать папки.", Text); return; }
            string name = Microsoft.VisualBasic.Interaction.InputBox("Название новой папки:", "Новая папка", "Новая папка");
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0) return;
            try { Directory.CreateDirectory(FileFilter.UniquePath(current, name.Trim())); }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось создать папку: " + ex.Message, Text); }
            Reload();
        }

        // ---------------------------------------------------------------- the answer

        private void Accept()
        {
            if (current == null) { MessageBox.Show(this, "Откройте папку слева.", Text); return; }
            FileRoot root = RootOf(current);
            if (root == null) return;
            FileDialogResult r = new FileDialogResult();
            r.TypeIndex = typeBox.SelectedIndex;
            r.Folder = current;

            if (save)
            {
                if (root.ReadOnly) { MessageBox.Show(this, "Сюда сохранять нельзя: папка только для чтения.", Text); return; }
                string finalName;
                string error = DialogLogic.CheckSaveName(nameBox.Text, exts, cfg, out finalName);
                if (error != null) { MessageBox.Show(this, error, Text); return; }
                string path = Path.Combine(current, finalName);
                if (File.Exists(path) &&
                    MessageBox.Show(this, "Файл «" + finalName + "» уже есть. Заменить его?", "Сохранить", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                r.Paths.Add(path);
            }
            else
            {
                List<string> chosen = new List<string>();
                foreach (ListViewItem it in list.SelectedItems)
                {
                    string p = it.Tag as string;
                    if (p != null && File.Exists(p)) chosen.Add(p);
                }
                if (chosen.Count == 0)
                {
                    string typed = nameBox.Text.Trim().Trim('"');
                    if (typed.Length > 0 && typed.IndexOfAny(new char[] { '\\', '/', ':', '*', '?', '<', '>', '|' }) < 0)
                    {
                        string p = Path.Combine(current, typed);
                        if (File.Exists(p)) chosen.Add(p);
                    }
                }
                if (chosen.Count == 0) { MessageBox.Show(this, "Выберите файл.", Text); return; }
                foreach (string p in chosen)
                {
                    if (RootOf(p) == null || !FileFilter.Allowed(p, cfg)) { MessageBox.Show(this, "Файл «" + Path.GetFileName(p) + "» открыть нельзя.", Text); return; }
                    r.Paths.Add(p);
                }
            }
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
