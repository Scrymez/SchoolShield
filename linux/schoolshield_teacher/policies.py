"""Тексты настроек, которые записывает администратор: политики браузеров, DNS, /etc/hosts. Чистые функции без побочных эффектов."""

import json

from . import config

HOSTS_BEGIN = "# >>> SchoolShield 18+ (редакция «Учитель») — не редактируйте вручную >>>"
HOSTS_END = "# <<< SchoolShield 18+ <<<"
DISPATCHER_MARK = "# managed by SchoolShield"

# Политики Chromium-браузеров: папка -> нужна ли политика Bing (только Edge).
CHROMIUM_POLICY_DIRS = [
    ("/etc/opt/chrome/policies/managed", False),               # Google Chrome
    ("/etc/chromium/policies/managed", False),                 # Chromium (Debian, Mint)
    ("/etc/chromium-browser/policies/managed", False),         # Chromium (Ubuntu)
    ("/etc/opt/edge/policies/managed", True),                  # Microsoft Edge
    ("/etc/brave/policies/managed", False),                    # Brave
    ("/etc/opt/vivaldi/policies/managed", False),              # Vivaldi
]
POLICY_FILE = "schoolshield-teacher.json"

# Firefox читает политики из своей папки distribution или из /etc/firefox/policies.
FIREFOX_POLICY_DIRS = [
    "/etc/firefox/policies",
    "/usr/lib/firefox/distribution",
    "/usr/lib/firefox-esr/distribution",
    "/opt/firefox/distribution",
]

# Chromium принимает около 1000 записей в URLBlocklist.
POLICY_LIMIT = 900


def chromium_policy(domains, edge=False):
    policy = {
        "ForceGoogleSafeSearch": True,
        "URLBlocklist": list(domains[:POLICY_LIMIT]),
    }
    if edge:
        policy["ForceBingSafeSearch"] = 2
    return policy


def firefox_block_patterns(domains):
    return ["*://*." + d + "/*" for d in domains[:POLICY_LIMIT] if "/" not in d]   # '*.' покрывает и сам домен


def firefox_merge(existing, domains):
    """Добавляет закрытые сайты к политикам, которые уже были в файле (чужие настройки сохраняются)."""
    doc = existing if isinstance(existing, dict) else {}
    pol = doc.get("policies")
    if not isinstance(pol, dict):
        pol = {}
    wf = pol.get("WebsiteFilter")
    if not isinstance(wf, dict):
        wf = {}
    block = [b for b in wf.get("Block", []) if isinstance(b, str)]
    for p in firefox_block_patterns(domains):
        if p not in block:
            block.append(p)
    wf["Block"] = block
    pol["WebsiteFilter"] = wf
    doc["policies"] = pol
    doc["_schoolshield"] = {"edition": "teacher", "note": "блок 18+; убрать: schoolshield-teacher remove"}
    return doc


def is_ours(doc):
    return isinstance(doc, dict) and "_schoolshield" in doc


def dumps(doc):
    return json.dumps(doc, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


# ----------------------------------------------------------------------------- DNS

def resolved_dropin(provider):
    p = config.DNS_PROVIDERS[provider]
    servers = ["%s#%s" % (ip, p["dot"]) for ip in p["v4"] + p["v6"]]
    return (
        "# managed by SchoolShield: семейный DNS-фильтр 18+ (%s)\n"
        "[Resolve]\n"
        "DNS=%s\n"
        "Domains=~.\n"
        "DNSOverTLS=opportunistic\n" % (provider, " ".join(servers))
    )


def dispatcher_script(provider):
    """NetworkManager после подключения к сети выдаёт свои DNS; этот скрипт заменяет их на семейный фильтр."""
    p = config.DNS_PROVIDERS[provider]
    ips = " ".join(p["v4"] + p["v6"])
    return (
        "#!/bin/sh\n"
        + DISPATCHER_MARK + ": семейный DNS-фильтр 18+ (" + provider + ")\n"
        'IFACE="$1"\n'
        'EVENT="$2"\n'
        'case "$EVENT" in up|dhcp4-change|dhcp6-change|reapply) ;; *) exit 0 ;; esac\n'
        # служебные и VPN-интерфейсы не трогаем: корпоративный VPN должен сохранить свой DNS
        'case "$IFACE" in lo|tun*|tap*|wg*|ppp*|tailscale*|docker*|br-*|veth*|virbr*) exit 0 ;; esac\n'
        'command -v resolvectl >/dev/null 2>&1 || exit 0\n'
        'resolvectl dns "$IFACE" ' + ips + ' >/dev/null 2>&1\n'
        "resolvectl domain \"$IFACE\" '~.' >/dev/null 2>&1\n"
        'resolvectl dnsovertls "$IFACE" opportunistic >/dev/null 2>&1\n'
        "exit 0\n"
    )


# ----------------------------------------------------------------------------- /etc/hosts

def hosts_block(domains):
    lines = [HOSTS_BEGIN]
    for d in domains:
        if "/" in d or "*" in d:
            continue
        for name in (d, "www." + d):
            lines.append("0.0.0.0 " + name)
            lines.append(":: " + name)
    lines.append(HOSTS_END)
    return "\n".join(lines) + "\n"


def replace_hosts_block(content, block):
    """Заменяет наш блок в /etc/hosts (или добавляет в конец); block=None — убирает его."""
    out = []
    skipping = False
    for line in content.splitlines():
        if line.startswith("# >>> SchoolShield 18+"):
            skipping = True
            continue
        if skipping:
            if line.startswith("# <<< SchoolShield 18+"):
                skipping = False
            continue
        out.append(line)
    text = "\n".join(out).rstrip("\n")
    if text:
        text += "\n"
    if block:
        text += block
    return text
