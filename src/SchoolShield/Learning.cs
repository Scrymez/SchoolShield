using System;
using System.Collections.Generic;
using System.IO;

namespace SchoolShield
{
    /// <summary>
    /// "Learning yes, installers no": what a student sees in the launcher's file lists (My files and Flash drives).
    /// Folders are always shown. A file is shown only if its type is educational: documents, tables, presentations, books, pictures, music,
    /// video, code, sketches (Arduino, Scratch), CAD / 3D models and everything of LEGO Education robotics (EV3, SPIKE, Mindstorms).
    /// Installers, scripts, shortcuts and archives never appear, whatever the administrator adds. Administrators and teachers see everything.
    /// </summary>
    internal static class FileFilter
    {
        private static readonly HashSet<string> Learning = Build(
            // documents, books, text
            ".doc", ".docx", ".dot", ".dotx", ".odt", ".rtf", ".txt", ".md", ".log", ".pdf", ".djvu", ".djv", ".epub", ".fb2", ".tex",
            // tables and databases
            ".xls", ".xlsx", ".xlt", ".xltx", ".ods", ".csv", ".db", ".sqlite", ".mdb", ".accdb",
            // presentations, interactive whiteboards
            ".ppt", ".pptx", ".pps", ".ppsx", ".pot", ".potx", ".odp", ".notebook", ".flipchart", ".iwb",
            // pictures and drawings
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".svg", ".ico", ".xcf", ".kra", ".psd", ".odg", ".vsd", ".vsdx", ".dia", ".cdr", ".ai",
            // video and audio
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".mpg", ".mpeg", ".m4v",
            ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".wma", ".aac", ".opus", ".mid", ".midi", ".mscz", ".mscx", ".aup3", ".kdenlive",
            // programming: Python, Pascal, КуМир, C/C++, C#, Java, web, SQL
            ".py", ".pyw", ".ipynb", ".pas", ".pp", ".dpr", ".lpr", ".lpi", ".lfm", ".pabc", ".kum", ".kumir",
            ".c", ".cc", ".cpp", ".cxx", ".h", ".hpp", ".cs", ".csproj", ".sln", ".java", ".vb", ".bas", ".lua", ".rb",
            ".html", ".htm", ".css", ".js", ".json", ".xml", ".yaml", ".yml", ".sql",
            // sketches and microcontrollers: Arduino, Processing, micro:bit, Raspberry Pi Pico, Scratch, mBlock, Fritzing, KiCad, TRIK Studio
            ".ino", ".pde", ".hex", ".uf2", ".sb3", ".sb2", ".sb", ".mblock", ".fzz", ".fzpz", ".kicad_pro", ".kicad_sch", ".kicad_pcb", ".qrs",
            // LEGO Education: EV3 (Lab and Classroom), NXT, SPIKE, Robot Inventor, digital builders
            ".ev3", ".ev3p", ".ev3m", ".ev3s", ".rbf", ".rgf", ".rsf", ".rdf", ".rxe", ".rbt",
            ".lms", ".lmsp", ".llsp", ".llsp3", ".lxf", ".lxfml", ".ldr", ".mpd", ".io",
            // CAD, 3D modelling and printing, КОМПАС-3D
            ".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".dxf", ".dwg", ".blend", ".fcstd", ".scad", ".skp", ".gcode", ".f3d",
            ".cdw", ".frw", ".m3d", ".a3d", ".spw", ".t3d", ".sldprt", ".sldasm", ".slddrw",
            // maths and science
            ".ggb", ".mcdx", ".xmind", ".mm", ".cmap");

