using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace SchoolShield
{
    internal sealed class WifiNetwork
    {
        public string Ssid;
        public string Profile;        // saved Windows profile (empty = none)
        public int Signal;            // 0..100
        public bool Secured;
        public bool Connected;
        public bool HasProfile { get { return !string.IsNullOrEmpty(Profile); } }
    }

    /// <summary>
    /// Wi-Fi without the Windows UI (a shell without Explorer has no network flyout): scan, connect, disconnect through the
    /// native WLAN API. Students can only connect to networks the administrator saved as profiles (and allowed in the
    /// settings), so a phone hotspot cannot be used to get around the filters. Profiles are created by the Admin panel.
    /// </summary>
    internal static class Wifi
    {
        private const uint ClientVersion = 2;

        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters p, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid iface, int opCode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, string xml, string security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanDeleteProfile(IntPtr handle, ref Guid iface, string name, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr p);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ConnectionParameters
        {
            public int Mode;                       // 0 = wlan_connection_mode_profile
            [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
            public IntPtr Ssid;
            public IntPtr BssidList;
            public int BssType;                    // 1 = infrastructure
            public uint Flags;
        }

        // Fixed layouts of the native structures (sizes verified against the Windows SDK headers).
        private const int InterfaceInfoSize = 532;      // GUID(16) + WCHAR[256] + state(4)
        private const int AvailableNetworkSize = 628;
        private const int ProfileInfoSize = 516;        // WCHAR[256] + flags(4)

        private sealed class Session : IDisposable
        {
            public IntPtr Handle;
            public Guid Iface;
            public bool HasInterface;

            public Session()
            {
                uint neg;
                if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out neg, out Handle) != 0) { Handle = IntPtr.Zero; return; }
                IntPtr list;
                if (WlanEnumInterfaces(Handle, IntPtr.Zero, out list) != 0) return;
                try
                {
                    int count = Marshal.ReadInt32(list, 0);
                    if (count > 0)
                    {
                        byte[] g = new byte[16];
                        Marshal.Copy(IntPtr.Add(list, 8), g, 0, 16);
                        Iface = new Guid(g);
                        HasInterface = true;
                    }
                }
                finally { WlanFreeMemory(list); }
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero) WlanCloseHandle(Handle, IntPtr.Zero);
            }
        }

        public static bool Available
        {
            get
            {
                try { using (Session s = new Session()) return s.HasInterface; }
                catch { return false; }
            }
        }

        private static string Utf16(IntPtr p, int offset, int maxChars)
        {
            string s = Marshal.PtrToStringUni(IntPtr.Add(p, offset), maxChars);
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        /// <summary>Visible networks (strongest first). Asks the adapter to rescan; the list improves on the next call.</summary>
        public static List<WifiNetwork> Scan(bool rescan)
        {
            List<WifiNetwork> result = new List<WifiNetwork>();
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return result;
                    Guid g = s.Iface;
                    if (rescan) WlanScan(s.Handle, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    IntPtr list;
                    if (WlanGetAvailableNetworkList(s.Handle, ref g, 0, IntPtr.Zero, out list) != 0) return result;
                    try
                    {
                        int count = Marshal.ReadInt32(list, 0);
                        Dictionary<string, WifiNetwork> bySsid = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr item = IntPtr.Add(list, 8 + i * AvailableNetworkSize);
                            WifiNetwork n = new WifiNetwork();
                            n.Profile = Utf16(item, 0, 256);
                            int len = Marshal.ReadInt32(item, 512);
                            byte[] ssid = new byte[Math.Min(32, Math.Max(0, len))];
                            Marshal.Copy(IntPtr.Add(item, 516), ssid, 0, ssid.Length);
                            n.Ssid = Encoding.UTF8.GetString(ssid);
                            n.Signal = Marshal.ReadInt32(item, 604);
                            n.Secured = Marshal.ReadInt32(item, 608) != 0;
                            n.Connected = (Marshal.ReadInt32(item, 620) & 1) != 0;
                            if (n.Ssid.Length == 0) continue;
                            WifiNetwork old;
                            if (bySsid.TryGetValue(n.Ssid, out old))
                            {
                                if (n.Signal > old.Signal) old.Signal = n.Signal;
                                old.Connected |= n.Connected;
                                if (!old.HasProfile) old.Profile = n.Profile;
                            }
                            else bySsid[n.Ssid] = n;
                        }
                        result.AddRange(bySsid.Values);
                    }
                    finally { WlanFreeMemory(list); }
                }
            }
            catch (Exception ex) { Log.Write("wifi", "Поиск сетей: " + ex.Message); }
            result.Sort(delegate (WifiNetwork a, WifiNetwork b) { return b.Signal.CompareTo(a.Signal); });
            return result;
        }

        /// <summary>The network we are connected to now, or null.</summary>
        public static WifiNetwork Current()
        {
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return null;
                    Guid g = s.Iface;
                    uint size;
                    IntPtr data;
                    int type;
                    if (WlanQueryInterface(s.Handle, ref g, 7 /* current connection */, IntPtr.Zero, out size, out data, out type) != 0) return null;
                    try
                    {
                        if (Marshal.ReadInt32(data, 0) != 1) return null;   // not "connected"
                        WifiNetwork n = new WifiNetwork();
                        n.Profile = Utf16(data, 8, 256);
                        int len = Marshal.ReadInt32(data, 520);
                        byte[] ssid = new byte[Math.Min(32, Math.Max(0, len))];
                        Marshal.Copy(IntPtr.Add(data, 524), ssid, 0, ssid.Length);
                        n.Ssid = Encoding.UTF8.GetString(ssid);
                        if (n.Ssid.Length == 0) n.Ssid = n.Profile;
                        n.Signal = Marshal.ReadInt32(data, 576);
                        n.Connected = true;
                        return n;
                    }
                    finally { WlanFreeMemory(data); }
                }
            }
            catch { return null; }
        }

        public static List<string> Profiles()
        {
            List<string> res = new List<string>();
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return res;
                    Guid g = s.Iface;
                    IntPtr list;
                    if (WlanGetProfileList(s.Handle, ref g, IntPtr.Zero, out list) != 0) return res;
                    try
                    {
                        int count = Marshal.ReadInt32(list, 0);
                        for (int i = 0; i < count; i++) res.Add(Utf16(list, 8 + i * ProfileInfoSize, 256));
                    }
                    finally { WlanFreeMemory(list); }
                }
            }
            catch { }
            return res;
        }

        public static bool Connect(string profile)
        {
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return false;
                    Guid g = s.Iface;
                    ConnectionParameters p = new ConnectionParameters();
                    p.Mode = 0;
                    p.Profile = profile;
                    p.BssType = 1;
                    uint rc = WlanConnect(s.Handle, ref g, ref p, IntPtr.Zero);
                    Log.Write("wifi", "Подключение к «" + profile + "»: код " + rc);
                    return rc == 0;
                }
            }
            catch (Exception ex) { Log.Write("wifi", "Подключение: " + ex.Message); return false; }
        }

        public static void Disconnect()
        {
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return;
                    Guid g = s.Iface;
                    WlanDisconnect(s.Handle, ref g, IntPtr.Zero);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ administrator: profiles

        public static string Escape(string s)
        {
            return SecurityElement.Escape(s ?? "");
        }

        /// <summary>Windows WLAN profile XML. security: "WPA2" (WPA2-Personal), "WPA3" (WPA3-Personal) or "Open".</summary>
        public static string BuildProfileXml(string ssid, string password, string security)
        {
            string name = Escape(ssid);
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>");
            sb.Append("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">");
            sb.Append("<name>").Append(name).Append("</name>");
            sb.Append("<SSIDConfig><SSID><name>").Append(name).Append("</name></SSID></SSIDConfig>");
            sb.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>");
            sb.Append("<MSM><security><authEncryption>");
            if (string.Equals(security, "Open", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>");
            }
            else
            {
                string auth = string.Equals(security, "WPA3", StringComparison.OrdinalIgnoreCase) ? "WPA3SAE" : "WPA2PSK";
                sb.Append("<authentication>").Append(auth).Append("</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>");
                sb.Append("<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>").Append(Escape(password)).Append("</keyMaterial></sharedKey>");
            }
            sb.Append("</security></MSM></WLANProfile>");
            return sb.ToString();
        }

        /// <summary>Saves a profile for all users (needs administrator rights). The password is stored by Windows, encrypted.</summary>
        public static bool AddProfile(string ssid, string password, string security, out string error)
        {
            error = null;
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) { error = "На этом компьютере нет адаптера Wi-Fi."; return false; }
                    Guid g = s.Iface;
                    uint reason;
                    uint rc = WlanSetProfile(s.Handle, ref g, 0, BuildProfileXml(ssid, password, security), null, true, IntPtr.Zero, out reason);
                    if (rc != 0) { error = "Windows отклонила профиль (код " + rc + ", причина " + reason + "). Проверьте пароль и тип защиты."; return false; }
                    Log.Write("wifi", "Добавлена сеть «" + ssid + "»");
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public static bool DeleteProfile(string name)
        {
            try
            {
                using (Session s = new Session())
                {
                    if (!s.HasInterface) return false;
                    Guid g = s.Iface;
                    return WlanDeleteProfile(s.Handle, ref g, name, IntPtr.Zero) == 0;
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ rules

        public static bool IsAllowed(Config cfg, string ssid)
        {
            if (cfg.Wifi.AllowedSsids.Count == 0) return true;   // empty list: every network the administrator saved
            foreach (string a in cfg.Wifi.AllowedSsids)
                if (string.Equals(a, ssid, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Disconnects a student from a network that is not on the administrator's list.</summary>
        public static void Enforce(Config cfg)
        {
            if (!cfg.Wifi.Enforce || cfg.Wifi.AllowedSsids.Count == 0 || State.AdminMode) return;
            WifiNetwork cur = Current();
            if (cur == null || IsAllowed(cfg, cur.Ssid)) return;
            Log.Write("wifi", "Отключаю от сети «" + cur.Ssid + "»: её нет в списке разрешённых");
            Disconnect();
            Ui.Toast("Эта сеть Wi-Fi не разрешена школой.");
        }
    }
}
