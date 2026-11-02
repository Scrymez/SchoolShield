using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SchoolShield
{
    internal static class AppInfo
    {
        public static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        public static string VersionString
        {
            get { Version v = Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string InstallDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
        public static string ExePath { get { return Path.Combine(InstallDir, "SchoolShield.exe"); } }
        public static string ScriptsDir { get { return Path.Combine(InstallDir, "scripts"); } }
    }

    internal static class Crypto
    {
        public static string NewSalt()
        {
            byte[] b = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider()) rng.GetBytes(b);
            return Convert.ToBase64String(b);
        }

        public static string Hash(string code, string saltB64, int iterations)
        {
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(code, Convert.FromBase64String(saltB64), iterations))
                return Convert.ToBase64String(kdf.GetBytes(32));
        }

        public static bool Verify(string code, Config cfg)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(cfg.AdminHash) || string.IsNullOrEmpty(cfg.AdminSalt))
                return false;
            byte[] a, b;
            try
            {
                a = Convert.FromBase64String(Hash(code, cfg.AdminSalt, cfg.Iterations));
                b = Convert.FromBase64String(cfg.AdminHash);
            }
            catch { return false; }
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string area, string message)
        {
            try
            {
                string dir = Path.Combine(Config.DataDir, "logs");
                string file = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                string line = DateTime.Now.ToString("HH:mm:ss") + " [" + Environment.UserName + "] " + area + ": " + message + Environment.NewLine;
                lock (Gate)
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(file, line, Encoding.UTF8);
                }
            }
            catch { /* logging must never break the shell */ }
        }

        public static string Tail(int lines)
        {
            try
            {
                string file = Path.Combine(Path.Combine(Config.DataDir, "logs"), DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                if (!File.Exists(file)) return "(лог за сегодня пуст)";
                string[] all = File.ReadAllLines(file, Encoding.UTF8);
                int from = Math.Max(0, all.Length - lines);
                StringBuilder sb = new StringBuilder();
                for (int i = from; i < all.Length; i++) sb.AppendLine(all[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return "Не удалось прочитать лог: " + ex.Message; }
        }
    }

    /// <summary>Process-wide state shared by hook, guard and UI.</summary>
    internal static class State
    {
        private static DateTime adminUntil = DateTime.MinValue;
        public static volatile bool Preview;
        public static volatile bool ExitRequested;

        public static bool AdminMode { get { return DateTime.Now < adminUntil; } }

        public static TimeSpan AdminLeft
        {
            get { return AdminMode ? adminUntil - DateTime.Now : TimeSpan.Zero; }
        }

        public static void EnableAdmin(int minutes)
        {
            adminUntil = DateTime.Now.AddMinutes(Math.Max(1, minutes));
            Log.Write("admin", "Админ-режим включён на " + minutes + " мин");
        }

        public static void DisableAdmin()
        {
            adminUntil = DateTime.MinValue;
            Log.Write("admin", "Админ-режим выключен");
        }
    }
}
