using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>Live output of a PowerShell script (install / remove protection).</summary>
    internal sealed class ScriptRunDialog : Form
    {
        private readonly RichTextBox box = new RichTextBox();
        private readonly Button close;

        public ScriptRunDialog(string title)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 520);
            BackColor = Theme.Background;
            ShowInTaskbar = false;

            box.SetBounds(12, 12, 796, 440);
            box.ReadOnly = true;
            box.BackColor = Color.Black;
            box.ForeColor = Color.FromArgb(74, 222, 128);
            box.Font = new Font("Consolas", 10.5f);
            box.BorderStyle = BorderStyle.None;
            Controls.Add(box);

            close = UiKit.Flat("Выполняется…", delegate { Close(); });
            close.AutoSize = false;
            close.SetBounds(12, 464, 796, 44);
            close.Enabled = false;
            Controls.Add(close);
        }

        private readonly List<string> pending = new List<string>();
        private int exitCode = int.MinValue;

        public void Append(string line)
        {
            if (IsDisposed) return;
            lock (pending)
            {
                if (!IsHandleCreated) { pending.Add(line); return; }
            }
            try
            {
                BeginInvoke(new Action(delegate
                {
                    box.AppendText(line + Environment.NewLine);
                    box.ScrollToCaret();
                }));
            }
            catch { }
        }

        public void Finished(int code)
        {
            lock (pending)
            {
                if (!IsHandleCreated) { exitCode = code; return; }
            }
            ApplyFinished(code);
        }

        private void ApplyFinished(int code)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    close.Text = code == 0 ? "Готово. Закрыть" : "Завершено с ошибкой (код " + code + "). Закрыть";
                    close.Enabled = true;
                }));
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            List<string> lines;
            int code;
            lock (pending)
            {
                lines = new List<string>(pending);
                pending.Clear();
                code = exitCode;
            }
            foreach (string l in lines) box.AppendText(l + Environment.NewLine);
            if (code != int.MinValue) ApplyFinished(code);
        }

        /// <summary>Runs a started process, streams nothing itself - call Append from the output callback.</summary>
        public static void Run(IWin32Window owner, string title, Func<Protection.DataReceived, Process> start)
        {
            ScriptRunDialog d = new ScriptRunDialog(title);
            Process p;
            try { p = start(d.Append); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Не удалось запустить: " + ex.Message, title);
                return;
            }
            p.Exited += delegate { d.Finished(SafeExit(p)); };
            d.ShowDialog(owner);
        }

        private static int SafeExit(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }
    }

    /// <summary>First-run wizard / "turn protection on": collects the options and runs the installer script.</summary>
    internal sealed class ProtectionWizard : Form
    {
        private readonly Config cfg;
        private readonly TextBox code1 = new TextBox();
        private readonly TextBox code2 = new TextBox();
        private readonly TextBox student = new TextBox();
        private readonly TextBox hotkey = new TextBox();
        private readonly ComboBox dns = new ComboBox();
        private readonly ComboBox web = new ComboBox();
        private readonly ComboBox downloads = new ComboBox();
        private readonly ComboBox usb = new ComboBox();
        private readonly CheckBox autologon = new CheckBox();
        private readonly CheckBox aiModel = new CheckBox();
        private readonly CheckBox keepCode = new CheckBox();
        private readonly CheckBox noShell = new CheckBox();

        public ProtectionWizard(Config cfg, bool alreadyConfigured)
        {
            this.cfg = cfg;
            Text = alreadyConfigured ? "Применить защиту заново" : "Первый запуск: включение защиты";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 640);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10.5f);

            int y = 14;
            Label intro = new Label();
            intro.Text = "Мастер настроит учётную запись ученика, запрет установки программ, фильтры сайтов и DNS, "
                + "а затем сделает лаунчер оболочкой Windows. После этого перезагрузите компьютер.";
            intro.SetBounds(20, y, 600, 54);
            intro.ForeColor = Theme.Muted;
            Controls.Add(intro);
            y += 62;

            AddLabel("Код администратора (минимум 8 символов)", y); y += 24;
            code1.SetBounds(20, y, 290, 28); code1.UseSystemPasswordChar = true; Style(code1); Controls.Add(code1);
            code2.SetBounds(330, y, 290, 28); code2.UseSystemPasswordChar = true; Style(code2); Controls.Add(code2);
            y += 34;
            keepCode.Text = "Оставить прежний код (при повторной настройке)";
            keepCode.AutoSize = true;
            keepCode.Location = new Point(20, y);
            keepCode.Visible = alreadyConfigured && !string.IsNullOrEmpty(cfg.AdminHash);
            keepCode.Checked = keepCode.Visible;
            keepCode.CheckedChanged += delegate { code1.Enabled = code2.Enabled = !keepCode.Checked; };
            code1.Enabled = code2.Enabled = !keepCode.Checked;
            Controls.Add(keepCode);
            y += keepCode.Visible ? 30 : 4;

            AddLabel("Горячая клавиша админа (зажать на 1,5 секунды): нажмите комбинацию в поле", y); y += 24;
            hotkey.SetBounds(20, y, 600, 28);
            hotkey.ReadOnly = true;
            hotkey.Text = cfg.AdminHotkey;
            Style(hotkey);
            hotkey.KeyDown += OnHotkeyDown;
            Controls.Add(hotkey);
            y += 40;

            AddLabel("Имя учётной записи ученика", y); y += 24;
            student.SetBounds(20, y, 290, 28); student.Text = cfg.Install.StudentUser; Style(student); Controls.Add(student);
            y += 40;

            AddLabel("Фильтр DNS", y); AddLabel("Интернет", y, 330); y += 24;
            Combo(dns, 20, y, 290, new string[] { "YandexCloudflare", "Yandex", "Cloudflare", "CleanBrowsing", "None" }, cfg.Install.Dns);
            Combo(web, 330, y, 290, new string[] { "Filter", "Whitelist" }, cfg.Install.WebMode);
            y += 40;

            AddLabel("Скачивания в браузерах", y); AddLabel("Флешки у ученика", y, 330); y += 24;
            Combo(downloads, 20, y, 290, new string[] { "BlockAll", "Dangerous", "Off" }, cfg.Install.Downloads);
            Combo(usb, 330, y, 290, new string[] { "block", "readonly", "allow" }, cfg.UsbMode);
            y += 44;

            autologon.Text = "Автоматический вход под учеником при включении ПК";
            autologon.Checked = cfg.Install.AutoLogon;
            autologon.AutoSize = true;
            autologon.Location = new Point(20, y);
            Controls.Add(autologon); y += 30;

            aiModel.Text = "Скачать локальный ИИ-фильтр изображений (23 МБ, бесплатно, работает без интернета)";
            aiModel.Checked = !NsfwEngine.ModelPresent;
            aiModel.Enabled = !NsfwEngine.ModelPresent;
            aiModel.AutoSize = true;
            aiModel.Location = new Point(20, y);
            Controls.Add(aiModel); y += 30;

            noShell.Text = "Пробный режим: не менять оболочку Windows (рабочий стол останется обычным)";
            noShell.AutoSize = true;
            noShell.Location = new Point(20, y);
            Controls.Add(noShell); y += 44;

            Button go = UiKit.Accent("Включить защиту", delegate { Go(); });
            go.AutoSize = false;
            go.SetBounds(20, y, 300, 44);
            Controls.Add(go);
            Button cancel = UiKit.Flat("Отмена", delegate { DialogResult = DialogResult.Cancel; Close(); });
            cancel.AutoSize = false;
            cancel.SetBounds(340, y, 280, 44);
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void Style(TextBox t)
        {
            t.BackColor = Theme.Card;
            t.ForeColor = Theme.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
        }

        private void AddLabel(string text, int y) { AddLabel(text, y, 20); }

        private void AddLabel(string text, int y, int x)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, 600, 22);
            l.ForeColor = Theme.Muted;
            Controls.Add(l);
        }

        private void Combo(ComboBox c, int x, int y, int w, string[] items, string selected)
        {
            c.SetBounds(x, y, w, 28);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            c.SelectedItem = selected;
            if (c.SelectedIndex < 0) c.SelectedIndex = 0;
            Controls.Add(c);
        }

        private void OnHotkeyDown(object s, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            Keys k = e.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.None) return;
            List<string> parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");
            if (parts.Count < 2)
            {
                MessageBox.Show(this, "Нужна комбинация минимум из двух клавиш-модификаторов (Ctrl, Shift, Alt) и ещё одной клавиши.", "Горячая клавиша");
                return;
            }
            parts.Add(k.ToString());
            hotkey.Text = string.Join("+", parts);
        }

        private void Go()
        {
            if (!keepCode.Checked)
            {
                if (code1.Text.Length < 8) { MessageBox.Show(this, "Код слишком короткий: нужно минимум 8 символов.", "Код администратора"); return; }
                if (code1.Text != code2.Text) { MessageBox.Show(this, "Коды не совпадают.", "Код администратора"); return; }
            }
            string name = student.Text.Trim();
            if (name.Length == 0 || name.IndexOfAny(new char[] { '\\', '/', '"', '[', ']', ':', ';', '|', '=', ',', '+', '*', '?', '<', '>', '@' }) >= 0)
            {
                MessageBox.Show(this, "Недопустимое имя учётной записи.", "Ученик");
                return;
            }
            try
            {
                Protection.EnsureConfigFiles();
                Config fresh = Config.Load();
                fresh.AdminHotkey = hotkey.Text;
                fresh.UsbMode = (string)usb.SelectedItem;
                fresh.Install.StudentUser = name;
                fresh.Install.Dns = (string)dns.SelectedItem;
                fresh.Install.WebMode = (string)web.SelectedItem;
                fresh.Install.Downloads = (string)downloads.SelectedItem;
                fresh.Install.AutoLogon = autologon.Checked;
                fresh.Save();
                cfg.AdminHotkey = fresh.AdminHotkey;
                cfg.UsbMode = fresh.UsbMode;
                cfg.Install = fresh.Install;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить настройки: " + ex.Message, "Ошибка");
                return;
            }

            bool downloadModel = aiModel.Enabled && aiModel.Checked;
            string code = keepCode.Checked ? null : code1.Text;
            bool shellless = noShell.Checked;
            Hide();
            ScriptRunDialog.Run(this, "Включение защиты", delegate (Protection.DataReceived output)
            {
                if (downloadModel)
                {
                    output("Скачиваю ИИ-фильтр (" + FileIndex.Size(Release.ModelSize) + ")…");
                    try
                    {
                        Downloader.InstallModel(null);
                        NsfwEngine.Reset();
                        output("ИИ-фильтр установлен.");
                    }
                    catch (Exception ex) { output("! ИИ-фильтр не установлен: " + ex.Message + " (можно повторить позже на вкладке «ИИ-фильтр»)"); }
                }
                return Protection.StartApply(cfg.Install, cfg.UsbMode, code, shellless, output);
            });
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>
    /// The administrator's control panel (desktop icon): status, protection on/off, programs (hide / uninstall),
    /// site lists, AI filter, updates from GitHub, settings, log.
    /// </summary>
    internal sealed class AdminPanelForm : Form
    {
        private readonly Config cfg;
        private readonly Panel nav = new Panel();
        private readonly Panel pageHost = new Panel();
        private readonly Dictionary<string, Func<Control>> builders = new Dictionary<string, Func<Control>>();
        private readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        private readonly List<string> order = new List<string>();
        private string currentId;

        public AdminPanelForm(Config cfg)
        {
            this.cfg = cfg;
            Text = "SchoolShield — панель администратора";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 720);
            MinimumSize = new Size(960, 640);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            nav.Dock = DockStyle.Left;
            nav.Width = 220;
            nav.BackColor = Theme.Sidebar;
            pageHost.Dock = DockStyle.Fill;
            pageHost.BackColor = Theme.Background;
            pageHost.Padding = new Padding(20);
            Controls.Add(pageHost);
            Controls.Add(nav);

            Add("status", "Состояние", BuildStatus);
            Add("apps", "Программы", BuildApps);
            Add("lists", "Сайты и слова", BuildLists);
            Add("ai", "ИИ-фильтр", BuildAi);
            Add("update", "Обновления", delegate { return new UpdatePanel(null, true); });
            Add("settings", "Настройки", BuildSettings);
            Add("log", "Журнал", BuildLog);
            Add("about", "О программе", BuildAbout);

            for (int i = order.Count - 1; i >= 0; i--) nav.Controls.Add(navButtons[order[i]]);
            Label brand = new Label();
            brand.Text = "SchoolShield";
            brand.Dock = DockStyle.Top;
            brand.Height = 70;
            brand.TextAlign = ContentAlignment.MiddleLeft;
            brand.Padding = new Padding(20, 0, 0, 0);
            brand.Font = Theme.UiFont(15, FontStyle.Bold);
            brand.ForeColor = Theme.Accent;
            nav.Controls.Add(brand);

            Show2("status");
            Shown += delegate
            {
                if (!Protection.Configured()) OpenWizard();
            };
        }

        private void Add(string id, string title, Func<Control> builder)
        {
            builders[id] = builder;
            order.Add(id);
            Button b = new Button();
            b.Text = "   " + title;
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Dock = DockStyle.Top;
            b.Height = 46;
            b.Font = Theme.UiFont(11.5f);
            b.ForeColor = Theme.Text;
            b.BackColor = Theme.Sidebar;
            b.Cursor = Cursors.Hand;
            b.Click += delegate { Show2(id); };
            navButtons[id] = b;
        }

        private void Show2(string id)
        {
            currentId = id;
            foreach (KeyValuePair<string, Button> kv in navButtons)
                kv.Value.BackColor = kv.Key == id ? Theme.Card : Theme.Sidebar;
            foreach (Control c in new List<Control>(Ctl(pageHost))) { pageHost.Controls.Remove(c); c.Dispose(); }
            Control page;
            try { page = builders[id](); }
            catch (Exception ex)
            {
                Label l = new Label();
                l.Text = "Не удалось открыть страницу: " + ex.Message;
                l.AutoSize = true;
                page = l;
            }
            page.Dock = DockStyle.Fill;
            pageHost.Controls.Add(page);
        }

        private static IEnumerable<Control> Ctl(Control c)
        {
            foreach (Control x in c.Controls) yield return x;
        }

        private void OpenWizard()
        {
            using (ProtectionWizard w = new ProtectionWizard(cfg, Protection.Configured()))
                w.ShowDialog(this);
            Show2(currentId ?? "status");
        }

        // ---------------------------------------------------------------- small layout helpers

        private static Label Lbl(Control parent, string text, int x, int y, int w, bool muted)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 24);
            if (muted) l.ForeColor = Theme.Muted;
            l.AutoEllipsis = true;
            parent.Controls.Add(l);
            return l;
        }

        private static TextBox Txt(Control parent, int x, int y, int w, string text)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 28);
            t.Text = text ?? "";
            t.BackColor = Theme.Card;
            t.ForeColor = Theme.Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(t);
            return t;
        }

        private static TextBox Memo(Control parent, int x, int y, int w, int h, string text)
        {
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ScrollBars = ScrollBars.Vertical;
            t.AcceptsReturn = true;
            t.SetBounds(x, y, w, h);
            t.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            t.BackColor = Theme.Card;
            t.ForeColor = Theme.Text;
            t.BorderStyle = BorderStyle.None;
            t.Font = new Font("Consolas", 10.5f);
            parent.Controls.Add(t);
            return t;
        }

        private static CheckBox Chk(Control parent, string text, int x, int y, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            c.ForeColor = Theme.Text;
            parent.Controls.Add(c);
            return c;
        }

        private static Button Btn(Control parent, string text, int x, int y, int w, bool accent, EventHandler click)
        {
            Button b = accent ? UiKit.Accent(text, click) : UiKit.Flat(text, click);
            b.AutoSize = false;
            b.SetBounds(x, y, w, 40);
            parent.Controls.Add(b);
            return b;
        }

        private void SaveConfig()
        {
            try
            {
                cfg.Save();
                MessageBox.Show(this, "Сохранено. Изменения вступят в силу при следующем входе ученика (или после перезагрузки ПК).", "SchoolShield");
            }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить: " + ex.Message, "Ошибка"); }
        }

        // ---------------------------------------------------------------- status

        private Control BuildStatus()
        {
            Panel p = new Panel();
            bool configured = Protection.Configured();
            int y = 4;
            if (!configured)
            {
                Label banner = new Label();
                banner.Text = "  Первый запуск. Защита ещё не включена: нажмите «Включить защиту…», задайте код и параметры.";
                banner.SetBounds(0, y, 820, 40);
                banner.TextAlign = ContentAlignment.MiddleLeft;
                banner.BackColor = Theme.Accent;
                banner.ForeColor = Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White;
                banner.Font = Theme.UiFont(11, FontStyle.Bold);
                p.Controls.Add(banner);
                y += 56;
            }

            string student = cfg.Install.StudentUser;
            Row(p, ref y, "Версия программы", AppInfo.VersionString + "  •  автор: " + Release.Author, true);
            Row(p, ref y, "Оболочка Windows", Protection.ShellRegistered() ? "лаунчер SchoolShield (включено)" : "обычный проводник (защита выключена)", Protection.ShellRegistered());
            Row(p, ref y, "Учётная запись ученика", student + (Protection.StudentExists(student) ? " — создана" : " — не создана"), Protection.StudentExists(student));
            Row(p, ref y, "Запрет установки и запуска (SRP)", Protection.SrpPresent() ? "включён" : "выключен", Protection.SrpPresent());
            Row(p, ref y, "ИИ-фильтр изображений", NsfwEngine.ModelPresent ? "модель установлена" : "модель не установлена (вкладка «ИИ-фильтр»)", NsfwEngine.ModelPresent);
            Row(p, ref y, "Горячая клавиша админа", cfg.AdminHotkey + " — удерживать " + (cfg.AdminHotkeyHoldMs / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " с", true);
            Row(p, ref y, "Папка настроек и журнала", Config.DataDir, true);
            y += 14;

            Btn(p, configured ? "Применить защиту заново…" : "Включить защиту…", 0, y, 300, true, delegate { OpenWizard(); });
            Btn(p, "Отключить защиту", 316, y, 240, false, delegate { RemoveProtection(); });
            Btn(p, "Предпросмотр лаунчера", 572, y, 240, false, delegate
            {
                try { Process.Start(AppInfo.ExePath, "--preview"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            });
            y += 56;
            Btn(p, "Перезагрузить компьютер", 0, y, 300, false, delegate
            {
                if (MessageBox.Show(this, "Перезагрузить компьютер сейчас?", "Перезагрузка", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Process.Start("shutdown.exe", "/r /t 5");
            });
            Btn(p, "Очистить корзины всех пользователей", 316, y, 340, false, delegate
            {
                if (MessageBox.Show(this, "Очистить корзину у ВСЕХ пользователей этого компьютера?", "Корзина", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    RecycleBin.EmptyAllUsersElevated();
                    MessageBox.Show(this, "Очистка запущена в фоне.", "Корзина");
                }
            });
            return p;
        }

        private static void Row(Panel p, ref int y, string caption, string value, bool ok)
        {
            Lbl(p, caption, 0, y, 280, true);
            Label v = Lbl(p, value, 290, y, 600, false);
            v.ForeColor = ok ? Theme.Text : Color.FromArgb(251, 191, 36);
            y += 32;
        }

        private void RemoveProtection()
        {
            if (MessageBox.Show(this,
                "Защита будет отключена: вернётся обычный рабочий стол, снимутся запреты, фильтры и правила брандмауэра.\n"
                + "Сама программа и настройки останутся. Продолжить?", "Отключение защиты",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ScriptRunDialog.Run(this, "Отключение защиты", delegate (Protection.DataReceived output) { return Protection.StartRemove(output); });
            Show2("status");
        }

        // ---------------------------------------------------------------- programs

        private Control BuildApps()
        {
            Panel p = new Panel();
            Label hint = Lbl(p, "Показывайте ученикам только нужное. Правая кнопка мыши на строке — удалить программу из Windows полностью.", 0, 0, 900, true);
            hint.AutoSize = false;

            DataGridView grid = new DataGridView();
            grid.SetBounds(0, 34, 840, 540);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            grid.BackgroundColor = Theme.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.CardHover;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.GridColor = Theme.Line;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            DataGridViewTextBoxColumn cName = new DataGridViewTextBoxColumn();
            cName.HeaderText = "Программа"; cName.ReadOnly = true; cName.Width = 380;
            DataGridViewComboBoxColumn cCat = new DataGridViewComboBoxColumn();
            cCat.HeaderText = "Раздел"; cCat.Width = 220; cCat.FlatStyle = FlatStyle.Flat;
            foreach (CategoryRule r in cfg.CategoryRules) cCat.Items.Add(r.Name);
            cCat.Items.Add("Другое");
            DataGridViewCheckBoxColumn cShow = new DataGridViewCheckBoxColumn();
            cShow.HeaderText = "Показывать ученикам"; cShow.Width = 190;
            grid.Columns.AddRange(cName, cCat, cShow);

            List<AppEntry> all = AppCatalog.LoadAll(cfg);
            HashSet<string> visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppEntry a in AppCatalog.Load(cfg)) visible.Add(a.Name);
            Dictionary<string, string> idByName = new Dictionary<string, string>();
            foreach (CategoryRule r in cfg.CategoryRules) idByName[r.Name] = r.Id;
            idByName["Другое"] = AppCatalog.OtherId;
            foreach (AppEntry a in all)
            {
                string catName = "Другое";
                foreach (CategoryRule r in cfg.CategoryRules) if (r.Id == a.Category) catName = r.Name;
                grid.Rows.Add(a.Name, catName, visible.Contains(a.Name));
            }

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem rm = new ToolStripMenuItem("Удалить из системы…");
            rm.Click += delegate
            {
                if (grid.CurrentRow == null) return;
                string name = Convert.ToString(grid.CurrentRow.Cells[0].Value);
                UninstallDialog d = new UninstallDialog(name, null);
                d.IsAdminPanel = true;
                d.ShowDialog(this);
            };
            menu.Items.Add(rm);
            grid.ContextMenuStrip = menu;
            grid.CellMouseDown += delegate (object s, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; grid.CurrentCell = grid.Rows[e.RowIndex].Cells[0]; }
            };
            p.Controls.Add(grid);

            Button save = Btn(p, "Сохранить", 0, 586, 200, true, delegate
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    string name = Convert.ToString(row.Cells[0].Value);
                    string catName = Convert.ToString(row.Cells[1].Value);
                    bool show = Convert.ToBoolean(row.Cells[2].Value);

                    cfg.HiddenExact.RemoveAll(delegate (string x) { return string.Equals(x, name, StringComparison.OrdinalIgnoreCase); });
                    cfg.AlwaysShow.RemoveAll(delegate (string x) { return string.Equals(x, name, StringComparison.OrdinalIgnoreCase); });
                    if (!show) cfg.HiddenExact.Add(name);
                    else cfg.AlwaysShow.Add(name);

                    string id = idByName.ContainsKey(catName) ? idByName[catName] : AppCatalog.OtherId;
                    string auto = AppCatalog.Classify(AutoOnly(), name);
                    if (id == auto) cfg.AppCategories.Remove(name); else cfg.AppCategories[name] = id;
                }
                SaveConfig();
            });
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Button rmAll = Btn(p, "Все установленные программы…", 216, 586, 300, false, delegate
            {
                UninstallDialog d = new UninstallDialog(null, null);
                d.IsAdminPanel = true;
                d.ShowDialog(this);
            });
            rmAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            return p;
        }

        private Config AutoOnly()
        {
            Config c = new Config();
            c.AppCategories = new Dictionary<string, string>();
            c.CategoryRules = cfg.CategoryRules;
            return c;
        }

        // ---------------------------------------------------------------- site and word lists

        private Control BuildLists()
        {
            Panel p = new Panel();
            Lbl(p, "Запрещённые сайты (по одному в строке)", 0, 0, 400, true);
            TextBox block = Memo(p, 0, 26, 400, 440, Read(Config.BlocklistPath));
            Lbl(p, "Разрешённые сайты (режим «Whitelist»)", 416, 0, 400, true);
            TextBox white = Memo(p, 416, 26, 400, 200, Read(Config.WhitelistPath));
            Lbl(p, "Запрещённые слова в заголовках окон (регулярные выражения)", 416, 236, 500, true);
            TextBox words = Memo(p, 416, 262, 400, 204, Read(Config.BadWordsPath));
            Btn(p, "Сохранить и применить к браузерам", 0, 486, 400, true, delegate
            {
                try
                {
                    Write(Config.BlocklistPath, block.Text);
                    Write(Config.WhitelistPath, white.Text);
                    Write(Config.BadWordsPath, words.Text);
                }
                catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить: " + ex.Message); return; }
                ScriptRunDialog.Run(this, "Применение списков", delegate (Protection.DataReceived output)
                {
                    return Protection.StartApply(cfg.Install, cfg.UsbMode, null, true, output);
                });
            });
            Lbl(p, "Слова действуют после перезагрузки ПК, сайты — после применения (кнопка слева).", 416, 486, 460, true);
            return p;
        }

        private static string Read(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; } catch { return ""; }
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- AI filter

        private Control BuildAi()
        {
            Panel p = new Panel();
            int y = 0;
            Lbl(p, "Локальный ИИ-фильтр изображений: Yahoo open_nsfw (лицензия BSD-2), ONNX Runtime. Работает на процессоре,", 0, y, 900, true); y += 24;
            Lbl(p, "ничего не отправляет в интернет. Проверяет экран ученика и скрывает неприемлемые картинки в галерее.", 0, y, 900, true); y += 40;

            Label state = Lbl(p, "", 0, y, 900, false);
            Action refresh = delegate
            {
                state.Text = "Модель: " + (NsfwEngine.ModelPresent ? "установлена (" + FileIndex.Size(new FileInfo(Release.ModelPath).Length) + ")" : "НЕ установлена");
            };
            refresh();
            y += 36;
            Button install = Btn(p, "Скачать и установить модель", 0, y, 300, true, null);
            install.Click += delegate
            {
                install.Enabled = false;
                install.Text = "Скачиваю…";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try { Downloader.InstallModel(null); NsfwEngine.Reset(); } catch (Exception ex) { err = ex.Message; }
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            install.Enabled = true;
                            install.Text = "Скачать и установить модель";
                            refresh();
                            MessageBox.Show(this, err == null ? "Модель установлена." : "Ошибка: " + err, "ИИ-фильтр");
                        }));
                    }
                    catch { }
                });
            };
            Btn(p, "Проверить картинку…", 316, y, 260, false, delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff";
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    NsfwEngine e = NsfwEngine.TryGet();
                    if (e == null) { MessageBox.Show(this, "ИИ-фильтр недоступен: " + NsfwEngine.LastError, "ИИ-фильтр"); return; }
                    try
                    {
                        double s = e.ScoreFile(ofd.FileName);
                        MessageBox.Show(this, "Вероятность неприемлемого содержимого: " + (s * 100).ToString("0.0") + "%\n"
                            + (s >= cfg.Ai.Threshold ? "Картинка была бы ЗАБЛОКИРОВАНА." : "Картинка была бы показана."), "Результат");
                    }
                    catch (Exception ex) { MessageBox.Show(this, "Не удалось проверить: " + ex.Message, "ИИ-фильтр"); }
                }
            });
            y += 64;

            CheckBox enabled = Chk(p, "Включить ИИ-фильтр экрана и галереи", 0, y, cfg.Ai.Enabled); y += 32;
            CheckBox tiles = Chk(p, "Дополнительно проверять четверти экрана (находит мелкие картинки, но нагружает процессор)", 0, y, cfg.Ai.Tiles); y += 32;
            CheckBox gallery = Chk(p, "Скрывать неприемлемые картинки в разделе «Фото, видео, музыка»", 0, y, cfg.Ai.HideInGallery); y += 40;

            Lbl(p, "Порог срабатывания, %:", 0, y, 200, true);
            NumericUpDown th = new NumericUpDown();
            th.SetBounds(210, y - 2, 80, 28); th.Minimum = 50; th.Maximum = 99; th.Value = (decimal)Math.Max(50, Math.Min(99, cfg.Ai.Threshold * 100));
            p.Controls.Add(th);
            Lbl(p, "Проверка каждые, мс:", 320, y, 200, true);
            NumericUpDown iv = new NumericUpDown();
            iv.SetBounds(520, y - 2, 90, 28); iv.Minimum = 1000; iv.Maximum = 30000; iv.Increment = 500; iv.Value = Math.Max(1000, Math.Min(30000, cfg.Ai.IntervalMs));
            p.Controls.Add(iv);
            y += 44;
            Lbl(p, "Ниже порог — строже, но больше ложных срабатываний (рекомендуется 90).", 0, y, 800, true); y += 44;

            Btn(p, "Сохранить", 0, y, 200, true, delegate
            {
                cfg.Ai.Enabled = enabled.Checked;
                cfg.Ai.Tiles = tiles.Checked;
                cfg.Ai.HideInGallery = gallery.Checked;
                cfg.Ai.Threshold = (double)th.Value / 100.0;
                cfg.Ai.IntervalMs = (int)iv.Value;
                SaveConfig();
            });
            return p;
        }

        // ---------------------------------------------------------------- settings

        private Control BuildSettings()
        {
            Panel p = new Panel();
            p.AutoScroll = true;
            int y = 0;
            Lbl(p, "Заголовок лаунчера", 0, y, 300, true); Lbl(p, "Подзаголовок", 330, y, 300, true); y += 24;
            TextBox title = Txt(p, 0, y, 310, cfg.Title);
            TextBox sub = Txt(p, 330, y, 310, cfg.Subtitle); y += 42;

            Lbl(p, "Горячая клавиша админа (нажмите комбинацию в поле)", 0, y, 400, true);
            Lbl(p, "Удерживать, мс", 430, y, 200, true); Lbl(p, "Админ-режим, мин", 580, y, 200, true); y += 24;
            TextBox hot = Txt(p, 0, y, 410, cfg.AdminHotkey);
            hot.ReadOnly = true;
            hot.KeyDown += delegate (object s, KeyEventArgs e)
            {
                e.SuppressKeyPress = true;
                Keys k = e.KeyCode;
                if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.None) return;
                List<string> parts = new List<string>();
                if (e.Control) parts.Add("Ctrl");
                if (e.Shift) parts.Add("Shift");
                if (e.Alt) parts.Add("Alt");
                if (parts.Count < 2) { MessageBox.Show(this, "Нужно минимум два модификатора (Ctrl, Shift, Alt) и ещё одна клавиша."); return; }
                parts.Add(k.ToString());
                hot.Text = string.Join("+", parts);
            };
            NumericUpDown hold = new NumericUpDown();
            hold.SetBounds(430, y, 130, 28); hold.Minimum = 500; hold.Maximum = 5000; hold.Increment = 100; hold.Value = Math.Max(500, Math.Min(5000, cfg.AdminHotkeyHoldMs));
            p.Controls.Add(hold);
            NumericUpDown mins = new NumericUpDown();
            mins.SetBounds(580, y, 100, 28); mins.Minimum = 1; mins.Maximum = 480; mins.Value = Math.Max(1, Math.Min(480, cfg.AdminModeMinutes));
            p.Controls.Add(mins);
            y += 48;

            CheckBox shut = Chk(p, "Ученик может выключать компьютер", 0, y, cfg.AllowStudentShutdown); y += 28;
            CheckBox rest = Chk(p, "Ученик может перезагружать компьютер", 0, y, cfg.AllowStudentRestart); y += 28;
            CheckBox del = Chk(p, "Ученик может удалять свои файлы (в корзину)", 0, y, cfg.StudentCanDelete); y += 28;
            CheckBox minim = Chk(p, "Запретить сворачивание окон", 0, y, cfg.BlockMinimize); y += 28;
            CheckBox bad = Chk(p, "Закрывать окна с запрещёнными словами", 0, y, cfg.CloseBadWindows); y += 28;
            CheckBox dl = Chk(p, "Удалять скачанные установщики и архивы", 0, y, cfg.DeleteForbiddenFiles); y += 40;

            Lbl(p, "Разделы лаунчера", 0, y, 300, true); y += 26;
            string[][] secs = new string[][] {
                new string[] { "home", "Главная" }, new string[] { "apps", "Программы" }, new string[] { "docs", "Документы" },
                new string[] { "media", "Фото, видео, музыка" }, new string[] { "files", "Мои файлы" }, new string[] { "web", "Интернет" },
                new string[] { "system", "Компьютер" }
            };
            List<CheckBox> secBoxes = new List<CheckBox>();
            int x = 0;
            foreach (string[] s in secs)
            {
                CheckBox c = Chk(p, s[1], x, y, cfg.SectionEnabled(s[0]));
                c.Tag = s[0];
                secBoxes.Add(c);
                x += 150;
                if (x > 700) { x = 0; y += 28; }
            }
            y += 44;

            Lbl(p, "Полезные сайты (Название | адрес)", 0, y, 400, true);
            Lbl(p, "Звонки (08:30-09:15 | Урок 1)", 430, y, 400, true); y += 24;
            TextBox links = Memo(p, 0, y, 410, 130, LinksText());
            TextBox bells = Memo(p, 430, y, 380, 130, BellsText()); y += 142;

            Lbl(p, "Общие папки для учеников (Название | путь | ro — только чтение)", 0, y, 600, true); y += 24;
            TextBox shared = Memo(p, 0, y, 810, 80, SharedText()); y += 96;

            Btn(p, "Сохранить", 0, y, 200, true, delegate
            {
                cfg.Title = title.Text;
                cfg.Subtitle = sub.Text;
                cfg.AdminHotkey = hot.Text;
                cfg.AdminHotkeyHoldMs = (int)hold.Value;
                cfg.AdminModeMinutes = (int)mins.Value;
                cfg.AllowStudentShutdown = shut.Checked;
                cfg.AllowStudentRestart = rest.Checked;
                cfg.StudentCanDelete = del.Checked;
                cfg.BlockMinimize = minim.Checked;
                cfg.CloseBadWindows = bad.Checked;
                cfg.DeleteForbiddenFiles = dl.Checked;
                cfg.Sections.Clear();
                foreach (CheckBox c in secBoxes) if (c.Checked) cfg.Sections.Add((string)c.Tag);
                if (cfg.Sections.Count == 0) cfg.Sections.Add("home");
                ParseLinks(links.Text);
                ParseBells(bells.Text);
                ParseShared(shared.Text);
                SaveConfig();
            });
            Btn(p, "Сменить код администратора…", 216, y, 300, false, delegate
            {
                using (SetCodeForm f = new SetCodeForm()) f.ShowDialog(this);
            });
            return p;
        }

        private string LinksText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LinkItem l in cfg.Links) sb.AppendLine(l.Name + " | " + l.Url);
            return sb.ToString();
        }

        private string BellsText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (BellItem b in cfg.Bells) sb.AppendLine(b.Start + "-" + b.End + " | " + b.Title);
            return sb.ToString();
        }

        private string SharedText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (SharedFolder f in cfg.SharedFolders) sb.AppendLine(f.Name + " | " + f.Path + (f.ReadOnly ? " | ro" : ""));
            return sb.ToString();
        }

        private static string[] Lines(string text)
        {
            return text.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void ParseLinks(string text)
        {
            cfg.Links.Clear();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 2) continue;
                cfg.Links.Add(new LinkItem(parts[0].Trim(), parts[1].Trim()));
            }
        }

        private void ParseBells(string text)
        {
            cfg.Bells.Clear();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                string[] t = parts[0].Split('-');
                if (t.Length != 2) continue;
                cfg.Bells.Add(new BellItem(t[0].Trim(), t[1].Trim(), parts.Length > 1 ? parts[1].Trim() : "Урок"));
            }
        }

        private void ParseShared(string text)
        {
            cfg.SharedFolders.Clear();
            foreach (string line in Lines(text))
            {
                string[] parts = line.Split('|');
                if (parts.Length < 2) continue;
                bool ro = parts.Length > 2 && parts[2].Trim().Equals("ro", StringComparison.OrdinalIgnoreCase);
                cfg.SharedFolders.Add(new SharedFolder(parts[0].Trim(), parts[1].Trim(), ro));
            }
        }

        // ---------------------------------------------------------------- log

        private Control BuildLog()
        {
            Panel p = new Panel();
            TextBox box = Memo(p, 0, 0, 840, 560, Log.Tail(400));
            box.ReadOnly = true;
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Button b1 = Btn(p, "Обновить", 0, 576, 180, true, delegate { box.Text = Log.Tail(400).Replace("\r\n", "\n").Replace("\n", Environment.NewLine); box.SelectionStart = box.TextLength; box.ScrollToCaret(); });
            b1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Button b2 = Btn(p, "Открыть папку журналов", 196, 576, 280, false, delegate
            {
                try { Process.Start(Path.Combine(Config.DataDir, "logs")); } catch { }
            });
            b2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            return p;
        }

        // ---------------------------------------------------------------- about

        private Control BuildAbout()
        {
            Panel p = new Panel();
            Label t = new Label();
            t.Text = "SchoolShield";
            t.Font = Theme.UiFont(28, FontStyle.Bold);
            t.ForeColor = Theme.Accent;
            t.SetBounds(0, 0, 800, 56);
            p.Controls.Add(t);
            int y = 70;
            foreach (string line in new string[] {
                "Версия " + AppInfo.VersionString,
                "Автор: " + Release.Author,
                "Защита школьного компьютера с Windows 10: лаунчер-оболочка, запрет установки ПО,",
                "фильтры сайтов и DNS, локальный ИИ-фильтр изображений, обновления из GitHub.",
                "",
                "Исходный код и релизы: " + Release.SiteUrl,
                "Лицензия: MIT. ИИ-модель: Yahoo open_nsfw (BSD-2-Clause). ONNX Runtime: MIT.",
                "Подробности — в файлах LICENSE и THIRD_PARTY_NOTICES.txt рядом с программой."
            })
            {
                Lbl(p, line, 0, y, 900, false);
                y += 28;
            }
            Btn(p, "Открыть страницу проекта", 0, y + 16, 300, false, delegate
            {
                try { Process.Start(Release.SiteUrl); } catch { }
            });
            return p;
        }
    }
}
