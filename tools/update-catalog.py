#!/usr/bin/env python3
"""Собирает config/catalog.json из tools/catalog-seed.json.

Для каждого установщика берёт SHA-256 у издателя (файл с суммами или значение GitHub; нет его: скачивает и считает сам), размер по
заголовку, по первым байтам определяет тип установщика (MSI, NSIS, Inno Setup) и подставляет «тихие» ключи установки. Если тип не определился, а ключи не заданы в заготовке,
скрипт останавливается: так в каталог не попадёт установщик, который молча повиснет с окном.

    python3 tools/update-catalog.py [--only id1,id2] [--verify]    # --only: пересчитать только эти программы, остальное взять из catalog.json
"""

import argparse
import datetime
import hashlib
import json
import os
import re
import socket
import ssl
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SEED = os.path.join(ROOT, "tools", "catalog-seed.json")
OUT = os.path.join(ROOT, "config", "catalog.json")
UA = "SchoolShield-catalog/1.0 (+https://github.com/Scrymez/SchoolShield)"

# Тихие ключи по типу установщика. /ALLUSERS и ALLUSERS=1: ставить в Program Files для всех (ученик запускает только оттуда).
ARGS = {
    "msi": "/qn /norestart ALLUSERS=1",
    "nsis": "/S",
    "inno": "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /ALLUSERS",
}


def detect_kind(url, head):
    if url.lower().split("?")[0].endswith(".msi") or head[:8] == b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1":
        return "msi"
    if b"Nullsoft" in head:
        return "nsis"
    if b"Inno Setup" in head:
        return "inno"
    return "unknown"


def _open(url, method="GET", headers=None):
    h = {"User-Agent": UA}
    h.update(headers or {})
    return urllib.request.urlopen(urllib.request.Request(url, headers=h, method=method), timeout=60)


