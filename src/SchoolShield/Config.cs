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

    /// <summary>Built-in browser (WebView2): the only place where ads can be blurred for sure.</summary>
    public class BrowserSettings
    {
        public bool BuiltIn = true;               // students browse in the launcher's own browser
        public bool BuiltInOnly = true;           // Chrome/Edge/Firefox/... are hidden and closed (when WebView2 is available)
        public bool AdBlur = true;                // blur all ads; students cannot switch it off
        public bool BlockAdNetworks = true;       // block requests to ad networks (not on YouTube / RuTube / VK Video)
        public bool AllowDownloads = false;
        public bool BlockIpAddresses = true;      // http://1.2.3.4/ cannot be used to get around the lists
        public string HomePage = "start";         // "start" = own start page with useful sites, or a full URL
        public string SearchUrl = "https://yandex.ru/search/?text={0}&fyandex=1";
        // Sites where ads INSIDE the video player are left alone (everything else on them is still blurred).
        public List<string> VideoSites = new List<string>();
    }

    public class WifiSettings
    {
        public bool StudentCanConnect = true;                          // students may switch between saved networks
        public bool Enforce = true;                                    // disconnect from networks that are not allowed
        public List<string> AllowedSsids = new List<string>();         // empty = every network the administrator saved
    }

    public class InstallOptions
    {
        public string StudentUser = "Student";
        public string Dns = "AdGuardFamily";
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
        public const string StudentEdition = "student";
        public const string TeacherEdition = "teacher";

        // "student": launcher instead of the desktop, everything locked. "teacher": ordinary Windows, only 18+ material is filtered.
        public string Edition = StudentEdition;

        public string Title = "Школьный компьютер";
        public string Subtitle = "Только для учёбы";

        // Admin code: PBKDF2-SHA1(code, salt, Iterations). Never stored in clear text.
        public string AdminSalt = "";
        public string AdminHash = "";
        public int Iterations = 100000;
        // Chord that must be HELD for AdminHotkeyHoldMs: a stray key press can never open the admin terminal.
        public string AdminHotkey = "Ctrl+Shift+Alt+A";
        // 7 quick clicks on the clock in the header also open the admin terminal (for keyboards that hide Fn keys).
        public bool AdminClickGesture = true;
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

        // Games: false = mini-games on sites (Yandex Games, Poki, CrazyGames, Roblox...), game launchers and game programs are closed.
        // GameExceptions: sites that stay open even then (one per line, "domain" or "domain/path").
        public bool AllowGames = false;
        public List<string> GameExceptions = new List<string>();

        // Removable drives for the student: "block" | "readonly" | "allow".
        public string UsbMode = "block";

        public AiSettings Ai = new AiSettings();
        public BrowserSettings Browser = new BrowserSettings();
        public WifiSettings Wifi = new WifiSettings();
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
                "schoolshield", "discord"
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
            Browser.VideoSites.AddRange(new string[] { "youtube.com", "youtu.be", "youtube-nocookie.com", "rutube.ru", "vkvideo.ru", "vk.com", "vk.ru" });

            CategoryRules.AddRange(DefaultCategoryRules());

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


        // ---------------------------------------------------------------- defaults that ship with the program (merged on load)

        /// <summary>
        /// Program categories. First match wins, so the order matters. These defaults are merged into whatever config.json
        /// holds, so an update brings new categories and keywords to computers that were set up earlier.
        /// </summary>
        public static List<CategoryRule> DefaultCategoryRules()
        {
            List<CategoryRule> r = new List<CategoryRule>();
            r.Add(new CategoryRule("office", "Офисные", "\uE8A5",
                "word", "excel", "powerpoint", "outlook", "onenote", "access", "publisher", "visio", "libreoffice", "openoffice",
                "writer", "calc", "impress", "wps", "acrobat", "reader", "foxit", "sumatra", "djvu", "офис", "office", "pdf"));
            r.Add(new CategoryRule("study", "Учебное ПО", "\uE8F1",
                // школьные предметы и тренажёры
                "scratch", "kumir", "кумир", "geogebra", "mathcad", "stellarium", "chem", "хими", "физик", "математик", "обучен",
                "учебн", "урок", "typing", "стамина", "stamina", "клавиатурный", "соло", "1с", "словарь", "dictionary", "lingvo", "переводчик",
                // программирование
                "idle", "thonny", "lazarus", "pascal", "алгоритм", "visual studio", "vs code", "vscode", "code::blocks", "codeblocks", "dev-c",
                "devc", "pycharm", "notepad++", "github", "git bash", "gitkraken", "java", "arduino", "intellij", "eclipse", "android studio",
                "python", "unity", "mblock", "makecode", "micro:bit", "microbit", "kodu", "pascalabc", "gcompris",
                // 3D-моделирование и печать
                "3d", "blender", "tinkercad", "sketchup", "freecad", "компас", "kompas", "autocad", "solidworks", "fusion", "cura",
                "openscad", "meshmixer", "nanocad",
                // робототехника
                "lego", "mindstorms", "wedo", "ev3", "spike", "robot", "робот", "education"));
            r.Add(new CategoryRule("internet", "Интернет", "\uE774",
                "chrome", "edge", "firefox", "yandex", "яндекс", "opera", "brave", "vivaldi", "browser", "браузер",
                "zoom", "teams", "skype", "meet", "webex"));
            r.Add(new CategoryRule("media", "Графика и медиа", "\uE91B",
                "paint", "gimp", "inkscape", "krita", "photos", "фотограф", "vlc", "media player", "mpc-hc", "powerdvd", "проигрыватель",
                "audacity", "kdenlive", "movies", "кино", "camera", "камера", "recorder", "звукозапись", "groove",
                "photoshop", "mp3", "video", "видео", "музык", "music"));
            r.Add(new CategoryRule("system", "Системные", "\uE770",
                "calculator", "калькулятор", "notepad", "блокнот", "snip", "ножницы", "sketch", "wordpad", "7-zip", "архиватор",
                "character map", "таблица символов", "magnifier", "лупа", "on-screen keyboard", "экранная клавиатура",
                "narrator", "clock", "часы", "будильник", "alarm", "sticky", "заметки", "realtek", "audio manager"));
            r.Add(new CategoryRule("microsoft", "Майкрософт", "\uE782",
                "microsoft", "windows", "msn", "silverlight", "math input", "directx", "xbox", "bing"));
            return r;
        }

        /// <summary>Programs that students never see (admin tools, remote access, games, assistants). AlwaysShow overrides this.</summary>
        public static readonly string[] BuiltInHidden = new string[] {
            "schoolshield", "безопасность windows", "windows security", "быстрая помощь", "quick assist", "cortana", "onedrive",
            "copilot", "game bar", "solitaire", "xbox", "feedback hub", "центр отзывов", "get help", "техническая поддержка",
            "your phone", "связь с телефоном", "mixed reality", "windows speech recognition", "распознавание речи", "remote desktop",
            "удалённого рабочего стола", "удаленного рабочего стола", "odbc", "iscsi", "memory diagnostic", "диагностика памяти",
            "resource monitor", "монитор ресурсов", "performance monitor", "системный монитор", "firewall", "брандмауэр",
            "disk cleanup", "очистка диска", "defragment", "дефрагментация", "print management", "управление печатью",
            "component services", "службы компонентов", "task scheduler", "планировщик заданий", "system information",
            "сведения о системе", "steps recorder", "средство записи действий", "windows fax", "факс", "internet explorer",
            "recovery drive", "диск восстановления", "windows tools", "средства windows", "registry", "реестр", "powershell",
            "терминал", "командная строка", "command prompt", "task manager", "диспетчер задач", "control panel", "панель управления",
            "settings", "параметры", "microsoft store", "магазин"
        };

        /// <summary>Extra processes the guard closes, on top of BlockedProcesses (help viewers open browsers; admin tools).</summary>
        public static readonly string[] BuiltInBlockedProcesses = new string[] {
            "hh", "helppane", "winhlp32", "quickassist", "msra", "mstsc", "odbcad32", "iscsicpl", "mdsched", "perfmon", "resmon",
            "taskschd", "wf", "cleanmgr", "dfrgui", "printmanagement", "comexp", "msinfo32", "psr", "wfs", "rstrui", "recdisc"
        };

        public bool IsTeacher()
        {
            return string.Equals(Edition, TeacherEdition, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Teacher edition: the only restriction is 18+ material (porn, erotica). Everything that makes a student PC safe
        /// (no installs, no minimize, process killing, locked drives, ad blur, whitelist mode) is switched off here, whatever
        /// config.json says, so one config file can never leave a teacher half-locked.
        /// </summary>
        internal void ApplyEdition()
        {
            if (!IsTeacher()) return;
            if (Title == "Школьный компьютер") Title = "Центр учителя";
            if (Subtitle == "Только для учёбы") Subtitle = "Фильтр 18+ включён";

            BlockMinimize = false;
            DeleteForbiddenFiles = false;
            KillBlockedProcesses = false;
            KillOutsideProgramFiles = false;
            CloseBadWindows = true;                 // 18+ window titles
            UsbMode = "allow";
            StudentCanDelete = true;
            AllowStudentShutdown = true;
            AllowStudentRestart = true;
            AdminClickGesture = false;
            ShowAllInstalled = true;
            AllowGames = true;                      // teachers may play; only 18+ is filtered

            HiddenApps.Clear();
            HiddenApps.Add("schoolshield");          // the launcher does not list itself
            BlockedProcesses.Clear();
            ForbiddenExtensions.Clear();
            AllowedPaths.Clear();

            Browser.BuiltInOnly = false;             // Chrome, Edge, Firefox... stay available (policies + DNS filter cover them)
            Browser.AdBlur = false;
            Browser.BlockAdNetworks = false;
            Browser.BlockIpAddresses = false;
            Browser.AllowDownloads = true;
            Wifi.Enforce = false;
            Wifi.StudentCanConnect = true;
            Install.WebMode = "Filter";
            Install.Downloads = "Off";
            Install.AutoLogon = false;

            // A wrong hit costs a teacher more than a missed one: a little less sensitive by default.
            if (Math.Abs(Ai.Threshold - 0.90) < 0.0001) Ai.Threshold = 0.93;
        }

        /// <summary>Game launchers and game programs: hidden from the launcher while games are forbidden.</summary>
        public static readonly string[] BuiltInGameApps = new string[] {
            "steam", "epic games", "roblox", "minecraft", "battle.net", "blizzard", "ea app", "ubisoft", "uplay", "riot client",
            "league of legends", "valorant", "genshin", "fortnite", "wargaming", "world of tanks", "мир танков", "lesta", "vk play",
            "vkplay", "game center", "gamecenter", "tlauncher", "bluestacks", "solitaire", "mahjong", "candy crush", "bubble witch",
            "microsoft casual", "asphalt", "hay day", "clash of clans", "brawl stars", "geforce now", "playkey", "playrix", "plarium"
        };

        /// <summary>Processes of game launchers and games, closed by the guard while games are forbidden.</summary>
        public static readonly string[] GameProcesses = new string[] {
            "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper", "eosoverlayrenderer",
            "robloxplayerbeta", "robloxplayerlauncher", "robloxstudiobeta", "robloxcrashhandler", "minecraft", "minecraftlauncher",
            "tlauncher", "battle.net", "origin", "eadesktop", "ealauncher", "ubisoftconnect", "upc", "uplay", "riotclientservices",
            "riotclientux", "leagueclient", "valorant", "valorant-win64-shipping", "genshinimpact", "yuanshen",
            "fortniteclient-win64-shipping", "fortnitelauncher", "csgo", "cs2", "dota2", "gta5", "worldoftanks", "wotlauncher", "wgc",
            "lestagamecenter", "bluestacks", "hd-player", "solitaire"
        };

        public static bool IsGameApp(string name)
        {
            foreach (string g in BuiltInGameApps)
                if (name.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Games are open for this configuration (teacher edition, or the administrator switched them on).</summary>
        public bool GamesOpen() { return IsTeacher() || AllowGames; }

        public static string GameWordsPath { get { return System.IO.Path.Combine(AppInfo.InstallDir, "config", "gamewords.txt"); } }
        public static string GamesListPath(string dir) { return System.IO.Path.Combine(dir, "games.txt"); }

        /// <summary>Time stamp of config.json (the launcher polls it to pick up a changed "Games" switch without a restart).</summary>
        public static DateTime Stamp()
        {
            DateTime a = DateTime.MinValue, b = DateTime.MinValue;
            try { if (File.Exists(ConfigPath)) a = File.GetLastWriteTimeUtc(ConfigPath); } catch { }
            try { string apps = System.IO.Path.Combine(DataDir, "apps.stamp"); if (File.Exists(apps)) b = File.GetLastWriteTimeUtc(apps); } catch { }
            return a > b ? a : b;     // apps.stamp is touched after the administrator installs a program from the catalog
        }

        public static bool IsBuiltInHidden(string name)
        {
            foreach (string h in BuiltInHidden)
                if (name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal void MergeDefaults()
        {
            // Programming merged into "Учебное ПО".
            CategoryRules.RemoveAll(delegate (CategoryRule x) { return x.Id == "dev"; });
            List<string> keys = new List<string>(AppCategories.Keys);
            foreach (string k in keys) if (AppCategories[k] == "dev") AppCategories[k] = "study";

            // Game launchers used to be hidden for good; they now follow the "Games" switch (BuiltInGameApps).
            HiddenApps.RemoveAll(delegate (string h)
            {
                foreach (string g in new string[] { "steam", "epic games", "battle.net", "origin", "roblox", "minecraft" })
                    if (string.Equals(h, g, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            });

            List<CategoryRule> merged = new List<CategoryRule>();
            foreach (CategoryRule def in DefaultCategoryRules())
            {
                CategoryRule have = CategoryRules.Find(delegate (CategoryRule x) { return x.Id == def.Id; });
                if (have == null) { merged.Add(def); continue; }
                have.Name = def.Name;
                have.Glyph = def.Glyph;
                foreach (string kw in def.Keywords)
                    if (!have.Keywords.Exists(delegate (string x) { return string.Equals(x, kw, StringComparison.OrdinalIgnoreCase); })) have.Keywords.Add(kw);
                merged.Add(have);
            }
            foreach (CategoryRule r in CategoryRules)
                if (!merged.Exists(delegate (CategoryRule x) { return x.Id == r.Id; })) merged.Insert(Math.Max(0, merged.Count - 0), r);
            CategoryRules = merged;
        }

        public static string DataDir { get { return Release.DataDir; } }
        public static string ConfigPath { get { return System.IO.Path.Combine(DataDir, "config.json"); } }
        public static string BadWordsPath { get { return System.IO.Path.Combine(DataDir, "badwords.txt"); } }
        public static string BlocklistPath { get { return System.IO.Path.Combine(DataDir, "blocklist.txt"); } }
        public static string WhitelistPath { get { return System.IO.Path.Combine(DataDir, "whitelist.txt"); } }
        public static string AdHostsPath { get { return System.IO.Path.Combine(DataDir, "adblock-domains.txt"); } }
        public static string ExtendedBlocklistPath { get { return System.IO.Path.Combine(DataDir, "blocklist-extended.txt"); } }
        public static string DisableFlagPath { get { return System.IO.Path.Combine(DataDir, "DISABLE"); } }

        /// <summary>Reads config.json and publishes the "games" switch and its exceptions to State (see State.GamesAllowed).</summary>
        public static Config Load()
        {
            Config c = LoadFile();
            State.GamesAllowed = c.GamesOpen();
            HostList ex = new HostList();
            ex.AddRange(c.GameExceptions);
            State.GameExceptions = ex;
            return c;
        }

        private static Config LoadFile()
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
                        if (c.Browser == null) c.Browser = new BrowserSettings();
                        if (c.Wifi == null) c.Wifi = new WifiSettings();
                        if (c.Wifi.AllowedSsids == null) c.Wifi.AllowedSsids = new List<string>();
                        if (c.Browser.VideoSites == null || c.Browser.VideoSites.Count == 0) c.Browser.VideoSites = new Config().Browser.VideoSites;
                        if (c.Sections == null || c.Sections.Count == 0) c.Sections = new Config().Sections;
                        if (c.CategoryRules == null || c.CategoryRules.Count == 0) c.CategoryRules = new Config().CategoryRules;
                        c.MergeDefaults();
                        if (c.AppCategories == null) c.AppCategories = new Dictionary<string, string>();
                        if (c.HiddenExact == null) c.HiddenExact = new List<string>();
                        if (c.AlwaysShow == null) c.AlwaysShow = new List<string>();
                        if (string.IsNullOrEmpty(c.Edition)) c.Edition = StudentEdition;
                        if (c.GameExceptions == null) c.GameExceptions = new List<string>();
                        c.ApplyEdition();
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

        /// <summary>Forbidden words: the list that ships with the program (so updates reach old installs) plus the administrator's own.</summary>
        public static List<Regex> LoadBadWords(Config cfg)
        {
            List<Regex> all = LoadBadWords(System.IO.Path.Combine(AppInfo.InstallDir, "config", BadWordsFile(cfg)));
            all.AddRange(LoadBadWords(System.IO.Path.Combine(DataDir, BadWordsExtraFile(cfg))));
            return all;
        }

        /// <summary>Student edition: the strict list. Teacher edition: only unmistakable porn markers, so "sex education" or a nude in an art lesson is not closed.</summary>
        public static string BadWordsFile(Config cfg) { return cfg != null && cfg.IsTeacher() ? "badwords-teacher.txt" : "badwords.txt"; }
        /// <summary>The administrator's own additions (student edition keeps its historic file name).</summary>
        public static string BadWordsExtraFile(Config cfg) { return cfg != null && cfg.IsTeacher() ? "badwords-extra.txt" : "badwords.txt"; }

        public static List<Regex> LoadBadWords(string path)
        {
            List<Regex> res = new List<Regex>();
            try
            {
                if (!File.Exists(path)) return res;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
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
