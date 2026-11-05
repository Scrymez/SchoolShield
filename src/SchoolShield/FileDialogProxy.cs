using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// The launcher's own "Open" and "Save as" windows for a student. The standard Windows dialog of any program cannot be replaced, but it can
    /// be taken over: when one appears, the launcher makes it invisible (fully transparent and click-through, NOT moved off screen, so that its own
    /// message boxes stay visible), shows its own window limited to the allowed folders and educational files, and puts the chosen path into the
    /// invisible dialog ("File name" box, then OK). If anything is not as expected, the standard dialog is made visible again and the student works
    /// with it as before: the feature can only ever fail open.
    /// </summary>
    internal sealed class FileDialogProxy : IDisposable
    {
        // ---------------------------------------------------------------- Win32
        private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        private const uint WM_CLOSE = 0x0010, WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, WM_COMMAND = 0x0111, BM_CLICK = 0x00F5;
        private const uint CB_GETCOUNT = 0x0146, CB_GETCURSEL = 0x0147, CB_GETLBTEXT = 0x0148, CB_GETLBTEXTLEN = 0x0149, CB_SETCURSEL = 0x014E;
        private const uint SMTO_ABORTIFHUNG = 2, LWA_ALPHA = 2, GW_OWNER = 4;
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
        private const int CtlFileName = 0x47C, CtlFileType = 0x470, IdOk = 1, IdYes = 6, CbnSelChange = 1;

        // ---------------------------------------------------------------- state
        private sealed class DialogInfo
        {
            public IntPtr Dlg, Edit, Types, Ok;
            public DialogLogic.Mode Mode;
            public string App = "";
            public string InitialName = "";
            public List<string> TypeTexts = new List<string>();
            public int TypeIndex;
            public int Pid;
        }

        private readonly MainForm main;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Dictionary<IntPtr, int> tries = new Dictionary<IntPtr, int>();      // looked at, not (yet) a file dialog
        private readonly HashSet<IntPtr> handled = new HashSet<IntPtr>();                    // taken over (or failed): never again
        private readonly Dictionary<string, string> lastFolder = new Dictionary<string, string>();
        private readonly int ownPid = Process.GetCurrentProcess().Id;
        private bool busy;

        public FileDialogProxy(MainForm main)
        {
            this.main = main;
            timer.Interval = 80;
            timer.Tick += delegate { Tick(); };
        }

        public void Start() { timer.Start(); }

        public void Dispose()
        {
            timer.Stop();
            timer.Dispose();
        }

        // ---------------------------------------------------------------- watching
        private void Tick()
        {
            if (busy) return;
            Config cfg = main.Cfg;
            if (!cfg.OwnFileDialogs || State.Privileged || State.Soft) return;

            DialogInfo found = null;
            HashSet<IntPtr> alive = new HashSet<IntPtr>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                try
                {
                    if (found != null || !Native.IsWindowVisible(h)) return true;
                    if (Native.GetWindowClass(h) != "#32770") return true;
                    alive.Add(h);
                    if (handled.Contains(h)) return true;
                    if (Native.GetWindowPid(h) == (uint)ownPid) return true;
                    int n;
                    tries.TryGetValue(h, out n);
                    if (n > 15) return true;                    // about 1.2 s of attempts: an ordinary message box, leave it alone
                    tries[h] = n + 1;
                    found = Inspect(h);
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // forget windows that are gone
            List<IntPtr> gone = new List<IntPtr>();
            foreach (IntPtr h in tries.Keys) if (!alive.Contains(h)) gone.Add(h);
            foreach (IntPtr h in gone) tries.Remove(h);
            handled.RemoveWhere(delegate (IntPtr h) { return !IsWindow(h); });

            if (found != null) TakeOver(found, cfg);
        }

        /// <summary>Is this dialog an Open or Save dialog with a "File name" box, a file-type list and OK? Otherwise null (left alone).</summary>
        private static DialogInfo Inspect(IntPtr dlg)
        {
            IntPtr name = IntPtr.Zero, types = IntPtr.Zero, ok = IntPtr.Zero;
            EnumChildWindows(dlg, delegate (IntPtr c, IntPtr l)
            {
                int id = GetDlgCtrlID(c);
                string cls = Native.GetWindowClass(c);
                if (id == CtlFileName && name == IntPtr.Zero) name = c;
                else if (id == CtlFileType && types == IntPtr.Zero && cls.IndexOf("ComboBox", StringComparison.OrdinalIgnoreCase) >= 0) types = c;
                else if (id == IdOk && ok == IntPtr.Zero && cls == "Button") ok = c;
                return true;
            }, IntPtr.Zero);
            if (name == IntPtr.Zero || types == IntPtr.Zero || ok == IntPtr.Zero) return null;

            // the "File name" control is an Edit itself (classic dialog) or a ComboBox that contains one (new style)
            IntPtr edit = name;
            if (Native.GetWindowClass(name) != "Edit")
            {
                edit = IntPtr.Zero;
                EnumChildWindows(name, delegate (IntPtr c, IntPtr l)
                {
                    if (edit == IntPtr.Zero && Native.GetWindowClass(c) == "Edit") edit = c;
                    return true;
                }, IntPtr.Zero);
                if (edit == IntPtr.Zero) return null;
            }

            DialogInfo d = new DialogInfo();
            d.Dlg = dlg; d.Edit = edit; d.Types = types; d.Ok = ok;
            d.Mode = DialogLogic.ModeOf(Native.GetWindowTitle(ok));
            if (d.Mode == DialogLogic.Mode.Unknown) return null;      // "Select folder" and the like: not ours
            d.Pid = (int)Native.GetWindowPid(dlg);
            d.InitialName = GetText(edit);
            IntPtr r;
            if (SendMessageTimeout(types, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out r) == IntPtr.Zero) return null;
            int count = Math.Max(0, Math.Min(200, r.ToInt32()));
            for (int i = 0; i < count; i++)
            {
                IntPtr len;
                SendMessageTimeout(types, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out len);
                StringBuilder sb = new StringBuilder(Math.Max(2, len.ToInt32() + 2));
                SendMessageTimeout(types, CB_GETLBTEXT, (IntPtr)i, sb, SMTO_ABORTIFHUNG, 1500, out len);
                d.TypeTexts.Add(sb.ToString());
            }
            IntPtr cur;
            SendMessageTimeout(types, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out cur);
            d.TypeIndex = Math.Max(0, cur.ToInt32());
            try { using (Process p = Process.GetProcessById(d.Pid)) d.App = p.ProcessName; } catch { }
            return d;
        }

        private static string GetText(IntPtr h)
        {
            IntPtr len;
            SendMessageTimeout(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out len);
            StringBuilder sb = new StringBuilder(Math.Max(2, len.ToInt32() + 2));
            IntPtr got;
            SendMessageTimeout(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 1500, out got);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- taking over
        private void TakeOver(DialogInfo d, Config cfg)
        {
            busy = true;
            handled.Add(d.Dlg);
            int origEx = Native.GetWindowLong(d.Dlg, Native.GWL_EXSTYLE);
            bool finished = false;
            try
            {
                Log.Write("dialog", "Перехвачено окно «" + (d.Mode == DialogLogic.Mode.Save ? "Сохранить" : "Открыть") + "» программы " + d.App
                    + "; типов файлов: " + d.TypeTexts.Count);
                MakeInvisible(d.Dlg, origEx);

                string start;
                lastFolder.TryGetValue(d.App, out start);
                FileDialogResult res = OwnFileDialogForm.Pick(d.Mode, d.App, d.TypeTexts, d.TypeIndex, d.InitialName, start, cfg);
                if (res == null)
                {
                    // cancelled in our window: cancel the program's dialog too
                    IntPtr r;
                    SendMessageTimeout(d.Dlg, WM_CLOSE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1500, out r);
                    finished = true;
                }
                else if (Inject(d, res))
                {
                    if (res.Folder != null) lastFolder[d.App] = res.Folder;
                    finished = true;
                }
            }
            catch (Exception ex) { Log.Write("dialog", "Ошибка: " + ex.Message); }
            finally
            {
                if (!finished && IsWindow(d.Dlg))
                {
                    Restore(d.Dlg, origEx);
                    Log.Write("dialog", "Не удалось передать выбор программе: стандартное окно возвращено ученику");
                    Ui.Toast("Окно лаунчера не смогло передать файл программе. Показано стандартное окно.");
                }
                busy = false;
            }
        }

        private static void MakeInvisible(IntPtr dlg, int origEx)
        {
            Native.SetWindowLong(dlg, Native.GWL_EXSTYLE, origEx | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetLayeredWindowAttributes(dlg, 0, 0, LWA_ALPHA);
        }

        private static void Restore(IntPtr dlg, int origEx)
        {
            SetLayeredWindowAttributes(dlg, 0, 255, LWA_ALPHA);
            Native.SetWindowLong(dlg, Native.GWL_EXSTYLE, origEx);
            Native.SetForegroundWindow(dlg);
        }

        private static bool Send(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
        {
            IntPtr r;
            return SendMessageTimeout(h, msg, wParam, lParam, SMTO_ABORTIFHUNG, 2000, out r) != IntPtr.Zero || Marshal.GetLastWin32Error() == 0;
        }

        private static void SetText(IntPtr edit, string text)
        {
            IntPtr r;
            SendMessageTimeout(edit, WM_SETTEXT, IntPtr.Zero, text, SMTO_ABORTIFHUNG, 2000, out r);
        }

        private static void ClickOk(IntPtr ok) { Native.PostMessage(ok, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }

        /// <summary>Puts the student's choice into the program's invisible dialog and presses OK. True when the dialog closed.</summary>
        private bool Inject(DialogInfo d, FileDialogResult r)
        {
            if (r.Paths.Count == 0) return false;
            // the file type first: programs read it back (the format of "Save as" depends on it)
            if (r.TypeIndex >= 0 && r.TypeIndex != d.TypeIndex && r.TypeIndex < d.TypeTexts.Count)
            {
                Send(d.Types, CB_SETCURSEL, (IntPtr)r.TypeIndex, IntPtr.Zero);
                IntPtr wp = (IntPtr)((CbnSelChange << 16) | CtlFileType);
                Send(d.Dlg, WM_COMMAND, wp, d.Types);
                Thread.Sleep(120);
            }

            if (r.Paths.Count == 1)
            {
                SetText(d.Edit, r.Paths[0]);
                ClickOk(d.Ok);
            }
            else
            {
                // several files: first go to their folder, then name them in quotes
                SetText(d.Edit, r.Folder);
                ClickOk(d.Ok);
                Thread.Sleep(450);
                List<string> names = new List<string>();
                foreach (string p in r.Paths) names.Add(System.IO.Path.GetFileName(p));
                SetText(d.Edit, DialogLogic.QuoteNames(names));
                ClickOk(d.Ok);
            }

            // wait until the dialog closes; answer "replace the file?" (we have asked the student already), stop at any other message
            DateTime until = DateTime.Now.AddSeconds(4);
            while (DateTime.Now < until)
            {
                if (!IsWindow(d.Dlg)) return true;
                bool other = false;
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    if (Native.GetWindow(h, GW_OWNER) != d.Dlg || !Native.IsWindowVisible(h)) return true;
                    IntPtr yes = IntPtr.Zero;
                    EnumChildWindows(h, delegate (IntPtr c, IntPtr l2)
                    {
                        if (GetDlgCtrlID(c) == IdYes) yes = c;
                        return true;
                    }, IntPtr.Zero);
                    if (yes != IntPtr.Zero) Native.PostMessage(yes, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    else other = true;
                    return true;
                }, IntPtr.Zero);
                if (other) return false;                        // an error message of the dialog: hand it to the student with the standard window
                Thread.Sleep(40);
            }
            return !IsWindow(d.Dlg);
        }
    }
}
