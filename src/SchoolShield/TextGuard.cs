using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolShield
{
    /// <summary>
    /// Makes the forbidden-word check hard to fool. Children write "p o r n o", "p.o.r.n", "p0rn", "пoрнo" (Latin letters in a
    /// Russian word) and similar. Every text is checked in several normalised forms.
    /// </summary>
    internal static class TextGuard
    {
        private static readonly Dictionary<char, char> CyrToLat = new Dictionary<char, char>();
        private static readonly Dictionary<char, char> LatToCyr = new Dictionary<char, char>();
        private static readonly Dictionary<char, char> Leet = new Dictionary<char, char>();
        private static readonly Regex Separators = new Regex(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

        static TextGuard()
        {
            string cyr = "авекмнорстух";
            string lat = "abekmhopctyx";
            for (int i = 0; i < cyr.Length; i++) { CyrToLat[cyr[i]] = lat[i]; LatToCyr[lat[i]] = cyr[i]; }
            CyrToLat['ё'] = 'e';
            LatToCyr['n'] = 'п';
            LatToCyr['i'] = 'і';
            Leet['0'] = 'o'; Leet['1'] = 'i'; Leet['3'] = 'e'; Leet['4'] = 'a'; Leet['5'] = 's'; Leet['7'] = 't';
            Leet['@'] = 'a'; Leet['$'] = 's'; Leet['!'] = 'i';
        }

        private static string Map(string s, Dictionary<char, char> map)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                char r;
                sb.Append(map.TryGetValue(c, out r) ? r : c);
            }
            return sb.ToString();
        }

        /// <summary>"p o r n o" -> "porno": runs of three or more single letters are glued together.</summary>
        public static string Collapse(string s)
        {
            string[] tokens = Separators.Split(s);
            StringBuilder sb = new StringBuilder();
            StringBuilder run = new StringBuilder();
            int runCount = 0;
            Action flush = delegate
            {
                if (runCount >= 3) sb.Append(run).Append(' ');
                else if (runCount > 0) for (int i = 0; i < run.Length; i++) sb.Append(run[i]).Append(' ');
                run.Length = 0;
                runCount = 0;
            };
            foreach (string t in tokens)
            {
                if (t.Length == 0) continue;
                if (t.Length == 1 && char.IsLetter(t[0])) { run.Append(t); runCount++; }
                else { flush(); sb.Append(t).Append(' '); }
            }
            flush();
            return sb.ToString().TrimEnd();
        }

        /// <summary>The text itself plus its normalised forms (lower case, look-alike letters, leet speak, glued single letters).</summary>
        public static List<string> Variants(string text)
        {
            List<string> res = new List<string>();
            if (string.IsNullOrEmpty(text)) return res;
            HashSet<string> seen = new HashSet<string>();
            Action<string> add = delegate (string v) { if (!string.IsNullOrEmpty(v) && seen.Add(v)) res.Add(v); };

            string lower = text.ToLowerInvariant();
            add(text);
            add(Collapse(lower));
            string lat = Map(lower, CyrToLat);      // Cyrillic look-alikes -> Latin
            string leet = Map(lat, Leet);           // 0->o, 1->i, 3->e ...
            string cyr = Map(lower, LatToCyr);      // Latin look-alikes -> Cyrillic (for Russian words)
            add(lat); add(Collapse(lat));
            add(leet); add(Collapse(leet));
            add(cyr); add(Collapse(cyr));
            return res;
        }

        public static bool Matches(IList<Regex> patterns, string text)
        {
            if (patterns.Count == 0 || string.IsNullOrEmpty(text) || text.Length < 3) return false;
            foreach (string v in Variants(text))
                foreach (Regex r in patterns)
                    if (r.IsMatch(v)) return true;
            return false;
        }
    }
}
