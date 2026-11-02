using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolShield
{
    internal sealed class InstalledProgram
    {
        public string Name;
        public string Publisher;
        public string Version;
        public string UninstallString;
        public string QuietUninstallString;
        public string Key;

        public string ProductCode
        {
            get
            {
                Match m = Regex.Match(UninstallString ?? "", @"\{[0-9A-Fa-f\-]{36}\}");
                return m.Success && Regex.IsMatch(UninstallString, @"msiexec", RegexOptions.IgnoreCase) ? m.Value : null;
            }
        }

        /// <summary>The command that will actually run (shown to the administrator before it does).</summary>
        public string Command
        {
            get
            {
                if (!string.IsNullOrEmpty(QuietUninstallString)) return QuietUninstallString;
                string pc = ProductCode;
                if (pc != null) return "msiexec.exe /x " + pc + " /qn /norestart";
                return UninstallString;
            }
        }
    }

    /// <summary>
    /// Removes programs from Windows completely through their own uninstallers (classic and MSI programs)
    /// or by removing the package (Store apps). Administrator-only: the uninstaller is started with the
    /// "run as administrator" prompt, so the Windows administrator password is still required.
    /// </summary>
    internal static class Programs
    {
        public static bool IsStoreApp(AppEntry e)
        {
            return e != null && e.ShellId != null && e.ShellId.IndexOf('!') > 0 && e.ShellId.IndexOf('\\') < 0;
        }

        /// <summary>
        /// Programs installed for all users (HKLM only). HKCU is deliberately ignored: a student can write to
        /// their own HKCU and could plant a fake entry that an administrator would then run elevated.
        /// </summary>
        public static List<InstalledProgram> Enumerate()
        {
            List<InstalledProgram> list = new List<InstalledProgram>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] roots = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string root in roots)
            {
                try
                {
                    using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (rk == null) continue;
                        foreach (string sub in rk.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey k = rk.OpenSubKey(sub))
                                {
                                    string name = k.GetValue("DisplayName") as string;
                                    if (string.IsNullOrWhiteSpace(name)) continue;
                                    if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) continue;
                                    if (k.GetValue("ParentKeyName") != null) continue;
                                    string rel = k.GetValue("ReleaseType") as string;
                                    if (rel == "Update" || rel == "Hotfix" || rel == "Security Update") continue;
                                    if (name.StartsWith("KB", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Update for", StringComparison.OrdinalIgnoreCase)) continue;
                                    if (name.IndexOf("SchoolShield", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                    string us = k.GetValue("UninstallString") as string;
                                    string qs = k.GetValue("QuietUninstallString") as string;
                                    if (string.IsNullOrWhiteSpace(us) && string.IsNullOrWhiteSpace(qs)) continue;
                                    if (!seen.Add(name + "|" + (k.GetValue("DisplayVersion") as string))) continue;

                                    InstalledProgram p = new InstalledProgram();
                                    p.Name = name.Trim();
                                    p.Publisher = k.GetValue("Publisher") as string ?? "";
                                    p.Version = k.GetValue("DisplayVersion") as string ?? "";
                                    p.UninstallString = us;
                                    p.QuietUninstallString = qs;
                                    p.Key = root + "\\" + sub;
                                    list.Add(p);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            list.Sort(delegate (InstalledProgram a, InstalledProgram b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        /// <summary>Best guess for the registry entry that belongs to a launcher tile (higher = better).</summary>
        public static int Score(string appName, string programName)
        {
            string a = appName.ToLowerInvariant().Trim(), p = programName.ToLowerInvariant().Trim();
            if (a == p) return 100;
            if (p.StartsWith(a) || a.StartsWith(p)) return 80;
            if (p.Contains(a) || (p.Length >= 4 && a.Contains(p))) return 60;
            int common = 0;
            foreach (string t in a.Split(new char[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries))
                if (t.Length >= 3 && p.Contains(t)) common++;
            return common * 10;
        }

        public static Process RunElevated(string file, string args, bool hidden)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args ?? "");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            if (hidden) psi.WindowStyle = ProcessWindowStyle.Hidden;
            return Process.Start(psi);
        }

        /// <summary>Splits a registry command line into program and arguments (handles unquoted paths with spaces).</summary>
        public static void Split(string commandLine, out string file, out string args)
        {
            string c = commandLine.Trim();
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                if (end > 0)
                {
                    file = c.Substring(1, end - 1);
                    args = c.Substring(end + 1).Trim();
                    return;
                }
            }
            int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
            {
                file = c.Substring(0, exe + 4);
                args = c.Substring(exe + 4).Trim();
                return;
            }
            file = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            args = "/c " + c;
        }

        /// <summary>Starts the uninstaller; returns the process (or null). Throws Win32Exception 1223 if the prompt is cancelled.</summary>
        public static Process Uninstall(InstalledProgram p)
        {
            string file, args;
            Split(p.Command, out file, out args);
            Log.Write("uninstall", "Удаление «" + p.Name + "»: " + p.Command);
            return RunElevated(file, args, false);
        }

        /// <summary>Removes a Store (UWP) package for all users, including the copy provisioned for new users.</summary>
        public static Process UninstallStoreApp(AppEntry e)
        {
            string aumid = e.ShellId;
            string fam = aumid.Substring(0, aumid.IndexOf('!'));
            if (!Regex.IsMatch(fam, @"^[A-Za-z0-9._\-]+$")) throw new InvalidOperationException("Недопустимое имя пакета.");
            string name = fam.Split('_')[0];
            string script =
                "$fam='" + fam + "'; $name='" + name + "'; " +
                "Get-AppxPackage -AllUsers | Where-Object { $_.PackageFamilyName -eq $fam } | Remove-AppxPackage -AllUsers; " +
                "Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $name } | Remove-AppxProvisionedPackage -Online | Out-Null";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Log.Write("uninstall", "Удаление Store-приложения «" + e.Name + "» (" + fam + ")");
            return RunElevated("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded, true);
        }
    }

    internal static class RecycleBin
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

        /// <summary>Empties the recycle bin of the current user on all drives, silently.</summary>
        public static bool EmptyCurrentUser()
        {
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            // 0 = ok; 0x8000FFFF-style "bin already empty" results are fine too.
            Log.Write("files", "Корзина очищена, код " + hr);
            return hr == 0 || hr == unchecked((int)0x80070012) || hr == 1;
        }

        /// <summary>Empties the bins of ALL users on all fixed drives. Needs an elevated process.</summary>
        public static void EmptyAllUsersElevated()
        {
            string script =
                "foreach ($d in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' }) { " +
                "$bin = Join-Path $d.RootDirectory.FullName '$Recycle.Bin'; if (-not (Test-Path -LiteralPath $bin)) { continue }; " +
                "Get-ChildItem -LiteralPath $bin -Force -ErrorAction SilentlyContinue | ForEach-Object { " +
                "& takeown.exe /f $_.FullName /r /d y | Out-Null; " +
                "& icacls.exe $_.FullName /grant '*S-1-5-32-544:(OI)(CI)F' /t /c | Out-Null; " +
                "Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue } }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Log.Write("files", "Очистка корзин всех пользователей");
            Programs.RunElevated("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded, true);
        }
    }

    /// <summary>List of installed programs with a "remove from Windows" button. Used by the launcher (admin mode) and the Admin panel.</summary>
    internal sealed class UninstallDialog : Form
    {
        private readonly ListView list = new ListView();
        private readonly TextBox filter = new TextBox();
        private readonly Button remove;
        private readonly Label hint = new Label();
        private readonly List<InstalledProgram> all;
        private readonly Action done;

        public UninstallDialog(string preselect, Action onFinished)
        {
            done = onFinished;
            all = Programs.Enumerate();

            Text = "Удаление программ из системы";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10.5f);
            MinimizeBox = false;

            Label head = new Label();
            head.Text = "Выберите программу. Она будет удалена с компьютера полностью, для всех пользователей.";
            head.AutoSize = false;
            head.SetBounds(16, 12, 868, 24);
            Controls.Add(head);

            filter.SetBounds(16, 44, 868, 28);
            filter.BackColor = Theme.Card;
            filter.ForeColor = Theme.Text;
            filter.BorderStyle = BorderStyle.FixedSingle;
            filter.TextChanged += delegate { Fill(null); };
            Controls.Add(filter);

            list.SetBounds(16, 82, 868, 420);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.BackColor = Theme.Card;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.Columns.Add("Программа", 420);
            list.Columns.Add("Издатель", 250);
            list.Columns.Add("Версия", 170);
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.DoubleClick += delegate { DoRemove(); };
            Controls.Add(list);

            hint.SetBounds(16, 510, 868, 36);
            hint.ForeColor = Theme.Muted;
            Controls.Add(hint);

            remove = UiKit.Accent("Удалить из системы", delegate { DoRemove(); });
            remove.AutoSize = false;
            remove.SetBounds(16, 550, 240, 38);
            remove.Enabled = false;
            Controls.Add(remove);

            Button close = UiKit.Flat("Закрыть", delegate { Close(); });
            close.AutoSize = false;
            close.SetBounds(644, 550, 240, 38);
            Controls.Add(close);
            CancelButton = close;

            Fill(preselect);
        }

        private void Fill(string select)
        {
            string q = filter.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            ListViewItem best = null;
            int bestScore = 0;
            foreach (InstalledProgram p in all)
            {
                if (q.Length > 0 && p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                ListViewItem it = new ListViewItem(p.Name);
                it.SubItems.Add(p.Publisher);
                it.SubItems.Add(p.Version);
                it.Tag = p;
                list.Items.Add(it);
                if (select != null)
                {
                    int s = Programs.Score(select, p.Name);
                    if (s > bestScore) { bestScore = s; best = it; }
                }
            }
            list.EndUpdate();
            if (best != null && bestScore >= 30) { best.Selected = true; best.EnsureVisible(); }
            UpdateButtons();
        }

        private InstalledProgram Selected()
        {
            return list.SelectedItems.Count == 0 ? null : list.SelectedItems[0].Tag as InstalledProgram;
        }

        private void UpdateButtons()
        {
            InstalledProgram p = Selected();
            remove.Enabled = p != null;
            hint.Text = p == null ? "Если нужной программы нет в списке, у неё нет записи об установке (портативная версия)."
                : "Будет выполнено: " + p.Command;
        }

        private void DoRemove()
        {
            InstalledProgram p = Selected();
            if (p == null) return;
            if (!State.Privileged && !IsAdminPanel)
            {
                MessageBox.Show(this, "Удаление доступно только администратору.", "Удаление");
                return;
            }
            if (MessageBox.Show(this,
                "Удалить «" + p.Name + "» с компьютера полностью?\nЭто нельзя отменить.",
                "Удаление программы", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                Process proc = Programs.Uninstall(p);
                Watch(proc);
                MessageBox.Show(this, "Удаление запущено. Если откроется окно программы удаления, следуйте ему.", "Удаление");
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) MessageBox.Show(this, "Не удалось запустить удаление: " + ex.Message, "Удаление");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось запустить удаление: " + ex.Message, "Удаление"); }
        }

        /// <summary>True when hosted by the elevated Admin panel (no need for the terminal's admin mode).</summary>
        public bool IsAdminPanel;

        private void Watch(Process proc)
        {
            if (proc == null) return;
            Thread t = new Thread(delegate ()
            {
                try { proc.WaitForExit(); } catch { }
                if (done != null) { try { done(); } catch { } }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
