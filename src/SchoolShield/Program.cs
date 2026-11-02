using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    internal static class Program
    {
        /// <summary>
        /// One executable, four roles:
        ///   (started by Windows as the shell)   administrator account -> normal Windows desktop and idle (the process
        ///                                       must stay alive, otherwise Winlogon ends the session);
        ///                                       any other account      -> student launcher.
        ///   --admin / desktop icon              administrator panel (asks for the Windows admin password if needed).
        ///   --preview                           windowed test mode of the launcher, blocks nothing.
        ///   --set-code                          dialog for changing the admin code (run elevated).
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool preview = Array.IndexOf(args, "--preview") >= 0;
            State.Preview = preview;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Array.IndexOf(args, "--set-code") >= 0)
            {
                Application.Run(new SetCodeForm());
                return 0;
            }

            if (Array.IndexOf(args, "--admin") >= 0) return RunPanel();
            if (Array.IndexOf(args, "--teacher-guard") >= 0) return TeacherHost.RunGuard();

            bool createdNew;
            using (Mutex single = new Mutex(true, @"Local\SchoolShield.Shell", out createdNew))
            {
                if (!createdNew) return 0;

                Config cfg = Config.Load();
                Log.Write("start", "Запуск" + (preview ? " (preview)" : "") + ", пользователь " + WindowsIdentity.GetCurrent().Name);

                // Teacher edition: ordinary Windows, the launcher is just a window. Checked before IsExempt: teachers are often administrators.
                if (!preview && cfg.IsTeacher()) return TeacherHost.RunCenter(cfg);

                if (!preview && IsExempt(cfg))
                {
                    // An administrator double-clicked the exe/icon inside a normal desktop: show the panel.
                    if (ExplorerRunning()) return RunPanel();
                    RunAsDesktopHost();
                    return 0;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("crash", e.Exception.ToString());
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("crash", Convert.ToString(e.ExceptionObject));
                };

                Theme.Apply(UserData.Load().Theme != "light");
                if (!preview) UserPolicy.Apply(cfg);

                // If the UI dies for any reason, bring it back: a student session must never end up on a blank screen.
                for (int attempt = 0; attempt < 50 && !State.ExitRequested; attempt++)
                {
                    try
                    {
                        Application.Run(new MainForm(cfg));
                        if (preview) break;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("crash", "Лаунчер упал, перезапуск: " + ex);
                        Thread.Sleep(1000);
                    }
                }
            }
            return 0;
        }

        // ------------------------------------------------------------------ administrator panel

        private static bool IsElevated()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        private static int RunPanel()
        {
            if (!IsElevated())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--admin");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";   // UAC prompt; a student cannot pass it without the admin password
                    Process.Start(psi);
                }
                catch { /* cancelled */ }
                return 0;
            }

            bool createdNew;
            using (Mutex single = new Mutex(true, @"Local\SchoolShield.Panel", out createdNew))
            {
                if (!createdNew) return 0;
                Config cfg = Config.Load();
                Application.Run(new AdminPanelForm(cfg));
            }
            return 0;
        }

        // ------------------------------------------------------------------ who gets the launcher

        private static bool IsExempt(Config cfg)
        {
            try
            {
                if (File.Exists(Config.DisableFlagPath)) return true;                 // emergency switch, see docs
                if (cfg.IsExemptUser(Environment.UserName)) return true;
                return IsAdministratorAccount();
            }
            catch { return false; }
        }

        private static bool IsAdministratorAccount()
        {
            SecurityIdentifier admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            WindowsIdentity id = WindowsIdentity.GetCurrent();

            try
            {
                // With UAC the admin's token is "filtered": the Administrators SID is present but deny-only.
                if (id.Groups != null)
                    foreach (IdentityReference g in id.Groups)
                        if (g.Equals(admins)) return true;
                if (new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)) return true;
            }
            catch { }

            // Second opinion straight from the account database.
            try
            {
                IntPtr buf;
                int read, total;
                int rc = Native.NetUserGetLocalGroups(null, id.Name, 0, 1 /* LG_INCLUDE_INDIRECT */, out buf, -1, out read, out total);
                if (rc == 0)
                {
                    try
                    {
                        string adminGroup = ((NTAccount)admins.Translate(typeof(NTAccount))).Value;
                        int slash = adminGroup.LastIndexOf('\\');
                        if (slash >= 0) adminGroup = adminGroup.Substring(slash + 1);
                        for (int i = 0; i < read; i++)
                        {
                            IntPtr p = Marshal.ReadIntPtr(buf, i * IntPtr.Size);
                            string name = Marshal.PtrToStringUni(p);
                            if (string.Equals(name, adminGroup, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                    finally { Native.NetApiBufferFree(buf); }
                }
            }
            catch { }
            return false;
        }

        private static bool ExplorerRunning()
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("explorer"))
            {
                try { if (p.SessionId == session) return true; }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        /// <summary>Administrator session started by Winlogon: give the normal desktop and stay alive in the background.</summary>
        private static void RunAsDesktopHost()
        {
            Log.Write("start", "Учётная запись администратора: запускаю обычный рабочий стол");
            try { Process.Start("explorer.exe"); }
            catch (Exception ex) { Log.Write("start", "explorer.exe: " + ex.Message); }

            // Winlogon logs the user off when its shell process exits, so never exit.
            while (true) Thread.Sleep(Timeout.Infinite);
        }
    }
}