        // Never shown to a student and never allowed through the administrator's extra list: they install or run code, or hide it.
        private static readonly HashSet<string> Never = Build(
            ".exe", ".msi", ".msp", ".msu", ".msix", ".msixbundle", ".appx", ".appxbundle", ".bat", ".cmd", ".com", ".scr", ".pif",
            ".ps1", ".psm1", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".jar", ".reg", ".inf", ".dll", ".sys", ".cpl", ".msc",
            ".lnk", ".url", ".gadget", ".application", ".apk", ".iso", ".img", ".vhd", ".vhdx", ".torrent", ".crx", ".xpi",
            ".docm", ".dotm", ".xlsm", ".xltm", ".pptm", ".potm", ".ppam", ".xlam", ".sh", ".bin", ".run", ".deb", ".dmg", ".pkg");

        private static HashSet<string> Build(params string[] exts)
        {
            return new HashSet<string>(exts, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The extension of a file name the way Windows sees it ("a.exe." and "a.exe " are ".exe").</summary>
        public static string ExtensionOf(string nameOrPath)
        {
            string name = (Path.GetFileName(nameOrPath ?? "") ?? "").TrimEnd('.', ' ');
            int i = name.LastIndexOf('.');
            return i < 0 ? "" : name.Substring(i).ToLowerInvariant();
        }

        public static bool IsNever(string ext) { return Never.Contains(ext); }

        /// <summary>Is a file of this type educational (built-in list plus the administrator's extra extensions)?</summary>
        public static bool Allowed(string nameOrPath, Config cfg)
        {
            string ext = ExtensionOf(nameOrPath);
            if (ext.Length == 0 || Never.Contains(ext)) return false;
            if (Learning.Contains(ext)) return true;
            if (cfg != null && cfg.AllowedExtensions != null)
                foreach (string x in cfg.AllowedExtensions)
                    if (string.Equals(Normalize(x), ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Does a listing show this entry? Folders always; files by type; administrators and teachers see everything.</summary>
        public static bool Visible(string path, bool isDir, Config cfg)
        {
            if (isDir || State.Privileged) return true;
            return Allowed(path, cfg);
        }

        /// <summary>".EV3" / "ev3" / " .ev3 " -> ".ev3"; an invalid value gives "".</summary>
        public static string Normalize(string ext)
        {
            string e = (ext ?? "").Trim().ToLowerInvariant();
            if (e.Length == 0) return "";
            if (!e.StartsWith(".")) e = "." + e;
            if (e.Length < 2 || e.Length > 16 || e.IndexOfAny(new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', ' ' }) >= 0) return "";
            return e;
        }

        /// <summary>
        /// Copies a folder; a student gets educational files only (the rest is skipped, hidden and system files too).
        /// Returns the number of files copied; <paramref name="skipped"/> counts what was left out.
        /// </summary>
        public static int CopyDirectory(string src, string dst, Config cfg, bool privileged, out int skipped)
        {
            skipped = 0;
            int copied = 0;
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                try
                {
                    if (!privileged && !Allowed(f, cfg)) { skipped++; continue; }
                    if (IsHidden(f)) { skipped++; continue; }
                    File.Copy(f, UniquePath(dst, Path.GetFileName(f)));
                    copied++;
                }
                catch { skipped++; }
            }
            foreach (string d in Directory.GetDirectories(src))
            {
                try
                {
                    if (IsHidden(d)) continue;
                    int s;
                    copied += CopyDirectory(d, UniquePath(dst, Path.GetFileName(d)), cfg, privileged, out s);
                    skipped += s;
                }
                catch { skipped++; }
            }
            return copied;
        }

        public static bool IsHidden(string path)
        {
            try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
            catch { return true; }
        }

        /// <summary>A path inside <paramref name="folder"/> that does not exist yet: "a.docx", "a (2).docx"...</summary>
        public static string UniquePath(string folder, string name)
        {
            string target = Path.Combine(folder, name);
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                target = Path.Combine(folder, stem + " (" + i + ")" + ext);
                if (!File.Exists(target) && !Directory.Exists(target)) return target;
            }
            return Path.Combine(folder, Guid.NewGuid().ToString("N") + ext);
        }
    }
}
