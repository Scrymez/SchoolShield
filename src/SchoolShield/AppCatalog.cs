using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace SchoolShield
{
    internal sealed class AppEntry
    {
        public string Name;
        public string ShellId;   // parsing name inside shell:AppsFolder (classic + Store apps)
        public string Path;      // custom entry: exe path
        public string Args;
        public string Category = "other";
        public Bitmap Icon;
    }

    /// <summary>Builds the list of programs shown in the launcher: everything installed on the PC, minus what admin hid.</summary>
    internal static class AppCatalog
    {
        public const string OtherId = "other";

        // MUST be called on an STA thread (COM Shell.Application).
        public static List<AppEntry> Load(Config cfg)
        {
            List<AppEntry> list = new List<AppEntry>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(t);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                dynamic items = folder.Items();
                int count = items.Count;
                Log.Write("catalog", "Shell AppsFolder: записей " + count);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
                        string name = item.Name;
                        string id = item.Path;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id)) continue;
                        if (!IsVisible(cfg, name)) continue;
                        if (!names.Add(name)) continue;
                        AppEntry e = new AppEntry();
                        e.Name = name;
                        e.ShellId = id;
                        e.Category = Classify(cfg, name);
                        list.Add(e);
                    }
                    catch { /* skip a broken entry */ }
                }
            }
            catch (Exception ex)
            {
                Log.Write("catalog", "Не удалось прочитать список программ: " + ex.Message);
            }

            // Without Explorer (the launcher is the Windows shell) or in a profile that has just been created, the shell may return an empty
            // "apps" folder. The shortcuts of the Start menu are plain files, so read them directly.
            if (list.Count == 0)
            {
                try { AddStartMenu(cfg, list, names); }
                catch (Exception ex) { Log.Write("catalog", "Ярлыки меню «Пуск» не прочитаны: " + ex.Message); }
            }

            foreach (CustomApp c in cfg.CustomApps)
            {
                if (c == null || string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Path)) continue;
                if (!names.Add(c.Name)) continue;
                AppEntry e = new AppEntry();
                e.Name = c.Name;
                e.Path = Environment.ExpandEnvironmentVariables(c.Path);
                e.Args = c.Args;
                e.Category = string.IsNullOrEmpty(c.Category) ? Classify(cfg, c.Name) : c.Category;
                list.Add(e);
            }

            list.Sort(delegate (AppEntry a, AppEntry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            Log.Write("catalog", "Программ в лаунчере: " + list.Count);
            return list;
        }

        private static readonly System.Text.RegularExpressions.Regex Junk = new System.Text.RegularExpressions.Regex(
            @"uninstall|удал|readme|прочти|справк|help|licen|лиценз|document|документац|website|веб-?сайт|на сайт|release notes|о программе|about ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static readonly string[] NotPrograms = new string[] { ".chm", ".txt", ".pdf", ".htm", ".html", ".url", ".rtf", ".md", ".log" };

        /// <summary>Shortcuts of the Start menu (all users and the current user), straight from the file system.</summary>
        private static void AddStartMenu(Config cfg, List<AppEntry> list, HashSet<string> names)
        {
            List<string> roots = new List<string>();
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")); } catch { }
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")); } catch { }

            dynamic wsh = null;
            try { wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); } catch { }

            int found = 0;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                List<string> files = new List<string>();
                ScanLinks(root, 0, files);
                foreach (string f in files)
                {
                    try
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrEmpty(name) || Junk.IsMatch(name)) continue;
                        if (!IsVisible(cfg, name)) continue;
                        if (names.Contains(name)) continue;
                        if (wsh != null)
                        {
                            string target = null;
                            try { target = wsh.CreateShortcut(f).TargetPath; } catch { }
                            if (!string.IsNullOrEmpty(target) && Array.IndexOf(NotPrograms, Path.GetExtension(target).ToLowerInvariant()) >= 0) continue;
                            if (!string.IsNullOrEmpty(target) && Directory.Exists(target)) continue;
                        }
                        names.Add(name);
                        AppEntry e = new AppEntry();
                        e.Name = name;
                        e.Path = f;
                        e.Category = Classify(cfg, name);
                        list.Add(e);
                        found++;
                    }
                    catch { }
                }
            }
            Log.Write("catalog", "Ярлыки меню «Пуск»: добавлено " + found);
        }

        private static void ScanLinks(string dir, int depth, List<string> files)
        {
            try { files.AddRange(Directory.GetFiles(dir, "*.lnk")); } catch { }
            if (depth >= 4) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (string d in subs) ScanLinks(d, depth + 1, files);
        }

        /// <summary>Everything installed, ignoring the hide-list (used by the Admin panel to edit it).</summary>
        public static List<AppEntry> LoadAll(Config cfg)
        {
            Config all = new Config();
            all.HiddenApps.Clear();
            all.HiddenExact.Clear();
            all.AlwaysShow.Clear();
            all.Browser.BuiltInOnly = false;   // the Admin panel lists every program
            all.ShowAllInstalled = true;
            all.CategoryRules = cfg.CategoryRules;
            all.AppCategories = cfg.AppCategories;
            all.CustomApps = new List<CustomApp>();
            return Load(all);
        }

        public static string Classify(Config cfg, string name)
        {
            string forced;
            if (cfg.AppCategories != null && cfg.AppCategories.TryGetValue(name, out forced) && !string.IsNullOrEmpty(forced))
                return forced;
            foreach (CategoryRule r in cfg.CategoryRules)
                foreach (string k in r.Keywords)
                    if (k.Length > 0 && name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return r.Id;
            return OtherId;
        }

        private static readonly System.Text.RegularExpressions.Regex BrowserName = new System.Text.RegularExpressions.Regex(
            @"(^|[^\p{L}])(chrome|firefox|opera|brave|vivaldi|browser|браузер)([^\p{L}]|$)|microsoft edge|internet explorer",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private static bool IsExternalBrowser(string name)
        {
            return BrowserName.IsMatch(name);
        }

        private static bool IsVisible(Config cfg, string name)
        {
            if (cfg.Browser.BuiltIn && cfg.Browser.BuiltInOnly && WebView2Probe.Available && IsExternalBrowser(name)) return false;
            foreach (string h in cfg.HiddenExact)
                if (string.Equals(h, name, StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string a in cfg.AlwaysShow)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            if (!cfg.IsTeacher() && Config.IsBuiltInHidden(name)) return false;
            if (!State.GamesAllowed && Config.IsGameApp(name)) return false;
            foreach (string h in cfg.HiddenApps)
                if (h.Length > 0 && name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return false;

            if (cfg.ShowAllInstalled) return true;

            foreach (string a in cfg.AllowedApps)
                if (a.Length > 0 && name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Loads the icon for one entry. Returns null if the shell cannot provide one.</summary>
        public static Bitmap LoadIcon(AppEntry e, int size)
        {
            try
            {
                string parsing = e.ShellId != null ? "shell:AppsFolder\\" + e.ShellId : e.Path;
                if (string.IsNullOrEmpty(parsing)) return null;
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                Native.IShellItemImageFactory factory;
                Native.SHCreateItemFromParsingName(parsing, IntPtr.Zero, ref iid, out factory);
                Native.SIZE sz = new Native.SIZE();
                sz.cx = size;
                sz.cy = size;
                IntPtr hbm;
                // 0x4 = SIIGBF_ICONONLY, 0x1 = BIGGERSIZEOK
                int hr = factory.GetImage(sz, 0x4 | 0x1, out hbm);
                Marshal.ReleaseComObject(factory);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                try { return Native.BitmapFromHBitmap(hbm); }
                finally { Native.DeleteObject(hbm); }
            }
            catch { return null; }
        }

        public static void Launch(AppEntry e)
        {
            try
            {
                ProcessStartInfo psi;
                if (e.ShellId != null)
                    psi = new ProcessStartInfo("shell:AppsFolder\\" + e.ShellId);
                else
                    psi = new ProcessStartInfo(e.Path, e.Args ?? "");
                psi.UseShellExecute = true;
                if (e.Path != null && !e.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    try { psi.WorkingDirectory = System.IO.Path.GetDirectoryName(e.Path); } catch { }
                }
                Process.Start(psi);
                Log.Write("launch", e.Name);
            }
            catch (Exception ex)
            {
                Log.Write("launch", "Ошибка запуска " + e.Name + ": " + ex.Message);
                throw;
            }
        }
    }
}
