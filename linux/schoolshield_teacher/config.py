"""Настройки: /etc/schoolshield/config.json (меняет администратор) и предпочтения пользователя ~/.config/schoolshield/prefs.json."""

import copy
import io
import json
import os

from . import paths

# Семейные DNS-фильтры: IPv4, IPv6 и имя для DNS-over-TLS.
DNS_PROVIDERS = {
    "AdGuardFamily": {
        "v4": ["94.140.14.15", "94.140.15.16"],
        "v6": ["2a10:50c0::bad1:ff", "2a10:50c0::bad2:ff"],
        "dot": "family.adguard-dns.com",
    },
    "Cloudflare": {
        "v4": ["1.1.1.3", "1.0.0.3"],
        "v6": ["2606:4700:4700::1113", "2606:4700:4700::1003"],
        "dot": "family.cloudflare-dns.com",
    },
    "CleanBrowsing": {
        "v4": ["185.228.168.168", "185.228.169.168"],
        "v6": ["2a0d:2a00:1::", "2a0d:2a00:2::"],
        "dot": "family-filter-dns.cleanbrowsing.org",
    },
    "Yandex": {
        "v4": ["77.88.8.7", "77.88.8.3"],
        "v6": ["2a02:6b8::feed:a11", "2a02:6b8:0:1::feed:a11"],
        "dot": "safe.dot.dns.yandex.net",
    },
}

DEFAULTS = {
    "edition": "teacher",
    "title": "Центр учителя",
    "dns": "AdGuardFamily",          # или "None": DNS не трогаем
    "hosts_block": True,             # крупные порно-сайты ещё и в /etc/hosts
    "browser_policies": True,        # SafeSearch и закрытые сайты в Chrome, Chromium, Edge, Brave, Firefox
    "guard": {"enabled": True, "notify": True},
    "ai": {"enabled": False, "threshold": 0.93, "interval": 3.0, "confirmations": 2},
    "links": [
        {"name": "Российская электронная школа", "url": "https://resh.edu.ru"},
        {"name": "Учи.ру", "url": "https://uchi.ru"},
        {"name": "ЯКласс", "url": "https://www.yaklass.ru"},
        {"name": "Решу ЕГЭ / ОГЭ", "url": "https://sdamgia.ru"},
        {"name": "Википедия", "url": "https://ru.wikipedia.org"},
    ],
    "bells": [
        {"start": "08:30", "end": "09:15", "title": "Урок 1"},
        {"start": "09:25", "end": "10:10", "title": "Урок 2"},
        {"start": "10:20", "end": "11:05", "title": "Урок 3"},
        {"start": "11:20", "end": "12:05", "title": "Урок 4"},
        {"start": "12:20", "end": "13:05", "title": "Урок 5"},
        {"start": "13:15", "end": "14:00", "title": "Урок 6"},
        {"start": "14:10", "end": "14:55", "title": "Урок 7"},
    ],
}


def _merge(base, over):
    """Вложенные словари объединяются, всё остальное из `over` заменяет значение."""
    out = copy.deepcopy(base)
    for k, v in (over or {}).items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = _merge(out[k], v)
        else:
            out[k] = v
    return out


def _read_json(path):
    try:
        with io.open(path, encoding="utf-8") as f:
            data = json.load(f)
        return data if isinstance(data, dict) else {}
    except (IOError, OSError, ValueError):
        return {}


def load():
    cfg = _merge(DEFAULTS, _read_json(paths.config_path()))
    cfg["edition"] = "teacher"
    if cfg.get("dns") not in DNS_PROVIDERS and cfg.get("dns") != "None":
        cfg["dns"] = "AdGuardFamily"
    try:
        th = float(cfg["ai"].get("threshold", 0.93))
    except (TypeError, ValueError):
        th = 0.93
    cfg["ai"]["threshold"] = min(0.99, max(0.5, th))
    return cfg


def save(cfg):
    os.makedirs(paths.etc_dir(), exist_ok=True)
    tmp = paths.config_path() + ".tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=2, sort_keys=True)
        f.write("\n")
    os.replace(tmp, paths.config_path())


# ----------------------------------------------------------------------------- настройки пользователя

PREF_DEFAULTS = {"theme": "light", "size": "large", "favorites": [], "sections": ["home", "files", "docs", "media", "apps", "net", "settings", "help", "protect"]}


def load_prefs():
    return _merge(PREF_DEFAULTS, _read_json(os.path.join(paths.user_config_dir(), "prefs.json")))


def save_prefs(prefs):
    clean = _merge(PREF_DEFAULTS, {
        "theme": "dark" if prefs.get("theme") == "dark" else "light",
        "size": prefs.get("size") if prefs.get("size") in ("normal", "large", "huge") else "large",
        "favorites": [str(x) for x in prefs.get("favorites", [])][:200],
    })
    d = paths.user_config_dir()
    os.makedirs(d, exist_ok=True)
    tmp = os.path.join(d, "prefs.json.tmp")
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(clean, f, ensure_ascii=False, indent=2)
    os.replace(tmp, os.path.join(d, "prefs.json"))
    return clean
