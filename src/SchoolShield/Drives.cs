using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;

namespace SchoolShield
{
    internal sealed class DriveEntry
    {
        public string Letter;     // "E:"
        public string Root;       // "E:\"
        public string Label;      // volume label, may be empty
        public string Kind;       // "Флешка", "Внешний диск", "Диск (CD/DVD)"
        public long Total;
        public long Free;
        public bool Usb;

        public string Title
        {
            get { return Kind + (string.IsNullOrEmpty(Label) ? "" : " «" + Label + "»") + " (" + Letter + ")"; }
        }
    }

    /// <summary>
    /// Flash drives and external disks. Removable media (USB sticks, card readers, CD/DVD) are found through DriveInfo; external USB hard
    /// disks and SSDs report themselves as fixed disks, so those are recognised through WMI (Win32_DiskDrive with InterfaceType USB).
    /// The WMI lookup is slow, so it runs in the background and is cached until the set of drives changes.
    /// </summary>
    internal static class Drives
    {
        private static readonly object Gate = new object();
        private static HashSet<string> usbLetters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string usbSignature = "";
        private static bool usbBusy;

        private static string SystemRoot
        {
            get { try { return Path.GetPathRoot(Environment.SystemDirectory); } catch { return "C:\\"; } }
        }

        /// <summary>Cheap fingerprint of the drive list: changes when a drive appears, disappears or becomes ready.</summary>
        public static string Signature()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    // Network drives are skipped: asking one that is offline can block for seconds. IsReady is asked of removable media and discs only.
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.CDRom && d.DriveType != DriveType.Fixed) continue;
                    bool ready = d.DriveType == DriveType.Fixed || d.IsReady;
                    sb.Append(d.Name).Append(':').Append((int)d.DriveType).Append(':').Append(ready ? '1' : '0').Append(';');
                }
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>Connected flash drives, external disks and discs. Empty card readers are left out.</summary>
        public static List<DriveEntry> List()
        {
            List<DriveEntry> res = new List<DriveEntry>();
            HashSet<string> usb = UsbLetters();
            string sys = SystemRoot;
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); }
            catch { return res; }
            foreach (DriveInfo d in all)
            {
                try
                {
                    if (string.Equals(d.Name, sys, StringComparison.OrdinalIgnoreCase)) continue;
                    string letter = d.Name.TrimEnd('\\');
                    string kind = null;
                    bool isUsb = usb.Contains(letter);
                    if (d.DriveType == DriveType.Removable) kind = isUsb ? "Флешка" : "Съёмный диск";
                    else if (d.DriveType == DriveType.CDRom) kind = "Диск (CD/DVD)";
                    else if (d.DriveType == DriveType.Fixed && isUsb) kind = "Внешний диск";
                    if (kind == null || !d.IsReady) continue;

                    DriveEntry e = new DriveEntry();
                    e.Letter = letter;
                    e.Root = d.Name;
                    e.Kind = kind;
                    e.Usb = isUsb;
                    try { e.Label = d.VolumeLabel; } catch { e.Label = ""; }
                    try { e.Total = d.TotalSize; e.Free = d.AvailableFreeSpace; } catch { }
                    res.Add(e);
                }
                catch { /* the drive vanished while we looked */ }
            }
            return res;
        }

        /// <summary>The drives as roots of the file manager. Read-only unless the administrator allowed writing to flash drives.</summary>
        public static List<FileRoot> Roots(Config cfg)
        {
            List<FileRoot> roots = new List<FileRoot>();
            string mode = (cfg.UsbMode ?? "readonly").ToLowerInvariant();
            if (mode == "block" && !State.Privileged) return roots;
            foreach (DriveEntry d in List())
            {
                FileRoot r = new FileRoot();
                r.Name = d.Title + (d.Total > 0 ? "  —  свободно " + FileIndex.Size(d.Free) + " из " + FileIndex.Size(d.Total) : "");
                r.Path = d.Root;
                r.ReadOnly = (mode != "allow" && !State.Privileged) || d.Kind.StartsWith("Диск (CD");
                r.NoIndex = true;
                r.Removable = true;
                r.Letter = d.Letter;
                roots.Add(r);
            }
            return roots;
        }

        public static bool IsOnRemovable(string path, Config cfg)
        {
            try { return FileRoots.RootOf(Roots(cfg), path) != null; }
            catch { return false; }
        }

        // ---------------------------------------------------------------- WMI (USB bus)

        private static HashSet<string> UsbLetters()
        {
            string sig = Signature();
            lock (Gate)
            {
                if (sig != usbSignature && !usbBusy)
                {
                    usbBusy = true;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        HashSet<string> found = QueryUsb();
                        lock (Gate) { usbLetters = found; usbSignature = sig; usbBusy = false; }
                    });
                }
                return new HashSet<string>(usbLetters, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Is the very latest answer already in? (False while the first WMI query is still running.)</summary>
        public static bool Settled { get { lock (Gate) return !usbBusy && usbSignature == Signature(); } }

        private static HashSet<string> QueryUsb()
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (ManagementObjectSearcher disks = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                foreach (ManagementObject disk in disks.Get())
                {
                    string id = Convert.ToString(disk["DeviceID"]).Replace("\\", "\\\\");
                    using (ManagementObjectSearcher parts = new ManagementObjectSearcher(
                        "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + id + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                    foreach (ManagementObject part in parts.Get())
                    {
                        using (ManagementObjectSearcher logical = new ManagementObjectSearcher(
                            "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + Convert.ToString(part["DeviceID"]) + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                        foreach (ManagementObject ld in logical.Get())
                        {
                            string letter = Convert.ToString(ld["DeviceID"]);
                            if (!string.IsNullOrEmpty(letter)) set.Add(letter);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("usb", "Поиск USB-дисков (WMI): " + ex.Message); }
            return set;
        }

        // ---------------------------------------------------------------- eject

        /// <summary>"Safely remove": the same verb Explorer uses. Returns false if Windows refused (a file on the drive is still open).</summary>
        public static bool Eject(string letter)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                dynamic shell = Activator.CreateInstance(t);
                dynamic item = shell.NameSpace(17).ParseName(letter.TrimEnd('\\') + "\\");   // 17 = "My Computer"
                if (item == null) return false;
                item.InvokeVerb("Eject");
                for (int i = 0; i < 10; i++)         // Windows needs a moment to take the drive away
                {
                    Thread.Sleep(300);
                    bool still = false;
                    foreach (DriveEntry d in List())
                        if (string.Equals(d.Letter, letter.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) still = true;
                    if (!still) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("usb", "Извлечение " + letter + ": " + ex.Message);
                return false;
            }
        }
    }
}
