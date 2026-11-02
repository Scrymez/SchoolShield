"""Программы (ярлыки .desktop), значки, документы и файлы для Центра учителя."""

import io
import locale
import os
import re
import shlex
import subprocess
import time

APP_DIRS = [
    "/usr/share/applications",
    "/usr/local/share/applications",
    "/var/lib/flatpak/exports/share/applications",
    "/var/lib/snapd/desktop/applications",
    "~/.local/share/applications",
    "~/.local/share/flatpak/exports/share/applications",
]

# Название раздела и перечень категорий freedesktop, которые к нему относятся. Порядок важен: берётся первое совпадение.
SECTIONS = [
    ("office", "Офисные", ["Office", "WordProcessor", "Spreadsheet", "Presentation", "Calendar", "ContactManagement", "Publishing", "Viewer"]),
    ("study", "Учебное ПО", ["Education", "Science", "Math", "Development", "IDE", "Electronics", "Engineering", "Robotics", "Astronomy",
                              "Chemistry", "Physics", "Biology", "Geography", "Languages", "Literature", "ComputerScience", "ArtificialIntelligence"]),
    ("internet", "Интернет", ["Network", "WebBrowser", "Email", "InstantMessaging", "Chat", "VideoConference", "FileTransfer", "RemoteAccess"]),
    ("media", "Графика и медиа", ["AudioVideo", "Audio", "Video", "Graphics", "Photography", "2DGraphics", "3DGraphics", "RasterGraphics",
                                  "VectorGraphics", "Player", "Recorder", "Music"]),
    ("system", "Системные", ["Utility", "System", "Settings", "Accessibility", "Archiving", "Calculator", "TextEditor", "FileManager", "TerminalEmulator"]),
]
OTHER = ("other", "Другое")
HIDDEN_IDS = ("schoolshield-teacher",)

_FIELD_CODES = re.compile(r"%[fFuUdDnNickvm]")


def _lang_suffixes():
    try:
        lang = (locale.getlocale()[0] or os.environ.get("LANG", "ru")).split(".")[0]
    except (ValueError, TypeError):
        lang = "ru"
    short = lang.split("_")[0]
    return ["[%s]" % lang, "[%s]" % short]


def parse_desktop(path):
    """Читает раздел [Desktop Entry]. Возвращает словарь ключ -> значение или None."""
    data = {}
    inside = False
    try:
        with io.open(path, encoding="utf-8", errors="replace") as f:
            for raw in f:
                line = raw.strip()
                if not line or line.startswith("#"):
                    continue
                if line.startswith("["):
                    if inside:
                        break
                    inside = line == "[Desktop Entry]"
                    continue
                if inside and "=" in line:
                    k, _, v = line.partition("=")
                    data[k.strip()] = v.strip()
    except OSError:
        return None
    return data or None


def localized(data, key):
    for suffix in _lang_suffixes():
        v = data.get(key + suffix)
        if v:
            return v
    return data.get(key, "")


def classify(categories, exec_line=""):
    cats = [c for c in categories.split(";") if c]
    for sid, _name, wanted in SECTIONS:
        if any(c in wanted for c in cats):
            return sid
    return OTHER[0]


def is_windows_program(path, exec_line):
    """Ярлыки Wine, Bottles, Lutris и Proton: Windows-программа, запущенная на Linux."""
    blob = (path + " " + exec_line).lower()
    return "/wine/" in blob or "wine " in blob or blob.startswith("wine") or "bottles" in blob or "lutris" in blob or "proton" in blob


def desktop_id(path):
    return os.path.basename(path)[:-len(".desktop")] if path.endswith(".desktop") else os.path.basename(path)


def strip_exec(exec_line):
    """Exec из .desktop без кодов %f %U и т.п. -> список аргументов."""
    cleaned = _FIELD_CODES.sub("", exec_line.replace("%%", "\0")).replace("\0", "%")
    try:
        return shlex.split(cleaned)
    except ValueError:
        return cleaned.split()


def load_apps(dirs=None):
    """Все видимые программы: список словарей id, name, generic, category, icon, exec, path, windows."""
    seen = {}
    for d in (dirs or APP_DIRS):
        d = os.path.expanduser(d)
        if not os.path.isdir(d):
            continue
        for root, _dirs, files in os.walk(d):
            for fn in files:
                if not fn.endswith(".desktop"):
                    continue
                path = os.path.join(root, fn)
                data = parse_desktop(path)
                if not data or data.get("Type", "Application") != "Application":
                    continue
                if data.get("NoDisplay", "").lower() == "true" or data.get("Hidden", "").lower() == "true":
                    continue
                only = data.get("OnlyShowIn")
                if only and not any(x in only for x in ("X-Cinnamon", "GNOME", "XFCE", "MATE", "KDE", "Unity", "LXQt")):
                    pass    # окружения Cinnamon не исключаем: показываем всё, что не скрыто
                name = localized(data, "Name")
                if not name:
                    continue
                aid = desktop_id(path)
                if aid in HIDDEN_IDS:
                    continue
                exec_line = data.get("Exec", "")
                seen[aid] = {
                    "id": aid,
                    "name": name,
                    "generic": localized(data, "GenericName") or localized(data, "Comment"),
                    "category": classify(data.get("Categories", ""), exec_line),
                    "icon": data.get("Icon", ""),
                    "exec": exec_line,
                    "path": path,
                    "terminal": data.get("Terminal", "").lower() == "true",
                    "windows": is_windows_program(path, exec_line),
                }
    return sorted(seen.values(), key=lambda a: a["name"].lower())


