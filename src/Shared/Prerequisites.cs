using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace SchoolShield
{
    internal sealed class PrereqItem
    {
        public string Id;
        public string Name;
        public bool Ok;
        public string Detail;
    }

    /// <summary>
    /// Everything the AI filter and the built-in browser need from Windows, with a one-call repair that downloads
    /// what is missing (Microsoft Visual C++ runtime, WebView2 runtime, the AI model). Used by the installer, by the
    /// Admin panel and, silently, whenever the panel is opened.
    /// </summary>
    internal static class Prerequisites
    {
        /// <summary>ONNX Runtime 1.20 is built with Visual Studio 2022: it needs msvcp140.dll / vcruntime140_1.dll of at least this version.</summary>
        public static readonly Version VcRequired = new Version(14, 38);

        public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
        public const string VcRedistFile = "vc_redist.x64.exe";

        /// <summary>
        /// The installer carries the Visual C++ runtime and the AI model inside itself and leaves them here, so that nothing has to be
        /// downloaded (and no VPN is needed) on a network where Microsoft or GitHub downloads fail.
        /// </summary>
        public static string BundledDir(string installDir)
        {
            return string.IsNullOrEmpty(installDir) ? null : Path.Combine(installDir, "prereq");
        }

        private static string Bundled(string installDir, string file)
        {
            string dir = BundledDir(installDir);
            if (dir == null) return null;
            string f = Path.Combine(dir, file);
            return File.Exists(f) ? f : null;
        }
        public const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        // ------------------------------------------------------------------ checks

        /// <summary>Version of the installed Visual C++ runtime (msvcp140.dll in System32), or null when it is missing.</summary>
        public static Version VcRuntimeVersion()
        {
            try
            {
                string f = Path.Combine(Environment.SystemDirectory, "msvcp140.dll");
                if (!File.Exists(f)) return null;
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(f);
                return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart, v.FilePrivatePart);
            }
            catch { return null; }
        }

        public static bool VcRuntimeOk()
        {
            Version v = VcRuntimeVersion();
            if (v == null || v < VcRequired) return false;
            return File.Exists(Path.Combine(Environment.SystemDirectory, "vcruntime140_1.dll"));
        }

        public static string WebView2Version()
        {
            string[] paths = new string[] {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
            };
            foreach (string p in paths)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(p))
                    {
                        string pv = k == null ? null : k.GetValue("pv") as string;
                        if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return pv;
                    }
                }
                catch { }
            }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
                {
                    string pv = k == null ? null : k.GetValue("pv") as string;
                    if (!string.IsNullOrEmpty(pv) && pv != "0.0.0.0") return pv;
                }
            }
            catch { }
            return null;
        }

        public static bool ModelOk()
        {
            try
            {
                return File.Exists(Release.ModelPath) && new FileInfo(Release.ModelPath).Length == Release.ModelSize
                    && string.Equals(Downloader.Sha256Hex(Release.ModelPath), Release.ModelSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static List<PrereqItem> Check(string installDir)
        {
            List<PrereqItem> list = new List<PrereqItem>();

            Version vc = VcRuntimeVersion();
            PrereqItem a = new PrereqItem();
            a.Id = "vc";
            a.Name = "Microsoft Visual C++ 2015–2022 (x64)";
            a.Ok = VcRuntimeOk();
            a.Detail = vc == null ? "не установлен" : "версия " + vc + (a.Ok ? "" : " — слишком старая, нужна " + VcRequired + " или новее");
            list.Add(a);

            string ort = installDir == null ? null : Path.Combine(installDir, "onnxruntime.dll");
            PrereqItem b = new PrereqItem();
            b.Id = "ort";
            b.Name = "Библиотека ONNX Runtime";
            b.Ok = ort != null && File.Exists(ort);
            b.Detail = b.Ok ? "на месте" : "файл onnxruntime.dll не найден — переустановите программу (Setup.exe)";
            list.Add(b);

            PrereqItem c = new PrereqItem();
            c.Id = "model";
            c.Name = "Модель ИИ-фильтра (open_nsfw, 23 МБ)";
            c.Ok = ModelOk();
            c.Detail = c.Ok ? "установлена, контрольная сумма совпала" : (File.Exists(Release.ModelPath) ? "файл повреждён" : "не скачана");
            list.Add(c);

            string wv = WebView2Version();
            PrereqItem d = new PrereqItem();
            d.Id = "webview2";
            d.Name = "Microsoft Edge WebView2 (встроенный браузер)";
            d.Ok = wv != null;
            d.Detail = wv != null ? "версия " + wv : "не установлен";
            list.Add(d);

            return list;
        }

        // ------------------------------------------------------------------ repair

        public static bool IsSignedByMicrosoft(string file)
        {
            X509Certificate2 cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
            X509Chain chain = new X509Chain();
            return chain.Build(cert) && cert.Subject.IndexOf("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Writes "  40%" to the log each time the download passes the next ten percent.</summary>
        private static Action<long, long> Percent(Action<string> log)
        {
            int last = -1;
            return delegate (long done, long total)
            {
                if (total <= 0) return;
                int p = (int)(done * 100 / total) / 10 * 10;
                if (p != last) { last = p; log("  " + p + "%"); }
            };
        }

        /// <summary>Runs a Microsoft installer: the copy that comes with the program if there is one, otherwise a download.</summary>
        private static bool RunInstaller(string url, string bundledFile, string fileName, string args, Action<string> log, string what)
        {
            string path = Path.Combine(Path.GetTempPath(), fileName);
            bool temp = true;
            try
            {
                if (bundledFile != null)
                {
                    try
                    {
                        if (IsSignedByMicrosoft(bundledFile)) { path = bundledFile; temp = false; log(what + ": беру установщик из комплекта программы, интернет не нужен."); }
                        else log("! Встроенный установщик " + what + " не прошёл проверку подписи, скачиваю заново.");
                    }
                    catch (Exception ex) { log("! Встроенный установщик " + what + ": " + ex.Message + "; скачиваю заново."); }
                }
                if (temp)
                {
                    log("Скачиваю " + what + "…");
                    Downloader.DownloadFile(url, path, Percent(log));
                    if (!IsSignedByMicrosoft(path)) throw new InvalidDataException("подпись файла не принадлежит Microsoft");
                }
                log("Устанавливаю " + what + "…");
                ProcessStartInfo psi = new ProcessStartInfo(path, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    // The WebView2 bootstrapper downloads its runtime itself, which takes minutes on a slow school network: wait, but not for ever.
                    if (!p.WaitForExit(20 * 60 * 1000))
                    {
                        try { p.Kill(); } catch { }
                        log("! " + what + ": установщик не завершился за 20 минут и остановлен. Повторите позже (панель администратора → «ИИ-фильтр» → «Исправить»).");
                        return false;
                    }
                    // 0 = ok, 3010 = ok but reboot recommended, 1638 = a newer version is already installed
                    bool ok = p.ExitCode == 0 || p.ExitCode == 3010 || p.ExitCode == 1638;
                    log(what + ": код завершения " + p.ExitCode + (p.ExitCode == 3010 ? " (рекомендуется перезагрузка)" : "") + (ok ? "" : " — ошибка"));
                    return ok;
                }
            }
            catch (Exception ex)
            {
                log("! " + what + " не установлен: " + ex.Message);
                return false;
            }
            finally { if (temp) { try { File.Delete(path); } catch { } } }
        }

        /// <summary>Installs whatever is missing. Needs administrator rights and internet. Returns true when everything is fine afterwards.</summary>
        public static bool Repair(string installDir, Action<string> log)
        {
            if (log == null) log = delegate { };
            List<PrereqItem> items = Check(installDir);
            foreach (PrereqItem it in items)
                log((it.Ok ? "[ok]  " : "[!]   ") + it.Name + ": " + it.Detail);

            if (!VcRuntimeOk())
                RunInstaller(VcRedistUrl, Bundled(installDir, VcRedistFile), VcRedistFile, "/install /quiet /norestart", log, "Visual C++ 2015–2022");
            if (!ModelOk())
            {
                try
                {
                    string own = Bundled(installDir, Release.ModelFile);
                    if (own != null && Downloader.InstallModelFrom(own)) log("Модель ИИ-фильтра установлена из комплекта программы, интернет не нужен.");
                    else
                    {
                        log("Скачиваю модель ИИ-фильтра (" + (Release.ModelSize / 1024 / 1024) + " МБ)…");
                        Downloader.InstallModel(Percent(log));
                        log("Модель установлена.");
                    }
                }
                catch (Exception ex) { log("! Модель не скачана: " + ex.Message); }
            }
            // WebView2 last: it is almost always present already, and its bootstrapper is the one piece that needs the network.
            if (WebView2Version() == null)
                RunInstaller(WebView2BootstrapperUrl, null, "MicrosoftEdgeWebview2Setup.exe", "/silent /install", log, "WebView2 Runtime");

            bool ok = true;
            foreach (PrereqItem it in Check(installDir))
            {
                if (!it.Ok) ok = false;
                log((it.Ok ? "[ok]  " : "[!]   ") + it.Name + ": " + it.Detail);
            }
            return ok;
        }

        // ------------------------------------------------------------------ diagnosis text

        public static string Describe(Exception ex)
        {
            StringBuilder sb = new StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (sb.Length > 0) sb.Append(" → ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
            }
            return sb.ToString();
        }

        /// <summary>A human hint for the typical reasons why the native ONNX Runtime does not start.</summary>
        public static string Hint(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is DllNotFoundException || e is BadImageFormatException || e is EntryPointNotFoundException)
                    return "Не загружается onnxruntime.dll. Обычно не хватает Microsoft Visual C++ 2015–2022 (x64): нажмите «Исправить» в панели администратора.";
            return null;
        }
    }
}
