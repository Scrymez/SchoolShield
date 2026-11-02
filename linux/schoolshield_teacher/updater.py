"""Обновления из GitHub с проверкой подписи. Тот же открытый ключ и та же схема, что у Windows-версии.

Пакет без подписи автора (RSA-SHA256) не устанавливается. Установку выполняет root (pkexec) и проверяет подпись ещё раз сам:
вызывающему процессу не верит.
"""

import io
import json
import os
import re
import subprocess
import tempfile
import urllib.request

from . import paths, REPO, VERSION

UPDATE_PUBLIC_KEY_PEM = """-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAmlQgPtsUBowvqMu3K1Hr
7Jt7O+VrjHpN4X8S7G/npSllGqCrlC+P8v8a/f97z6RH9tAxHoA7OG1S3+NuCh9u
TFeec2b0+YMnCycFTmAqqaMeap0oxgsipOpd5eAQZCTtFNJTLBECcKYVAyWUjmKl
dnwEHxyeXtpv6jBOP3mImbP0qe3NXuDTfz2+TJDye9J8q+QIxsZSebaorOk9cMII
kJnbQ+CJ/ZitYw0fjIXrDHuyEjiWY+EDiagg5yMAQ5qMFT25MIt9LfSTZVzQU3+P
KDfa8KDxBqdHdnnzZUuZZ9/rfWpFsaMjOm2iGmDF7O1ozoOqLisIazad5gDxuzcw
5d7eW74m6pad/Dd5L05th8vcXQlTUZL68ZNXSYlLyAfSlUJDEVXAHCkfOq7UzW+Q
UxZiVaWqxegbft9Vi8o5eJp1nUuuUQfJU2UvIdsFRlgW3rVonVw4wbvHTS5X+1Ge
w8B4FbTDZ/p7LTV8DgzjeGumQw5qTIfjz0cOZRu08ErDAgMBAAE=
-----END PUBLIC KEY-----
"""

API = "https://api.github.com/repos/%s/releases/latest" % REPO
DEB_RE = re.compile(r"^schoolshield-teacher_(\d+\.\d+\.\d+)_all\.deb$")


def version_tuple(v):
    return tuple(int(x) for x in re.findall(r"\d+", v)[:3])


def _get(url, timeout=20):
    req = urllib.request.Request(url, headers={"User-Agent": "SchoolShield-Teacher/" + VERSION, "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def check():
    """Словарь {current, latest, available, url, sig_url, notes} или {error}. Нужен интернет."""
    try:
        rel = json.loads(_get(API).decode("utf-8"))
    except Exception as e:
        return {"error": "Не удалось связаться с GitHub: %s" % e, "current": VERSION}
    tag = str(rel.get("tag_name", "")).lstrip("v")
    deb = sig = None
    for a in rel.get("assets", []):
        if DEB_RE.match(a.get("name", "")):
            deb = a.get("browser_download_url")
        elif DEB_RE.match(a.get("name", "")[:-4]) and a.get("name", "").endswith(".sig"):
            sig = a.get("browser_download_url")
    try:
        newer = bool(tag) and version_tuple(tag) > version_tuple(VERSION)
    except ValueError:
        newer = False
    return {"current": VERSION, "latest": tag, "available": bool(newer and deb and sig), "url": deb, "sig_url": sig,
            "notes": (rel.get("body") or "")[:1500]}


def verify(data, signature):
    """Проверка подписи RSA-SHA256 через openssl (он есть в любой установке Linux Mint)."""
    with tempfile.TemporaryDirectory() as d:
        pub, sig, blob = (os.path.join(d, n) for n in ("pub.pem", "file.sig", "file.bin"))
        for p, content in ((pub, UPDATE_PUBLIC_KEY_PEM.encode("ascii")), (sig, signature), (blob, data)):
            with io.open(p, "wb") as f:
                f.write(content)
        r = subprocess.run(["openssl", "dgst", "-sha256", "-verify", pub, "-signature", sig, blob],
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
        return r.returncode == 0


def download(info, dest_dir, log=print):
    """Скачивает .deb и подпись в папку пользователя. Возвращает пути (deb, sig)."""
    os.makedirs(dest_dir, exist_ok=True)
    deb_path = os.path.join(dest_dir, os.path.basename(info["url"]))
    sig_path = deb_path + ".sig"
    log("Скачиваю " + os.path.basename(deb_path))
    for url, path in ((info["url"], deb_path), (info["sig_url"], sig_path)):
        with io.open(path, "wb") as f:
            f.write(_get(url, 300))
    return deb_path, sig_path


def install_deb(deb_path, sig_path, log=print):
    """Запускается от root. Копирует файлы в защищённую папку, проверяет подпись и ставит пакет."""
    with io.open(deb_path, "rb") as f:
        data = f.read(200 * 1024 * 1024)
    with io.open(sig_path, "rb") as f:
        sig = f.read(4096)
    if not DEB_RE.match(os.path.basename(deb_path)):
        log("! Имя файла не похоже на пакет SchoolShield.")
        return False
    if not verify(data, sig):
        log("! ПОДПИСЬ НЕ СОВПАЛА. Пакет не установлен.")
        return False
    log("Подпись автора верна.")
    target_dir = os.path.join(paths.state_dir(), "updates")
    os.makedirs(target_dir, mode=0o700, exist_ok=True)
    target = os.path.join(target_dir, os.path.basename(deb_path))
    with io.open(target, "wb") as f:       # устанавливаем копию, проверенную выше, а не файл, который можно подменить
        f.write(data)
    os.chmod(target, 0o600)
    r = subprocess.run(["apt-get", "install", "-y", target], env=dict(os.environ, DEBIAN_FRONTEND="noninteractive"))
    try:
        os.remove(target)
    except OSError:
        pass
    log("Пакет установлен." if r.returncode == 0 else "! apt-get завершился с ошибкой %d" % r.returncode)
    return r.returncode == 0