def launch(app):
    """Запуск программы так же, как из меню: gtk-launch, иначе gio launch, иначе прямой запуск Exec."""
    for cmd in (["gtk-launch", app["id"]], ["gio", "launch", app["path"]]):
        try:
            if subprocess.call(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15) == 0:
                return True
        except (OSError, subprocess.SubprocessError):
            continue
    argv = strip_exec(app["exec"])
    if not argv:
        return False
    if app.get("terminal"):
        argv = ["x-terminal-emulator", "-e"] + argv
    try:
        subprocess.Popen(argv, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        return True
    except OSError:
        return False


# ----------------------------------------------------------------------------- значки

ICON_BASES = ["~/.local/share/icons", "~/.icons", "/usr/share/icons", "/usr/share/pixmaps", "/var/lib/flatpak/exports/share/icons",
              "/var/lib/snapd/desktop/icons"]
ICON_THEMES = ["hicolor", "Mint-Y", "Mint-X", "Adwaita", "gnome", "Yaru", "breeze"]
ICON_SIZES = ["48x48", "64x64", "128x128", "256x256", "scalable", "96x96", "32x32", "512x512", "24x24"]
_icon_cache = {}


def find_icon(name):
    """Путь к файлу значка по имени из .desktop (или None)."""
    if not name:
        return None
    if name in _icon_cache:
        return _icon_cache[name]
    found = None
    if os.path.isabs(name):
        found = name if os.path.isfile(name) else None
    else:
        exts = (".png", ".svg", ".xpm")
        for base in ICON_BASES:
            base = os.path.expanduser(base)
            if base.endswith("pixmaps"):
                for e in exts:
                    p = os.path.join(base, name + e)
                    if os.path.isfile(p):
                        found = p
                        break
            else:
                for theme in ICON_THEMES:
                    for size in ICON_SIZES:
                        for e in (".png", ".svg"):
                            p = os.path.join(base, theme, size, "apps", name + e)
                            if os.path.isfile(p):
                                found = p
                                break
                        if found:
                            break
                    if found:
                        break
            if found:
                break
    _icon_cache[name] = found
    return found


# ----------------------------------------------------------------------------- документы и файлы

KINDS = {
    "docs": (".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".fb2", ".epub", ".djvu"),
    "sheets": (".xls", ".xlsx", ".ods", ".csv"),
    "slides": (".ppt", ".pptx", ".odp", ".pps", ".ppsx"),
    "pdf": (".pdf",),
    "images": (".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".tif", ".tiff"),
    "video": (".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".mpg", ".mpeg"),
    "audio": (".mp3", ".ogg", ".wav", ".flac", ".m4a", ".opus"),
}
_EXT_TO_KIND = {e: k for k, exts in KINDS.items() for e in exts}


def kind_of(path):
    return _EXT_TO_KIND.get(os.path.splitext(path)[1].lower())


def user_folders():
    home = os.path.expanduser("~")
    names = [("Рабочий стол", "Desktop"), ("Документы", "Documents"), ("Загрузки", "Downloads"),
             ("Изображения", "Pictures"), ("Видео", "Videos"), ("Музыка", "Music")]
    res = [{"name": "Домашняя папка", "path": home}]
    for title, folder in names:
        p = os.path.join(home, folder)
        if os.path.isdir(p):
            res.append({"name": title, "path": p})
    user = os.environ.get("USER", "")
    for base in ("/media/" + user, "/run/media/" + user, "/mnt"):
        if os.path.isdir(base):
            try:
                for entry in sorted(os.listdir(base)):
                    p = os.path.join(base, entry)
                    if os.path.isdir(p):
                        res.append({"name": "Диск: " + entry, "path": p})
            except OSError:
                pass
    res.append({"name": "Вся файловая система", "path": "/"})
    return res


def scan_files(kinds, limit=400, max_depth=4, roots=None):
    """Недавние файлы нужных видов в домашних папках, новые сверху."""
    home = os.path.expanduser("~")
    roots = roots or [os.path.join(home, d) for d in ("Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music")]
    found = []
    for root in roots:
        if not os.path.isdir(root):
            continue
        base_depth = root.rstrip(os.sep).count(os.sep)
        for cur, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if not d.startswith(".")]
            if cur.count(os.sep) - base_depth >= max_depth:
                dirs[:] = []
            for fn in files:
                if fn.startswith("."):
                    continue
                p = os.path.join(cur, fn)
                k = kind_of(p)
                if k in kinds:
                    try:
                        found.append((os.path.getmtime(p), p, k))
                    except OSError:
                        pass
    found.sort(reverse=True)
    return [{"path": p, "name": os.path.basename(p), "kind": k, "modified": int(m),
             "folder": os.path.basename(os.path.dirname(p))} for m, p, k in found[:limit]]


def list_dir(path):
    entries = []
    try:
        with os.scandir(path) as it:
            for e in it:
                if e.name.startswith("."):
                    continue
                try:
                    is_dir = e.is_dir()
                    st = e.stat()
                    entries.append({"name": e.name, "path": e.path, "dir": is_dir, "size": 0 if is_dir else st.st_size,
                                    "modified": int(st.st_mtime), "kind": None if is_dir else kind_of(e.name)})
                except OSError:
                    continue
    except OSError:
        return None
    entries.sort(key=lambda x: (not x["dir"], x["name"].lower()))
    return entries


def open_path(path):
    try:
        subprocess.Popen(["xdg-open", path], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        return True
    except OSError:
        return False
