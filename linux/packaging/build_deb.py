#!/usr/bin/env python3
"""Собирает schoolshield-teacher_<версия>_all.deb чистым Python: dpkg-deb не нужен, работает на macOS, Linux и Windows.

    python3 linux/packaging/build_deb.py [--out dist]

Пакет Architecture: all (Python без компиляции). Содержимое задаёт список FILES ниже.
"""

import argparse
import gzip
import hashlib
import io
import os
import re
import sys
import tarfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
PKG_SRC = os.path.join(REPO, "linux", "schoolshield_teacher")
FILES_DIR = os.path.join(HERE, "files")
EPOCH = 1793577600      # 2026-11-02: одинаковая дата в архивах, сборка воспроизводима

CONTROL = """Package: schoolshield-teacher
Version: {version}
Section: education
Priority: optional
Architecture: all
Maintainer: Латипов С.А. <noreply@users.noreply.github.com>
Installed-Size: {size}
Depends: python3 (>= 3.8), python3-gi, gir1.2-webkit2-4.1 | gir1.2-webkit2-4.0, wmctrl, libnotify-bin, policykit-1 | polkitd, openssl, network-manager, xdg-utils, libglib2.0-bin
Recommends: python3-pil, python3-numpy, python3-venv, pulseaudio-utils, libreoffice
Suggests: wine, bottles
Homepage: https://github.com/Scrymez/SchoolShield
Description: SchoolShield, редакция «Учитель»: фильтр 18+ и Центр учителя
 Для компьютеров учителей на Linux Mint. Закрывается только материал 18+ (порно, эротика):
 семейный DNS-фильтр, политики Chrome, Chromium, Edge, Brave, Firefox, список в /etc/hosts,
 охрана окон и необязательный локальный ИИ-фильтр экрана. Установка программ и всё остальное
 не ограничиваются. Центр учителя собирает программы, документы, файлы, Wi-Fi и звук в одном окне.
"""


def version():
    with open(os.path.join(PKG_SRC, "__init__.py"), encoding="utf-8") as f:
        return re.search(r'VERSION\s*=\s*"([^"]+)"', f.read()).group(1)


def collect():
    """[(путь в пакете, содержимое bytes, права)]"""
    items = []

    def add(dest, data, mode=0o644):
        items.append((dest, data, mode))

    def read(path):
        with open(path, "rb") as f:
            return f.read()

    for fn in sorted(os.listdir(PKG_SRC)):
        p = os.path.join(PKG_SRC, fn)
        if fn.endswith(".py") and fn != "demo.py":
            add("usr/lib/schoolshield-teacher/schoolshield_teacher/" + fn, read(p))
    add("usr/lib/schoolshield-teacher/schoolshield_teacher/ui/index.html", read(os.path.join(PKG_SRC, "ui", "index.html")))
    add("usr/lib/schoolshield-teacher/run.py", read(os.path.join(FILES_DIR, "run.py")))
    add("usr/bin/schoolshield-teacher", read(os.path.join(FILES_DIR, "schoolshield-teacher.sh")), 0o755)

    cfg = os.path.join(REPO, "config")
    add("usr/share/schoolshield-teacher/config/blocklist.txt", read(os.path.join(cfg, "blocklist.txt")))
    add("usr/share/schoolshield-teacher/config/badwords-teacher.txt", read(os.path.join(cfg, "badwords-teacher.txt")))
    add("usr/share/applications/schoolshield-teacher.desktop", read(os.path.join(FILES_DIR, "schoolshield-teacher.desktop")))
    add("etc/xdg/autostart/schoolshield-teacher-guard.desktop", read(os.path.join(FILES_DIR, "schoolshield-teacher-guard.desktop")))
    add("usr/share/polkit-1/actions/ru.schoolshield.teacher.policy", read(os.path.join(FILES_DIR, "ru.schoolshield.teacher.policy")))
    add("usr/share/icons/hicolor/scalable/apps/schoolshield-teacher.svg", read(os.path.join(FILES_DIR, "schoolshield-teacher.svg")))
    add("usr/share/doc/schoolshield-teacher/copyright", read(os.path.join(FILES_DIR, "copyright")))
    add("usr/share/doc/schoolshield-teacher/README.md", read(os.path.join(REPO, "linux", "README.md")))
    add("etc/schoolshield/config.json", b'{\n  "edition": "teacher",\n  "dns": "AdGuardFamily"\n}\n')
    return items


def _tarinfo(name, size=0, mode=0o644, is_dir=False):
    ti = tarfile.TarInfo(name)
    ti.size = 0 if is_dir else size
    ti.mode = mode
    ti.uid = ti.gid = 0
    ti.uname = ti.gname = "root"
    ti.mtime = EPOCH
    ti.type = tarfile.DIRTYPE if is_dir else tarfile.REGTYPE
    return ti


