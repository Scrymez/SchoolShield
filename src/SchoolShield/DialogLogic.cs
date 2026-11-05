using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolShield
{
    /// <summary>
    /// The rules behind the launcher's own "Open" and "Save as" windows, without any window code so they can be tested on their own:
    /// reading the file-type list of the program's standard dialog, checking a name typed by a student, quoting several names.
    /// </summary>
    internal static class DialogLogic
    {
        public enum Mode { Unknown, Open, Save }

        // Windows rules, written out: Path.GetInvalidFileNameChars() depends on the platform the code runs on.
        private static readonly char[] InvalidNameChars = "\\/:*?\"<>|\0\t\r\n".ToCharArray();

        private static readonly Regex Pattern = new Regex(@"\*\.(\*|[A-Za-z0-9_+~\-]+)", RegexOptions.Compiled);
        private static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        /// <summary>Open or Save, judged by the text of the dialog's OK button ("&amp;Открыть", "Сохранить", "Save", "Open").</summary>
        public static Mode ModeOf(string okButtonText)
        {
            string t = (okButtonText ?? "").Replace("&", "").Trim().ToLowerInvariant();
            if (t.Contains("сохран") || t.StartsWith("save")) return Mode.Save;
            if (t.Contains("откр") || t.StartsWith("open")) return Mode.Open;
            return Mode.Unknown;
        }

        /// <summary>
        /// "Документы Word (*.docx;*.doc)" gives ["docx", "doc"]. "Все файлы (*.*)" or a text without a pattern gives null: every
        /// educational file matches. The extensions are lower case, without the dot.
        /// </summary>
        public static List<string> ExtensionsOf(string filterText)
        {
            List<string> res = new List<string>();
            foreach (Match m in Pattern.Matches(filterText ?? ""))
            {
                string e = m.Groups[1].Value.ToLowerInvariant();
                if (e == "*") return null;
                if (!res.Contains(e)) res.Add(e);
            }
            return res.Count == 0 ? null : res;
        }

        public static bool MatchesFilter(string fileName, List<string> exts)
        {
            if (exts == null) return true;
            string e = FileFilter.ExtensionOf(fileName);
            return e.Length > 1 && exts.Contains(e.Substring(1));
        }

        public static string DefaultExtension(List<string> exts)
        {
            return exts == null || exts.Count == 0 ? null : exts[0];
        }

        /// <summary>A name typed without an extension gets the one of the chosen file type: "Реферат" + docx gives "Реферат.docx".</summary>
        public static string EnsureExtension(string name, List<string> exts)
        {
            string n = (name ?? "").Trim();
            string def = DefaultExtension(exts);
            if (n.Length == 0 || def == null) return n;
            return FileFilter.ExtensionOf(n).Length == 0 ? n.TrimEnd('.') + "." + def : n;
        }

        /// <summary>
        /// Checks a file name for saving. Returns null when it is fine (the final name, with the extension added, is in
        /// <paramref name="finalName"/>), otherwise the message for the student.
        /// </summary>
        public static string CheckSaveName(string name, List<string> exts, Config cfg, out string finalName)
        {
            finalName = EnsureExtension(name, exts);
            string n = finalName.Trim();
            if (n.Length == 0) return "Введите имя файла.";
            if (n.IndexOfAny(InvalidNameChars) >= 0) return "В имени нельзя использовать знаки \\ / : * ? \" < > |";
            if (n.Length > 150) return "Слишком длинное имя файла.";
            if (n.EndsWith(".") || n.EndsWith(" ")) return "Имя не может заканчиваться точкой или пробелом.";
            string stem = Path.GetFileNameWithoutExtension(n).Trim();
            if (stem.Length == 0) return "Введите имя файла.";
            foreach (string r in Reserved) if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase)) return "Это имя нельзя использовать.";
            if (!FileFilter.Allowed(n, cfg)) return "Файлы этого типа сохранять нельзя.";
            finalName = n;
            return null;
        }

        /// <summary>Several names for the standard dialog's "File name" box: "a.docx" "b.docx" (the dialog accepts that form for multi-select).</summary>
        public static string QuoteNames(IEnumerable<string> names)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string n in names)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('"').Append(n.Replace("\"", "")).Append('"');
            }
            return sb.ToString();
        }

        /// <summary>Is <paramref name="path"/> inside <paramref name="root"/> (a folder), after normalizing "..", slashes and case?</summary>
        public static bool IsInside(string path, string root)
        {
            try
            {
                string p = Path.GetFullPath(path).TrimEnd('\\', '/');
                string r = Path.GetFullPath(root).TrimEnd('\\', '/');
                if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
                return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