def _curl(args, binary=False):
    """Запасной путь для сайтов, у которых сервер не отдаёт промежуточный сертификат (Python его не достраивает, а curl и Windows достраивают)."""
    r = subprocess.run(["curl", "-sL", "--max-time", "900"] + args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode != 0:
        raise RuntimeError("curl: код %d %s" % (r.returncode, r.stderr.decode("utf-8", "replace").strip()))
    return r.stdout


def curl_size(url):
    for line in reversed(_curl(["-I", url]).decode("latin-1").splitlines()):
        if line.lower().startswith("content-length:"):
            return int(line.split(":", 1)[1])
    return 0


def curl_head(url, n):
    return _curl(["-r", "0-%d" % (n - 1), url])[:n]


def curl_hash(url):
    data = _curl([url])
    return hashlib.sha256(data).hexdigest(), len(data)


def head_bytes(url, n=4 << 20):
    """Начало файла (для определения типа установщика): запрашиваем только первые n байт."""
    with _open(url, headers={"Range": "bytes=0-%d" % (n - 1)}) as r:
        return r.read(n)


def remote_size(url):
    with _open(url, "HEAD") as r:
        return int(r.headers.get("Content-Length") or 0)


def download_hash(url):
    """Скачивает файл потоком и считает SHA-256: запасной путь, когда издатель не публикует контрольную сумму."""
    h = hashlib.sha256()
    size = 0
    with _open(url) as r:
        expected = int(r.headers.get("Content-Length") or 0)
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            h.update(chunk)
            size += len(chunk)
    if expected and size != expected:      # зеркало оборвало соединение: хэш от обрывка в каталог попасть не должен
        raise RuntimeError("загрузка оборвалась: получено %d из %d байт" % (size, expected))
    return h.hexdigest(), size


def hash_from_text(text, filename):
    """Файл контрольных сумм издателя: строки «хэш  имя» (или одно значение)."""
    for line in text.splitlines():
        parts = line.strip().split()
        if len(parts) >= 2 and parts[-1].lstrip("*").endswith(filename):
            return parts[0].lower()
    tokens = text.split()
    if len(tokens) == 1 and len(tokens[0]) == 64:
        return tokens[0].lower()
    return None


def github_digest(url):
    """SHA-256 файла, который GitHub сам считает для каждого вложения релиза."""
    m = re.match(r"https://github\.com/([^/]+)/([^/]+)/releases/download/([^/]+)/(.+)$", url)
    if not m:
        return None
    owner, repo, tag, asset = m.groups()
    with _open("https://api.github.com/repos/%s/%s/releases/tags/%s" % (owner, repo, urllib.parse.quote(tag)),
               headers={"Accept": "application/vnd.github+json"}) as r:
        for a in json.load(r).get("assets", []):
            if a["name"] == urllib.parse.unquote(asset) and str(a.get("digest", "")).startswith("sha256:"):
                return a["digest"][7:].lower()
    return None


def resolve_hash(pk, verify):
    """(sha256, откуда взята). По порядку: из заготовки, файл издателя, GitHub, иначе скачиваем и считаем сами."""
    url = pk["url"]
    name = url.rsplit("/", 1)[-1]
    if pk.get("sha256"):
        found = (pk["sha256"].lower(), "заготовка")
    elif pk.get("sha256_url"):
        with _open(pk["sha256_url"]) as r:
            text = r.read().decode("utf-8", "replace")
        sha = hash_from_text(text, name)
        found = (sha, "файл издателя") if sha else None
    else:
        sha = github_digest(url)
        found = (sha, "GitHub") if sha else None
    if found is None:
        sha, _ = download_hash(url)
        return sha, "скачано и посчитано"
    if verify:
        mine, _ = download_hash(url)
        if mine != found[0]:
            raise RuntimeError("хэш издателя не совпал с хэшем файла: %s != %s" % (found[0][:12], mine[:12]))
        return found[0], found[1] + ", проверено скачиванием"
    return found


def main():
    socket.setdefaulttimeout(90)
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--verify", action="store_true", help="дополнительно скачать каждый файл и сверить с хэшем издателя (долго)")
    args = ap.parse_args()
    only = set(x for x in args.only.split(",") if x)

    with open(SEED, encoding="utf-8") as f:
        seed = json.load(f)
    old = {}
    if os.path.exists(OUT):
        with open(OUT, encoding="utf-8") as f:
            old = {i["id"]: i for i in json.load(f).get("items", [])}

    items, problems = [], []
    for it in seed["items"]:
        if only and it["id"] not in only and it["id"] in old:
            items.append(old[it["id"]])
            continue
        out = {k: v for k, v in it.items() if k != "packages"}
        out["packages"] = []
        total = 0
        for pk in it["packages"]:
            url = pk["url"]
            sys.stdout.write("%-14s %s ... " % (it["id"], url.rsplit("/", 1)[-1]))
            sys.stdout.flush()
            try:
                try:
                    sha, source = resolve_hash(pk, args.verify)
                    size = remote_size(url)
                    head = b"" if url.lower().split("?")[0].endswith(".msi") else head_bytes(url, 3 << 20)   # MSI опознаётся по расширению
                except (ssl.SSLError, urllib.error.URLError) as e:
                    if "CERTIFICATE_VERIFY_FAILED" not in str(e):
                        raise
                    sha, got = curl_hash(url)
                    source = "скачано через curl и посчитано"
                    size = curl_size(url)
                    if size and got != size:
                        raise RuntimeError("загрузка оборвалась: %d из %d байт" % (got, size))
                    head = b"" if url.lower().split("?")[0].endswith(".msi") else curl_head(url, 3 << 20)
            except Exception as e:
                print("ОШИБКА: %s" % e)
                problems.append("%s: %s" % (it["id"], e))
                continue
            kind = detect_kind(url, head)
            cmd = pk.get("args") or ARGS.get(kind)
            if not cmd:
                problems.append("%s: тип установщика не определён, задайте args в заготовке" % it["id"])
            elif kind == "msi" and "ALLUSERS" not in cmd:
                cmd += " ALLUSERS=1"
            print("%s, %.1f МБ, %s (%s)" % (kind, size / 1048576.0, sha[:12], source))
            out["packages"].append({"url": url, "sha256": sha, "size": size, "kind": "msi" if kind == "msi" else "exe", "installer": kind, "args": cmd or ""})
            total += size
        out["size_mb"] = int(round(total / 1048576.0))
        items.append(out)

    if problems:
        print("\nПроблемы:")
        for p in problems:
            print("  - " + p)
        return 1
    data = {"version": 1, "updated": datetime.date.today().isoformat(), "items": items}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print("\nГотово: %s (%d программ)" % (OUT, len(items)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