def make_tar_gz(entries, dirs=()):
    """entries: [(имя, bytes, mode)]. Возвращает сжатый tar без отметок времени и владельцев."""
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.GNU_FORMAT) as tar:
        for d in dirs:
            tar.addfile(_tarinfo("./" + d, is_dir=True, mode=0o755))
        for name, data, mode in entries:
            tar.addfile(_tarinfo("./" + name, len(data), mode), io.BytesIO(data))
    out = io.BytesIO()
    with gzip.GzipFile(fileobj=out, mode="wb", mtime=0, compresslevel=9) as gz:
        gz.write(raw.getvalue())
    return out.getvalue()


def ar_member(name, data):
    # Как у dpkg-deb: имя без завершающего «/», дополненное пробелами до 16 знаков.
    header = "%-16s%-12d%-6d%-6d%-8s%-10d`\n" % (name, EPOCH, 0, 0, "100644", len(data))
    body = header.encode("ascii") + data
    if len(data) % 2:
        body += b"\n"
    return body


def build(out_dir):
    ver = version()
    items = collect()
    dirs = sorted({"/".join(p.split("/")[:i]) for p, _d, _m in items for i in range(1, len(p.split("/")))})
    size_kb = (sum(len(d) for _p, d, _m in items) + 1023) // 1024

    md5s = "".join("%s  %s\n" % (hashlib.md5(d).hexdigest(), p) for p, d, _m in items if not p.startswith("etc/")).encode("utf-8")
    control_files = [
        ("control", CONTROL.format(version=ver, size=size_kb).encode("utf-8"), 0o644),
        ("md5sums", md5s, 0o644),
        ("conffiles", b"/etc/schoolshield/config.json\n", 0o644),
    ]
    for script in ("postinst", "prerm", "postrm"):
        with open(os.path.join(FILES_DIR, script), "rb") as f:
            control_files.append((script, f.read(), 0o755))

    control_tar = make_tar_gz(control_files, dirs=[""])
    data_tar = make_tar_gz(items, dirs=dirs)
    deb = b"!<arch>\n" + ar_member("debian-binary", b"2.0\n") + ar_member("control.tar.gz", control_tar) + ar_member("data.tar.gz", data_tar)

    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "schoolshield-teacher_%s_all.deb" % ver)
    with open(path, "wb") as f:
        f.write(deb)
    return path


def verify(path):
    """Читает собранный .deb обратно: структура ar, обязательные файлы, md5sums."""
    with open(path, "rb") as f:
        blob = f.read()
    assert blob.startswith(b"!<arch>\n"), "нет сигнатуры ar"
    pos, members = 8, {}
    while pos < len(blob):
        hdr = blob[pos:pos + 60]
        assert hdr[58:60] == b"`\n", "битый заголовок ar"
        name = hdr[:16].decode("ascii").strip().rstrip("/")
        size = int(hdr[48:58].decode("ascii").strip())
        members[name] = blob[pos + 60:pos + 60 + size]
        pos += 60 + size + (size % 2)
    assert list(members) == ["debian-binary", "control.tar.gz", "data.tar.gz"], list(members)
    assert members["debian-binary"] == b"2.0\n"
    ctl = tarfile.open(fileobj=io.BytesIO(members["control.tar.gz"]), mode="r:gz")
    names = {m.name for m in ctl.getmembers()}
    assert {"./control", "./md5sums", "./postinst", "./prerm", "./postrm", "./conffiles"} <= names, names
    data = tarfile.open(fileobj=io.BytesIO(members["data.tar.gz"]), mode="r:gz")
    files = {m.name[2:]: m for m in data.getmembers() if m.isfile()}
    for must in ("usr/bin/schoolshield-teacher", "usr/lib/schoolshield-teacher/run.py", "usr/lib/schoolshield-teacher/schoolshield_teacher/__main__.py",
                 "usr/lib/schoolshield-teacher/schoolshield_teacher/ui/index.html", "usr/share/schoolshield-teacher/config/blocklist.txt",
                 "usr/share/schoolshield-teacher/config/badwords-teacher.txt", "etc/schoolshield/config.json"):
        assert must in files, "в пакете нет " + must
    assert not any("demo.py" in n or "tests" in n for n in files), "в пакет попали лишние файлы"
    assert all(m.uid == 0 and m.gid == 0 for m in data.getmembers()), "владелец не root"
    for line in ctl.extractfile("./md5sums").read().decode().splitlines():
        digest, _, rel = line.partition("  ")
        assert hashlib.md5(data.extractfile("./" + rel).read()).hexdigest() == digest, "md5 не совпал: " + rel
    return len(files)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(REPO, "dist"))
    args = ap.parse_args()
    out = build(args.out)
    n = verify(out)
    print("%s: %d файлов, %d КБ" % (out, n, os.path.getsize(out) // 1024))
