using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SchoolShield
{
    /// <summary>
    /// A list of sites: "domain" (with sub-domains), "name.*" (that name on any zone), "domain/path" (a section of a site;
    /// "www." and other sub-domains of the domain are covered). Used for the games list and for its exceptions.
    /// </summary>
    internal sealed class HostList
    {
        private readonly HashSet<string> exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> wild = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> prefixes = new List<string>();

        public int Count { get { return exact.Count + wild.Count + prefixes.Count; } }

        public void Add(string entry)
        {
            string t = (entry ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0 || t.StartsWith("#")) return;
            int i = t.IndexOf("://", StringComparison.Ordinal);
            if (i >= 0) t = t.Substring(i + 3);
            t = t.TrimEnd('/');
            if (t.StartsWith("www.")) t = t.Substring(4);
            if (t.Length == 0) return;
            if (t.EndsWith(".*"))
            {
                string label = t.Substring(0, t.Length - 2);
                if (label.Length >= 4 && label.IndexOf('.') < 0 && label.IndexOf('/') < 0) wild.Add(label);
            }
            else if (t.IndexOf('/') >= 0) { if (!prefixes.Contains(t)) prefixes.Add(t); }
            else exact.Add(t);
        }

        public void AddRange(IEnumerable<string> entries)
        {
            if (entries == null) return;
            foreach (string e in entries) Add(e);
        }

        public static HostList FromFile(params string[] paths)
        {
            HostList l = new HostList();
            foreach (string p in paths)
            {
                try
                {
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8)) l.Add(line);
                }
                catch { }
            }
            return l;
        }

        /// <summary>Is this host (or a parent domain of it) in the list as a plain domain, or does its name match a "name.*" entry?</summary>
        public bool MatchesHost(string host)
        {
            host = (host ?? "").ToLowerInvariant().Trim('.');
            if (host.Length == 0) return false;
            string h = host;
            while (true)
            {
                if (exact.Contains(h)) return true;
                int dot = h.IndexOf('.');
                if (dot < 0) break;
                string rest = h.Substring(dot + 1);
                if (rest.IndexOf('.') < 0) break;      // never test a bare zone such as "com"
                h = rest;
            }
            string[] labels = host.Split('.');
            int n = labels.Length;
            if (n >= 2 && wild.Contains(labels[n - 2])) return true;
            if (n >= 3 && wild.Contains(labels[n - 3])) return true;
            return false;
        }

        /// <summary>Host match, or a "domain/path" entry that the address starts with (checked for the host and each of its parent domains).</summary>
        public bool MatchesUrl(Uri u)
        {
            string host = u.Host.ToLowerInvariant().Trim('.');
            if (MatchesHost(host)) return true;
            if (prefixes.Count == 0) return false;
            string path = u.AbsolutePath.ToLowerInvariant();
            string h = host;
            while (true)
            {
                string hp = h + path;
                foreach (string p in prefixes)
                {
                    if (!hp.StartsWith(p, StringComparison.Ordinal)) continue;
                    // "yandex.ru/games" must match "/games" and "/games/..." but not "/gamesXYZ" (a different page)
                    if (hp.Length == p.Length || p.EndsWith("/") || hp[p.Length] == '/' || hp[p.Length] == '.' || hp[p.Length] == '-') return true;
                }
                int dot = h.IndexOf('.');
                if (dot < 0) break;
                string rest = h.Substring(dot + 1);
                if (rest.IndexOf('.') < 0) break;
                h = rest;
            }
            return false;
        }
    }
}
