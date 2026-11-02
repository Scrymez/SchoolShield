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

            // Sticky keys / filter keys / toggle keys pop-ups can open Ease of Access settings.
            SetString(@"Control Panel\Accessibility\StickyKeys", "Flags", "506");
            SetString(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122");
            SetString(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58");

            string rs = @"Software\Policies\Microsoft\Windows\RemovableStorageDevices\" + RemovableGuid;
            string mode = (cfg.UsbMode ?? "block").ToLowerInvariant();
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
                DeleteValue(rs, "Deny_Execute");
            }

            Log.Write("policy", "Пользовательские ограничения применены (USB: " + mode + ")");
        }
    }
}
