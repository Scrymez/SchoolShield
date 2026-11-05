using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Teacher edition. Windows stays Windows (normal desktop, no installer ban, nothing minimised or killed); the only thing
    /// that is enforced is the 18+ filter:
    ///   - "--teacher-guard"   a background process with a tray icon: closes 18+ windows, checks the screen with the local AI;
    ///   - default start       the teacher centre: the same launcher as the student one, but an ordinary window.
    /// DNS filter, browser policies and the blocklists are set by scripts\Install-SchoolShield-Teacher.ps1.
    /// </summary>
    internal static class TeacherHost
    {
        private const string GuardMutexName = @"Local\SchoolShield.TeacherGuard";

        /// <summary>Is the background guard of this session alive?</summary>
        public static bool GuardRunning()
        {
            try
            {
                using (Mutex m = Mutex.OpenExisting(GuardMutexName)) return m != null;
            }
            catch { return false; }
        }

        /// <summary>Starts the guard in the current session (no-op when it already runs).</summary>
        public static void StartGuard()
        {
            try
            {
                if (GuardRunning()) return;
                ProcessStartInfo psi = new ProcessStartInfo(AppInfo.ExePath, "--teacher-guard");
                psi.UseShellExecute = false;
                psi.WorkingDirectory = AppInfo.InstallDir;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("guard", "Не удалось запустить охрану: " + ex.Message); }
        }

        /// <summary>The teacher centre: the launcher as an ordinary window.</summary>
        public static int RunCenter(Config cfg)
        {
            State.Windowed = true;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("crash", e.Exception.ToString()); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e) { Log.Write("crash", Convert.ToString(e.ExceptionObject)); };

            Theme.Apply(UserData.Load().Theme != "light");
            Log.Write("start", "Центр учителя");
            if (Protection.Configured()) StartGuard();   // the filter is on, so its guard must be alive too

            try { Application.Run(new MainForm(cfg)); }
            catch (Exception ex) { Log.Write("crash", "Центр учителя упал: " + ex); }
            return 0;
        }

        /// <summary>The background guard with a tray icon. Runs until the session ends.</summary>
        public static int RunGuard()
        {
            bool createdNew;
            using (Mutex single = new Mutex(true, GuardMutexName, out createdNew))
            {
                if (!createdNew) return 0;
                Config cfg = Config.Load();
                if (!cfg.IsTeacher()) return 0;   // a stale autostart entry of a student machine must not do anything here
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log.Write("crash", e.Exception.ToString()); };
                Log.Write("start", "Охрана 18+ запущена");
                Application.Run(new GuardContext(cfg));
            }
            return 0;
        }

        private sealed class GuardContext : ApplicationContext
        {
            private readonly NotifyIcon tray = new NotifyIcon();
            private readonly Control sync = new Control();
            private readonly Guard guard;
            private readonly AiGuard ai;
            private DateTime lastBalloon = DateTime.MinValue;

            public GuardContext(Config cfg)
            {
                sync.CreateControl();
                IntPtr unused = sync.Handle;   // forces the handle so BeginInvoke works from the guard threads

                try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Shield; }
                tray.Text = "SchoolShield: фильтр 18+ включён";
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.Items.Add("Открыть Центр учителя", null, delegate { OpenCenter(); });
                menu.Items.Add("Управление защитой (нужен администратор)", null, delegate { Launch("--admin"); });
                menu.Items.Add("Журнал", null, delegate
                {
                    try { Process.Start(Path.Combine(Config.DataDir, "logs")); } catch { }
                });
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += delegate { OpenCenter(); };
                tray.Visible = true;

                guard = new Guard(cfg);
                guard.Alert += delegate (ViolationKind k, string detail)
                {
                    Notify(k == ViolationKind.Adult ? "Материал 18+. Окно закрыто." : Sarcasm.PlainFor(k).Title + " " + Sarcasm.PlainFor(k).Line);
                };
                guard.Start();

                ai = new AiGuard(cfg);
                ai.Alert += Notify;
                ai.Start();
            }

            private void Notify(string message)
            {
                try
                {
                    sync.BeginInvoke(new Action(delegate
                    {
                        if ((DateTime.Now - lastBalloon).TotalSeconds < 5) return;   // one balloon at a time
                        lastBalloon = DateTime.Now;
                        tray.ShowBalloonTip(4000, "SchoolShield", message, ToolTipIcon.Info);
                    }));
                }
                catch { }
            }

            private static void OpenCenter() { Launch(null); }

            private static void Launch(string args)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(AppInfo.ExePath, args ?? "");
                    psi.UseShellExecute = true;
                    psi.WorkingDirectory = AppInfo.InstallDir;
                    Process.Start(psi);
                }
                catch (Exception ex) { Log.Write("start", "Не удалось открыть: " + ex.Message); }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try
                    {
                        tray.Visible = false;
                        tray.Dispose();
                        guard.Dispose();
                        ai.Dispose();
                        sync.Dispose();
                    }
                    catch { }
                }
                base.Dispose(disposing);
            }
        }
    }
}
