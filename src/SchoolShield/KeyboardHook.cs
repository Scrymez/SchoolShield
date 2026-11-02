using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SchoolShield
{
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
