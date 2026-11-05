using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SchoolShield
{
    /// <summary>Turns the protection on and off by running the PowerShell scripts shipped with the program.</summary>
    internal static class Protection
    {
        private const string Winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        public static bool ShellRegistered()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Winlogon))
                {
                    string shell = k == null ? null : k.GetValue("Shell") as string;
                    return shell != null && shell.IndexOf("SchoolShield", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static bool SrpPresent()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers"))
                    return k != null;
            }
            catch { return false; }
        }

        public static bool Configured()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SchoolShield"))
                    return k != null && Convert.ToInt32(k.GetValue("Configured") ?? 0) == 1;
            }
            catch { return false; }
        }

        private static readonly System.Collections.Generic.Dictionary<string, string[]> DnsV4 = new System.Collections.Generic.Dictionary<string, string[]>
        {
            { "AdGuardFamily", new string[] { "94.140.14.15", "94.140.15.16" } },
            { "Yandex", new string[] { "77.88.8.7", "77.88.8.3" } },
            { "Cloudflare", new string[] { "1.1.1.3", "1.0.0.3" } },
            { "CleanBrowsing", new string[] { "185.228.168.168", "185.228.169.168" } },
            { "YandexCloudflare", new string[] { "77.88.8.7", "1.1.1.3" } }
        };

        /// <summary>Is the chosen family DNS filter the resolver of at least one working network adapter?</summary>
        public static bool DnsFilterActive(string provider)
        {
            try
            {
                string[] want;
                if (!DnsV4.TryGetValue(provider ?? "", out want)) return false;
                foreach (System.Net.NetworkInformation.NetworkInterface nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (System.Net.IPAddress a in nic.GetIPProperties().DnsAddresses)
                        if (Array.IndexOf(want, a.ToString()) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Has the installer written the SafeSearch / URL block policies for Chrome or Edge?</summary>
        public static bool BrowserPoliciesPresent()
        {
            try
            {
                foreach (string key in new string[] { @"SOFTWARE\Policies\Google\Chrome", @"SOFTWARE\Policies\Microsoft\Edge" })
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key + @"\URLBlocklist"))
                        if (k != null && k.ValueCount > 0) return true;
            }
            catch { }
            return false;
        }

        public static bool AutostartPresent()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                {
                    string v = k == null ? null : k.GetValue("SchoolShield") as string;
                    return v != null && v.IndexOf("--teacher-guard", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static bool StudentExists(string name)
        {
            try
            {
                using (System.DirectoryServices.AccountManagement.PrincipalContext ctx =
                    new System.DirectoryServices.AccountManagement.PrincipalContext(System.DirectoryServices.AccountManagement.ContextType.Machine))
                    return System.DirectoryServices.AccountManagement.UserPrincipal.FindByIdentity(ctx, name) != null;
            }
            catch { return false; }
        }

        /// <summary>Copies the default config files into %ProgramData%\SchoolShield if they are not there yet.</summary>
        public static void EnsureConfigFiles()
        {
            string src = Path.Combine(AppInfo.InstallDir, "config");
            Directory.CreateDirectory(Config.DataDir);
            foreach (string f in new string[] { "config.json", "badwords.txt", "blocklist.txt", "whitelist.txt" })
            {
                string dst = Path.Combine(Config.DataDir, f);
                string from = Path.Combine(src, f);
                if (!File.Exists(dst) && File.Exists(from)) File.Copy(from, dst);
            }
        }

        private static ProcessStartInfo Powershell(string script, string args, string adminCode)
        {
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(AppInfo.ScriptsDir, script) + "\" " + args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            // The code travels in the environment of the child process only, never on a command line.
            if (adminCode != null) psi.EnvironmentVariables["SS_ADMIN_CODE"] = adminCode;
            return psi;
        }

        /// <summary>Builds the Install script arguments from the chosen options.</summary>
        public static string ApplyArguments(InstallOptions o, string usb)
        {
            StringBuilder sb = new StringBuilder("-NonInteractive");
            sb.Append(" -StudentUser ").Append(Quote(o.StudentUser));
            sb.Append(" -AdminUser ").Append(Quote(string.IsNullOrEmpty(o.AdminUser) ? "SchoolAdmin" : o.AdminUser));
            sb.Append(" -Dns ").Append(o.Dns);
            sb.Append(" -WebMode ").Append(o.WebMode);
            sb.Append(" -Downloads ").Append(o.Downloads);
            sb.Append(" -Usb ").Append(usb);
            if (!o.AutoLogon) sb.Append(" -NoAutoLogon");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            return "'" + (s ?? "").Replace("'", "''") + "'";
        }

        /// <summary>Starts the Install script. adminCode may be null to keep the current code.</summary>
        public static Process StartApply(InstallOptions o, string usb, string adminCode, bool noShell, DataReceived output)
        {
            return StartApply(o, usb, adminCode, null, noShell, output);
        }

        /// <summary>
        /// Starts the Install script. <paramref name="adminPassword"/> is the Windows password of the separate administrator account; it travels
        /// in the environment of the child process only (never on a command line) and is null to leave an existing account alone.
        /// </summary>
        public static Process StartApply(InstallOptions o, string usb, string adminCode, string adminPassword, bool noShell, DataReceived output)
        {
            string args = ApplyArguments(o, usb) + (noShell ? " -NoShell" : "");
            ProcessStartInfo psi = Powershell("Install-SchoolShield.ps1", args, adminCode);
            if (adminPassword != null) psi.EnvironmentVariables["SS_ADMIN_PASSWORD"] = adminPassword;
            return Run(psi, output);
        }

        /// <summary>Teacher edition: DNS filter, browser policies, autostart of the guard. Nothing else is touched.</summary>
        public static Process StartApplyTeacher(InstallOptions o, DataReceived output)
        {
            string args = "-NonInteractive -Dns " + (string.IsNullOrEmpty(o.Dns) ? "AdGuardFamily" : o.Dns);
            return Run(Powershell("Install-SchoolShield-Teacher.ps1", args, null), output);
        }

        /// <summary>Starts the Uninstall script in "turn protection off" mode: keeps the program and the settings.</summary>
        public static Process StartRemove(DataReceived output)
        {
            return Run(Powershell("Uninstall-SchoolShield.ps1", "-KeepProgram -KeepData", null), output);
        }

        public delegate void DataReceived(string line);

        private static Process Run(ProcessStartInfo psi, DataReceived output)
        {
            Process p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null && output != null) output(e.Data); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null && output != null) output("! " + e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }
    }
}
