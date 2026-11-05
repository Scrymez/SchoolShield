using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SchoolShield
{
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string Notes;
        public string SetupName;
        public string SetupUrl;
        public string SigUrl;
        public long Size;
    }

    /// <summary>
    /// Online update from GitHub Releases. The installer is downloaded over HTTPS and its RSA-SHA256 signature is
    /// checked against the public key built into this program: an update that was not signed by the author is never run.
    /// </summary>
    internal static class Updater
    {
        public static UpdateInfo Check()
        {
            string json = Downloader.GetString("https://api.github.com/repos/" + Release.Repo + "/releases/latest");
            Dictionary<string, object> d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
            UpdateInfo info = new UpdateInfo();
            info.Tag = Convert.ToString(d["tag_name"]);
            info.Notes = d.ContainsKey("body") ? Convert.ToString(d["body"]) : "";
            Version v;
            if (!Version.TryParse(info.Tag.TrimStart('v', 'V'), out v)) throw new InvalidDataException("Непонятный номер версии: " + info.Tag);
            info.Version = v;

            Dictionary<string, string> assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, long> sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            ArrayList list = d.ContainsKey("assets") ? d["assets"] as ArrayList : null;
            if (list != null)
            {
                foreach (object o in list)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Convert.ToString(a["name"]);
                    assets[name] = Convert.ToString(a["browser_download_url"]);
                    sizes[name] = Convert.ToInt64(a["size"]);
                }
            }
            foreach (string name in assets.Keys)
            {
                if (Regex.IsMatch(name, @"^SchoolShield-Setup-.+\.exe$", RegexOptions.IgnoreCase) && assets.ContainsKey(name + ".sig"))
                {
                    info.SetupName = name;
                    info.SetupUrl = assets[name];
                    info.SigUrl = assets[name + ".sig"];
                    info.Size = sizes[name];
                    break;
                }
            }
            if (info.SetupUrl == null) throw new InvalidDataException("В релизе нет подписанного установщика.");
            return info;
        }

        public static bool IsNewer(UpdateInfo info)
        {
            Version cur = AppInfo.Version;
            Version cur3 = new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
            Version rel3 = new Version(info.Version.Major, info.Version.Minor, Math.Max(0, info.Version.Build));
            return rel3 > cur3;
        }

        /// <summary>Downloads the installer and verifies its signature. Returns the path of the verified file.</summary>
        public static string DownloadAndVerify(UpdateInfo info, Action<long, long> progress)
        {
            string dir = Path.Combine(Path.GetTempPath(), "SchoolShield-update");
            Directory.CreateDirectory(dir);
            string setup = Path.Combine(dir, info.SetupName);
            byte[] sig = Downloader.GetBytes(info.SigUrl);
            Downloader.DownloadFile(info.SetupUrl, setup, progress);
            if (!SignatureVerifier.Verify(setup, sig))
            {
                try { File.Delete(setup); } catch { }
                Log.Write("update", "ПОДПИСЬ НЕ СОВПАЛА: " + info.SetupName);
                throw new InvalidDataException("Подпись обновления не совпала с ключом автора. Установка отменена.");
            }
            Log.Write("update", "Скачано и проверено: " + info.SetupName);
            return setup;
        }

        /// <summary>
        /// Starts the installer. It needs administrator rights, so Windows shows its consent window (a student session is asked for the
        /// administrator's login and password); this call returns only after the answer. Run it off the UI thread.
        /// </summary>
        public static void LaunchSetup(string setupPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo(setupPath, "/update");
            psi.UseShellExecute = true;   // the installer asks for administrator rights itself
            Log.Write("update", "Запускаю установщик: " + setupPath);
            using (Process p = Process.Start(psi))
                Log.Write("update", "Установщик запущен" + (p == null ? "" : " (pid " + p.Id + ")"));
        }
    }

    /// <summary>"Check for updates" page. Embedded in the Admin panel and in the small window opened from the terminal.</summary>
    internal sealed class UpdatePanel : UserControl
    {
        private readonly Label current = new Label();
        private readonly Label state = new Label();
        private readonly TextBox notes = new TextBox();
        private readonly ProgressBar bar = new ProgressBar();
        private readonly Button check;
        private readonly Button install;
        private readonly Action beforeInstall;
        private readonly bool exitAfterLaunch;
        private UpdateInfo found;

        /// <param name="exitAfterLaunch">true in the Admin panel; false inside the student shell, whose exit would end the session.</param>
        public UpdatePanel(Action onBeforeInstall, bool exitAfterLaunch)
        {
            beforeInstall = onBeforeInstall;
            this.exitAfterLaunch = exitAfterLaunch;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(10.5f);
            Dock = DockStyle.Fill;

            current.Text = "Установлена версия " + AppInfo.VersionString + "   •   источник: github.com/" + Release.Repo;
            current.SetBounds(20, 16, 800, 26);
            current.Font = Theme.UiFont(11, FontStyle.Bold);

            check = UiKit.Accent("Проверить обновления", delegate { DoCheck(); });
            check.AutoSize = false;
            check.SetBounds(20, 54, 240, 38);

            install = UiKit.Flat("Скачать и установить", delegate { DoInstall(); });
            install.AutoSize = false;
            install.SetBounds(272, 54, 240, 38);
            install.Enabled = false;

            state.SetBounds(20, 104, 860, 26);
            state.ForeColor = Theme.Muted;

            bar.SetBounds(20, 134, 860, 14);
            bar.Visible = false;

            notes.Multiline = true;
            notes.ReadOnly = true;
            notes.ScrollBars = ScrollBars.Vertical;
            notes.BackColor = Theme.Card;
            notes.ForeColor = Theme.Text;
            notes.BorderStyle = BorderStyle.None;
            notes.SetBounds(20, 160, 860, 300);
            notes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            Controls.AddRange(new Control[] { current, check, install, state, bar, notes });
        }

        /// <summary>Starts the installer on a worker thread (the administrator prompt blocks the caller) and keeps the window alive meanwhile.</summary>
        private void StartSetup(string path)
        {
            System.Windows.Forms.Timer reminder = new System.Windows.Forms.Timer();
            reminder.Interval = 20000;
            reminder.Tick += delegate
            {
                SetState("Всё ещё жду ответа на запрос прав администратора. Он мог оказаться за другими окнами: нажмите Alt+Tab или посмотрите на панель задач.", false);
                reminder.Stop();
            };
            reminder.Start();

            Thread launch = new Thread(delegate ()
            {
                string failure = null;
                try { Updater.LaunchSetup(path); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    failure = ex.NativeErrorCode == 1223 ? "Запрос прав администратора отклонён. Нажмите «Скачать и установить» ещё раз и введите пароль администратора." : "Не удалось запустить установщик: " + ex.Message;
                }
                catch (Exception ex) { failure = "Не удалось запустить установщик: " + ex.Message; }
                if (failure != null) Log.Write("update", failure);
                UiPost(delegate
                {
                    reminder.Stop();
                    reminder.Dispose();
                    bar.Style = ProgressBarStyle.Continuous;
                    if (failure != null)
                    {
                        SetState(failure, true);
                        bar.Visible = false;
                        check.Enabled = true;
                        install.Enabled = true;
                        return;
                    }
                    if (exitAfterLaunch) { Application.Exit(); return; }
                    SetState("Установщик запущен: его окно покажет ход установки (обычно до минуты). Когда он напишет «Обновление установлено», перезагрузите компьютер. "
                        + "Охрана приостановлена на 20 минут; чтобы вернуть её раньше, введите lock в терминале.", false);
                    bar.Visible = false;
                });
            });
            launch.IsBackground = true;
            launch.Start();
        }

        private void SetState(string text, bool error)
        {
            state.Text = text;
            state.ForeColor = error ? Color.FromArgb(248, 113, 113) : Theme.Muted;
        }

        private void UiPost(Action a)
        {
            try { BeginInvoke(new Action(a)); } catch { }
        }

        public void DoCheck()
        {
            check.Enabled = false;
            install.Enabled = false;
            SetState("Проверяю…", false);
            notes.Text = "";
            Thread t = new Thread(delegate ()
            {
                try
                {
                    UpdateInfo info = Updater.Check();
                    UiPost(delegate
                    {
                        found = info;
                        if (Updater.IsNewer(info))
                        {
                            SetState("Доступна версия " + info.Tag + " (" + FileIndex.Size(info.Size) + ").", false);
                            install.Enabled = true;
                            install.BackColor = Theme.Accent;
                            install.ForeColor = Theme.Dark ? Color.FromArgb(7, 12, 24) : Color.White;
                        }
                        else SetState("У вас последняя версия (" + info.Tag + ").", false);
                        notes.Text = (info.Notes ?? "").Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                        check.Enabled = true;
                    });
                }
                catch (Exception ex)
                {
                    UiPost(delegate
                    {
                        SetState("Не удалось проверить: " + ex.Message, true);
                        check.Enabled = true;
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void DoInstall()
        {
            if (found == null) return;
            install.Enabled = false;
            check.Enabled = false;
            bar.Visible = true;
            bar.Value = 0;
            if (!exitAfterLaunch)
            {
                // Inside the student shell the guard deletes any .exe that appears in %TEMP% and kills programs started from there. Only an
                // administrator who entered the code can reach this page, so the guard is paused for the time of the update (as "unlock" does).
                State.EnableAdmin(20);
                Log.Write("update", "Админ-режим на 20 минут на время обновления");
            }
            SetState("Скачиваю " + found.SetupName + "…", false);
            UpdateInfo info = found;
            Thread t = new Thread(delegate ()
            {
                try
                {
                    string path = Updater.DownloadAndVerify(info, delegate (long done, long total)
                    {
                        if (total > 0) UiPost(delegate { bar.Value = (int)Math.Min(100, done * 100 / total); });
                    });
                    if (!File.Exists(path))
                        throw new FileNotFoundException("Скачанный установщик пропал: его удалила охрана лаунчера или антивирус. Введите в терминале unlock 30 и повторите обновление.");
                    UiPost(delegate
                    {
                        // Windows is about to ask for administrator rights. Do not stay on top of its window, and say what to do.
                        Form host = FindForm();
                        if (host != null) host.TopMost = false;
                        bar.Style = ProgressBarStyle.Marquee;
                        SetState("Подпись проверена. Сейчас Windows запросит права администратора: введите логин и пароль в появившемся окне. Если окна не видно, нажмите Alt+Tab.", false);
                        if (beforeInstall != null) beforeInstall();
                        StartSetup(path);
                    });
                }
                catch (Exception ex)
                {
                    UiPost(delegate
                    {
                        SetState("Ошибка: " + ex.Message, true);
                        bar.Visible = false;
                        check.Enabled = true;
                        install.Enabled = true;
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    /// <summary>Small window with the update page, opened by the "update" terminal command.</summary>
    internal sealed class UpdateForm : Form
    {
        public UpdateForm()
        {
            Text = "SchoolShield — обновление";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 480);
            BackColor = Theme.Background;
            UpdatePanel p = new UpdatePanel(null, false);
            Controls.Add(p);
            Shown += delegate { p.DoCheck(); };
        }
    }
}
