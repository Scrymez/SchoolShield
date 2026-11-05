using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace SchoolShield.Setup
{
    internal static class Program
    {
        /// <summary>
        /// SchoolShield-Setup.exe             wizard (install or update)
        /// SchoolShield-Setup.exe /S          silent install  (/nomodel, /noicon, /edition=teacher|student, /noprotect)
        /// SchoolShield-Setup.exe /update     update (started by the program itself after a signed download)
        /// SchoolShield-Setup.exe /uninstall  uninstall (/S for silent; /auto without questions; /purge also settings and log; /removestudent also the student account)
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = Has(args, "/S") || Has(args, "/silent");
            bool uninstall = Has(args, "/uninstall");
            bool update = Has(args, "/update");
            bool auto = Has(args, "/auto");          // run without questions, with a progress window (the panel's self-destruct uses it)
            bool purge = Has(args, "/purge");        // uninstall: delete settings, log and the AI model too

            Installer inst = new Installer();
            inst.DownloadModel = !Has(args, "/nomodel") && !update;   // an update only replaces files; missing components are fetched by the admin panel
            inst.DesktopIcon = !Has(args, "/noicon");
            inst.ApplyTeacherProtection = !Has(args, "/noprotect");
            inst.RemoveStudent = Has(args, "/removestudent");
            foreach (string a in args)
            {
                if (a.StartsWith("/edition=", StringComparison.OrdinalIgnoreCase))
                {
                    string ed = a.Substring(9).Trim().ToLowerInvariant();
                    if (ed == "teacher" || ed == "student") inst.Edition = ed;
                }
            }

            if (silent)
            {
                try
                {
                    inst.Log = delegate (string m) { Console.WriteLine(m); };
                    if (uninstall) inst.Uninstall(purge);
                    else inst.Install();
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "SchoolShield-setup.log"), ex + Environment.NewLine); } catch { }
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(inst, uninstall, update, auto, purge));
            return 0;
        }

        private static bool Has(string[] args, string flag)
        {
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal sealed class SetupForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(11, 18, 32);
        private static readonly Color Card = Color.FromArgb(17, 28, 51);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);
        private static readonly Color TextColor = Color.FromArgb(226, 232, 240);
        private static readonly Color Muted = Color.FromArgb(148, 163, 184);

        private readonly Installer inst;
        private readonly bool uninstall;
        private readonly bool update;
        private readonly Panel page1 = new Panel();
        private readonly Panel page2 = new Panel();
        private readonly RichTextBox log = new RichTextBox();
        private readonly ProgressBar bar = new ProgressBar();
        private readonly Button action = new Button();
        private readonly Button cancel = new Button();
        private readonly CheckBox icon = new CheckBox();
        private readonly CheckBox model = new CheckBox();
        private readonly CheckBox launch = new CheckBox();
        private readonly CheckBox keepSettings = new CheckBox();
        private readonly CheckBox removeStudent = new CheckBox();
        private readonly bool auto;
        private readonly bool purge;
        private readonly RadioButton forStudents = new RadioButton();
        private readonly RadioButton forTeachers = new RadioButton();
        private readonly CheckBox protect = new CheckBox();
        private bool done;
        private bool failed;
        private bool busy;

        public SetupForm(Installer inst, bool uninstall, bool update, bool auto, bool purge)
        {
            this.inst = inst;
            this.uninstall = uninstall;
            this.update = update;
            this.auto = auto;
            this.purge = purge;

            Text = uninstall ? "Удаление SchoolShield" : update ? "Обновление SchoolShield" : "Установка SchoolShield";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ClientSize = new Size(700, 500);
            BackColor = Bg;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10.5f);
            try { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }

            Label title = new Label();
            title.Text = uninstall ? "Удаление SchoolShield" : update ? "Обновление SchoolShield" : "SchoolShield";
            title.Font = new Font("Segoe UI", 24, FontStyle.Bold);
            title.ForeColor = Accent;
            title.SetBounds(28, 20, 640, 50);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "Версия " + Installer.Version + "  •  " + Release.Author;
            sub.ForeColor = Muted;
            sub.SetBounds(32, 72, 640, 24);
            Controls.Add(sub);

            BuildPage1();
            BuildPage2();

            Style(action, true);
            action.SetBounds(28, 440, 300, 44);
            action.Click += delegate { OnAction(); };
            Style(cancel, false);
            cancel.Text = "Отмена";
            cancel.SetBounds(344, 440, 160, 44);
            cancel.Click += delegate { if (!busy) Close(); };
            Controls.Add(action);
            Controls.Add(cancel);

            action.Text = uninstall ? "Удалить" : update ? "Обновить" : "Установить";
            if (update || auto)
            {
                // The update (or the panel's self-destruct) runs without questions, and its window must not hide behind the launcher.
                TopMost = true;
                Shown += delegate { Activate(); BringToFront(); OnAction(); };
            }
        }

        private static void Style(Button b, bool accent)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = accent ? Accent : Card;
            b.ForeColor = accent ? Color.FromArgb(7, 12, 24) : TextColor;
            b.Font = new Font("Segoe UI", 11, accent ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
        }

        private void BuildPage1()
        {
            page1.SetBounds(28, 108, 644, 320);
            Controls.Add(page1);

            Label info = new Label();
            info.SetBounds(0, 0, 644, 150);
            info.ForeColor = TextColor;
            if (uninstall)
            {
                info.Text = "Будут сняты все ограничения, возвращён обычный рабочий стол Windows и удалена программа.";
                keepSettings.Text = "Сохранить настройки и журнал (C:\\ProgramData\\SchoolShield)";
                keepSettings.Checked = !purge;
                keepSettings.AutoSize = true;
                keepSettings.Location = new Point(0, 160);
                keepSettings.ForeColor = TextColor;
                page1.Controls.Add(keepSettings);

                removeStudent.Text = "Удалить также учётную запись ученика вместе с её файлами";
                removeStudent.Checked = inst.RemoveStudent;
                removeStudent.AutoSize = true;
                removeStudent.Location = new Point(0, 192);
                removeStudent.ForeColor = Color.FromArgb(251, 191, 36);
                page1.Controls.Add(removeStudent);
            }
            else
            {
                info.Height = 112;
                string existing = inst.EffectiveEdition;
                info.Text = "Защита компьютера с Windows 10 и 11. Ученическая редакция: лаунчер вместо рабочего стола, запрет установки "
                    + "программ, фильтры сайтов и DNS, ИИ-фильтр изображений. Редакция для учителей: обычная Windows, закрыт только материал 18+.\n\n"
                    + "Обновления берутся из GitHub и проверяются подписью.";

                if (!update)
                {
                    forStudents.Text = "Школьный компьютер для учеников (полная защита)";
                    forStudents.AutoSize = true;
                    forStudents.Location = new Point(0, 120);
                    forStudents.ForeColor = TextColor;
                    forStudents.Checked = inst.Edition == null ? existing != "teacher" : inst.Edition == "student";
                    page1.Controls.Add(forStudents);

                    forTeachers.Text = "Компьютер учителя (только фильтр 18+, без других запретов)";
                    forTeachers.AutoSize = true;
                    forTeachers.Location = new Point(0, 148);
                    forTeachers.ForeColor = TextColor;
                    forTeachers.Checked = !forStudents.Checked;
                    page1.Controls.Add(forTeachers);
                }

                protect.Text = "Сразу включить фильтр 18+ (рекомендуется)";
                protect.Checked = true;
                protect.AutoSize = true;
                protect.Location = new Point(0, 176);
                protect.ForeColor = TextColor;
                protect.Visible = !update && forTeachers.Checked;
                forTeachers.CheckedChanged += delegate { protect.Visible = forTeachers.Checked; launch.Text = forTeachers.Checked ? "Сразу открыть Центр учителя" : "Сразу открыть мастер первого запуска"; };
                page1.Controls.Add(protect);

                icon.Text = "Значок на рабочем столе и в меню «Пуск»";
                icon.Checked = true;
                icon.AutoSize = true;
                icon.Location = new Point(0, 206);
                icon.ForeColor = TextColor;
                page1.Controls.Add(icon);

                model.Text = "Скачать локальный ИИ-фильтр изображений (23 МБ, бесплатно)";
                model.Checked = true;
                model.AutoSize = true;
                model.Location = new Point(0, 236);
                model.ForeColor = TextColor;
                page1.Controls.Add(model);

                launch.Text = forTeachers.Checked ? "Сразу открыть Центр учителя" : "Сразу открыть мастер первого запуска";
                launch.Checked = !update;
                launch.AutoSize = true;
                launch.Location = new Point(0, 266);
                launch.ForeColor = TextColor;
                page1.Controls.Add(launch);
            }
            page1.Controls.Add(info);
        }

        private void BuildPage2()
        {
            page2.SetBounds(28, 108, 644, 320);
            page2.Visible = false;
            Controls.Add(page2);

            bar.SetBounds(0, 0, 644, 16);
            bar.Style = ProgressBarStyle.Continuous;
            page2.Controls.Add(bar);

            log.SetBounds(0, 28, 644, 288);
            log.ReadOnly = true;
            log.BackColor = Color.Black;
            log.ForeColor = Color.FromArgb(74, 222, 128);
            log.Font = new Font("Consolas", 10);
            log.BorderStyle = BorderStyle.None;
            page2.Controls.Add(log);
        }

        private static void LogToFile(string text)
        {
            try
            {
                string dir = Path.Combine(Release.DataDir, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "setup.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        private void Append(string text)
        {
            LogToFile(text);
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    log.AppendText(text + Environment.NewLine);
                    log.ScrollToCaret();
                }));
            }
            catch { }
        }

        private void OnAction()
        {
            if (done)
            {
                if (!failed && !uninstall && !update && launch.Checked) StartPanel();
                Close();
                return;
            }
            if (busy) return;

            if (!update && !uninstall) inst.Edition = forTeachers.Checked ? "teacher" : "student";
            inst.ApplyTeacherProtection = protect.Checked || update;
            inst.DesktopIcon = icon.Checked || update;
            inst.StartMenuIcon = inst.DesktopIcon;
            inst.DownloadModel = model.Checked && !update;
            inst.Log = Append;
            inst.Progress = delegate (int p) { try { BeginInvoke(new Action(delegate { bar.Value = Math.Max(0, Math.Min(100, p)); })); } catch { } };

            page1.Visible = false;
            page2.Visible = true;
            busy = true;
            action.Enabled = false;
            cancel.Enabled = false;
            bool removeSettings = uninstall && !keepSettings.Checked;
            if (uninstall) inst.RemoveStudent = removeStudent.Checked;

            Thread t = new Thread(delegate ()
            {
                try
                {
                    if (uninstall) inst.Uninstall(removeSettings); else inst.Install();
                }
                catch (Exception ex)
                {
                    failed = true;
                    Append("ОШИБКА: " + ex.Message);
                    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "SchoolShield-setup.log"), ex + Environment.NewLine); } catch { }
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        done = true;
                        TopMost = false;
                        action.Enabled = true;
                        action.Text = failed ? "Закрыть" : (!uninstall && !update && launch.Checked ? (inst.IsTeacher ? "Готово — открыть Центр" : "Готово — открыть мастер") : "Закрыть");
                        if (!failed) bar.Value = 100;
                        if (!failed && update)
                        {
                            Append("Обновление установлено. Чтобы изменения вступили в силу на компьютерах учеников, перезагрузите их.");
                            Append("Если не хватает Visual C++, WebView2 или модели ИИ, они докачаются, когда вы откроете панель администратора.");
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void StartPanel()
        {
            try
            {
                string exe = Path.Combine(inst.InstallDir, "SchoolShield.exe");
                if (inst.IsTeacher)
                {
                    // explorer.exe starts it with the user's normal rights, not the installer's administrator token
                    Process.Start("explorer.exe", "\"" + exe + "\"");
                    return;
                }
                ProcessStartInfo psi = new ProcessStartInfo(exe, "--admin");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
