using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace SchoolShield
{
    /// <summary>
    /// Background watchdog for the student session:
    ///  - closes windows whose title matches badwords.txt (porn etc.);
    ///  - makes minimizing impossible (restores minimized windows, removes the minimize button);
    ///  - kills forbidden programs (cmd, regedit, Task Manager, installers...) and anything running
    ///    from the user profile / Temp / USB (works even where Software Restriction Policies do not, e.g. Windows Home);
    ///  - deletes downloaded installers/archives from user folders.
    /// In preview mode it only writes to the log. Everything pauses while State.AdminMode is on.
    /// </summary>
    internal sealed class Guard : IDisposable
    {
        internal static readonly string[] Untouchable = new string[] {
            "explorer", "winlogon", "csrss", "dwm", "svchost", "lsass", "services", "smss", "system", "wininit",
            "applicationframehost", "runtimebroker", "sihost", "taskhostw", "fontdrvhost", "ctfmon", "searchapp",
            "shellexperiencehost", "startmenuexperiencehost", "textinputhost", "conhost", "schoolshield"
        };

        public static readonly string[] ExternalBrowsers = new string[] {
            "chrome", "msedge", "firefox", "browser", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "chromium",
            "maxthon", "waterfox", "palemoon", "librewolf", "tor", "torbrowser", "amigo", "sputnik", "slimjet"
        };

        private static readonly string[] TempExecutableExt = new string[] {
            ".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".ps1", ".vbs", ".scr", ".com", ".jar", ".apk"
        };

        private readonly Config cfg;
        private readonly List<Regex> bad;
        private readonly HashSet<string> blocked;
        private readonly HashSet<string> forbiddenExt;
        private readonly string[] denyRoots;
        private readonly string tempDir;
        private readonly Thread thread;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Dictionary<IntPtr, DateTime> closing = new Dictionary<IntPtr, DateTime>();
        private readonly HashSet<IntPtr> stripped = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> styledDialogs = new HashSet<IntPtr>();
        private readonly Dictionary<int, bool> pathOk = new Dictionary<int, bool>();
        private readonly int sessionId;
        private readonly uint ownPid;
        private volatile bool stop;
        private int tick;

        public int Strikes;
        public event Action<string> Alert;

        public Guard(Config cfg)
        {
            this.cfg = cfg;
            bad = Config.LoadBadWords();
            blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in cfg.BlockedProcesses) blocked.Add(s.Trim().ToLowerInvariant().Replace(".exe", ""));
            foreach (string s in Config.BuiltInBlockedProcesses) blocked.Add(s);
            if (cfg.Browser.BuiltIn && cfg.Browser.BuiltInOnly && WebView2Probe.Available)
            {
                // Ad blur and the site lists live in the built-in browser, so no other browser may run for a student.
                foreach (string b in ExternalBrowsers) blocked.Add(b);
                Log.Write("guard", "Внешние браузеры закрываются: работает только встроенный");
            }
            forbiddenExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in cfg.ForbiddenExtensions) forbiddenExt.Add(s.StartsWith(".") ? s : "." + s);

            Process me = Process.GetCurrentProcess();
            sessionId = me.SessionId;
            ownPid = (uint)me.Id;

            tempDir = Path.GetTempPath();
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            roots.Add(tempDir);
            string pub = Environment.GetEnvironmentVariable("PUBLIC");
            if (!string.IsNullOrEmpty(pub)) roots.Add(pub);
            denyRoots = roots.ToArray();

            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "SchoolShield.Guard";
        }

        public void Start()
        {
            Log.Write("guard", "Запуск. Плохих шаблонов: " + bad.Count + ", запрещённых процессов: " + blocked.Count
                + (State.Preview ? " [PREVIEW: только лог]" : ""));
            StartFileWatchers();
            thread.Start();
        }

        public void Dispose()
        {
            stop = true;
            foreach (FileSystemWatcher w in watchers)
            {
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
            }
        }

        private void Raise(string message)
        {
            Action<string> h = Alert;
            if (h != null) h(message);
        }

        // ---------------------------------------------------------------- main loop

        private void Loop()
        {
            while (!stop)
            {
                try
                {
                    if (!State.AdminMode) Tick();
                    else { closing.Clear(); }
                }
                catch (Exception ex) { Log.Write("guard", "Ошибка цикла: " + ex.Message); }
                Thread.Sleep(400);
            }
        }

        private void Tick()
        {
            List<WinInfo> wins = WindowList.Enumerate();
            HashSet<IntPtr> present = new HashSet<IntPtr>();

            foreach (WinInfo w in wins)
            {
                present.Add(w.Handle);
                if (w.Pid == ownPid) continue;

                if (cfg.CloseBadWindows && MatchesBad(w.Title))
                {
                    HandleBadWindow(w);
                    continue;
                }

                if (cfg.BlockMinimize && !State.Preview)
                {
                    if (w.Iconic)
                    {
                        Native.ShowWindow(w.Handle, Native.SW_RESTORE);
                    }
                    else if ((w.Style & Native.WS_MINIMIZEBOX) != 0 && !stripped.Contains(w.Handle))
                    {
                        stripped.Add(w.Handle);
                        Native.SetWindowLong(w.Handle, Native.GWL_STYLE, w.Style & ~Native.WS_MINIMIZEBOX);
                        Native.SetWindowPos(w.Handle, IntPtr.Zero, 0, 0, 0, 0,
                            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                    }
                }
            }

            // forget handles of windows that are gone
            List<IntPtr> gone = new List<IntPtr>();
            foreach (IntPtr h in closing.Keys) if (!present.Contains(h)) gone.Add(h);
            foreach (IntPtr h in gone) closing.Remove(h);
            stripped.RemoveWhere(delegate(IntPtr h) { return !present.Contains(h); });

            if ((tick++ % 3) == 0) CheckProcesses();
            StyleDialogs();
        }

        // ---------------------------------------------------------------- "Save as" / "Open" windows

        /// <summary>
        /// Common file dialogs are owned windows, so the task list never shows them. Remove their "?" help button (it opens
        /// a browser) and give them a dark title bar that matches the launcher.
        /// </summary>
        private void StyleDialogs()
        {
            if (State.Preview) return;
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    if (!Native.IsWindowVisible(h) || styledDialogs.Contains(h)) return true;
                    if (Native.GetWindowClass(h) != "#32770") return true;
                    if (Native.GetWindowPid(h) == ownPid) return true;
                    styledDialogs.Add(h);
                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    if ((ex & 0x400 /* WS_EX_CONTEXTHELP */) != 0)
                    {
                        Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex & ~0x400);
                        Native.SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
                    }
                    int on = 1;
                    Native.DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, 4);
                    Native.DwmSetWindowAttribute(h, 19, ref on, 4);   // the same flag on Windows 10 1809
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            if (styledDialogs.Count > 400) styledDialogs.Clear();
        }

        // ---------------------------------------------------------------- bad windows

        private bool MatchesBad(string title)
        {
            foreach (Regex r in bad)
                if (r.IsMatch(title)) return true;
            return false;
        }

        private void HandleBadWindow(WinInfo w)
        {
            DateTime first;
            if (!closing.TryGetValue(w.Handle, out first))
            {
                closing[w.Handle] = DateTime.Now;
                Strikes++;
                Log.Write("guard", "Запрещённое окно: \"" + w.Title + "\" (pid " + w.Pid + ")" + (State.Preview ? " [preview]" : ""));
                Raise("Запрещённый контент. Окно закрыто.");
                if (State.Preview) return;
                if (w.Iconic) Native.ShowWindow(w.Handle, Native.SW_RESTORE);
                Native.PostMessage(w.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            else if (!State.Preview && (DateTime.Now - first).TotalSeconds > 2)
            {
                // Window ignored WM_CLOSE: kill the owner.
                closing[w.Handle] = DateTime.Now;
                KillPid((int)w.Pid, "окно не закрылось: " + w.Title);
            }
        }

        // ---------------------------------------------------------------- processes

        private void CheckProcesses()
        {
            Process[] all = Process.GetProcesses();
            HashSet<int> alive = new HashSet<int>();
            foreach (Process p in all)
            {
                try
                {
                    if (p.Id == (int)ownPid || p.SessionId != sessionId) continue;
                    alive.Add(p.Id);
                    string name = p.ProcessName.ToLowerInvariant();

                    if (cfg.KillBlockedProcesses && blocked.Contains(name))
                    {
                        KillProcess(p, "запрещённая программа");
                        continue;
                    }

                    if (cfg.KillOutsideProgramFiles && Array.IndexOf(Untouchable, name) < 0 && !pathOk.ContainsKey(p.Id))
                    {
                        string path = Native.GetProcessPath(p.Id);
                        bool ok = path == null || IsPathAllowed(path);
                        pathOk[p.Id] = ok;
                        if (!ok) KillProcess(p, "запуск из недопустимого места: " + path);
                    }
                }
                catch { /* process exited or access denied */ }
                finally { p.Dispose(); }
            }

            List<int> dead = new List<int>();
            foreach (int pid in pathOk.Keys) if (!alive.Contains(pid)) dead.Add(pid);
            foreach (int pid in dead) pathOk.Remove(pid);
        }

        private bool IsPathAllowed(string path)
        {
            foreach (string allowed in cfg.AllowedPaths)
                if (allowed.Length > 0 && path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return true;

            foreach (string root in denyRoots)
                if (!string.IsNullOrEmpty(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(path));
                if (d.DriveType == DriveType.Removable || d.DriveType == DriveType.Network || d.DriveType == DriveType.CDRom)
                    return false;
            }
            catch { }
            return true;
        }

        private void KillProcess(Process p, string reason)
        {
            string name = p.ProcessName;
            Log.Write("guard", "Завершаю " + name + " (pid " + p.Id + "): " + reason + (State.Preview ? " [preview]" : ""));
            if (State.Preview) return;
            try { p.Kill(); }
            catch (Exception ex) { Log.Write("guard", "Не удалось завершить " + name + ": " + ex.Message); }
        }

        private void KillPid(int pid, string reason)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    if (Array.IndexOf(Untouchable, name) >= 0) return;
                    KillProcess(p, reason);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- downloaded files

        private void StartFileWatchers()
        {
            if (!cfg.DeleteForbiddenFiles) return;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<string> dirs = new List<string>();
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            dirs.Add(Path.Combine(profile, "Downloads"));
            dirs.Add(tempDir);
            string pub = Environment.GetEnvironmentVariable("PUBLIC");
            if (!string.IsNullOrEmpty(pub)) dirs.Add(pub);

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || !seen.Add(dir)) continue;
                    FileSystemWatcher w = new FileSystemWatcher(dir);
                    w.IncludeSubdirectories = true;
                    w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime;
                    w.Created += delegate(object s, FileSystemEventArgs e) { OnNewFile(e.FullPath); };
                    w.Renamed += delegate(object s, RenamedEventArgs e) { OnNewFile(e.FullPath); };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception ex) { Log.Write("guard", "Не могу следить за " + dir + ": " + ex.Message); }
            }
        }

        private bool IsForbiddenFile(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            bool inTemp = path.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase);
            if (inTemp) return Array.IndexOf(TempExecutableExt, ext.ToLowerInvariant()) >= 0;
            return forbiddenExt.Contains(ext);
        }

        private void OnNewFile(string path)
        {
            try
            {
                if (State.AdminMode || !IsForbiddenFile(path)) return;
                ThreadPool.QueueUserWorkItem(delegate { TryDelete(path); });
            }
            catch { }
        }

        private void TryDelete(string path)
        {
            for (int i = 0; i < 40; i++)
            {
                if (State.AdminMode) return;
                try
                {
                    if (!File.Exists(path)) return;
                    if (State.Preview)
                    {
                        Log.Write("guard", "Удалил бы файл: " + path + " [preview]");
                        return;
                    }
                    File.Delete(path);
                    Log.Write("guard", "Удалён запрещённый файл: " + path);
                    Raise("Скачивание программ запрещено. Файл удалён.");
                    return;
                }
                catch { Thread.Sleep(1000); }   // still being written/locked: retry
            }
            Log.Write("guard", "Не удалось удалить файл: " + path);
        }
    }
}
