using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// The launcher: a full-screen "desktop" with sections (programs by category, documents, media, files, internet).
    /// In shell mode it cannot be minimized, closed or alt-tabbed away from, and Windows Explorer is not available.
    /// </summary>
    internal sealed class MainForm : Form
    {
        public readonly Config Cfg;
        public UserData User;
        public List<AppEntry> Apps = new List<AppEntry>();

        private readonly Dictionary<string, Section> sections = new Dictionary<string, Section>();
        private readonly List<string> order = new List<string>();
        private readonly Dictionary<string, SidebarButton> buttons = new Dictionary<string, SidebarButton>();
        private readonly Dictionary<string, double> aiCache = new Dictionary<string, double>();
        private readonly System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer hotTimer = new System.Windows.Forms.Timer();

        private Panel sidebar, content, host, header;
        private Label titleLabel, bellLabel, clockLabel;
        private TextBox search;
        private Section current;
        private bool suppressSearch;

        private Chord chord;
        private KeyboardHook hook;
        private Guard guard;
        private AiGuard ai;
        private DockForm dock;
        private AdminConsoleForm console;
        private Native.RECT originalWorkArea;
        private bool workAreaChanged;
        private bool desktopMode;

        public MainForm(Config cfg)
        {
            Cfg = cfg;
            User = UserData.Load();
            Theme.Apply(User.Theme != "light");

            Text = "SchoolShield";
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = false;

            if (State.Preview)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1280, 800);
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ControlBox = false;
                MinimizeBox = false;
                MaximizeBox = false;
                Bounds = Screen.PrimaryScreen.Bounds;
            }

            BuildUi("home");
        }

        // ------------------------------------------------------------------ UI construction

        private void BuildUi(string openSection)
        {
            SuspendLayout();
            foreach (Control c in new List<Control>(ControlsOf(this))) { Controls.Remove(c); c.Dispose(); }
            sections.Clear();
            order.Clear();
            buttons.Clear();
            current = null;

            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Padding = new Padding(0, State.Preview ? 0 : DockForm.BarHeight, 0, 0);

            // sections (only those the administrator enabled)
            AddSection(new HomeSection(this));
            AddSection(new AppsSection(this));
            AddSection(new DocsSection(this));
            AddSection(new MediaSection(this));
            AddSection(new FilesSection(this));
            AddSection(new WebSection(this));
            AddSection(new SystemSection(this));

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Background;

            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Background;

            header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 92;
            header.BackColor = Theme.Background;

            titleLabel = new Label();
            titleLabel.Font = Theme.UiFont(26, FontStyle.Bold);
            titleLabel.ForeColor = Theme.Text;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(32, 16);

            bellLabel = new Label();
            bellLabel.Font = Theme.UiFont(11);
            bellLabel.ForeColor = Theme.Muted;
            bellLabel.AutoSize = true;
            bellLabel.Location = new Point(36, 62);

            clockLabel = new Label();
            clockLabel.Font = Theme.UiFont(24, FontStyle.Bold);
            clockLabel.ForeColor = Theme.Accent;
            clockLabel.AutoSize = true;
            clockLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            search = new TextBox();
            search.Font = Theme.UiFont(13);
            search.BackColor = Theme.Card;
            search.ForeColor = Theme.Text;
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Width = 300;
            search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            search.TextChanged += delegate { OnSearchChanged(); };

            header.Controls.Add(titleLabel);
            header.Controls.Add(bellLabel);
            header.Controls.Add(clockLabel);
            header.Controls.Add(search);
            header.Resize += delegate { PlaceHeader(); };

            content.Controls.Add(host);
            content.Controls.Add(header);

            sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 236;
            sidebar.BackColor = Theme.Sidebar;

            // Dock.Top stacks in reverse order of adding, so add the last button first.
            for (int i = order.Count - 1; i >= 0; i--)
            {
                Section s = sections[order[i]];
                SidebarButton b = new SidebarButton(s.Glyph, s.Title);
                string id = s.Id;
                b.MouseUp += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSection(id); };
                buttons[id] = b;
                sidebar.Controls.Add(b);
            }
            Label brand = new Label();
            brand.Text = Cfg.Title;
            brand.Dock = DockStyle.Top;
            brand.Height = 76;
            brand.TextAlign = ContentAlignment.MiddleLeft;
            brand.Padding = new Padding(24, 8, 8, 0);
            brand.Font = Theme.UiFont(13, FontStyle.Bold);
            brand.ForeColor = Theme.Text;
            brand.BackColor = Theme.Sidebar;
            sidebar.Controls.Add(brand);

            Controls.Add(content);
            Controls.Add(sidebar);
            ResumeLayout();

            PlaceHeader();
            UiKit.SetPlaceholder(search, "Поиск…");
            ShowSection(sections.ContainsKey(openSection) ? openSection : order[0]);
        }

        private static IEnumerable<Control> ControlsOf(Control c)
        {
            foreach (Control x in c.Controls) yield return x;
        }

        private void AddSection(Section s)
        {
            if (!Cfg.SectionEnabled(s.Id)) { s.Dispose(); return; }
            sections[s.Id] = s;
            order.Add(s.Id);
        }

        private void PlaceHeader()
        {
            if (header == null) return;
            clockLabel.Location = new Point(header.Width - clockLabel.Width - 32, 14);
            search.Location = new Point(header.Width - search.Width - 32, 56);
            bellLabel.MaximumSize = new Size(Math.Max(200, header.Width - 400), 0);
        }

        public void ShowSection(string id)
        {
            Section s;
            if (!sections.TryGetValue(id, out s)) return;
            host.Controls.Clear();
            host.Controls.Add(s);
            current = s;
            foreach (KeyValuePair<string, SidebarButton> kv in buttons)
            {
                kv.Value.Selected = kv.Key == id;
                kv.Value.Invalidate();
            }
            titleLabel.Text = s.Title;
            suppressSearch = true;
            search.Text = "";
            suppressSearch = false;
            UpdateHeader();
            s.OnShown();
        }

        private void OnSearchChanged()
        {
            if (suppressSearch || current == null) return;
            string q = search.Text.Trim();
            // On pages without a list, searching jumps to the program list.
            if (q.Length > 0 && (current.Id == "home" || current.Id == "system" || current.Id == "web") && sections.ContainsKey("apps"))
            {
                ShowSection("apps");
                suppressSearch = true;
                search.Text = q;
                search.SelectionStart = q.Length;
                suppressSearch = false;
            }
            current.OnSearch(q);
        }

        private void UpdateHeader()
        {
            clockLabel.Text = DateTime.Now.ToString("HH:mm");
            string bell = Bells.Status(Cfg, DateTime.Now);
            bellLabel.Text = bell ?? Cfg.Subtitle;
            PlaceHeader();
        }

        public void ToggleTheme()
        {
            User.Theme = Theme.Dark ? "light" : "dark";
            User.Save();
            Theme.Apply(!Theme.Dark);
            string id = current == null ? "home" : current.Id;
            BuildUi(id);
            ReloadCatalog();
            if (dock != null) dock.ApplyTheme();
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Ui.Sync = this;

            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate { UpdateHeader(); };
            clockTimer.Start();

            if (!State.Preview) ReserveDockArea();

            dock = new DockForm(this);
            dock.Show();

            hook = new KeyboardHook();
            hook.Install();   // in preview it blocks nothing

            // The admin chord must be HELD: an accidental press can never open the terminal.
            chord = new Chord(Cfg.AdminHotkey);
            if (!chord.Valid) chord = new Chord("Ctrl+Shift+Alt+F12");
            hotTimer.Interval = 100;
            hotTimer.Tick += delegate
            {
                if (chord.Poll(Math.Max(500, Cfg.AdminHotkeyHoldMs))) ShowAdminConsole();
            };
            hotTimer.Start();

            guard = new Guard(Cfg);
            guard.Alert += delegate (string m) { Ui.Toast(m); };
            guard.Start();

            ai = new AiGuard(Cfg);
            ai.Alert += delegate (string m) { Ui.Toast(m); };
            ai.OwnWindowHit += delegate { try { BeginInvoke(new Action(delegate { ShowSection("home"); })); } catch { } };
            ai.Start();

            StartInputService();
            ReloadCatalog();
            Activate();
        }

        /// <summary>Without Explorer nobody starts ctfmon, and then Alt+Shift / Ctrl+Shift cannot switch the keyboard layout.</summary>
        private void StartInputService()
        {
            try
            {
                int me = Process.GetCurrentProcess().SessionId;
                foreach (Process p in Process.GetProcessesByName("ctfmon"))
                {
                    try { if (p.SessionId == me) return; } finally { p.Dispose(); }
                }
                Process.Start("ctfmon.exe");
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!State.ExitRequested && !State.Preview)
            {
                e.Cancel = true;   // the shell must never go away
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                hotTimer.Stop();
                clockTimer.Stop();
                if (hook != null) hook.Dispose();
                if (guard != null) guard.Dispose();
                if (ai != null) ai.Dispose();
                if (dock != null) dock.Dispose();
                RestoreWorkArea();
            }
            catch { }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SYSCOMMAND && !State.Preview && !State.AdminMode)
            {
                int cmd = m.WParam.ToInt32() & 0xFFF0;
                if (cmd == Native.SC_MINIMIZE || cmd == Native.SC_CLOSE) return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ work area (room for the dock)

        private void ReserveDockArea()
        {
            try
            {
                Native.RECT cur = new Native.RECT();
                if (!Native.SystemParametersInfo(Native.SPI_GETWORKAREA, 0, ref cur, 0)) return;
                originalWorkArea = cur;
                Native.RECT wa = cur;
                wa.Top = Screen.PrimaryScreen.Bounds.Top + DockForm.BarHeight;
                workAreaChanged = Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref wa, 0);
            }
            catch { }
        }

        private void RestoreWorkArea()
        {
            if (!workAreaChanged) return;
            try
            {
                Native.RECT wa = originalWorkArea;
                Native.SystemParametersInfo(Native.SPI_SETWORKAREA, 0, ref wa, 0);
                workAreaChanged = false;
            }
            catch { }
        }

        // ------------------------------------------------------------------ programs

        public void ReloadCatalog()
        {
            Thread t = new Thread(CatalogThread);
            t.SetApartmentState(ApartmentState.STA);   // Shell.Application is an STA COM object
            t.IsBackground = true;
            t.Start();
        }

        private void CatalogThread()
        {
            List<AppEntry> list;
            try { list = AppCatalog.Load(Cfg); }
            catch (Exception ex)
            {
                Log.Write("catalog", ex.Message);
                list = new List<AppEntry>();
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    Apps = list;
                    foreach (Section s in sections.Values) s.OnAppsChanged();
                }));
            }
            catch { return; }

            int n = 0;
            foreach (AppEntry entry in list)
            {
                Bitmap bmp = AppCatalog.LoadIcon(entry, 64);
                if (bmp == null) continue;
                AppEntry e2 = entry;
                bool flush = (++n % 6) == 0;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        e2.Icon = bmp;
                        if (flush) InvalidateTiles(host);
                    }));
                }
                catch { return; }
            }
            try { BeginInvoke(new Action(delegate { InvalidateTiles(host); })); } catch { }
        }

        private static void InvalidateTiles(Control root)
        {
            if (root == null) return;
            foreach (Control c in root.Controls)
            {
                if (c is TileControl) c.Invalidate();
                else if (c.HasChildren) InvalidateTiles(c);
            }
        }

        public AppEntry FindApp(string name)
        {
            foreach (AppEntry a in Apps)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        public string CategoryName(string id)
        {
            foreach (CategoryRule r in Cfg.CategoryRules) if (r.Id == id) return r.Name;
            return "Другое";
        }

        public List<string> AppNames()
        {
            List<string> names = new List<string>();
            foreach (AppEntry a in Apps) names.Add(a.Name);
            return names;
        }

        public TileControl MakeAppTile(AppEntry e)
        {
            TileControl t = new TileControl(e);
            t.Star = User.IsFavorite(e.Name);
            t.Activated += delegate { LaunchApp(e); };
            t.ContextRequested += delegate { Menus.ForApp(this, e, t); };
            return t;
        }

        public void LaunchApp(AppEntry entry)
        {
            try
            {
                AppCatalog.Launch(entry);
                User.AddRecentApp(entry.Name);
            }
            catch (Exception) { Ui.Toast("Не удалось запустить: " + entry.Name); }
        }

        public void ToggleFavorite(AppEntry e)
        {
            User.ToggleFavorite(e.Name);
            foreach (Section s in sections.Values) s.OnAppsChanged();
        }

        // ------------------------------------------------------------------ files

        public bool IsPictureBlocked(string path, NsfwEngine engine, Bitmap bmpOrNull)
        {
            if (State.AdminMode || engine == null || !Cfg.Ai.Enabled) return false;
            string key = path;
            try { key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; } catch { }
            double score;
            lock (aiCache)
            {
                if (aiCache.TryGetValue(key, out score)) return score >= Cfg.Ai.Threshold;
            }
            try { score = bmpOrNull != null ? engine.Score(bmpOrNull) : engine.ScoreFile(path); }
            catch { return false; }
            lock (aiCache) aiCache[key] = score;
            bool blocked = score >= Cfg.Ai.Threshold;
            if (blocked) Log.Write("ai", "Картинка скрыта (" + score.ToString("0.00") + "): " + path);
            return blocked;
        }

        public void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path)) { Ui.Toast("Файл не найден."); return; }
                string ext = Path.GetExtension(path);
                if (Cfg.ForbiddenExtensions.Exists(delegate (string x) { return string.Equals(x, ext, StringComparison.OrdinalIgnoreCase); }))
                {
                    Ui.Toast("Файлы этого типа запрещены.");
                    return;
                }
                if (!State.AdminMode && FileRoots.RootOf(FileRoots.Resolve(Cfg), path) == null)
                {
                    Ui.Toast("Этот файл вне разрешённых папок.");
                    return;
                }
                if (FileKinds.Of(path) == FileKind.Image && Cfg.Ai.Enabled)
                {
                    NsfwEngine engine = NsfwEngine.TryGet();
                    if (engine != null && IsPictureBlocked(path, engine, null))
                    {
                        Ui.Toast("Изображение скрыто фильтром.");
                        return;
                    }
                }
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
                User.AddRecentFile(path);
                Log.Write("open", path);
            }
            catch (Exception ex)
            {
                Log.Write("open", "Ошибка открытия " + path + ": " + ex.Message);
                Ui.Toast("Не удалось открыть файл.");
            }
        }

        public void ShowInFiles(string path)
        {
            if (!sections.ContainsKey("files")) return;
            ShowSection("files");
            ((FilesSection)sections["files"]).Reveal(path);
        }

        public void OpenUrl(string url)
        {
            try
            {
                Uri u;
                if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return;
                ProcessStartInfo psi = new ProcessStartInfo(u.AbsoluteUri);
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log.Write("web", u.Host);
            }
            catch { Ui.Toast("Не удалось открыть сайт."); }
        }

        // ------------------------------------------------------------------ power

        public void Power(bool restart)
        {
            string what = restart ? "перезагрузить" : "выключить";
            if (MessageBox.Show(this, "Точно " + what + " компьютер?\nНе забудьте сохранить работу.", "Питание",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (State.Preview) { Ui.Toast("[предпросмотр] действие пропущено"); return; }
            Log.Write("power", restart ? "restart" : "shutdown");
            try { Process.Start("shutdown.exe", (restart ? "/r" : "/s") + " /t 3"); } catch { }
        }

        // ------------------------------------------------------------------ admin

        public int Strikes { get { return guard == null ? 0 : guard.Strikes; } }

        public void ShowAdminConsole()
        {
            try
            {
                if (console != null && !console.IsDisposed)
                {
                    console.BringUp();
                    return;
                }
                console = new AdminConsoleForm(Cfg, this);
                console.BringUp();
            }
            catch (Exception ex) { Log.Write("admin", "Не удалось открыть терминал: " + ex.Message); }
        }

        /// <summary>Admin only: remove a program from Windows completely.</summary>
        public void UninstallApp(AppEntry app)
        {
            if (!State.AdminMode) { Ui.Toast("Удаление доступно только администратору."); return; }
            try
            {
                if (Programs.IsStoreApp(app))
                {
                    if (MessageBox.Show(this, "Удалить приложение «" + app.Name + "» из Windows для всех пользователей?",
                        "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    Process p = Programs.UninstallStoreApp(app);
                    WatchThenReload(p);
                    return;
                }
                ShowUninstallDialog(app.Name);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) Ui.Toast("Не удалось запустить удаление.");
            }
            catch (Exception ex) { Ui.Toast("Не удалось: " + ex.Message); }
        }

        public void ShowUninstallDialog(string preselect)
        {
            if (!State.AdminMode) { Ui.Toast("Удаление доступно только администратору."); return; }
            UninstallDialog d = new UninstallDialog(preselect, delegate { try { BeginInvoke(new Action(ReloadCatalog)); } catch { } });
            d.Show();
        }

        private void WatchThenReload(Process p)
        {
            if (p == null) return;
            Thread t = new Thread(delegate ()
            {
                try { p.WaitForExit(); } catch { }
                try { BeginInvoke(new Action(ReloadCatalog)); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void EmptyRecycleBin()
        {
            if (!State.AdminMode) { Ui.Toast("Очистка корзины доступна только администратору."); return; }
            if (MessageBox.Show(this, "Очистить корзину навсегда?", "Корзина", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Ui.Toast(RecycleBin.EmptyCurrentUser() ? "Корзина очищена." : "Корзина уже пуста или недоступна.");
        }

        /// <summary>Admin asked for the real Windows desktop: hide the launcher, start explorer.exe.</summary>
        public void EnterDesktopMode()
        {
            desktopMode = true;
            State.EnableAdmin(Cfg.AdminModeMinutes);
            RestoreWorkArea();
            if (dock != null) dock.Hide();
            Hide();
            try { Process.Start("explorer.exe"); }
            catch (Exception ex) { Log.Write("admin", "explorer.exe: " + ex.Message); }
        }

        /// <summary>Back to student mode: stop explorer, show the launcher again.</summary>
        public void LeaveAdminMode()
        {
            State.DisableAdmin();
            if (desktopMode)
            {
                desktopMode = false;
                int me = Process.GetCurrentProcess().SessionId;
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    try { if (p.SessionId == me) p.Kill(); } catch { }
                    finally { p.Dispose(); }
                }
                Show();
                if (!State.Preview) ReserveDockArea();
                if (dock != null) dock.Show();
            }
            ShowMenu();
        }

        public void ShowMenu()
        {
            if (desktopMode) return;
            Activate();
            BringToFront();
            Native.SetForegroundWindow(Handle);
        }

        public void ExitSession()
        {
            State.ExitRequested = true;
            if (State.Preview) { Close(); return; }
            Native.ExitWindowsEx(Native.EWX_LOGOFF, 0);
        }
    }
}
