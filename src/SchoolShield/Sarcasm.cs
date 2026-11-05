using System;
using System.Collections.Generic;

namespace SchoolShield
{
    /// <summary>What the student tried to do. Decides the colour, the picture and the joke of the message.</summary>
    internal enum ViolationKind
    {
        Generic,
        Site,        // a blocked or not allowed site
        Download,    // downloading or saving an installer
        Game,        // games while they are forbidden
        Adult,       // 18+ content
        Program,     // a program that must not run
        File,        // a file of a forbidden type
        Folder,      // a folder outside the allowed ones
        Wifi         // a Wi-Fi network the school did not allow
    }

    /// <summary>One message for the student: a short headline, one more line, and an optional remark for repeated attempts.</summary>
    internal sealed class Joke
    {
        public string Title;
        public string Line;
        public string Remark;      // null on the first attempts
        public bool Sarcastic;
    }

    /// <summary>
    /// The texts shown when a student does something forbidden. Ironic, never insulting: the joke is about the attempt, not about the child.
    /// The administrator can switch the irony off (then the plain sentence is shown). No window code here, so it can be tested on its own.
    /// </summary>
    internal static class Sarcasm
    {
        private static readonly Dictionary<ViolationKind, string[][]> Bank = new Dictionary<ViolationKind, string[][]>
        {
            { ViolationKind.Site, new string[][] {
                new string[] { "Хорошая попытка.", "Этого сайта нет в школьном меню." },
                new string[] { "Страница не пришла.", "Кажется, она поняла, что ей здесь не рады." },
                new string[] { "Ага, сейчас.", "Администратор заранее предусмотрел этот маршрут и закрыл его." },
                new string[] { "Адрес не найден… специально.", "Мы потрудились, чтобы вы туда не дошли." },
                new string[] { "Туда мы не ходим.", "Там нет ничего для урока. Мы проверяли." },
                new string[] { "Дальше путь закрыт.", "Любознательность — это прекрасно. Только в другом направлении." },
                new string[] { "Ой, а тут стена.", "Прозрачная, но очень крепкая." },
                new string[] { "Нет доступа.", "Зато есть много интересных учебных сайтов. Серьёзно." }
            } },
            { ViolationKind.Download, new string[][] {
                new string[] { "Файл? Какой файл?", "Он уже удалён и даже не успел устроиться." },
                new string[] { "Скачивание отменено.", "Интернет — не склад для установщиков." },
                new string[] { "Хорошая попытка.", "Но устанавливать программы на этом компьютере может только администратор." },
                new string[] { "Загрузка прервана.", "Файл решил, что ему лучше остаться в интернете." },
                new string[] { "Мы это не качаем.", "Мы вообще ничего не качаем. Принцип такой." },
                new string[] { "Нет, спасибо.", "Компьютер сыт: новые программы ему не нужны." },
                new string[] { "Улов конфискован.", "Рыбалка в школьной сети не предусмотрена." },
                new string[] { "Файл самоликвидировался.", "Он осознал, что пришёл не вовремя." }
            } },
            { ViolationKind.Game, new string[][] {
                new string[] { "Игры — после звонка.", "Перемена не за горами. Потерпите." },
                new string[] { "Игра окончена.", "Не успев начаться. Рекорд." },
                new string[] { "Уровень не пройден.", "Босс называется «Урок». Вернитесь к нему." },
                new string[] { "Нажали не туда.", "Мини-игра свернулась сама. Бывает." },
                new string[] { "Респаун отменён.", "Этот компьютер сейчас работает в режиме «учёба»." },
                new string[] { "Game over.", "Возможно, это лучший результат за сегодня. А теперь к заданию." },
                new string[] { "Пауза.", "Ну, вы поняли: бессрочная." },
                new string[] { "Вы не пройдёте.", "Это не баг, это школьная политика." }
            } },
            { ViolationKind.Adult, new string[][] {
                new string[] { "Так, это явно не по программе.", "Окно закрыто. Давайте сделаем вид, что ничего не было." },
                new string[] { "Нет. Просто нет.", "Это не учебное пособие." },
                new string[] { "Окно закрыто.", "Мы тактично не будем задавать вопросов." },
                new string[] { "Не-не-не.", "Такое показывают только взрослым и в другом месте." },
                new string[] { "Фильтр сработал.", "Он умный, быстрый и совсем не любопытный." },
                new string[] { "Эта страница вне зоны доступа.", "Для вашего спокойствия. И нашего." }
            } },
            { ViolationKind.Program, new string[][] {
                new string[] { "Программа не прошла собеседование.", "Здесь работают только проверенные кадры." },
                new string[] { "Запуск отменён.", "Этой программы нет в списке. А список — святое." },
                new string[] { "Не сегодня.", "Загляните в раздел «Программы»: там тоже есть чем заняться." },
                new string[] { "Процесс завершён.", "Принудительно, но с уважением." },
                new string[] { "Не вписывается в интерьер.", "Школьный компьютер очень разборчив в знакомствах." }
            } },
            { ViolationKind.File, new string[][] {
                new string[] { "Такой файл здесь не живёт.", "У нас принимают учебное, а не всякое." },
                new string[] { "Файл не прошёл фейс-контроль.", "Слишком подозрительный." },
                new string[] { "Нельзя.", "Документы, таблицы, презентации — пожалуйста. Остальное — к администратору." },
                new string[] { "Отказ.", "Тип файла не из списка. Расширение выдало себя." },
                new string[] { "Ой, а это не учебное.", "Хорошая маскировка, но не сегодня." }
            } },
            { ViolationKind.Folder, new string[][] {
                new string[] { "Дальше нельзя.", "Дальше служебный вход, только для администраторов." },
                new string[] { "Эта папка закрыта.", "Там живут системные файлы, и они любят тишину." },
                new string[] { "Вы вышли за флажки.", "Возвращайтесь в «Мои документы»." },
                new string[] { "Проход закрыт.", "Зато «Мои файлы» всегда рады гостям." }
            } },
            { ViolationKind.Wifi, new string[][] {
                new string[] { "Эта сеть не из нашего района.", "Подключайтесь к школьной: она уже ждёт." },
                new string[] { "Чужой Wi-Fi.", "Интернет бывает бесплатным, но не всегда безопасным." },
                new string[] { "Сеть отклонена.", "Список доверенных сетей ведёт администратор, а не соседи." }
            } },
            { ViolationKind.Generic, new string[][] {
                new string[] { "Здесь так нельзя.", "Но есть множество мест, где можно." },
                new string[] { "Действие отменено.", "Это не наказание, это настройки." },
                new string[] { "Не пройдёт.", "Хорошая идея, но схема не сработала." }
            } }
        };

