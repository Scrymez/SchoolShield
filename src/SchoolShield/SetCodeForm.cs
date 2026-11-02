using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SchoolShield
{
    /// <summary>
    /// "SchoolShield.exe --set-code": small dialog to change the admin code. Must run elevated,
    /// because config.json is read-only for students.
    /// </summary>
    internal sealed class SetCodeForm : Form
    {
        private readonly TextBox code1 = new TextBox();
        private readonly TextBox code2 = new TextBox();

        public SetCodeForm()
        {
            Text = "SchoolShield — смена кода администратора";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(380, 190);
            Font = new Font("Segoe UI", 10);

            Label l1 = new Label();
            l1.Text = "Новый код (не короче 8 символов):";
            l1.SetBounds(16, 14, 350, 22);
            code1.UseSystemPasswordChar = true;
            code1.SetBounds(16, 38, 348, 26);

            Label l2 = new Label();
            l2.Text = "Повторите код:";
            l2.SetBounds(16, 76, 350, 22);
            code2.UseSystemPasswordChar = true;
            code2.SetBounds(16, 100, 348, 26);

            Button ok = new Button();
            ok.Text = "Сохранить";
            ok.SetBounds(16, 142, 160, 32);
            ok.Click += delegate { Save(); };

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.SetBounds(204, 142, 160, 32);
            cancel.Click += delegate { Close(); };

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { l1, code1, l2, code2, ok, cancel });
        }

        private void Save()
        {
            if (code1.Text.Length < 8)
            {
                MessageBox.Show("Код слишком короткий: нужно минимум 8 символов.", "SchoolShield");
                return;
            }
            if (code1.Text != code2.Text)
            {
                MessageBox.Show("Коды не совпадают.", "SchoolShield");
                return;
            }
            try
            {
                Config cfg = Config.Load();
                string salt = Crypto.NewSalt();
                string hash = Crypto.Hash(code1.Text, salt, cfg.Iterations);
                Patch(salt, hash);
                Log.Write("admin", "Код администратора изменён");
                MessageBox.Show("Код изменён. Он действует сразу после перезагрузки лаунчера (или ПК).", "SchoolShield");
                Close();
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Нет прав на запись config.json. Запустите от имени администратора Windows.", "SchoolShield");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "SchoolShield");
            }
        }

        private static void Patch(string salt, string hash)
        {
            string path = Config.ConfigPath;
            if (!File.Exists(path)) throw new FileNotFoundException("config.json не найден: " + path);
            string json = File.ReadAllText(path, Encoding.UTF8);
            json = SetField(json, "AdminSalt", salt);
            json = SetField(json, "AdminHash", hash);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static string SetField(string json, string name, string value)
        {
            Regex r = new Regex("\"" + name + "\"\\s*:\\s*\"[^\"]*\"");
            string repl = "\"" + name + "\": \"" + value + "\"";
            if (r.IsMatch(json)) return r.Replace(json, repl.Replace("$", "$$"), 1);
            int brace = json.IndexOf('{');
            if (brace < 0) throw new InvalidDataException("config.json повреждён");
            return json.Substring(0, brace + 1) + "\n  " + repl + "," + json.Substring(brace + 1);
        }
    }
}
