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

        private Panel sidebar, content, host;
        private HeaderBar header;
        private SearchBox search;
        private readonly System.Windows.Forms.Timer wifiTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer configTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer driveTimer = new System.Windows.Forms.Timer();
        private DateTime configStamp;
        private string driveLetters;                  // null until the first look: drives that were there at the start do not make a toast
        private readonly ToolTip sidebarTips = new ToolTip();
        private Section current;
        private bool suppressSearch;

        private readonly List<Chord> chords = new List<Chord>();
        private HoldOverlay holdOverlay;
        private readonly List<DateTime> clockClicks = new List<DateTime>();
        private KeyboardHook hook;
        private Guard guard;
        private AiGuard ai;
        private FileDialogProxy dialogProxy;
        private DockForm dock;
        private AdminConsoleForm console;
        private Native.RECT originalWorkArea;
        private bool workAreaChanged;
        private bool desktopMode;

        public MainForm(Config cfg)
        {
            Cfg = cfg;
            Metrics.Init();
            Motion.Configure(cfg.Animations);
            Ui.Sarcastic = cfg.SarcasticMessages;
            User = UserData.Load();
            Theme.Apply(User.Theme != "light");

            Text = "SchoolShield";
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = false;

            if (State.Windowed)
            {
                // Teacher edition: an ordinary resizable window with a taskbar button, nothing is locked.
                FormBorderStyle = FormBorderStyle.Sizable;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = true;
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Size = new Size(Math.Min(1500, (int)(wa.Width * 0.92)), Math.Min(940, (int)(wa.Height * 0.92)));
                MinimumSize = new Size(Math.Min(900, wa.Width), Math.Min(600, wa.Height));
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            else if (State.Preview)
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
            Padding = new Padding(0, State.Soft ? 0 : DockForm.BarHeight, 0, 0);

            // sections (only those the administrator enabled)
            AddSection(new HomeSection(this));
            AddSection(new AppsSection(this));
            AddSection(new DocsSection(this));
            AddSection(new MediaSection(this));
            AddSection(new FilesSection(this));
            AddSection(new UsbSection(this));
            AddSection(new WebSection(this));
            AddSection(new SystemSection(this));

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Background;

            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Background;

            search = new SearchBox("Поиск программ и файлов…");
            search.QueryChanged += delegate { OnSearchChanged(); };

            header = new HeaderBar(search);
            header.BackColor = Theme.Header;
            header.ClockClicked += delegate { OnClockClick(); };

            content.Controls.Add(host);
            content.Controls.Add(header);

            sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = Metrics.SidebarWidth;
            sidebar.BackColor = Theme.Sidebar;

            // Dock.Top stacks in reverse order of adding, so add the last button first.
            for (int i = order.Count - 1; i >= 0; i--)
            {
                Section s = sections[order[i]];
                SidebarButton b = new SidebarButton(s.Glyph, s.Title);
                string id = s.Id;
                b.MouseUp += delegate (object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSection(id); };
                sidebarTips.SetToolTip(b, s.Title);
                buttons[id] = b;
                sidebar.Controls.Add(b);
            }
            BrandControl brand = new BrandControl();
            brand.Caption = Cfg.Title;
            sidebar.Controls.Add(brand);

            Controls.Add(content);
            Controls.Add(sidebar);
            ResumeLayout();

            PlaceHeader();
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

        /// <summary>Positions the header from its real width: nothing can overlap on a small screen.</summary>
        private void PlaceHeader()
        {
            if (header == null || header.Width <= 0) return;
            header.Place();
        }

        public void ShowSection(string id)
        {
            Section s;
            if (!sections.TryGetValue(id, out s)) return;
            if (current == s) { s.OnShown(); return; }
            bool animate = current != null && IsHandleCreated && Visible && !(s is WebSection) && !(current is WebSection);
            Action swap = delegate
            {
                host.Controls.Clear();
                host.Controls.Add(s);
                current = s;
                foreach (KeyValuePair<string, SidebarButton> kv in buttons) kv.Value.Selected = kv.Key == id;
                header.Title = s.Title;
                suppressSearch = true;
                search.ClearQuiet();
                suppressSearch = false;
                UpdateHeader();
                s.OnShown();
                PlayEnter(s);
            };
            if (animate) Fx.Crossfade(host, swap, 240); else swap();
        }

        /// <summary>The tiles of the new page pop in one after another.</summary>
        private static void PlayEnter(Control page) { Fx.PlayEnter(page); }

        private void OnSearchChanged()
        {
            if (suppressSearch || current == null) return;
            string q = search.Query;
            // On pages without a list, searching jumps to the program list.
            if (q.Length > 0 && (current.Id == "home" || current.Id == "system" || current.Id == "web") && sections.ContainsKey("apps"))
            {
                ShowSection("apps");
                suppressSearch = true;
                search.SetQuery(q);
                suppressSearch = false;
            }
            current.OnSearch(q);
        }

        private void UpdateHeader()
        {
            header.ClockText = DateTime.Now.ToString("HH:mm");
            string bell = Bells.Status(Cfg, DateTime.Now);
            header.Subtitle = bell ?? Cfg.Subtitle;
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
            StartDriveWatch();

            if (State.Windowed)
            {
                // Teacher edition: no dock bar, no keyboard hook, no admin chord; the 18+ guard runs in its own background process.
                ReloadCatalog();
                Activate();
                return;
            }

            if (!State.Preview) ReserveDockArea();

            dock = new DockForm(this);
            dock.Show();

            hook = new KeyboardHook();
            hook.Install();   // in preview it blocks nothing

            // The admin chord must be HELD: an accidental press can never open the terminal. Besides the configured chord two
            // built-in ones always work (Fn keys of some laptops hide F12), so an administrator cannot be locked out.
            AddChord(Cfg.AdminHotkey);
            AddChord("Ctrl+Shift+Alt+A");
            AddChord("Ctrl+Shift+Alt+F12");
            holdOverlay = new HoldOverlay();
            hotTimer.Interval = 100;
            hotTimer.Tick += delegate { PollAdminChords(); };
            hotTimer.Start();

            guard = new Guard(Cfg);
            guard.Alert += delegate (ViolationKind k, string detail) { Ui.Violation(k, detail); };
            guard.Start();

            ai = new AiGuard(Cfg);
            ai.Alert += delegate (string m) { Ui.Violation(ViolationKind.Adult, ""); };
            ai.OwnWindowHit += delegate { try { BeginInvoke(new Action(delegate { BlankBrowser(); ShowSection("home"); })); } catch { } };
            ai.Start();

            dialogProxy = new FileDialogProxy(this);      // the launcher's own "Open" / "Save as" windows for a student
            dialogProxy.Start();

            StartInputService();
            wifiTimer.Interval = 15000;
            wifiTimer.Tick += delegate
            {
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { Wifi.Enforce(Cfg); } catch { } });
            };
            wifiTimer.Start();

            // The administrator may flip the "Games" switch in the panel (another process): pick it up without a restart.
            configStamp = Config.Stamp();
            configTimer.Interval = 4000;
            configTimer.Tick += delegate { CheckConfigChange(); };
            configTimer.Start();

            ReloadCatalog();
            Activate();
            if (Cfg.Intro && !State.Windowed) IntroForm.Play(Cfg.Title, Cfg.Subtitle);
        }

        /// <summary>A toast when a flash drive is plugged in or taken out (the "Flash drives" section also refreshes itself).</summary>
        private void StartDriveWatch()
        {
            driveTimer.Interval = 2500;
            driveTimer.Tick += delegate { CheckDrives(); };
            driveTimer.Start();
        }

        private void CheckDrives()
        {
            try
            {
                string mode = (Cfg.UsbMode ?? "readonly").ToLowerInvariant();
                if (!Cfg.SectionEnabled("usb") || (mode == "block" && !State.Privileged)) { driveLetters = ""; return; }
                List<DriveEntry> now = Drives.List();
                List<string> letters = new List<string>();
                foreach (DriveEntry d in now) letters.Add(d.Letter);
                string joined = string.Join(",", letters.ToArray());
                string before = driveLetters;
                driveLetters = joined;
                if (before == null || before == joined) return;
                foreach (DriveEntry d in now)
                    if (("," + before + ",").IndexOf("," + d.Letter + ",", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Log.Write("usb", "Подключено: " + d.Title);
                        Ui.Toast("Подключена: " + d.Title + ". Откройте раздел «Флешки и диски».");
                    }
            }
            catch { }
        }

        private void CheckConfigChange()
        {
            try
            {
                DateTime stamp = Config.Stamp();
                if (stamp == configStamp) return;
                configStamp = stamp;
                bool before = State.GamesAllowed;
                Config fresh = Config.Load();            // also publishes State.GamesAllowed and State.GameExceptions
                Cfg.AllowGames = fresh.AllowGames;
                Cfg.GameExceptions = fresh.GameExceptions;
                Cfg.UsbMode = fresh.UsbMode;
                Cfg.OwnFileDialogs = fresh.OwnFileDialogs;
                Cfg.Animations = fresh.Animations;
                Cfg.SarcasticMessages = fresh.SarcasticMessages;
                Motion.Configure(Cfg.Animations);
                Ui.Sarcastic = Cfg.SarcasticMessages;
                Cfg.AllowedExtensions = fresh.AllowedExtensions;
                if (!State.Soft) UserPolicy.ApplyUsb(Cfg);        // the flash-drive mode takes effect at once
                ReloadCatalog();                          // also after the administrator installed a program from the catalog
                if (before == State.GamesAllowed) return;
                Log.Write("games", State.GamesAllowed ? "Игры разрешены администратором" : "Игры запрещены администратором");
                Ui.Toast(State.GamesAllowed ? "Администратор разрешил игры." : "Игры снова запрещены.");
            }
            catch { }
        }

        private void AddChord(string spec)
        {
            Chord c = new Chord(spec);
            if (!c.Valid) return;
            foreach (Chord x in chords) if (string.Equals(x.Spec, c.Spec, StringComparison.OrdinalIgnoreCase)) return;
            chords.Add(c);
        }

        private bool chordLogged;

        private void PollAdminChords()
        {
            int need = Math.Max(500, Cfg.AdminHotkeyHoldMs);
            int best = 0;
            bool fire = false;
            foreach (Chord c in chords)
            {
                if (c.Poll(need)) fire = true;
                if (c.HeldMs > best) best = c.HeldMs;
            }
            if (best >= 200 && !chordLogged) { chordLogged = true; Log.Write("hotkey", "Админ-комбинация нажата"); }
            if (best == 0) chordLogged = false;
            if (best >= 250 && !fire) holdOverlay.SetProgress(Math.Min(1.0, best / (double)need));
            else holdOverlay.HideNow();
            if (fire)
            {
                Log.Write("hotkey", "Админ-комбинация удержана: открываю терминал");
                ShowAdminConsole();
            }
        }

        /// <summary>7 quick clicks on the clock: an alternative to the keyboard chord.</summary>
        private void OnClockClick()
        {
            if (!Cfg.AdminClickGesture) return;
            DateTime now = DateTime.Now;
            clockClicks.Add(now);
            clockClicks.RemoveAll(delegate (DateTime t) { return (now - t).TotalSeconds > 6; });
            if (clockClicks.Count >= 7)
            {
                clockClicks.Clear();
                Log.Write("hotkey", "Админ-жест: 7 нажатий на часы");
                ShowAdminConsole();
            }
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
            if (!State.ExitRequested && !State.Soft)
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
                wifiTimer.Stop();
                configTimer.Stop();
                driveTimer.Stop();
                if (holdOverlay != null) holdOverlay.Dispose();
                clockTimer.Stop();
                if (hook != null) hook.Dispose();
                if (guard != null) guard.Dispose();
                if (ai != null) ai.Dispose();
                if (dialogProxy != null) dialogProxy.Dispose();
                if (dock != null) dock.Dispose();
                RestoreWorkArea();
            }
            catch { }
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_SYSCOMMAND && !State.Soft && !State.AdminMode)
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
                    Ui.Violation(ViolationKind.File, Path.GetFileName(path));
                    return;
                }
                if (!State.Privileged && FileRoots.RootOf(FileRoots.ResolveAll(Cfg), path) == null)
                {
                    Ui.Violation(ViolationKind.Folder, Path.GetFileName(path));
                    return;
                }
                if (!FileFilter.Allowed(path, Cfg) && !State.Privileged)
                {
                    Ui.Violation(ViolationKind.File, Path.GetFileName(path));
                    return;
                }
                if (FileKinds.Of(path) == FileKind.Image && Cfg.Ai.Enabled)
                {
                    NsfwEngine engine = NsfwEngine.TryGet();
                    if (engine != null && IsPictureBlocked(path, engine, null))
                    {
                        Ui.Violation(ViolationKind.Adult, Path.GetFileName(path));
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
                Section web;
                if (sections.TryGetValue("web", out web) && ((WebSection)web).HasBuiltInBrowser)
                {
                    ShowSection("web");
                    ((WebSection)web).Navigate(u.AbsoluteUri);   // goes through the school's site lists
                    Log.Write("web", u.Host);
                    return;
                }
                ProcessStartInfo psi = new ProcessStartInfo(u.AbsoluteUri);
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log.Write("web", u.Host);
            }
            catch { Ui.Toast("Не удалось открыть сайт."); }
        }

        /// <summary>The screen filter found something unacceptable in our own window: leave the page.</summary>
        public void BlankBrowser()
        {
            Section web;
            if (sections.TryGetValue("web", out web)) ((WebSection)web).Blank();
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
            if (!State.Privileged) { Ui.Toast("Удаление доступно только администратору."); return; }
            try
            {
                if (Programs.IsStoreApp(app))
                {
                    if (MessageBox.Show(this, "Удалить приложение «" + app.Name + "» из Windows для всех пользователей?",
                        "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    ElevationPrompt prompt = ElevationPrompt.BeginIfNeeded(delegate { Ui.Toast(ElevationPrompt.NotShownHelp); });
                    Programs.UninstallStoreAppAsync(app,
                        delegate (Process p) { BeginInvoke(new Action(delegate { ElevationPrompt.End(prompt); })); WatchThenReload(p); },
                        delegate (string message) { BeginInvoke(new Action(delegate { ElevationPrompt.End(prompt); Ui.Toast(message); })); });
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
            if (!State.Privileged) { Ui.Toast("Удаление доступно только администратору."); return; }
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
            if (!State.Privileged) { Ui.Toast("Очистка корзины доступна только администратору."); return; }
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
            if (!State.Preview) UserPolicy.DialogLock(false);   // the administrator needs drives and menus on the real desktop
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
                if (!State.Preview) UserPolicy.DialogLock(true);
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
            if (State.Soft) { Close(); return; }
            Native.ExitWindowsEx(Native.EWX_LOGOFF, 0);
        }
    }
}
