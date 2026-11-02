using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SchoolShield
{
    /// <summary>
    /// Which keys are down right now. Two sources are combined: GetAsyncKeyState and the low-level hook. Some laptops
    /// (Fn keys, OEM hotkey drivers) report a key through only one of them, so a chord is accepted if either sees it.
    /// </summary>
    internal static class KeyState
    {
        private static readonly System.Collections.Generic.HashSet<int> Down = new System.Collections.Generic.HashSet<int>();

        public static int Normalize(int vk)
        {
            if (vk == 0xA0 || vk == 0xA1) return Native.VK_SHIFT;
            if (vk == 0xA2 || vk == 0xA3) return Native.VK_CONTROL;
            if (vk == 0xA4 || vk == 0xA5) return Native.VK_MENU;
            return vk;
        }

        public static void Track(int vk, bool down)
        {
            vk = Normalize(vk);
            if (down) Down.Add(vk); else Down.Remove(vk);
        }

        public static bool IsDown(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0 || Down.Contains(vk);
        }
    }

    /// <summary>
    /// Global low-level keyboard hook. Swallows Win key, Alt+Tab, Alt+Esc, Ctrl+Esc and Alt+F4 on the
    /// launcher itself. Switched off while State.AdminMode is on and in preview mode.
    /// (The admin chord is detected separately, by holding it: see Chord.)
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly Native.LowLevelKeyboardProc proc;
        private IntPtr hook = IntPtr.Zero;
        private readonly uint ownPid;

        public KeyboardHook()
        {
            proc = Callback;
            ownPid = (uint)Process.GetCurrentProcess().Id;
        }

        public void Install()
        {
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
            {
                hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(m.ModuleName), 0);
            }
            if (hook == IntPtr.Zero)
                Log.Write("hook", "Не удалось установить хук клавиатуры, код " + Marshal.GetLastWin32Error());
        }

        private static bool Down(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    int m = wParam.ToInt32();
                    Native.KBDLLHOOKSTRUCT kk = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    if (m == Native.WM_KEYDOWN || m == Native.WM_SYSKEYDOWN) KeyState.Track((int)kk.vkCode, true);
                    else if (m == 0x0101 /* WM_KEYUP */ || m == 0x0105 /* WM_SYSKEYUP */) KeyState.Track((int)kk.vkCode, false);
                }
                catch { }
            }
            if (nCode >= 0 && !State.AdminMode && !State.Preview)
            {
                try
                {
                    Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    bool ctrl = Down(Native.VK_CONTROL);
                    bool alt = (k.flags & 0x20) != 0 || Down(Native.VK_MENU);
                    if (ShouldBlock(k.vkCode, ctrl, alt)) return (IntPtr)1;
                }
                catch { /* never break the keyboard */ }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        private bool ShouldBlock(uint vk, bool ctrl, bool alt)
        {
            if (vk == Native.VK_LWIN || vk == Native.VK_RWIN || vk == Native.VK_APPS) return true;
            if (vk == 0x70) return true;   // F1: "Help" in most programs opens a browser window
            if (alt && (vk == Native.VK_TAB || vk == Native.VK_ESCAPE)) return true;
            if (ctrl && vk == Native.VK_ESCAPE) return true;   // also covers Ctrl+Shift+Esc (Task Manager)
            if (alt && vk == Native.VK_F4)
            {
                // Let students close their own programs with Alt+F4, but never the launcher/dock/terminal.
                return Native.GetWindowPid(Native.GetForegroundWindow()) == ownPid;
            }
            return false;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }
}
