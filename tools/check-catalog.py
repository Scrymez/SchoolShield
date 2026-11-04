#!/usr/bin/env python3
"""Проверка config/catalog.json перед сборкой: поля, https-адреса, SHA-256, тихие ключи, русские описания. Без сети."""

import json
import os
import re
import sys

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "config", "catalog.json")
CATEGORIES = {"office", "study", "media", "system"}


def main():
    with open(PATH, encoding="utf-8") as f:
        data = json.load(f)
    errors, ids = [], set()
    for it in data["items"]:
        i = it.get("id", "?")
        if i in ids:
            errors.append("%s: повтор id" % i)
        ids.add(i)
        if it.get("category") not in CATEGORIES:
            errors.append("%s: неизвестный раздел" % i)
        if not re.search(r"[а-яА-Я]{4}", it.get("description", "")):
            errors.append("%s: нет русского описания" % i)
        if not re.search(r"[Рр]усск", it.get("russian", "")):
            errors.append("%s: нет пометки о русском интерфейсе" % i)
        if not it.get("detect"):
            errors.append("%s: нет имени для поиска в установленных программах" % i)
        if not it.get("site", "").startswith("https://"):
            errors.append("%s: сайт не https" % i)
        if not it.get("packages"):
            errors.append("%s: нет пакетов" % i)
        for p in it.get("packages", []):
            if not p["url"].startswith("https://"):
                errors.append("%s: адрес не https" % i)
            if not re.fullmatch(r"[0-9a-f]{64}", p.get("sha256", "")):
                errors.append("%s: нет SHA-256" % i)
            if p.get("size", 0) < 100000:
                errors.append("%s: подозрительный размер" % i)
            args = p.get("args", "")
            if not args:
                errors.append("%s: нет тихих ключей" % i)
            if p.get("kind") == "msi" and ("/qn" not in args or "ALLUSERS=1" not in args):
                errors.append("%s: MSI должен ставиться тихо и для всех пользователей" % i)
    if errors:
        print("Каталог не прошёл проверку:")
        for e in errors:
            print("  - " + e)
        return 1
    print("Каталог в порядке: %d программ" % len(data["items"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
