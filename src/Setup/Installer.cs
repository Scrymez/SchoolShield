using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SchoolShield.Setup
{
    /// <summary>Copies the program, registers it with Windows and creates the desktop icon. Never turns the protection on by itself.</summary>
    internal sealed class Installer
    {
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SchoolShield";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string target, int flags);

        public Action<string> Log = delegate { };
        public Action<int> Progress = delegate { };
        public string InstallDir = Release.InstallDirDefault;
        public bool DesktopIcon = true;
        public bool StartMenuIcon = true;
        public bool DownloadModel = true;

        /// <summary>"student", "teacher" or null = keep what the computer already has (updates never change the edition).</summary>
        public string Edition;
        public bool ApplyTeacherProtection = true;

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static bool IsInstalled { get { return File.Exists(Path.Combine(Release.InstallDirDefault, "SchoolShield.exe")); } }

        // ------------------------------------------------------------------ install / update

        public void Install()
        {
            Log("Установка SchoolShield " + Version + " в " + InstallDir);
            Progress(2);
            CleanupLeftovers();

            Progress(10);
            Extract();

            Progress(70);
            CopySelf();
            CreateDataDir();
            if (!string.IsNullOrEmpty(Edition)) SetEdition(Edition);
            CreateShortcuts();
            RegisterUninstall();

            Progress(82);
            if (DownloadModel) InstallPrerequisites();   // Visual C++, WebView2 and the AI model: downloaded here, not left to chance

            // Fresh install of the teacher edition: switch the 18+ filter on. Update: refresh it only if it was on before.
            if (IsTeacher && ApplyTeacherProtection && (!string.IsNullOrEmpty(Edition) || FilterWasOn()))
            {
                Progress(92);
                ApplyTeacher();
            }

            Progress(100);
            Log("Готово.");
        }

        // ------------------------------------------------------------------ edition

        private static string ConfigPath { get { return Path.Combine(Release.DataDir, "config.json"); } }

        /// <summary>The edition that is (or is about to be) installed: the explicit choice, else what config.json says, else student.</summary>
        public string EffectiveEdition
        {
            get
            {
                if (!string.IsNullOrEmpty(Edition)) return Edition.ToLowerInvariant();
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                            File.ReadAllText(ConfigPath), "\"Edition\"\\s*:\\s*\"(\\w+)\"");
                        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
                    }
                }
                catch { }
                return "student";
            }
        }

        public bool IsTeacher { get { return EffectiveEdition == "teacher"; } }

        private static bool FilterWasOn()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SchoolShield"))
                    return k != null && Convert.ToInt32(k.GetValue("Configured") ?? 0) == 1;
            }
            catch { return false; }
        }

        /// <summary>Writes "Edition" into config.json (the rest of the file, which the administrator may have edited, is left alone).</summary>
        private void SetEdition(string edition)
        {
            try
            {
                string path = ConfigPath;
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                string field = "\"Edition\": \"" + edition.ToLowerInvariant() + "\"";
                System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex("\"Edition\"\\s*:\\s*\"\\w*\"");
                if (re.IsMatch(json)) json = re.Replace(json, field, 1);
                else
                {
                    int i = json.IndexOf('{');
                    json = json.Substring(0, i + 1) + "\n  " + field + "," + json.Substring(i + 1);
                }
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                Log("Редакция: " + (edition.ToLowerInvariant() == "teacher" ? "Учитель" : "Ученик"));

                if (edition.ToLowerInvariant() == "teacher")
                {
                    // The student lists in ProgramData are copies of the shipped ones; teachers get empty "my additions" files instead.
                    string block = Path.Combine(Release.DataDir, "blocklist.txt");
                    if (File.Exists(block) && new FileInfo(block).Length > 20000)
                    {
                        string backup = Path.Combine(Release.DataDir, "blocklist.student-backup.txt");
                        if (!File.Exists(backup)) File.Move(block, backup); else File.Delete(block);
                    }
                    WriteIfMissing(block, "# Ваши дополнительные закрытые сайты, по одному в строке (поддомены закрываются тоже).\n# Основной список идёт с программой.\n");
                    WriteIfMissing(Path.Combine(Release.DataDir, "badwords-extra.txt"),
                        "# Ваши дополнительные запрещённые слова в названиях окон: одно регулярное выражение в строке.\n# Основной список идёт с программой.\n");
                }
            }
            catch (Exception ex) { Log("! Редакция не записана: " + ex.Message); }
        }

        private static void WriteIfMissing(string path, string text)
        {
            if (!File.Exists(path)) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }

        /// <summary>DNS filter, browser policies, autostart of the guard: runs the teacher script from the installed copy.</summary>
        private void ApplyTeacher()
        {
            string script = Path.Combine(InstallDir, "scripts", "Install-SchoolShield-Teacher.ps1");
            if (!File.Exists(script)) { Log("! Нет скрипта фильтра 18+: " + script); return; }
            Log("Включаю фильтр 18+…");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -NonInteractive");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null) Log(line);
                    p.WaitForExit();
                    if (p.ExitCode != 0) Log("! Скрипт фильтра 18+ завершился с кодом " + p.ExitCode + ". Откройте «Управление защитой» и нажмите «Применить фильтр 18+».");
                }
            }
            catch (Exception ex) { Log("! Фильтр 18+: " + ex.Message); }
        }

        private void CleanupLeftovers()
        {
            try
            {
                if (!Directory.Exists(InstallDir)) return;
                foreach (string f in Directory.GetFiles(InstallDir, "*.old-*", SearchOption.AllDirectories))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (string f in Directory.GetFiles(InstallDir, "*.new", SearchOption.AllDirectories))
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        private void Extract()
        {
            Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
            if (res == null) throw new InvalidOperationException("В установщике нет файлов программы (payload.zip).");
            string root = Path.GetFullPath(InstallDir).TrimEnd('\\') + "\\";
            using (res)
            using (ZipArchive zip = new ZipArchive(res, ZipArchiveMode.Read))
            {
                int total = zip.Entries.Count, n = 0;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    n++;
                    if (e.FullName.EndsWith("/") || e.Name.Length == 0) continue;
                    string target = Path.GetFullPath(Path.Combine(InstallDir, e.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Недопустимый путь в архиве: " + e.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    WriteReplacing(target, e);
                    Progress(25 + 45 * n / Math.Max(1, total));
                }
                Log("Файлов установлено: " + total);
            }
        }

        /// <summary>Writes a file even if the old one is running or locked: the old copy is renamed aside and removed after a reboot.</summary>
        private void WriteReplacing(string target, ZipArchiveEntry entry)
        {
            string tmp = target + ".new";
            using (Stream s = entry.Open())
            using (FileStream fs = File.Create(tmp))
                s.CopyTo(fs);
            Replace(target, tmp);
        }

        private void Replace(string target, string tmp)
        {
            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                string old = target + ".old-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                File.Move(target, old);   // renaming a running exe/dll is allowed
                File.Move(tmp, target);
                MoveFileEx(old, null, 4 /* MOVEFILE_DELAY_UNTIL_REBOOT */);
                Log("  файл занят, заменён после переименования: " + Path.GetFileName(target));
            }
        }

        private void CopySelf()
        {
            try
            {
                string self = Assembly.GetExecutingAssembly().Location;
                string dst = Path.Combine(InstallDir, "SchoolShield-Setup.exe");
                if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) return;
                string tmp = dst + ".new";
                File.Copy(self, tmp, true);
                Replace(dst, tmp);
            }
            catch (Exception ex) { Log("! Не удалось сохранить установщик для удаления: " + ex.Message); }
        }

        private void CreateDataDir()
        {
            try
            {
                Directory.CreateDirectory(Release.DataDir);
                Directory.CreateDirectory(Path.Combine(Release.DataDir, "logs"));
                string src = Path.Combine(InstallDir, "config");
                foreach (string f in new string[] { "config.json", "badwords.txt", "blocklist.txt", "whitelist.txt" })
                {
                    string dst = Path.Combine(Release.DataDir, f);
                    string from = Path.Combine(src, f);
                    if (!File.Exists(dst) && File.Exists(from)) File.Copy(from, dst);   // never overwrite the admin's settings
                }
            }
            catch (Exception ex) { Log("! Папка настроек: " + ex.Message); }
        }

        // ------------------------------------------------------------------ shortcuts and Add/Remove Programs

        private static string DesktopDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory); } }
        private string DesktopLink { get { return Path.Combine(DesktopDir, "SchoolShield.lnk"); } }
        private string TeacherDesktopLink { get { return Path.Combine(DesktopDir, "Центр учителя.lnk"); } }
        private string MenuDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SchoolShield"); } }
        private string MenuLink { get { return Path.Combine(MenuDir, "SchoolShield.lnk"); } }
        private string TeacherMenuLink { get { return Path.Combine(MenuDir, "Центр учителя.lnk"); } }

        private void CreateShortcuts()
        {
            string exe = Path.Combine(InstallDir, "SchoolShield.exe");
            try
            {
                if (IsTeacher)
                {
                    // The desktop icon opens the teacher centre; administration lives in the Start menu and the tray icon.
                    try { File.Delete(DesktopLink); } catch { }
                    if (DesktopIcon) { MakeLink(TeacherDesktopLink, exe, "", "Центр учителя SchoolShield"); Log("Значок на рабочем столе: " + TeacherDesktopLink); }
                    if (StartMenuIcon)
                    {
                        Directory.CreateDirectory(MenuDir);
                        MakeLink(TeacherMenuLink, exe, "", "Центр учителя SchoolShield");
                        MakeLink(MenuLink, exe, "--admin", "SchoolShield — управление фильтром 18+");
                    }
                    return;
                }
                try { File.Delete(TeacherDesktopLink); } catch { }
                try { File.Delete(TeacherMenuLink); } catch { }
                if (DesktopIcon) { MakeLink(DesktopLink, exe, "--admin", "SchoolShield — панель администратора"); Log("Значок на рабочем столе: " + DesktopLink); }
                if (StartMenuIcon)
                {
                    Directory.CreateDirectory(MenuDir);
                    MakeLink(MenuLink, exe, "--admin", "SchoolShield — панель администратора");
                }
            }
            catch (Exception ex) { Log("! Не удалось создать ярлыки: " + ex.Message); }
        }

        private static void MakeLink(string path, string exe, string args, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = exe;
            lnk.Arguments = args;
            lnk.WorkingDirectory = Path.GetDirectoryName(exe);
            lnk.IconLocation = exe + ",0";
            lnk.Description = description;
            lnk.Save();
        }

        private void RegisterUninstall()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    string exe = Path.Combine(InstallDir, "SchoolShield.exe");
                    k.SetValue("DisplayName", IsTeacher ? "SchoolShield — Учитель" : "SchoolShield");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", Release.Author);
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("UninstallString", "\"" + Path.Combine(InstallDir, "SchoolShield-Setup.exe") + "\" /uninstall");
                    k.SetValue("URLInfoAbout", Release.SiteUrl);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", 60000, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log("! Запись в «Установка и удаление программ»: " + ex.Message); }
        }

        // ------------------------------------------------------------------ prerequisites (VC++, WebView2, AI model)

        private void InstallPrerequisites()
        {
            try
            {
                bool ok = Prerequisites.Repair(InstallDir, Log);
                if (!ok) Log("! Не всё удалось установить. Откройте панель администратора: вкладка «ИИ-фильтр» → «Исправить» (нужен интернет).");
            }
            catch (Exception ex)
            {
                Log("! Проверка компонентов: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ uninstall

        public void Uninstall(bool removeSettings)
        {
            Log("Удаление SchoolShield…");
            string script = Path.Combine(InstallDir, "scripts", "Uninstall-SchoolShield.ps1");
            if (File.Exists(script))
            {
                string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -KeepProgram" + (removeSettings ? "" : " -KeepData");
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null) Log(line);
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException("Скрипт снятия защиты завершился с ошибкой " + p.ExitCode + ". Программа не удалена.");
                }
            }
            try { File.Delete(DesktopLink); } catch { }
            try { File.Delete(TeacherDesktopLink); } catch { }
            try { if (Directory.Exists(MenuDir)) Directory.Delete(MenuDir, true); } catch { }
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }

            // This very file lives in InstallDir: delete the folder from a helper once we have exited.
            ProcessStartInfo del = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & rd /s /q \"" + InstallDir + "\"");
            del.CreateNoWindow = true;
            del.UseShellExecute = false;
            Process.Start(del);
            Log("Готово. Программа удалена; рабочий стол Windows восстановлен.");
        }
    }
}
