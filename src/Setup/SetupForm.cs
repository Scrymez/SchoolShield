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
        /// SchoolShield-Setup.exe /S          silent install  (/nomodel, /noicon)
        /// SchoolShield-Setup.exe /update     update (started by the program itself after a signed download)
        /// SchoolShield-Setup.exe /uninstall  uninstall (/S for silent)
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = Has(args, "/S") || Has(args, "/silent");
            bool uninstall = Has(args, "/uninstall");
            bool update = Has(args, "/update");

            Installer inst = new Installer();
            inst.DownloadModel = !Has(args, "/nomodel");
            inst.DesktopIcon = !Has(args, "/noicon");

            if (silent)
            {
                try
                {
                    inst.Log = delegate (string m) { Console.WriteLine(m); };
                    if (uninstall) inst.Uninstall(false);
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
            Application.Run(new SetupForm(inst, uninstall, update));
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
        private bool done;
        private bool failed;
        private bool busy;

        public SetupForm(Installer inst, bool uninstall, bool update)
        {
            this.inst = inst;
            this.uninstall = uninstall;
            this.update = update;

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
            if (update) Shown += delegate { OnAction(); };   // update runs without questions
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
                keepSettings.Checked = true;
                keepSettings.AutoSize = true;
                keepSettings.Location = new Point(0, 160);
                keepSettings.ForeColor = TextColor;
                page1.Controls.Add(keepSettings);
            }
            else
            {
                info.Text = "Защита школьного компьютера с Windows 10: лаунчер вместо рабочего стола, запрет установки программ, "
                    + "фильтры сайтов и DNS, локальный ИИ-фильтр изображений, обновления из GitHub.\n\n"
                    + "Установка только копирует программу и создаёт значок «SchoolShield» на рабочем столе. "
                    + "Защита включается позже, при первом запуске с этого значка (мастер).";

                icon.Text = "Значок на рабочем столе и в меню «Пуск»";
                icon.Checked = true;
                icon.AutoSize = true;
                icon.Location = new Point(0, 170);
                icon.ForeColor = TextColor;
                page1.Controls.Add(icon);

                model.Text = "Скачать локальный ИИ-фильтр изображений (23 МБ, бесплатно)";
                model.Checked = true;
                model.AutoSize = true;
                model.Location = new Point(0, 202);
                model.ForeColor = TextColor;
                page1.Controls.Add(model);

                launch.Text = "Сразу открыть мастер первого запуска";
                launch.Checked = !update;
                launch.AutoSize = true;
                launch.Location = new Point(0, 234);
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

        private void Append(string text)
        {
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

            inst.DesktopIcon = icon.Checked || update;
            inst.StartMenuIcon = inst.DesktopIcon;
            inst.DownloadModel = model.Checked || update;
            inst.Log = Append;
            inst.Progress = delegate (int p) { try { BeginInvoke(new Action(delegate { bar.Value = Math.Max(0, Math.Min(100, p)); })); } catch { } };

            page1.Visible = false;
            page2.Visible = true;
            busy = true;
            action.Enabled = false;
            cancel.Enabled = false;
            bool removeSettings = uninstall && !keepSettings.Checked;

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
                        action.Enabled = true;
                        action.Text = failed ? "Закрыть" : (!uninstall && !update && launch.Checked ? "Готово — открыть мастер" : "Закрыть");
                        if (!failed) bar.Value = 100;
                        if (!failed && update)
                            Append("Обновление установлено. Чтобы изменения вступили в силу на компьютерах учеников, перезагрузите их.");
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
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(inst.InstallDir, "SchoolShield.exe"), "--admin");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
