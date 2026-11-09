using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolShield
{
    internal struct NavDecision
    {
        public bool Allowed;
        public string Reason;
        public string Host;
        public ViolationKind Kind;

        public static NavDecision Allow(string host) { NavDecision d = new NavDecision(); d.Allowed = true; d.Host = host; return d; }
        public static NavDecision Deny(string reason, string host) { return Deny(ViolationKind.Site, reason, host); }
        public static NavDecision Deny(ViolationKind kind, string reason, string host) { NavDecision d = new NavDecision(); d.Allowed = false; d.Reason = reason; d.Host = host; d.Kind = kind; return d; }
    }

    /// <summary>
    /// What the built-in browser may open: the adult-site lists (tens of thousands of domains, no 1000-entry limit
    /// as in Chrome/Edge policies), the whitelist mode, forbidden words in addresses, safe-search rewriting and the
    /// ad-network list. Pure logic: no browser code here, so it is unit-tested on its own.
    /// </summary>
    internal sealed class WebPolicy
    {
        private readonly HashSet<string> blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> wildLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> blockedPrefixes = new List<string>();     // "host/path"
        private readonly HashSet<string> adHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> videoSites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Regex> badWords;
        private readonly HostList games;
        private readonly List<Regex> gameWords;
        private readonly bool whitelist;
        private readonly bool blockIp;
        private readonly bool blockAds;
        private readonly bool teacher;
        private readonly string searchTemplate;

        public int BlockedDomains { get { return blocked.Count + wildLabels.Count; } }
        public int AdHostCount { get { return adHosts.Count; } }
        public IEnumerable<string> AdHostList { get { return adHosts; } }

        public WebPolicy(Config cfg, string shippedDir, string dataDir)
        {
            teacher = cfg.IsTeacher();
            whitelist = !teacher && string.Equals(cfg.Install.WebMode, "Whitelist", StringComparison.OrdinalIgnoreCase);
            blockIp = cfg.Browser.BlockIpAddresses;
            blockAds = cfg.Browser.BlockAdNetworks;
            searchTemplate = cfg.Browser.SearchUrl;
            foreach (string v in cfg.Browser.VideoSites) videoSites.Add(v.Trim().ToLowerInvariant());

            // The lists that ship with the program (refreshed by every update) plus the administrator's own additions.
            foreach (string dir in new string[] { shippedDir, dataDir })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                ReadBlocklist(Path.Combine(dir, "blocklist.txt"));
                ReadBlocklist(Path.Combine(dir, "blocklist-extended.txt"));
                foreach (string h in ReadLines(Path.Combine(dir, "adblock-domains.txt"), false)) adHosts.Add(h);
                if (whitelist) foreach (string h in ReadLines(Path.Combine(dir, "whitelist.txt"), false)) allowed.Add(h);
            }
            // Game sites (shipped list + the administrator's own games.txt) and the words of game pages; both are inactive while State.GamesAllowed.
            games = HostList.FromFile(string.IsNullOrEmpty(shippedDir) ? null : Config.GamesListPath(shippedDir),
                string.IsNullOrEmpty(dataDir) ? null : Config.GamesListPath(dataDir));
            gameWords = string.IsNullOrEmpty(shippedDir) ? new List<Regex>() : Config.LoadBadWords(Path.Combine(shippedDir, "gamewords.txt"));
            badWords = new List<Regex>();
            if (!string.IsNullOrEmpty(shippedDir)) badWords.AddRange(Config.LoadBadWords(Path.Combine(shippedDir, Config.BadWordsFile(cfg))));
            if (!string.IsNullOrEmpty(dataDir)) badWords.AddRange(Config.LoadBadWords(Path.Combine(dataDir, Config.BadWordsExtraFile(cfg))));
        }

        public static WebPolicy Load(Config cfg)
        {
            return new WebPolicy(cfg, Path.Combine(AppInfo.InstallDir, "config"), Config.DataDir);
        }

        // ---------------------------------------------------------------- loading

        /// <summary>Everything after this line of blocklist.txt (anonymizers, extension stores) is for the student edition only.</summary>
        public const string StudentOnlyMarker = "# @student-only";

        private static IEnumerable<string> ReadLines(string path, bool stopAtStudentOnly)
        {
            List<string> res = new List<string>();
            try
            {
                if (!File.Exists(path)) return res;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string t = raw.Trim().ToLowerInvariant();
                    if (stopAtStudentOnly && t.StartsWith(StudentOnlyMarker, StringComparison.Ordinal)) break;
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int i = t.IndexOf("://", StringComparison.Ordinal);
                    if (i >= 0) t = t.Substring(i + 3);
                    t = t.TrimEnd('/');
                    if (t.Length > 0) res.Add(t);
                }
            }
            catch { }
            return res;
        }

        private void ReadBlocklist(string path)
        {
            foreach (string t in ReadLines(path, teacher))
            {
                if (t.EndsWith(".*"))
                {
                    string label = t.Substring(0, t.Length - 2);
                    if (label.Length >= 4 && label.IndexOf('.') < 0) wildLabels.Add(label);
                }
                else if (t.IndexOf('/') >= 0) blockedPrefixes.Add(t);
                else blocked.Add(t);
            }
        }

        // ---------------------------------------------------------------- host matching

        public static string HostOf(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "";
        }

        private static bool Matches(HashSet<string> set, string host)
        {
            if (host.Length == 0) return false;
            string h = host;
            while (true)
            {
                if (set.Contains(h)) return true;
                int dot = h.IndexOf('.');
                if (dot < 0) return false;
                string rest = h.Substring(dot + 1);
                if (rest.IndexOf('.') < 0) return false;   // never test a bare TLD such as "com"
                h = rest;
            }
        }

        public bool IsBlockedHost(string host)
        {
            if (Matches(blocked, host)) return true;
            string[] labels = host.Split('.');
            int n = labels.Length;
            // "name.*": the registrable label sits second-to-last (example.com) or third-to-last (example.co.uk).
            if (n >= 2 && wildLabels.Contains(labels[n - 2])) return true;
            if (n >= 3 && wildLabels.Contains(labels[n - 3])) return true;
            return false;
        }

        public bool IsBlockedUrl(Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            if (IsBlockedHost(host)) return true;
            if (blockedPrefixes.Count > 0)
            {
                string hp = host + u.AbsolutePath.ToLowerInvariant();
                foreach (string p in blockedPrefixes)
                    if (hp.StartsWith(p, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public bool IsAdHost(string host) { return Matches(adHosts, host); }
        public bool IsVideoSite(string host) { return Matches(videoSites, host); }

        /// <summary>Request to an ad network that must not be sent. On video sites the network block is off: ads inside the player stay.</summary>
        public bool IsAdRequest(string requestHost, string pageHost)
        {
            if (!blockAds || requestHost.Length == 0) return false;
            if (IsVideoSite(pageHost)) return false;
            return IsAdHost(requestHost);
        }

        public bool IsYouTubeHost(string host)
        {
            return host == "www.youtube.com" || host == "m.youtube.com" || host == "youtube.com" || host == "youtubei.googleapis.com"
                || host == "youtube.googleapis.com" || host == "www.youtube-nocookie.com";
        }

        public bool IsBadText(string text)
        {
            return TextGuard.Matches(badWords, text);
        }

        public int GameEntries { get { return games.Count; } }

        /// <summary>A game site, while games are forbidden and the site is not one of the administrator's exceptions.</summary>
        public bool IsGameUrl(Uri u)
        {
            if (State.GamesAllowed) return false;
            if (!games.MatchesUrl(u)) return false;
            return !State.GameExceptions.MatchesUrl(u);
        }

        /// <summary>Words of a game page ("Яндекс Игры", "Play free online games"...), while games are forbidden.</summary>
        public bool IsGameText(string text)
        {
            return !State.GamesAllowed && TextGuard.Matches(gameWords, text);
        }

        // ---------------------------------------------------------------- navigation

        public NavDecision Check(string url, bool mainFrame, bool internalPage)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return NavDecision.Deny("Недопустимый адрес.", "");
            string scheme = u.Scheme.ToLowerInvariant();

            if (scheme == "about") return u.AbsoluteUri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase) || u.AbsoluteUri.StartsWith("about:srcdoc", StringComparison.OrdinalIgnoreCase)
                ? NavDecision.Allow("") : NavDecision.Deny("Недопустимый адрес.", "");
            if (scheme == "data") return (internalPage || !mainFrame) ? NavDecision.Allow("") : NavDecision.Deny("Недопустимый адрес.", "");
            if (scheme == "blob") return mainFrame ? NavDecision.Deny("Недопустимый адрес.", "") : NavDecision.Allow("");
            if (scheme != "http" && scheme != "https") return NavDecision.Deny("Этот тип адресов запрещён.", "");

            string host = u.Host.ToLowerInvariant();
            if (host.Length == 0) return NavDecision.Deny("Недопустимый адрес.", "");

            IPAddress ip;
            if (blockIp && IPAddress.TryParse(host.Trim('[', ']'), out ip) && !IPAddress.IsLoopback(ip))
                return NavDecision.Deny("Открывать сайты по IP-адресу нельзя.", host);

            if (IsBlockedUrl(u)) return NavDecision.Deny(ViolationKind.Adult, "Сайт заблокирован: взрослый контент или обход защиты.", host);
            if (IsGameUrl(u)) return NavDecision.Deny(ViolationKind.Game, "Игры сейчас запрещены администратором.", host);

            if (mainFrame)
            {
                if (whitelist && !Matches(allowed, host) && !IsAllowedHelper(host))
                    return NavDecision.Deny("Этот сайт не входит в список разрешённых школой.", host);
                // Each path segment and each query value is checked on its own: "q=p+o+r+n+o" must not glue the parameter name
                // "q" to the word.
                foreach (string piece in UrlPieces(u))
                {
                    if (IsBadText(piece)) return NavDecision.Deny(ViolationKind.Adult, "Запрещённый запрос или адрес.", host);
                    if (IsGameText(piece)) return NavDecision.Deny(ViolationKind.Game, "Игры сейчас запрещены администратором.", host);
                }
            }
            return NavDecision.Allow(host);
        }

        // In whitelist mode the sign-in and captcha hosts of allowed sites would otherwise break; keep the list tiny.
        private static bool IsAllowedHelper(string host)
        {
            return false;
        }

        private static IEnumerable<string> UrlPieces(Uri u)
        {
            List<string> pieces = new List<string>();
            foreach (string seg in u.AbsolutePath.Split('/'))
                if (seg.Length > 0) pieces.Add(SafeUnescape(seg));
            foreach (string pair in u.Query.TrimStart('?').Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                pieces.Add(SafeUnescape(eq < 0 ? pair : pair.Substring(eq + 1)));
            }
            return pieces;
        }

        private static string SafeUnescape(string s)
        {
            try { return Uri.UnescapeDataString(s.Replace('+', ' ')); } catch { return s; }
        }

        // ---------------------------------------------------------------- safe search

        /// <summary>Forces strict SafeSearch on the big search engines. Returns the same URL when nothing has to change.</summary>
        public Uri ApplySafeSearch(Uri u)
        {
            string host = u.Host.ToLowerInvariant();
            string path = u.AbsolutePath;
            string pName = null, pValue = null;

            if (host.StartsWith("www.")) host = host.Substring(4);
            if ((host.StartsWith("google.") || host == "google.com") && path.StartsWith("/search")) { pName = "safe"; pValue = "active"; }
            else if (host == "bing.com" && path.StartsWith("/search")) { pName = "adlt"; pValue = "strict"; }
            else if ((host == "yandex.ru" || host == "yandex.com" || host == "yandex.by" || host == "yandex.kz" || host == "ya.ru") && (path.StartsWith("/search") || path == "/")
                     && u.Query.Length > 1) { pName = "fyandex"; pValue = "1"; }
            else if (host == "duckduckgo.com" && u.Query.Length > 1) { pName = "kp"; pValue = "1"; }
            if (pName == null) return u;

            string q = u.Query.TrimStart('?');
            List<string> parts = new List<string>();
            bool found = false;
            foreach (string part in q.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string name = eq < 0 ? part : part.Substring(0, eq);
                if (string.Equals(name, pName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    string val = eq < 0 ? "" : part.Substring(eq + 1);
                    if (val == pValue) { parts.Add(part); continue; }
                    parts.Add(pName + "=" + pValue);
                    continue;
                }
                parts.Add(part);
            }
            if (!found) parts.Add(pName + "=" + pValue);
            UriBuilder b = new UriBuilder(u);
            b.Query = string.Join("&", parts.ToArray());
            Uri result = b.Uri;
            return string.Equals(result.AbsoluteUri, u.AbsoluteUri, StringComparison.Ordinal) ? u : result;
        }

        // ---------------------------------------------------------------- address bar

        /// <summary>What the student typed -> a URL to open (an address or a search query).</summary>
        public string ResolveInput(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return "about:blank";
            Uri u;
            if (Regex.IsMatch(t, @"^[a-z][a-z0-9+.\-]*://", RegexOptions.IgnoreCase) && Uri.TryCreate(t, UriKind.Absolute, out u)) return u.AbsoluteUri;
            if (!t.Contains(" ") && t.Contains(".") && !t.EndsWith(".") && Uri.TryCreate("https://" + t, UriKind.Absolute, out u)) return u.AbsoluteUri;
            return string.Format(searchTemplate, Uri.EscapeDataString(t));
        }

        // ---------------------------------------------------------------- pages

        private static string Css(bool dark)
        {
            string bg = dark ? "#151130" : "#f9f7ff", card = dark ? "#231d4e" : "#ffffff", hover = dark ? "#342b70" : "#f3e8ff",
                accent = dark ? "#c084fc" : "#7e22ce", text = dark ? "#f3f4f6" : "#1f2937", muted = dark ? "#b0aad6" : "#586073",
                line = dark ? "#423980" : "#ded7f0";
            string on = "#fff";
            return "html,body{margin:0;height:100%;background:" + bg + ";color:" + text + ";font:16px 'Segoe UI',system-ui,sans-serif}" +
                "body{background:radial-gradient(900px 480px at 15% 0%," + accent + "1f,transparent 70%)," + bg + ";animation:fade .45s ease both}" +
                "@keyframes fade{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}" +
                "@keyframes up{from{opacity:0;transform:translateY(16px)}to{opacity:1;transform:none}}" +
                ".w{max-width:980px;margin:0 auto;padding:40px 28px}h1{font-size:36px;margin:0 0 6px;letter-spacing:-.4px}p.m{color:" + muted + ";margin:4px 0 22px}" +
                "form{display:flex;gap:10px;margin:0 0 28px}input[type=text]{flex:1;font:18px 'Segoe UI';padding:13px 18px;border:1px solid " + line + ";border-radius:99px;background:" + card +
                ";color:" + text + ";outline:0;transition:border-color .2s,box-shadow .2s}input[type=text]:focus{border-color:" + accent + ";box-shadow:0 0 0 4px " + accent + "33}" +
                "button{font:700 16px 'Segoe UI';padding:0 26px;border:0;border-radius:99px;background:linear-gradient(90deg," + (dark ? "#9333ea" : "#7e22ce") + ",#4f46e5);color:" + on +
                ";cursor:pointer;transition:transform .18s,box-shadow .18s}button:hover{transform:translateY(-2px);box-shadow:0 10px 26px " + accent + "55}button:active{transform:scale(.96)}" +
                ".g{display:flex;flex-wrap:wrap;gap:14px}a.c{display:block;width:200px;padding:20px 18px;background:" + card +
                ";color:" + text + ";text-decoration:none;border-radius:18px;font-size:16px;line-height:1.3;box-shadow:inset 0 0 0 1px " + line + ";animation:up .5s both;transition:transform .2s,box-shadow .2s,background .2s}" +
                "a.c:nth-child(2){animation-delay:.05s}a.c:nth-child(3){animation-delay:.1s}a.c:nth-child(4){animation-delay:.15s}a.c:nth-child(5){animation-delay:.2s}a.c:nth-child(n+6){animation-delay:.25s}" +
                "a.c:hover{background:" + hover + ";transform:translateY(-5px);box-shadow:inset 0 0 0 2px " + accent + ",0 14px 30px #00000040}a.c:active{transform:scale(.97)}" +
                ".b{display:inline-block;margin-top:14px;padding:11px 24px;border-radius:99px;background:" + (dark ? "#9333ea" : "#7e22ce") + ";color:" + on + ";text-decoration:none;font-weight:700}.i{font-size:46px;color:" + accent + "}" +
                "@media (prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}";
        }

        private static string H(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public string StartPage(Config cfg, bool dark)
        {
            string tpl = cfg.Browser.SearchUrl;
            string action = tpl, param = "text";
            StringBuilder hidden = new StringBuilder();
            int q = tpl.IndexOf('?');
            if (q > 0)
            {
                action = tpl.Substring(0, q);
                foreach (string pair in tpl.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq < 0) continue;
                    string name = pair.Substring(0, eq), val = pair.Substring(eq + 1);
                    if (val.Contains("{0}")) param = name;
                    else hidden.Append("<input type=\"hidden\" name=\"").Append(H(name)).Append("\" value=\"").Append(H(val)).Append("\">");
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Интернет</title><style>").Append(Css(dark)).Append("</style></head><body><div class=\"w\">");
            sb.Append("<h1>Интернет</h1><p class=\"m\">Безопасный поиск включён. Реклама на сайтах размыта.</p>");
            sb.Append("<form action=\"").Append(H(action)).Append("\" method=\"get\"><input type=\"text\" name=\"").Append(H(param)).Append("\" placeholder=\"Что вы хотите найти?\" autofocus autocomplete=\"off\">")
              .Append(hidden).Append("<button type=\"submit\">Найти</button></form>");
            if (cfg.Links.Count > 0)
            {
                sb.Append("<h2>Полезные сайты</h2><div class=\"g\">");
                foreach (LinkItem l in cfg.Links)
                    if (l != null && !string.IsNullOrEmpty(l.Url))
                        sb.Append("<a class=\"c\" href=\"").Append(H(l.Url)).Append("\">").Append(H(l.Name)).Append("</a>");
                sb.Append("</div>");
            }
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        /// <summary>The page shown instead of a blocked site (a joke, a picture, motion). <paramref name="attempt"/> counts the session's violations.</summary>
        public string BlockedPage(ViolationKind kind, string reason, string host, bool dark, int attempt)
        {
            Joke joke = Sarcasm.Pick(kind, attempt, Ui.Sarcastic);
            return BlockPage.Html(kind, joke, reason, host ?? "", dark, Motion.Enabled, attempt);
        }
    }
}