        private static readonly Dictionary<ViolationKind, string[]> Plain = new Dictionary<ViolationKind, string[]>
        {
            { ViolationKind.Site, new string[] { "Сайт недоступен.", "Он закрыт администратором." } },
            { ViolationKind.Download, new string[] { "Скачивание запрещено.", "Файл удалён." } },
            { ViolationKind.Game, new string[] { "Игры сейчас запрещены.", "Окно закрыто." } },
            { ViolationKind.Adult, new string[] { "Запрещённый контент.", "Окно закрыто." } },
            { ViolationKind.Program, new string[] { "Программа запрещена.", "Запуск отменён." } },
            { ViolationKind.File, new string[] { "Файлы этого типа запрещены.", "Действие отменено." } },
            { ViolationKind.Folder, new string[] { "Эта папка недоступна.", "Выберите другую." } },
            { ViolationKind.Wifi, new string[] { "Эта сеть не разрешена школой.", "Выберите другую сеть." } },
            { ViolationKind.Generic, new string[] { "Действие запрещено.", "Обратитесь к учителю." } }
        };

        private static readonly Dictionary<ViolationKind, int> last = new Dictionary<ViolationKind, int>();
        private static readonly Random Rng = new Random();

        public static int Count(ViolationKind kind)
        {
            string[][] list;
            return Bank.TryGetValue(kind, out list) ? list.Length : 0;
        }

        /// <summary>Joke number <paramref name="index"/> of a kind (the tests walk over all of them).</summary>
        public static Joke At(ViolationKind kind, int index, int attempt)
        {
            string[][] list = Bank[kind];
            string[] j = list[((index % list.Length) + list.Length) % list.Length];
            return new Joke { Title = j[0], Line = j[1], Remark = Remark(attempt), Sarcastic = true };
        }

        /// <summary>A random joke that is not the one shown last time for this kind.</summary>
        public static Joke Pick(ViolationKind kind, int attempt, bool sarcastic)
        {
            if (!sarcastic) return PlainFor(kind);
            int n = Count(kind);
            int prev;
            if (!last.TryGetValue(kind, out prev)) prev = -1;
            int i = Rng.Next(n);
            if (n > 1 && i == prev) i = (i + 1 + Rng.Next(n - 1)) % n;
            last[kind] = i;
            return At(kind, i, attempt);
        }

        public static Joke PlainFor(ViolationKind kind)
        {
            string[] p = Plain[kind];
            return new Joke { Title = p[0], Line = p[1], Remark = null, Sarcastic = false };
        }

        /// <summary>A remark for repeated attempts in one session, null for the first one or two.</summary>
        public static string Remark(int attempt)
        {
            if (attempt < 2) return null;
            if (attempt == 2) return "Это уже второй раз. Запоминаем.";
            if (attempt == 3) return "Третья попытка. Настойчивость похвальна, направление — нет.";
            if (attempt == 4) return "Четыре. Мы начинаем считать это хобби.";
            if (attempt == 5) return "Пять попыток. Может, займёмся уроком?";
            if (attempt < 8) return "Попытка №" + attempt + ". Защита пока держится, и вы тоже держитесь.";
            if (attempt < 12) return "Попытка №" + attempt + ". Мы не сердимся, мы просто считаем.";
            if (attempt < 20) return "Попытка №" + attempt + ". У вас талант проверять защиту. После школы вас ждёт вакансия тестировщика.";
            return "Попытка №" + attempt + ". Рекорд записан в историю.";
        }

        /// <summary>The kind of a violation by the words of an old-style message (a few places still only know the text).</summary>
        public static ViolationKind GuessKind(string message)
        {
            string m = (message ?? "").ToLowerInvariant();
            if (m.Contains("игр")) return ViolationKind.Game;
            if (m.Contains("скачив") || m.Contains("установщик") || m.Contains("файл удалён")) return ViolationKind.Download;
            if (m.Contains("18+") || m.Contains("запрещённый контент") || m.Contains("неприемлем")) return ViolationKind.Adult;
            if (m.Contains("wi-fi") || m.Contains("сеть")) return ViolationKind.Wifi;
            if (m.Contains("папк")) return ViolationKind.Folder;
            if (m.Contains("типа") || m.Contains("файл")) return ViolationKind.File;
            if (m.Contains("программ")) return ViolationKind.Program;
            if (m.Contains("сайт")) return ViolationKind.Site;
            return ViolationKind.Generic;
        }
    }
}
