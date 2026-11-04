using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SchoolShield
{
    // Field names repeat the keys of config\catalog.json (JavaScriptSerializer maps them one to one).

    public class CatalogPackage
    {
        public string url = "";
        public string sha256 = "";
        public long size;
        public string kind = "exe";        // "msi" (run through msiexec) or "exe"
        public string installer = "";      // what tools/update-catalog.py detected: msi, nsis, inno
        public string args = "";           // silent switches
    }

    public class CatalogItem
    {
        public string id = "";
        public string name = "";
        public string category = "study";  // office, study, media, system
        public string description = "";
        public string site = "";
        public string russian = "";
        public int size_mb;
        public List<string> detect = new List<string>();      // parts of the "Programs and Features" name
        public List<CatalogPackage> packages = new List<CatalogPackage>();
    }

    public class CatalogFile
    {
        public int version;
        public string updated = "";
        public List<CatalogItem> items = new List<CatalogItem>();
    }

    /// <summary>
    /// Catalog of free educational programs with a Russian interface. Every installer has a pinned SHA-256: a file that does not match
    /// is deleted and never started. The list itself is produced by tools/update-catalog.py and ships with the program.
    /// </summary>
    internal static class Catalog
    {
        public static string Path { get { return System.IO.Path.Combine(AppInfo.InstallDir, "config", "catalog.json"); } }

        public static CatalogFile Load()
        {
            try
            {
                if (!File.Exists(Path)) return new CatalogFile();
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                CatalogFile f = js.Deserialize<CatalogFile>(File.ReadAllText(Path, Encoding.UTF8));
                return f ?? new CatalogFile();
            }
            catch (Exception ex)
            {
                Log.Write("software", "Не прочитан catalog.json: " + ex.Message);
                return new CatalogFile();
            }
        }

        public static string CategoryName(string id)
        {
            switch (id)
            {
                case "office": return "Офисные";
                case "study": return "Учебное ПО";
                case "media": return "Графика и медиа";
                case "system": return "Системные";
                default: return "Другое";
            }
        }

        /// <summary>Installed for all users (HKLM only, like the rest of the panel)? Returns the installed version through <paramref name="version"/>.</summary>
        public static bool IsInstalled(CatalogItem item, List<InstalledProgram> installed, out string version)
        {
            version = "";
            foreach (InstalledProgram p in installed)
            {
                foreach (string d in item.detect)
                {
                    if (d.Length > 0 && p.Name.IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        version = p.Version ?? "";
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Exit codes of Windows Installer that mean "done": 0, 3010 (reboot needed), 1641 (rebooting), 1638 (same product already there).</summary>
        public static bool ExitCodeOk(int code)
        {
            return code == 0 || code == 3010 || code == 1641 || code == 1638;
        }

        public static string QuoteArg(string s)
        {
            return "\"" + s.Replace("\"", "") + "\"";
        }

        /// <summary>The command a package is started with (program, arguments).</summary>
        public static KeyValuePair<string, string> Command(CatalogPackage pk, string file)
        {
            if (string.Equals(pk.kind, "msi", StringComparison.OrdinalIgnoreCase))
                return new KeyValuePair<string, string>(Environment.ExpandEnvironmentVariables(@"%windir%\System32\msiexec.exe"), "/i " + QuoteArg(file) + " " + pk.args);
            return new KeyValuePair<string, string>(file, pk.args);
        }

        public static string FormatSize(long bytes)
        {
            return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0") + " МБ" : Math.Max(1, bytes / 1024) + " КБ";
        }
    }

    internal static class SoftwareInstaller
    {
        private static string DownloadDir { get { return System.IO.Path.Combine(Config.DataDir, "downloads"); } }

        /// <summary>Marker file: the student launcher watches it and refreshes its program list after an install.</summary>
        public static string StampPath { get { return System.IO.Path.Combine(Config.DataDir, "apps.stamp"); } }

        public static void Touch()
        {
            try { File.WriteAllText(StampPath, DateTime.Now.ToString("o")); } catch { }
        }

        /// <summary>Downloads, checks and silently installs every package of the item. Returns false (with the reason in the log) on any failure.</summary>
        public static bool Install(CatalogItem item, Action<string> log)
        {
            try
            {
                long need = 0;
                foreach (CatalogPackage p in item.packages) need += p.size;
                if (!EnoughSpace(need * 3, log)) return false;
                Directory.CreateDirectory(DownloadDir);

                int n = 0;
                foreach (CatalogPackage pk in item.packages)
                {
                    n++;
                    string ext = string.Equals(pk.kind, "msi", StringComparison.OrdinalIgnoreCase) ? ".msi" : ".exe";
                    string file = System.IO.Path.Combine(DownloadDir, item.id + "-" + n + ext);
                    try
                    {
                        log("Скачиваю " + System.IO.Path.GetFileName(new Uri(pk.url).LocalPath) + " (" + Catalog.FormatSize(pk.size) + ")…");
                        int lastPercent = -1;
                        Downloader.DownloadFile(pk.url, file, delegate (long done, long total)
                        {
                            if (total <= 0) return;
                            int percent = (int)(done * 100 / total) / 10 * 10;
                            if (percent != lastPercent) { lastPercent = percent; log("  " + percent + "%"); }
                        });

                        string actual = Downloader.Sha256Hex(file);
                        if (!string.Equals(actual, pk.sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            log("! КОНТРОЛЬНАЯ СУММА НЕ СОВПАЛА. Файл удалён и не запускался.");
                            log("  Ожидалась " + pk.sha256.Substring(0, 16) + "…, получена " + actual.Substring(0, 16) + "…");
                            log("  Скорее всего, программа на сайте обновилась: обновите SchoolShield, в новой версии каталог свежий.");
                            Log.Write("software", item.id + ": контрольная сумма не совпала (" + actual + ")");
                            return false;
                        }
                        log("Контрольная сумма верна. Устанавливаю…");

                        KeyValuePair<string, string> cmd = Catalog.Command(pk, file);
                        ProcessStartInfo psi = new ProcessStartInfo(cmd.Key, cmd.Value);
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.WorkingDirectory = DownloadDir;
                        using (Process p = Process.Start(psi))
                        {
                            if (!p.WaitForExit(30 * 60 * 1000))
                            {
                                try { p.Kill(); } catch { }
                                log("! Установщик не завершился за 30 минут и остановлен.");
                                Log.Write("software", item.id + ": таймаут установщика");
                                return false;
                            }
                            if (!Catalog.ExitCodeOk(p.ExitCode))
                            {
                                log("! Установщик завершился с кодом " + p.ExitCode + ".");
                                Log.Write("software", item.id + ": код возврата " + p.ExitCode);
                                return false;
                            }
                            if (p.ExitCode == 3010 || p.ExitCode == 1641) log("  Для завершения нужна перезагрузка компьютера.");
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(file)) File.Delete(file); } catch { }
                        try { if (File.Exists(file + ".part")) File.Delete(file + ".part"); } catch { }
                    }
                }
                Log.Write("software", "Установлено: " + item.name);
                return true;
            }
            catch (Exception ex)
            {
                log("! " + ex.Message);
                Log.Write("software", item.id + ": " + ex.Message);
                return false;
            }
        }

        private static bool EnoughSpace(long bytes, Action<string> log)
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(Config.DataDir);
                DriveInfo d = new DriveInfo(root);
                if (d.AvailableFreeSpace < bytes)
                {
                    log("! Мало места на диске " + root + ": нужно около " + Catalog.FormatSize(bytes) + ", свободно " + Catalog.FormatSize(d.AvailableFreeSpace) + ".");
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
