using System;
using System.Collections.Generic;

namespace SchoolShield
{
    internal sealed class WinInfo
    {
        public IntPtr Handle;
        public uint Pid;
        public string Title;
        public string Class;
        public int Style;
        public int ExStyle;
        public bool Iconic;
    }

    internal static class WindowList
    {
        private static readonly string[] IgnoredClasses = new string[] {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
            "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
        };

        /// <summary>Visible, un-owned, non-tool top-level windows with a title (what a taskbar would list).</summary>
        public static List<WinInfo> Enumerate()
        {
            List<WinInfo> result = new List<WinInfo>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                try
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return true;
                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
                    int cloaked = 0;
                    Native.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out cloaked, 4);
                    if (cloaked != 0) return true;
                    string title = Native.GetWindowTitle(h);
                    if (title.Length == 0) return true;
                    string cls = Native.GetWindowClass(h);
                    if (Array.IndexOf(IgnoredClasses, cls) >= 0) return true;

                    WinInfo w = new WinInfo();
                    w.Handle = h;
                    w.Pid = Native.GetWindowPid(h);
                    w.Title = title;
                    w.Class = cls;
                    w.Style = Native.GetWindowLong(h, Native.GWL_STYLE);
                    w.ExStyle = ex;
                    w.Iconic = Native.IsIconic(h);
                    result.Add(w);
                }
                catch { /* a window vanished mid-enumeration */ }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
