#!/usr/bin/env python3
"""
Rebuilds config/blocklist.txt (curated) and config/blocklist-extended.txt (everything else) from open sources:
  - The Block List Project: porn.txt (MIT)        https://github.com/blocklistproject/Lists
  - StevenBlack/hosts: porn extension (MIT)       https://github.com/StevenBlack/hosts
  - Tranco popularity ranking                     https://tranco-list.eu
Only WHOLE-SITE entries are used (platform sub-domains such as xyz.tumblr.com never put tumblr.com on the list), and a site
must be in both porn lists or contain an explicit adult word. Ad/tracker/malware domains that the porn lists also contain
are removed. Run:  python3 -m venv v && v/bin/pip install tldextract && v/bin/python tools/update-blocklists.py
"""
import collections, datetime, io, os, re, sys, urllib.request, zipfile
import tldextract

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ext = tldextract.TLDExtract(suffix_list_urls=())
reg = lambda d: ext(d).top_domain_under_public_suffix
sld = lambda d: ext(d).domain
DOM = re.compile(r'^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$')


def fetch(url):
    with urllib.request.urlopen(url, timeout=300) as r:
        return r.read()


def hosts(url):
    out = set()
    for line in fetch(url).decode("utf-8", "ignore").splitlines():
        line = line.strip().lower()
        if not line or line.startswith("#"):
            continue
        parts = line.split()
        d = parts[-1] if len(parts) > 1 else parts[0]
        if d not in ("localhost", "0.0.0.0", "broadcasthost") and DOM.match(d):
            out.add(d)
    return out


print("downloading…")
blp = hosts("https://blocklistproject.github.io/Lists/porn.txt")
sb = hosts("https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/porn/hosts")
other = hosts("https://blocklistproject.github.io/Lists/ads.txt") | hosts("https://blocklistproject.github.io/Lists/tracking.txt") \
    | hosts("https://blocklistproject.github.io/Lists/malware.txt")
rank = {}
with zipfile.ZipFile(io.BytesIO(fetch("https://tranco-list.eu/top-1m.csv.zip"))) as z:
    for line in z.read(z.namelist()[0]).decode().splitlines():
        r, d = line.split(",", 1)
        rank[d.lower()] = int(r)

KW = re.compile(r'(porn|porno|xxx|sex|seks|hentai|fuck|nude|naked|milf|erotic|nsfw|escort|bdsm|fetish|shemale|tits|boob|pussy|slut|whore|adult|xvideo|xnxx|xhamster|camgirl|livecam|lesbian|incest|upskirt|bukkake|gangbang|blowjob|cumshot|orgasm|stripper|hardcore|amateur)')
DENY = {"kinopoisk.ru", "nicovideo.jp", "dmm.co.jp", "fc2.com", "xiaohongshu.com", "character.ai", "gotinder.com", "grindr.com",
        "match.com", "cosmopolitan.com", "ekstrabladet.dk", "in.net", "eu.cc", "html-load.com", "hath.network", "janitorai.com",
        "comix.to", "mangago.me", "manhwaweb.com", "scan-manga.com", "suruga-ya.jp", "dto.jp", "donmai.us", "e621.net",
        "furaffinity.net", "mmcdn.com", "doppiocdn.com", "bcvcdn.com", "51.la", "freevideo.cz"}

both = {d for d in blp & sb if reg(d) == d}
single = {d for d in (blp | sb) if reg(d) == d} - both
kw = {d for d in single if d in rank and d not in other and KW.search(d.split(".")[0])
      and not re.search(r"(essex|sussex|middlesex|sexton)", d)}
ranked = sorted((rank.get(d, 10 ** 9), d) for d in (both | kw) - DENY)

RU_TLD = {"ru", "su", "xn--p1ai", "by", "kz"}
STRONG = re.compile(r"(porn|porno|sex|seks|xxx|erot|studentk|devk|devush|siski|pizd|trahk|dojki|hentai|fuck|nude|milf|cam)")
RU_MARK = re.compile(r"(studentk|rusk|russ|ruporn|porno365|porno666|pornolab|mult-porno|trahkino|seks-|seks\.|devk|devush|shlyuh|zrel|mamk|babk|golye|golaya|intim|siski|pizd|trah|ero-|erotik|erotika|russkoe|rus-|-rus|rusx|sexru|sex-ru|ru-sex)")
is_ru = lambda d: (d.rsplit(".", 1)[-1] in RU_TLD or RU_MARK.search(d.split(".")[0])) and STRONG.search(d) and "cdn" not in d


def dedupe(items, per, limit):
    cnt, out = collections.Counter(), []
    for d in items:
        if cnt[sld(d)] < per:
            cnt[sld(d)] += 1
            out.append(d)
        if len(out) >= limit:
            break
    return out


ru_all = [d for r, d in ranked if is_ru(d)]
RU = dedupe(ru_all, 3, 260)
world = dedupe([d for r, d in ranked if r < 10 ** 9 and d not in set(ru_all) and "cdn" not in d], 3, 480)
fam = collections.defaultdict(list)
for r, d in ranked:
    fam[sld(d)].append(d)
wild = sorted(l for l, ds in fam.items() if len(ds) >= 4 and len(l) >= 6 and STRONG.search(l))

old = open(os.path.join(ROOT, "config", "blocklist.txt"), encoding="utf-8").read()
marker = "# --- анонимайзеры, веб-прокси, VPN-сайты ---"
tail = old[old.index(marker):] if marker in old else ""
today = datetime.date.today().isoformat()
head = ("# Сайты, которые блокируются всегда (Chrome, Edge, Firefox и встроенный браузер SchoolShield).\n"
        "# Один домен в строке. Строка  имя.*  (только встроенный браузер) закрывает имя на любой доменной зоне.\n"
        "# Chrome/Edge принимают не более ~1000 записей, поэтому в их политики попадают первые строки.\n"
        f"# Источники: The Block List Project (MIT), StevenBlack/hosts (MIT), рейтинг Tranco. Обновлено {today}.\n\n")
with open(os.path.join(ROOT, "config", "blocklist.txt"), "w", encoding="utf-8") as f:
    f.write(head + "# ===== Порно и эротика: мировой сегмент =====\n" + "".join(d + "\n" for d in world)
            + "\n# ===== Порно и эротика: российский сегмент =====\n" + "".join(d + "\n" for d in RU)
            + "\n# ===== Зеркала: семейства доменов на любой зоне =====\n" + "".join(l + ".*\n" for l in wild) + "\n" + tail)
cur = set(world) | set(RU)
with open(os.path.join(ROOT, "config", "blocklist-extended.txt"), "w", encoding="utf-8") as f:
    f.write(f"# Расширенный список порно/эротики для встроенного браузера. Обновлено {today}.\n\n"
            + "".join(d + "\n" for r, d in ranked if d not in cur))
print(f"world={len(world)} ru={len(RU)} wildcards={len(wild)} extended={len(ranked) - len(cur)}")
