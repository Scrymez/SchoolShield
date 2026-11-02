using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SchoolShield
{
    // ------------------------------------------------------------------ per-student preferences

    /// <summary>Favorites, recent items and theme of the student. Stored in the student's own %APPDATA%.</summary>
    internal sealed class UserData
    {
        public List<string> Favorites = new List<string>();
        public List<string> RecentApps = new List<string>();
        public List<string> RecentFiles = new List<string>();
        public string Theme = "dark";

        private static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchoolShield", "user.json");
            }
        }

        public static UserData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    UserData u = new JavaScriptSerializer().Deserialize<UserData>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (u != null)
                    {
                        if (u.Favorites == null) u.Favorites = new List<string>();
                        if (u.RecentApps == null) u.RecentApps = new List<string>();
                        if (u.RecentFiles == null) u.RecentFiles = new List<string>();
                        return u;
                    }
                }
            }
            catch { }
            return new UserData();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void Touch(List<string> list, string value, int max)
        {
            list.RemoveAll(delegate (string s) { return string.Equals(s, value, StringComparison.OrdinalIgnoreCase); });
            list.Insert(0, value);
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
        }

        public void AddRecentApp(string name) { Touch(RecentApps, name, 12); Save(); }
        public void AddRecentFile(string path) { Touch(RecentFiles, path, 20); Save(); }

        public bool IsFavorite(string name)
        {
            return Favorites.Exists(delegate (string s) { return string.Equals(s, name, StringComparison.OrdinalIgnoreCase); });
        }

        public void ToggleFavorite(string name)
        {
            if (IsFavorite(name))
                Favorites.RemoveAll(delegate (string s) { return string.Equals(s, name, StringComparison.OrdinalIgnoreCase); });
            else Favorites.Add(name);
            Save();
        }
    }

    // ------------------------------------------------------------------ shell icons for file types

    internal static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        /// <summary>Icon for a file extension (or a folder) without touching the disk. 32 px.</summary>
        public static Bitmap Get(string ext, bool folder)
        {
            string key = folder ? "<dir>" : (ext ?? "").ToLowerInvariant();
            Bitmap bmp;
            if (Cache.TryGetValue(key, out bmp)) return bmp;
            bmp = null;
            try
            {
                SHFILEINFO fi = new SHFILEINFO();
                uint attrs = folder ? 0x10u : 0x80u;   // DIRECTORY / NORMAL
                string name = folder ? "folder" : "file" + (key.Length > 0 ? key : ".txt");
                IntPtr r = SHGetFileInfo(name, attrs, ref fi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    0x100 /* ICON */ | 0x10 /* USEFILEATTRIBUTES */);
                if (r != IntPtr.Zero && fi.hIcon != IntPtr.Zero)
                {
                    using (Icon ic = Icon.FromHandle(fi.hIcon)) bmp = ic.ToBitmap();
                    DestroyIcon(fi.hIcon);
                }
            }
            catch { }
            Cache[key] = bmp;
            return bmp;
        }
    }

    // ------------------------------------------------------------------ files

    internal enum FileKind { Other, Document, Presentation, Table, Book, Text, Image, Video, Audio }

    internal sealed class FileItem
    {
        public string Path;
        public string Name;
        public string Folder;
        public DateTime Modified;
        public long Size;
        public FileKind Kind;
    }

    internal sealed class FileRoot
    {
        public string Name;
        public string Path;
        public bool ReadOnly;
    }

    internal static class FileKinds
    {
        private static readonly Dictionary<string, FileKind> Map = Build();

        private static Dictionary<string, FileKind> Build()
        {
            Dictionary<string, FileKind> m = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
            Add(m, FileKind.Document, ".doc", ".docx", ".docm", ".dot", ".dotx", ".odt", ".rtf");
            Add(m, FileKind.Presentation, ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".pot", ".potx", ".odp");
            Add(m, FileKind.Table, ".xls", ".xlsx", ".xlsm", ".xlt", ".xltx", ".csv", ".ods");
            Add(m, FileKind.Book, ".pdf", ".djvu", ".djv", ".epub", ".fb2");
            Add(m, FileKind.Text, ".txt", ".md", ".log");
            Add(m, FileKind.Image, ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff");
            Add(m, FileKind.Video, ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".m4v");
            Add(m, FileKind.Audio, ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".wma", ".aac");
            return m;
        }

        private static void Add(Dictionary<string, FileKind> m, FileKind k, params string[] exts)
        {
            foreach (string e in exts) m[e] = k;
        }

        public static FileKind Of(string path)
        {
            FileKind k;
            return Map.TryGetValue(System.IO.Path.GetExtension(path) ?? "", out k) ? k : FileKind.Other;
        }
    }

    internal static class FileRoots
    {
        public static List<FileRoot> Resolve(Config cfg)
        {
            List<FileRoot> list = new List<FileRoot>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(list, "Мои документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false);
            Add(list, "Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false);
            Add(list, "Загрузки", Path.Combine(profile, "Downloads"), false);
            Add(list, "Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), false);
            Add(list, "Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), false);
            Add(list, "Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), false);
            string pub = Environment.GetEnvironmentVariable("PUBLIC");
            if (!string.IsNullOrEmpty(pub)) Add(list, "Общая папка", Path.Combine(pub, "Documents"), false);
            foreach (SharedFolder s in cfg.SharedFolders)
                if (s != null && !string.IsNullOrEmpty(s.Path)) Add(list, s.Name ?? s.Path, s.Path, s.ReadOnly);
            return list;
        }

        private static void Add(List<FileRoot> list, string name, string path, bool ro)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                string full = Normalize(path);
                foreach (FileRoot r in list)
                    if (string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase)) return;
                FileRoot root = new FileRoot();
                root.Name = name;
                root.Path = full;
                root.ReadOnly = ro;
                list.Add(root);
            }
            catch { }
        }

        public static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }

        /// <summary>The root that contains <paramref name="path"/>, or null if it is outside every allowed folder.</summary>
        public static FileRoot RootOf(List<FileRoot> roots, string path)
        {
            string full;
            try { full = Normalize(path); }
            catch { return null; }
            FileRoot best = null;
            foreach (FileRoot r in roots)
            {
                bool inside = string.Equals(full, r.Path, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(r.Path + "\\", StringComparison.OrdinalIgnoreCase);
                if (inside && (best == null || r.Path.Length > best.Path.Length)) best = r;
            }
            return best;
        }
    }

    internal static class FileIndex
    {
        /// <summary>Scans the allowed folders (never anything outside them). Runs on a worker thread.</summary>
        public static List<FileItem> Scan(List<FileRoot> roots, Predicate<FileKind> want, int max)
        {
            List<FileItem> result = new List<FileItem>();
            foreach (FileRoot r in roots)
            {
                if (result.Count >= max) break;
                Walk(r.Path, 0, want, result, max);
            }
            result.Sort(delegate (FileItem a, FileItem b) { return b.Modified.CompareTo(a.Modified); });
            return result;
        }

        private static void Walk(string dir, int depth, Predicate<FileKind> want, List<FileItem> result, int max)
        {
            if (depth > 8 || result.Count >= max) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (result.Count >= max) return;
                    FileKind k = FileKinds.Of(f);
                    if (k == FileKind.Other || !want(k)) continue;
                    FileInfo fi = new FileInfo(f);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    FileItem it = new FileItem();
                    it.Path = f;
                    it.Name = fi.Name;
                    it.Folder = dir;
                    it.Modified = fi.LastWriteTime;
                    it.Size = fi.Length;
                    it.Kind = k;
                    result.Add(it);
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith(".") || name.StartsWith("$")) continue;
                    try
                    {
                        if ((File.GetAttributes(d) & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch { continue; }
                    Walk(d, depth + 1, want, result, max);
                }
            }
            catch { /* no access / path vanished */ }
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " Б";
            if (bytes < 1024 * 1024) return (bytes / 1024) + " КБ";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " МБ";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " ГБ";
        }
    }

    // ------------------------------------------------------------------ lesson bells

    internal static class Bells
    {
        /// <summary>"Урок 3 • до звонка 12 мин" / "Перемена • урок через 5 мин" / null when outside the schedule.</summary>
        public static string Status(Config cfg, DateTime now)
        {
            if (cfg.Bells == null || cfg.Bells.Count == 0) return null;
            TimeSpan t = now.TimeOfDay;
            TimeSpan prevEnd = TimeSpan.Zero;
            bool first = true;
            foreach (BellItem b in cfg.Bells)
            {
                TimeSpan s, e;
                if (!TimeSpan.TryParse(b.Start, CultureInfo.InvariantCulture, out s) || !TimeSpan.TryParse(b.End, CultureInfo.InvariantCulture, out e)) continue;
                if (t >= s && t < e) return b.Title + "  •  до звонка " + Math.Max(1, (int)Math.Ceiling((e - t).TotalMinutes)) + " мин";
                if (t < s)
                {
                    if (first) return null;   // before the first lesson
                    return "Перемена  •  " + b.Title + " через " + Math.Max(1, (int)Math.Ceiling((s - t).TotalMinutes)) + " мин";
                }
                prevEnd = e;
                first = false;
            }
            return null;
        }
    }

    // ------------------------------------------------------------------ keyboard layout

    internal static class InputLang
    {
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int n, IntPtr[] list);

        /// <summary>"RU" / "EN" for the window that currently has the keyboard.</summary>
        public static string Current()
        {
            try
            {
                IntPtr hkl = LayoutOfForeground();
                int lang = (int)((long)hkl & 0xFFFF);
                return new CultureInfo(lang).TwoLetterISOLanguageName.ToUpperInvariant();
            }
            catch { return "??"; }
        }

        private static IntPtr LayoutOfForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            uint pid;
            uint tid = Native.GetWindowThreadProcessId(fg, out pid);
            return GetKeyboardLayout(tid);
        }

        /// <summary>Switch to the next installed layout (the shell has no language bar, so the dock provides it).</summary>
        public static void Toggle()
        {
            try
            {
                int n = GetKeyboardLayoutList(0, null);
                if (n < 2) return;
                IntPtr[] list = new IntPtr[n];
                GetKeyboardLayoutList(n, list);
                IntPtr cur = LayoutOfForeground();
                int idx = Array.IndexOf(list, cur);
                IntPtr next = list[(idx + 1) % n];
                Native.PostMessage(Native.GetForegroundWindow(), 0x0050 /* WM_INPUTLANGCHANGEREQUEST */, IntPtr.Zero, next);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ system volume (Core Audio)

    internal static class AudioControl
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        private static IAudioEndpointVolume endpoint;
        private static bool failed;

        private static IAudioEndpointVolume Get()
        {
            if (endpoint != null) return endpoint;
            if (failed) return null;
            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out dev) != 0) { failed = true; return null; }
                Guid iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                object o;
                if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out o) != 0) { failed = true; return null; }
                endpoint = (IAudioEndpointVolume)o;
                return endpoint;
            }
            catch
            {
                failed = true;
                return null;
            }
        }

        public static bool Available { get { return Get() != null; } }

        public static float Volume
        {
            get
            {
                IAudioEndpointVolume e = Get();
                float v;
                return e != null && e.GetMasterVolumeLevelScalar(out v) == 0 ? v : 0f;
            }
            set
            {
                IAudioEndpointVolume e = Get();
                Guid g = Guid.Empty;
                if (e != null) e.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, value)), ref g);
            }
        }

        public static bool Muted
        {
            get
            {
                IAudioEndpointVolume e = Get();
                bool m;
                return e != null && e.GetMute(out m) == 0 && m;
            }
            set
            {
                IAudioEndpointVolume e = Get();
                Guid g = Guid.Empty;
                if (e != null) e.SetMute(value, ref g);
            }
        }
    }

    // ------------------------------------------------------------------ admin chord (held, not tapped)

    internal sealed class Chord
    {
        private readonly List<int> keys = new List<int>();
        private DateTime heldSince = DateTime.MinValue;
        private bool fired;

        public readonly string Spec;
        public bool Valid { get { return keys.Count >= 2; } }

        /// <summary>How long the whole chord has been held, in ms (0 when it is not fully pressed).</summary>
        public int HeldMs { get { return heldSince == DateTime.MinValue ? 0 : (int)(DateTime.Now - heldSince).TotalMilliseconds; } }

        public Chord(string spec)
        {
            Spec = spec ?? "";
            foreach (string part in Spec.Split('+'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                if (p.Equals("ctrl", StringComparison.OrdinalIgnoreCase)) keys.Add(Native.VK_CONTROL);
                else if (p.Equals("shift", StringComparison.OrdinalIgnoreCase)) keys.Add(Native.VK_SHIFT);
                else if (p.Equals("alt", StringComparison.OrdinalIgnoreCase)) keys.Add(Native.VK_MENU);
                else
                {
                    try { keys.Add((int)(Keys)Enum.Parse(typeof(Keys), p, true)); }
                    catch { Log.Write("hotkey", "Неизвестная клавиша в AdminHotkey: " + p); }
                }
            }
        }

        /// <summary>Call every ~100 ms. Returns true once when the whole chord has been held for holdMs.</summary>
        public bool Poll(int holdMs)
        {
            if (!Valid) return false;
            foreach (int vk in keys)
            {
                if (!KeyState.IsDown(vk))
                {
                    heldSince = DateTime.MinValue;
                    fired = false;
                    return false;
                }
            }
            if (heldSince == DateTime.MinValue) heldSince = DateTime.Now;
            if (!fired && HeldMs >= holdMs)
            {
                fired = true;
                return true;
            }
            return false;
        }
    }
}
