"""Демо-режим (SCHOOLSHIELD_DEMO=1): подставляет придуманные данные, чтобы посмотреть интерфейс без Linux Mint. В пакет для пользователей не нужен."""

import os
import tempfile
import time

from . import apps, apply as apply_mod, system

_ICON_DIR = None


def _fake_apps():
    items = [
        ("libreoffice-writer", "LibreOffice Writer", "Текстовый редактор", "office", False),
        ("libreoffice-calc", "LibreOffice Calc", "Электронные таблицы", "office", False),
        ("libreoffice-impress", "LibreOffice Impress", "Презентации", "office", False),
        ("onlyoffice", "ONLYOFFICE Desktop Editors", "Офис", "office", False),
        ("evince", "Просмотр документов", "PDF", "office", False),
        ("scratch", "Scratch 3", "Программирование для школьников", "study", False),
        ("geogebra", "GeoGebra Classic", "Математика", "study", False),
        ("stellarium", "Stellarium", "Планетарий", "study", False),
        ("thonny", "Thonny", "Python IDE", "study", False),
        ("kumir", "Кумир", "Учебная среда", "study", False),
        ("blender", "Blender", "3D-моделирование", "study", False),
        ("firefox", "Firefox", "Веб-браузер", "internet", False),
        ("chromium", "Chromium", "Веб-браузер", "internet", False),
        ("thunderbird", "Thunderbird", "Почта", "internet", False),
        ("telegram", "Telegram Desktop", "Мессенджер", "internet", False),
        ("zoom", "Zoom", "Видеоконференции", "internet", False),
        ("gimp", "GIMP", "Редактор изображений", "media", False),
        ("inkscape", "Inkscape", "Векторная графика", "media", False),
        ("vlc", "VLC", "Медиаплеер", "media", False),
        ("audacity", "Audacity", "Звук", "media", False),
        ("kdenlive", "Kdenlive", "Видеомонтаж", "media", False),
        ("nemo", "Файлы", "Файловый менеджер", "system", False),
        ("gnome-calculator", "Калькулятор", "", "system", False),
        ("xed", "Текстовый редактор", "", "system", False),
        ("gnome-terminal", "Терминал", "", "system", False),
        ("cinnamon-settings", "Параметры системы", "", "system", False),
        ("wine-notepad++", "Notepad++", "Windows-программа (Wine)", "study", True),
        ("bottles-1c", "1С:Предприятие", "Windows-программа (Bottles)", "other", True),
        ("wine-kompas", "КОМПАС-3D", "Windows-программа (Wine)", "study", True),
    ]
    return [{"id": i, "name": n, "generic": g, "category": c, "windows": w, "icon": i, "exec": i, "path": "/demo/" + i + ".desktop", "terminal": False}
            for i, n, g, c, w in items]


def _icon(name):
    global _ICON_DIR
    if _ICON_DIR is None:
        _ICON_DIR = tempfile.mkdtemp(prefix="ss-demo-icons-")
    palette = ["#38bdf8", "#4ade80", "#fbbf24", "#f472b6", "#a78bfa", "#fb923c", "#2dd4bf"]
    color = palette[sum(map(ord, name)) % len(palette)]
    path = os.path.join(_ICON_DIR, name + ".svg")
    if not os.path.exists(path):
        letter = name[0].upper()
        with open(path, "w") as f:
            f.write('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48"><rect width="48" height="48" rx="11" fill="%s"/>'
                    '<text x="24" y="32" font-family="sans-serif" font-size="24" font-weight="700" text-anchor="middle" fill="#07101e">%s</text></svg>' % (color, letter))
    return path


def _docs(kinds, limit=400, max_depth=4, roots=None):
    now = int(time.time())
    names = {
        "docs": [("План урока — квадратные уравнения.docx", "Документы"), ("Рабочая программа 8 класс.docx", "Документы"), ("Протокол родительского собрания.odt", "Рабочий стол")],
        "sheets": [("Журнал успеваемости 8Б.xlsx", "Документы"), ("Расписание звонков.ods", "Документы")],
        "slides": [("Великая Отечественная война.pptx", "Документы"), ("Строение клетки.odp", "Загрузки")],
        "pdf": [("Методичка ФГОС.pdf", "Загрузки"), ("Демоверсия ОГЭ по информатике.pdf", "Загрузки")],
        "images": [("Опыт по физике.jpg", "Изображения"), ("Класс на экскурсии.png", "Изображения"), ("Схема.svg", "Документы")],
        "video": [("Лабораторная работа.mp4", "Видео")],
        "audio": [("Диктант — Пушкин.mp3", "Музыка")],
    }
    res = []
    for k in kinds:
        for i, (n, folder) in enumerate(names.get(k, [])):
            res.append({"path": "/demo/" + n, "name": n, "kind": k, "modified": now - 86400 * (i + 1) * 2, "folder": folder})
    res.sort(key=lambda x: -x["modified"])
    return res


def install():
    fake = _fake_apps()
    apps.load_apps = lambda dirs=None: fake
    apps.find_icon = lambda name: _icon(name) if name else None
    apps.scan_files = _docs
    apps.launch = lambda app: True
    apps.open_path = lambda p: True
    system.get_volume = lambda: {"volume": 62, "muted": False, "available": True}
    system.set_volume = lambda v: True
    system.battery = lambda: {"present": True, "percent": 78, "charging": False}
    system.wifi_enabled = lambda: True
    system.wifi_list = lambda: [
        {"ssid": "Школа-Учителя", "signal": 88, "secure": True, "active": True},
        {"ssid": "Школа-Гости", "signal": 64, "secure": False, "active": False},
        {"ssid": "Tele2-4F2A", "signal": 31, "secure": True, "active": False},
    ]
    system.power = lambda a: True
    apply_mod.status = lambda: {
        "version": "demo", "applied": True, "browsers_chromium": ["/etc/opt/chrome/policies/managed", "/etc/chromium/policies/managed"],
        "browsers_firefox": ["/usr/lib/firefox/distribution"], "hosts": True, "dns_provider": "AdGuardFamily", "dns_configured": True, "dns_live": True,
    }
