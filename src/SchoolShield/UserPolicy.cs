using System;
using Microsoft.Win32;

namespace SchoolShield
{
    /// <summary>
    /// Per-user (HKCU) restrictions. They are written by the launcher at the start of every STUDENT session,
    /// so administrators and any future admin accounts are never affected.
    /// </summary>
    internal static class UserPolicy
    {
        private const string RemovableGuid = "{53f5630d-b6bf-11d0-94f2-00a0c91efb8b}";

        private static void SetDword(string path, string name, int value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(path))
                    k.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch (Exception ex) { Log.Write("policy", path + "\\" + name + ": " + ex.Message); }
        }

        private static void SetString(string path, string name, string value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(path))
                    k.SetValue(name, value, RegistryValueKind.String);
            }
            catch (Exception ex) { Log.Write("policy", path + "\\" + name + ": " + ex.Message); }
        }

        private static void DeleteValue(string path, string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(path, true))
                    if (k != null) k.DeleteValue(name, false);
            }
            catch { }
        }


        /// <summary>
        /// "Save as" / "Open" windows of every program: saving into Documents keeps working, but the student cannot wander
        /// around the computer. Drives and Network are hidden (paths still work), the shell right-click menu is off
        /// (no "Open", "Send to", "Properties"), recent-file lists are not shown. Listing of drive roots is denied by the
        /// installer through NTFS permissions, so even a typed path "C:\\" shows nothing. Switched off while the
        /// administrator works on the real Windows desktop.
        /// </summary>
        public static void DialogLock(bool on)
        {
            const string exp = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            const string net = @"Software\Microsoft\Windows\CurrentVersion\Policies\Network";
            const string dlg = @"Software\Microsoft\Windows\CurrentVersion\Policies\comdlg32";
            if (on)
            {
                SetDword(exp, "NoDrives", 0x3FFFFFF);
                SetDword(exp, "NoNetHood", 1);
                SetDword(exp, "NoComputersNearMe", 1);
                SetDword(exp, "NoViewContextMenu", 1);
                SetDword(net, "NoEntireNetwork", 1);
                SetDword(dlg, "NoPlacesBar", 1);
                SetDword(dlg, "NoFileMru", 1);
            }
            else
            {
                DeleteValue(exp, "NoDrives");
                DeleteValue(exp, "NoNetHood");
                DeleteValue(exp, "NoComputersNearMe");
                DeleteValue(exp, "NoViewContextMenu");
                DeleteValue(net, "NoEntireNetwork");
                DeleteValue(dlg, "NoPlacesBar");
                DeleteValue(dlg, "NoFileMru");
            }
        }

        public static void Apply(Config cfg)
        {
            const string sys = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
            const string exp = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

            SetDword(sys, "DisableTaskMgr", 1);
            SetDword(sys, "DisableRegistryTools", 1);
            SetDword(sys, "DisableChangePassword", 1);
            SetDword(exp, "NoRun", 1);
            SetDword(exp, "NoControlPanel", 1);      // also hides the Settings app
            SetDword(exp, "NoWinKeys", 1);
            SetDword(exp, "NoDriveTypeAutoRun", 255);
            SetDword(exp, "NoNetConnectDisconnect", 1);
            SetDword(@"Software\Policies\Microsoft\Windows\System", "DisableCMD", 1);

            DialogLock(true);

            // Dark mode for system windows (title bars, file dialogs of programs that follow it) to match the launcher.
            int light = Theme.Dark ? 0 : 1;
            SetDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", light);
            SetDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", light);

            // Sticky keys / filter keys / toggle keys pop-ups can open Ease of Access settings.
            SetString(@"Control Panel\Accessibility\StickyKeys", "Flags", "506");
            SetString(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122");
            SetString(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58");

            ApplyUsb(cfg);
            Log.Write("policy", "Пользовательские ограничения применены (флешки: " + (cfg.UsbMode ?? "readonly") + ")");
        }

        /// <summary>
        /// Flash drives for the student. "block": Windows denies everything on removable media. "readonly": reading and copying from a flash
        /// drive works, writing and running do not. "allow": reading and writing work, running from a flash drive stays blocked
        /// (programs only start from Windows and Program Files). The launcher calls this again when the administrator changes the mode.
        /// </summary>
        public static void ApplyUsb(Config cfg)
        {
            string rs = @"Software\Policies\Microsoft\Windows\RemovableStorageDevices\" + RemovableGuid;
            string mode = (cfg.UsbMode ?? "readonly").ToLowerInvariant();
            if (mode == "block")
            {
                SetDword(rs, "Deny_Read", 1);
                SetDword(rs, "Deny_Write", 1);
                SetDword(rs, "Deny_Execute", 1);
            }
            else if (mode == "readonly")
            {
                DeleteValue(rs, "Deny_Read");
                SetDword(rs, "Deny_Write", 1);
                SetDword(rs, "Deny_Execute", 1);
            }
            else
            {
                DeleteValue(rs, "Deny_Read");
                DeleteValue(rs, "Deny_Write");
                SetDword(rs, "Deny_Execute", 1);       // programs never run from a flash drive, even when writing is allowed
            }
        }
    }
}
