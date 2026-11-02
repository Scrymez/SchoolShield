using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SchoolShield
{
    public class CustomApp
    {
        public string Name;
        public string Path;
        public string Args;
        public string Category;
    }

    public class LinkItem
    {
        public string Name;
        public string Url;
        public LinkItem() { }
        public LinkItem(string name, string url) { Name = name; Url = url; }
    }

    public class BellItem
    {
        public string Start;   // "08:30"
        public string End;     // "09:15"
        public string Title;   // "Урок 1"
        public BellItem() { }
        public BellItem(string start, string end, string title) { Start = start; End = end; Title = title; }
    }

    public class SharedFolder
    {
        public string Name;
        public string Path;
        public bool ReadOnly;
        public SharedFolder() { }
        public SharedFolder(string name, string path, bool ro) { Name = name; Path = path; ReadOnly = ro; }
    }

    public class CategoryRule
    {
        public string Id;
        public string Name;
        public string Glyph;
        public List<string> Keywords = new List<string>();
        public CategoryRule() { }
        public CategoryRule(string id, string name, string glyph, params string[] keywords)
        {
            Id = id; Name = name; Glyph = glyph;
            Keywords.AddRange(keywords);
        }
    }

    public class AiSettings
    {
        public bool Enabled = true;
        public double Threshold = 0.90;     // NSFW probability that counts as a hit
        public int IntervalMs = 2500;       // how often the screen is checked
        public int Confirmations = 2;       // consecutive hits before the window is closed
        public bool Tiles = false;          // also check 4 quarters of the screen (slower, finds small pictures)
        public bool HideInGallery = true;   // hide flagged pictures in the launcher gallery
    }

    public class InstallOptions
    {
        public string StudentUser = "Student";
        public string Dns = "YandexCloudflare";
        public string WebMode = "Filter";
        public string Downloads = "BlockAll";
        public bool AutoLogon = true;
        public bool Configured = false;
    }

    /// <summary>
    /// config.json lives in %ProgramData%\SchoolShield. Students get read-only access (set by the installer),
    /// so only an administrator can change it (Admin panel or the admin terminal).
    /// </summary>
    public class Config
    {
        public string Title = "Школьный компьютер";
        public string Subtitle = "Только для учёбы";

        // Admin code: PBKDF2-SHA1(code, salt, Iterations). Never stored in clear text.
        public string AdminSalt = "";
        public string AdminHash = "";
        public int Iterations = 100000;
        // Chord that must be HELD for AdminHotkeyHoldMs: a stray key press can never open the admin terminal.
        public string AdminHotkey = "Ctrl+Shift+Alt+F12";
        public int AdminHotkeyHoldMs = 1500;
        public int AdminModeMinutes = 30;
        public int MaxCodeAttempts = 5;
        public int LockoutSeconds = 60;

        // Launcher sections (ids): home, apps, docs, media, files, web, system
        public List<string> Sections = new List<string>();

        // Programs. ShowAllInstalled=true -> everything installed except HiddenApps/HiddenExact.
        // ShowAllInstalled=false -> only AllowedApps (substring of the name) + CustomApps.
        public bool ShowAllInstalled = true;
        public List<string> AllowedApps = new List<string>();
        public List<string> HiddenApps = new List<string>();     // substring match
        public List<string> HiddenExact = new List<string>();    // exact names (set from the Admin panel)
        public List<string> AlwaysShow = new List<string>();     // exact names that stay visible even if a HiddenApps pattern matches
        public List<CustomApp> CustomApps = new List<CustomApp>();
        public Dictionary<string, string> AppCategories = new Dictionary<string, string>();   // exact name -> category id
        public List<CategoryRule> CategoryRules = new List<CategoryRule>();

        // Files and documents inside the launcher (Explorer itself is unavailable to students).
        public List<SharedFolder> SharedFolders = new List<SharedFolder>();
        public bool StudentCanDelete = true;

        // "Useful sites" shown in the Internet section.
        public List<LinkItem> Links = new List<LinkItem>();

        // Lesson bell schedule shown on the home screen. Empty list = off.
        public List<BellItem> Bells = new List<BellItem>();

        public bool AllowStudentShutdown = true;
        public bool AllowStudentRestart = true;

        // Guard.
        public bool BlockMinimize = true;
        public bool CloseBadWindows = true;
        public bool DeleteForbiddenFiles = true;
        public bool KillBlockedProcesses = true;
        public bool KillOutsideProgramFiles = true;
        public List<string> BlockedProcesses = new List<string>();
        public List<string> ForbiddenExtensions = new List<string>();
        public List<string> AllowedPaths = new List<string>();
        public List<string> ExemptUsers = new List<string>();

        // Removable drives for the student: "block" | "readonly" | "allow".
        public string UsbMode = "block";

        public AiSettings Ai = new AiSettings();
        public InstallOptions Install = new InstallOptions();

        public Config()
        {
            HiddenApps.AddRange(new string[] {
                "uninstall", "удалить", "удаление", "справка", "help", "readme", "release notes",
                "документация", "documentation", "website", "веб-сайт",
                "командная строка", "command prompt", "powershell", "windows terminal",
                "редактор реестра", "registry editor", "диспетчер задач", "task manager",
                "панель управления", "control panel", "параметры", "settings",
                "microsoft store", "магазин", "выполнить", "проводник", "file explorer",
                "services", "службы", "управление компьютером", "computer management",
                "конфигурация системы", "system configuration", "просмотр событий", "event viewer",
                "schoolshield", "steam", "epic games", "battle.net", "origin", "roblox", "minecraft", "discord"
            });
            BlockedProcesses.AddRange(new string[] {
                "cmd", "powershell", "powershell_ise", "pwsh", "wt", "regedit", "reg", "taskmgr",
                "mmc", "msconfig", "control", "systemsettings", "winstore.app", "wscript", "cscript",
                "mshta", "msiexec", "wsl", "bash", "bitsadmin", "certutil", "wmic", "schtasks", "sc",
                "net", "net1", "taskkill", "runas", "winget", "onedrivesetup", "explorer", "perfmon",
                "resmon", "eventvwr", "compmgmt", "diskmgmt", "installutil", "msbuild",
                "csc", "vbc", "regsvr32", "cmstp", "setup", "install", "installer"
            });
            ForbiddenExtensions.AddRange(new string[] {
                ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".bat", ".cmd", ".ps1",
                ".vbs", ".vbe", ".wsf", ".scr", ".com", ".jar", ".apk", ".iso", ".img", ".reg",
                ".zip", ".rar", ".7z", ".torrent"
            });
            Sections.AddRange(new string[] { "home", "apps", "docs", "media", "files", "web", "system" });

            CategoryRules.Add(new CategoryRule("office", "Офисные", "",
                "word", "excel", "powerpoint", "outlook", "onenote", "access", "publisher", "libreoffice", "openoffice",
                "writer", "calc", "impress", "wps", "acrobat", "reader", "foxit", "sumatra", "djvu", "офис", "office", "pdf"));
            CategoryRules.Add(new CategoryRule("study", "Учебные", "",
                "scratch", "idle", "thonny", "kumir", "кумир", "geogebra", "lazarus", "pascal", "алгоритм", "mathcad",
                "stellarium", "chem", "хими", "физик", "математик", "обучен", "учебн", "урок", "typing", "стамина", "stamina",
                "клавиатурный", "соло", "1с", "словарь", "dictionary", "lingvo", "переводчик"));
            CategoryRules.Add(new CategoryRule("internet", "Интернет", "",
                "chrome", "edge", "firefox", "yandex", "яндекс", "opera", "brave", "vivaldi", "browser", "браузер",
                "zoom", "teams", "skype", "meet", "webex"));
            CategoryRules.Add(new CategoryRule("dev", "Программирование", "",
                "visual studio", "vs code", "vscode", "code::blocks", "codeblocks", "dev-c", "pycharm", "notepad++",
                "git", "java", "arduino", "intellij", "eclipse", "android studio", "python", "devc"));
            CategoryRules.Add(new CategoryRule("media", "Графика и медиа", "",
                "paint", "gimp", "inkscape", "krita", "photos", "фотограф", "vlc", "media player", "проигрыватель",
                "audacity", "kdenlive", "movies", "кино", "camera", "камера", "recorder", "звукозапись", "groove",
                "photoshop", "blender", "mp3", "video", "видео", "музык", "music"));
            CategoryRules.Add(new CategoryRule("system", "Системные", "",
                "calculator", "калькулятор", "notepad", "блокнот", "snip", "ножницы", "sketch", "wordpad",
                "character map", "таблица символов", "magnifier", "лупа", "on-screen keyboard", "экранная клавиатура",
                "narrator", "clock", "часы", "будильник", "alarm", "quick assist", "remote desktop", "sticky", "заметки"));

            Links.Add(new LinkItem("Российская электронная школа", "https://resh.edu.ru"));
            Links.Add(new LinkItem("Учи.ру", "https://uchi.ru"));
            Links.Add(new LinkItem("ЯКласс", "https://www.yaklass.ru"));
            Links.Add(new LinkItem("Решу ЕГЭ / ОГЭ", "https://sdamgia.ru"));
            Links.Add(new LinkItem("Википедия", "https://ru.wikipedia.org"));

            Bells.Add(new BellItem("08:30", "09:15", "Урок 1"));
            Bells.Add(new BellItem("09:25", "10:10", "Урок 2"));
            Bells.Add(new BellItem("10:20", "11:05", "Урок 3"));
            Bells.Add(new BellItem("11:20", "12:05", "Урок 4"));
            Bells.Add(new BellItem("12:20", "13:05", "Урок 5"));
            Bells.Add(new BellItem("13:15", "14:00", "Урок 6"));
            Bells.Add(new BellItem("14:10", "14:55", "Урок 7"));
        }

        public static string DataDir { get { return Release.DataDir; } }
        public static string ConfigPath { get { return System.IO.Path.Combine(DataDir, "config.json"); } }
        public static string BadWordsPath { get { return System.IO.Path.Combine(DataDir, "badwords.txt"); } }
        public static string BlocklistPath { get { return System.IO.Path.Combine(DataDir, "blocklist.txt"); } }
        public static string WhitelistPath { get { return System.IO.Path.Combine(DataDir, "whitelist.txt"); } }
        public static string DisableFlagPath { get { return System.IO.Path.Combine(DataDir, "DISABLE"); } }

        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = int.MaxValue;
                    Config c = js.Deserialize<Config>(json);
                    if (c != null)
                    {
                        if (c.Ai == null) c.Ai = new AiSettings();
                        if (c.Install == null) c.Install = new InstallOptions();
                        if (c.Sections == null || c.Sections.Count == 0) c.Sections = new Config().Sections;
                        if (c.CategoryRules == null || c.CategoryRules.Count == 0) c.CategoryRules = new Config().CategoryRules;
                        if (c.AppCategories == null) c.AppCategories = new Dictionary<string, string>();
                        if (c.HiddenExact == null) c.HiddenExact = new List<string>();
                        if (c.AlwaysShow == null) c.AlwaysShow = new List<string>();
                        return c;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("config", "Ошибка чтения config.json, использую значения по умолчанию: " + ex.Message);
            }
            return new Config();
        }

        /// <summary>Writes config.json (pretty-printed). Needs administrator rights.</summary>
        public void Save()
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            string json = JsonPretty.Format(js.Serialize(this));
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
        }

        public static List<Regex> LoadBadWords()
        {
            List<Regex> res = new List<Regex>();
            try
            {
                if (!File.Exists(BadWordsPath)) return res;
                foreach (string raw in File.ReadAllLines(BadWordsPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    try
                    {
                        res.Add(new Regex(line, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                    }
                    catch (Exception ex)
                    {
                        Log.Write("config", "Плохой шаблон в badwords.txt '" + line + "': " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Log.Write("config", "badwords.txt: " + ex.Message); }
            return res;
        }

        public bool IsExemptUser(string userName)
        {
            foreach (string u in ExemptUsers)
                if (string.Equals(u, userName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool SectionEnabled(string id)
        {
            foreach (string s in Sections)
                if (string.Equals(s, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal static class JsonPretty
    {
        /// <summary>Indents compact JSON (what JavaScriptSerializer produces).</summary>
        public static string Format(string json)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
                        {
                            sb.Append(c).Append(json[++i]);
                        }
                        else
                        {
                            sb.Append(c).Append('\n').Append(' ', ++indent * 2);
                        }
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n').Append(' ', --indent * 2).Append(c);
                        break;
                    case ',': sb.Append(c).Append('\n').Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
