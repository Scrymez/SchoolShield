"""Списки: закрытые сайты 18+ и запрещённые слова. Те же файлы, что в Windows-версии (config/blocklist.txt, badwords-teacher.txt)."""

import io
import os
import re

from . import textguard

STUDENT_ONLY_MARKER = "# @student-only"


def read_lines(path, stop_at_student_only=False):
    """Строки списка без комментариев, в нижнем регистре, без http:// и хвостового '/'."""
    res = []
    try:
        with io.open(path, encoding="utf-8-sig") as f:
            for raw in f:
                t = raw.strip().lower()
                if stop_at_student_only and t.startswith(STUDENT_ONLY_MARKER):
                    break
                if not t or t.startswith("#"):
                    continue
                i = t.find("://")
                if i >= 0:
                    t = t[i + 3:]
                t = t.rstrip("/")
                if t:
                    res.append(t)
    except (IOError, OSError):
        pass
    return res


class Blocklist(object):
    """Сайты: точные домены (с поддоменами), 'имя.*' на любой зоне и префиксы 'домен/путь'."""

    def __init__(self):
        self.blocked = set()
        self.order = []          # домены в порядке файла: самые посещаемые первыми
        self.wild = set()
        self.prefixes = []

    def add(self, entry):
        if entry.endswith(".*"):
            label = entry[:-2]
            if len(label) >= 4 and "." not in label:
                self.wild.add(label)
        elif "/" in entry:
            self.prefixes.append(entry)
        elif entry not in self.blocked:
            self.blocked.add(entry)
            self.order.append(entry)

    def load(self, path, stop_at_student_only=True):
        for e in read_lines(path, stop_at_student_only):
            self.add(e)
        return self

    def is_blocked_host(self, host):
        host = host.lower().strip(".")
        h = host
        while True:
            if h in self.blocked:
                return True
            dot = h.find(".")
            if dot < 0:
                break
            rest = h[dot + 1:]
            if "." not in rest:     # голую зону вроде «com» не проверяем
                break
            h = rest
        labels = host.split(".")
        n = len(labels)
        if n >= 2 and labels[n - 2] in self.wild:
            return True
        if n >= 3 and labels[n - 3] in self.wild:
            return True
        return False

    def concrete_domains(self):
        """Домены без масок и путей: их можно записать в политики браузеров и в /etc/hosts."""
        return list(self.order)


def load_blocklist(share_dir, extra_path=None):
    bl = Blocklist()
    bl.load(os.path.join(share_dir, "blocklist.txt"), True)
    if extra_path:
        bl.load(extra_path, False)
    return bl


def compile_words(lines):
    res = []
    for raw in lines:
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        try:
            res.append(re.compile(line, re.IGNORECASE | re.UNICODE))
        except re.error:
            continue
    return res


def _read_text_lines(path):
    try:
        with io.open(path, encoding="utf-8-sig") as f:
            return f.read().splitlines()
    except (IOError, OSError):
        return []


def load_badwords(share_dir, extra_path=None):
    lines = _read_text_lines(os.path.join(share_dir, "badwords-teacher.txt"))
    if extra_path:
        lines += _read_text_lines(extra_path)
    return compile_words(lines)


def is_bad_text(patterns, text):
    return textguard.matches(patterns, text)
