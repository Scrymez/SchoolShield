using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// "Flash drives": connected USB sticks, external disks and discs, each one a root of the same file manager as "My files".
    /// A student sees folders and educational files only (documents, tables, presentations, pictures, music, code, sketches, LEGO EV3 and
    /// other robotics projects); installers never appear. The list refreshes by itself when a drive is plugged in or removed.
    /// </summary>
    internal sealed class UsbSection : FilesSection
    {
        private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        private string known = "";

        public UsbSection(MainForm main) : base(main)
        {
            AddTextTool("Скопировать на компьютер", "Копирует выбранные файлы и папки в «Мои документы» → «С флешки»", delegate { CopyToComputer(); });
            AddTextTool("Безопасно извлечь", "Нажмите перед тем, как вынуть флешку", delegate { EjectSelected(); });
            poll.Interval = 2000;
            poll.Tick += delegate { Poll(); };
        }

        public override string Id { get { return "usb"; } }
        public override string Title { get { return "Флешки и диски"; } }
        public override string Glyph { get { return ""; } }

        protected override List<FileRoot> ResolveRoots() { return Drives.Roots(Main.Cfg); }
        protected override string RootsTitle { get { return "Выберите флешку или диск"; } }

        protected override string EmptyRootsText
        {
            get
            {
                string mode = (Main.Cfg.UsbMode ?? "readonly").ToLowerInvariant();
                if (mode == "block" && !State.Privileged) return "Флешки отключены администратором.";
                return "Флешка не подключена. Вставьте её в компьютер: она появится здесь сама.";
            }
        }

        public override void OnShown()
        {
            base.OnShown();
            known = Fingerprint();
            poll.Start();
        }

        private static string Fingerprint() { return Drives.Signature() + "|" + Drives.Settled; }

        private void Poll()
        {
            if (Parent == null || IsDisposed) return;          // another section is on screen
            string now = Fingerprint();
            if (now == known) return;
            known = now;
            RefreshRoots();
        }

        // ---------------------------------------------------------------- copy to the computer

        private static string SafeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Флешка";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        private void CopyToComputer()
        {
            if (current == null) { Ui.Toast("Откройте флешку и отметьте файлы или папки."); return; }
            List<string> sel = SelectedPaths();
            if (sel.Count == 0) { Ui.Toast("Отметьте файлы или папки на флешке."); return; }
            FileRoot root = FileRoots.RootOf(roots, current);
            string label = root == null ? "Флешка" : SafeName(root.Name.Split(new string[] { "  —  " }, StringSplitOptions.None)[0]);
            string target = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "С флешки"), label);
            Config cfg = Main.Cfg;
            bool privileged = State.Privileged;

            ThreadPool.QueueUserWorkItem(delegate
            {
                int copied = 0, skipped = 0;
                string error = null;
                try
                {
                    Directory.CreateDirectory(target);
                    foreach (string src in sel)
                    {
                        if (Directory.Exists(src))
                        {
                            int s;
                            copied += FileFilter.CopyDirectory(src, FileFilter.UniquePath(target, Path.GetFileName(src)), cfg, privileged, out s);
                            skipped += s;
                        }
                        else if (File.Exists(src))
                        {
                            if (!privileged && !FileFilter.Allowed(src, cfg)) { skipped++; continue; }
                            File.Copy(src, FileFilter.UniquePath(target, Path.GetFileName(src)));
                            copied++;
                        }
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                Log.Write("usb", "Копирование с флешки: " + copied + " файлов в " + target + (error == null ? "" : "; ошибка: " + error));
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (error != null) Ui.Toast("Не удалось скопировать: " + error);
                        else if (copied == 0) Ui.Toast("Нечего копировать: здесь нет учебных файлов.");
                        else Ui.Toast("Скопировано файлов: " + copied + ". Они в «Мои документы» → «С флешки»." + (skipped > 0 ? " Пропущено: " + skipped : ""));
                    }));
                }
                catch { }
            });
        }

        // ---------------------------------------------------------------- safe removal

        private void EjectSelected()
        {
            string letter = null;
            if (current != null)
            {
                FileRoot r = FileRoots.RootOf(roots, current);
                if (r != null) letter = r.Letter;
            }
            else
            {
                foreach (string p in SelectedPaths())
                {
                    FileRoot r = FileRoots.RootOf(roots, p);
                    if (r != null) { letter = r.Letter; break; }
                }
            }
            if (letter == null && roots.Count == 1) letter = roots[0].Letter;
            if (letter == null) { Ui.Toast(roots.Count == 0 ? "Нет подключённых флешек." : "Откройте флешку или выберите её в списке."); return; }

            ShowRoots();                                   // leave the drive so that we do not hold it open
            string drive = letter;
            Ui.Toast("Извлекаю " + drive + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Drives.Eject(drive);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        Ui.Toast(ok ? "Флешку " + drive + " можно вынимать." : "Не удалось извлечь " + drive + ". Закройте файлы с флешки и попробуйте ещё раз.");
                        known = "";                          // force a redraw on the next tick
                    }));
                }
                catch { }
            });
        }
    }
}
