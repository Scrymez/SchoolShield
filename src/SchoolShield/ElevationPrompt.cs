using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// While Windows asks for administrator rights, the launcher hides all of its own windows. In a student session there is no taskbar,
    /// and the request (when UAC does not use the secure desktop) can end up behind the full-screen launcher where nobody finds it.
    /// If no answer comes within 30 seconds the windows come back (with <paramref name="onTimeout"/> to explain what to do),
    /// so that an empty desktop never traps the user. Create and end it on the UI thread.
    /// </summary>
    internal sealed class ElevationPrompt
    {
        private readonly List<Form> hidden = new List<Form>();
        private readonly System.Windows.Forms.Timer failsafe = new System.Windows.Forms.Timer();
        private bool ended;

        private ElevationPrompt(Action onTimeout)
        {
            foreach (Form f in new List<Form>(AllForms()))
            {
                if (f.IsDisposed || !f.Visible) continue;
                hidden.Add(f);
                f.Hide();
            }
            failsafe.Interval = 30000;
            failsafe.Tick += delegate
            {
                End();
                if (onTimeout != null) onTimeout();
            };
            failsafe.Start();
        }

        private static IEnumerable<Form> AllForms()
        {
            foreach (Form f in Application.OpenForms) yield return f;
        }

        /// <summary>Hides the windows when the process is not elevated yet (an elevated process gets no prompt); otherwise returns null.</summary>
        public static ElevationPrompt BeginIfNeeded(Action onTimeout)
        {
            return Programs.IsElevated() ? null : new ElevationPrompt(onTimeout);
        }

        public void End()
        {
            if (ended) return;
            ended = true;
            failsafe.Stop();
            failsafe.Dispose();
            foreach (Form f in hidden)
                if (!f.IsDisposed) f.Show();
        }

        public static void End(ElevationPrompt p)
        {
            if (p != null) p.End();
        }

        /// <summary>What to do when the request never showed up.</summary>
        public const string NotShownHelp =
            "Запрос прав администратора не появился. Обновляйте и удаляйте программы под учётной записью администратора: Ctrl+Alt+Del → «Выйти» → войти администратором → "
            + "значок SchoolShield на рабочем столе → вкладки «Обновления» и «Программы». Там запрос не нужен.";
    }
}
