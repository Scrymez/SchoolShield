using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// Terminal-style admin window. Opens with the secret hotkey, asks for the admin code, then accepts commands.
    /// </summary>
    internal sealed class AdminConsoleForm : Form
    {
        private enum Stage { Code, Shell }

        private readonly Config cfg;
        private readonly MainForm main;
        private readonly RichTextBox output = new RichTextBox();
        private readonly TextBox input = new TextBox();
        private readonly Label prompt = new Label();
        private readonly Timer idle = new Timer();
        private Stage stage = Stage.Code;
        private int attempts;
        private DateTime lockedUntil = DateTime.MinValue;
        private DateTime lastActivity = DateTime.Now;
        private static readonly Color Green = Color.FromArgb(74, 222, 128);

        public AdminConsoleForm(Config cfg, MainForm main)
        {
            this.cfg = cfg;
            this.main = main;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle sc = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Metrics.S(820), (int)(sc.Width * 0.92)), Math.Min(Metrics.S(480), (int)(sc.Height * 0.82)));
            BackColor = Green;
            Padding = new Padding(1);

            Panel inner = new Panel();
            inner.Dock = DockStyle.Fill;
            inner.BackColor = Color.Black;
            inner.Padding = new Padding(12);

            output.Dock = DockStyle.Fill;
            output.ReadOnly = true;
            output.BorderStyle = BorderStyle.None;
            output.BackColor = Color.Black;
            output.ForeColor = Green;
            output.Font = new Font("Consolas", 11.5f);
            output.TabStop = false;
            output.DetectUrls = false;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 30;
            bottom.BackColor = Color.Black;

            prompt.AutoSize = false;
            prompt.Dock = DockStyle.Left;
            prompt.Width = Metrics.S(190);
            prompt.TextAlign = ContentAlignment.MiddleLeft;
            prompt.ForeColor = Green;
            prompt.BackColor = Color.Black;
            prompt.Font = new Font("Consolas", 11.5f, FontStyle.Bold);

            input.Dock = DockStyle.Fill;
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Color.Black;
            input.ForeColor = Color.White;
            input.Font = new Font("Consolas", 11.5f);
            input.KeyDown += OnInputKey;

            bottom.Controls.Add(input);
            bottom.Controls.Add(prompt);
            inner.Controls.Add(output);
            inner.Controls.Add(bottom);
            Controls.Add(inner);

            idle.Interval = 5000;
            idle.Tick += delegate
            {
                if ((DateTime.Now - lastActivity).TotalSeconds > 120) Close();
            };
            idle.Start();

            Print("SchoolShield — терминал администратора");
            Print(new string('─', 44));
            if (string.IsNullOrEmpty(cfg.AdminHash))
            {
                Print("Код администратора не задан. Запустите установщик заново.");
                input.Enabled = false;
            }
            else SetStage(Stage.Code);
            Log.Write("admin", "Терминал открыт");
        }

        /// <summary>Show the window and force keyboard focus even if another program is in front.</summary>
        public void BringUp()
        {
            Native.keybd_event((byte)Native.VK_MENU, 0, 0, UIntPtr.Zero);
            Native.keybd_event((byte)Native.VK_MENU, 0, 2, UIntPtr.Zero);   // Alt tap lifts the foreground lock
            if (!Visible) Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            input.Focus();
        }

        private void SetStage(Stage s)
        {
            stage = s;
            if (s == Stage.Code)
            {
                prompt.Text = "Код доступа: ";
                input.UseSystemPasswordChar = true;
                Print("Введите код администратора и нажмите Enter. Esc — закрыть.");
            }
            else
            {
                prompt.Text = "admin@school:~$ ";
                input.UseSystemPasswordChar = false;
                Print("Доступ разрешён. Введите help для списка команд.");
            }
            input.Clear();
            input.Focus();
        }

        private void Print(string text)
        {
            output.AppendText(text + Environment.NewLine);
            output.SelectionStart = output.TextLength;
            output.ScrollToCaret();
        }

        private void OnInputKey(object sender, KeyEventArgs e)
        {
            lastActivity = DateTime.Now;
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Close();
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;

            string line = input.Text;
            input.Clear();
            if (stage == Stage.Code) CheckCode(line);
            else Run(line.Trim());
        }

        private void CheckCode(string code)
        {
            if (DateTime.Now < lockedUntil)
            {
                Print("Заблокировано. Подождите " + (int)(lockedUntil - DateTime.Now).TotalSeconds + " с.");
                return;
            }
            if (Crypto.Verify(code, cfg))
            {
                attempts = 0;
                Log.Write("admin", "Вход в терминал: успешно");
                Print("");
                SetStage(Stage.Shell);
                return;
            }
            attempts++;
            Log.Write("admin", "Неверный код (попытка " + attempts + ")");
            Print("Неверный код (" + attempts + "/" + cfg.MaxCodeAttempts + ")");
            if (attempts >= cfg.MaxCodeAttempts)
            {
                lockedUntil = DateTime.Now.AddSeconds(cfg.LockoutSeconds);
                attempts = 0;
                Print("Слишком много попыток. Блокировка на " + cfg.LockoutSeconds + " с.");
            }
        }

        // ------------------------------------------------------------------ commands

        private void Run(string line)
        {
            if (line.Length == 0) return;
            Print("> " + line);
            string cmd = line;
            string arg = "";
            int sp = line.IndexOf(' ');
            if (sp > 0)
            {
                cmd = line.Substring(0, sp);
                arg = line.Substring(sp + 1).Trim();
            }

            try
            {
                switch (cmd.ToLowerInvariant())
                {
                    case "help":
                    case "?":
                        Print("  status            состояние защиты");
                        Print("  apps              список программ в лаунчере");
                        Print("  explorer          открыть рабочий стол Windows (админ-режим)");
                        Print("  unlock [мин]      снять ограничения (клавиши, сворачивание, охрана) на N минут");
                        Print("  lock              вернуть защиту, закрыть рабочий стол");
                        Print("  sudo <exe> [арг]  запустить программу от имени администратора Windows");
                        Print("  uninstall [имя]   удалить программу из Windows полностью (нужен admin-режим)");
                        Print("  emptybin          очистить корзину (нужен admin-режим)");
                        Print("  update            проверить и установить обновление с GitHub");
                        Print("  panel             открыть панель администратора (нужен пароль админа Windows)");
                        Print("  config            править config.json (блокнот, от админа)");
                        Print("  badwords          править список запрещённых слов");
                        Print("  passwd            сменить код администратора");
                        Print("  reload            перечитать список программ");
                        Print("  logs [N]          последние N строк журнала (по умолчанию 30)");
                        Print("  logoff            выйти из учётной записи (вход под админом Windows)");
                        Print("  restart           перезагрузить ПК");
                        Print("  shutdown          выключить ПК");
                        Print("  exit              закрыть терминал");
                        break;

                    case "status":
                        Print("Пользователь : " + Environment.UserName);
                        Print("Админ-режим  : " + (State.AdminMode ? "ВКЛ, осталось " + (int)State.AdminLeft.TotalMinutes + " мин" : "выкл"));
                        Print("Нарушений    : " + main.Strikes);
                        Print("Программ     : " + main.AppNames().Count);
                        Print("Режим        : " + (State.Preview ? "предпросмотр (ничего не блокируется)" : "оболочка Windows"));
                        Print("Конфиг       : " + Config.ConfigPath);
                        break;

                    case "apps":
                        foreach (string n in main.AppNames()) Print("  " + n);
                        break;

                    case "unlock":
                        {
                            int min = cfg.AdminModeMinutes;
                            if (arg.Length > 0 && !int.TryParse(arg, out min)) min = cfg.AdminModeMinutes;
                            State.EnableAdmin(min);
                            Print("Ограничения сняты на " + min + " мин. Команда lock вернёт защиту.");
                        }
                        break;

                    case "lock":
                        main.LeaveAdminMode();
                        Print("Защита включена.");
                        Close();
                        break;

                    case "explorer":
                    case "desktop":
                        main.EnterDesktopMode();
                        Close();
                        break;

                    case "sudo":
                        if (arg.Length == 0) { Print("Использование: sudo <программа> [аргументы]"); break; }
                        Elevated(arg);
                        break;

                    case "uninstall":
                        if (!State.AdminMode) { Print("Сначала включите админ-режим: unlock"); break; }
                        main.ShowUninstallDialog(arg.Length == 0 ? null : arg);
                        break;

                    case "emptybin":
                        if (!State.AdminMode) { Print("Сначала включите админ-режим: unlock"); break; }
                        main.EmptyRecycleBin();
                        break;

                    case "update":
                        new UpdateForm().Show();
                        Print("Окно обновления открыто.");
                        break;

                    case "panel":
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--admin");
                            psi.UseShellExecute = true;
                            psi.Verb = "runas";
                            Process.Start(psi);
                            Print("Откроется панель администратора (потребуется пароль администратора Windows).");
                        }
                        break;

                    case "config":
                        ElevatedFile("notepad.exe", Config.ConfigPath);
                        Print("Блокнот открыт от админа. После правки выполните: reload (или перезапустите ПК).");
                        break;

                    case "badwords":
                        ElevatedFile("notepad.exe", Config.BadWordsPath);
                        Print("Изменения применятся после перезагрузки ПК.");
                        break;

                    case "passwd":
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--set-code");
                            psi.UseShellExecute = true;
                            psi.Verb = "runas";
                            Process.Start(psi);
                            Print("Откроется окно смены кода (потребуется пароль администратора Windows).");
                        }
                        break;

                    case "reload":
                        main.ReloadCatalog();
                        Print("Список обновляется.");
                        break;

                    case "logs":
                        {
                            int n = 30;
                            if (arg.Length > 0) int.TryParse(arg, out n);
                            Print(Log.Tail(n));
                        }
                        break;

                    case "logoff":
                        if (State.Preview) { Print("[preview] выход пропущен"); break; }
                        main.ExitSession();
                        break;

                    case "restart":
                        if (State.Preview) { Print("[preview] перезагрузка пропущена"); break; }
                        Process.Start("shutdown.exe", "/r /t 3");
                        Print("Перезагрузка…");
                        break;

                    case "shutdown":
                        if (State.Preview) { Print("[preview] выключение пропущено"); break; }
                        Process.Start("shutdown.exe", "/s /t 3");
                        Print("Выключение…");
                        break;

                    case "exit":
                    case "quit":
                        Close();
                        break;

                    default:
                        Print("Неизвестная команда: " + cmd + ". Введите help.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Print("Ошибка: " + ex.Message);
                Log.Write("admin", "Команда '" + cmd + "': " + ex.Message);
            }
            Log.Write("admin", "Команда: " + cmd);
        }

        private static void Elevated(string commandLine)
        {
            string file = commandLine;
            string args = "";
            if (commandLine.StartsWith("\""))
            {
                int end = commandLine.IndexOf('"', 1);
                if (end > 0)
                {
                    file = commandLine.Substring(1, end - 1);
                    args = commandLine.Substring(end + 1).Trim();
                }
            }
            else
            {
                int sp = commandLine.IndexOf(' ');
                if (sp > 0)
                {
                    file = commandLine.Substring(0, sp);
                    args = commandLine.Substring(sp + 1).Trim();
                }
            }
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        private static void ElevatedFile(string exe, string path)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, "\"" + path + "\"");
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            Process.Start(psi);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            idle.Stop();
            idle.Dispose();
            Log.Write("admin", "Терминал закрыт");
            base.OnFormClosed(e);
        }
    }
}
