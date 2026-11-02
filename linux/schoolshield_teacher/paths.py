"""Пути. Переменная SCHOOLSHIELD_ROOT переносит /etc и /var в песочницу: так проверяются тесты без прав root."""

import os

ROOT = os.environ.get("SCHOOLSHIELD_ROOT", "")


def under_root(path):
    """'/etc/x' -> '<ROOT>/etc/x', если задана песочница."""
    if not ROOT:
        return path
    return os.path.join(ROOT, path.lstrip("/"))


def etc_dir():
    return under_root("/etc/schoolshield")


def state_dir():
    return under_root("/var/lib/schoolshield")


def config_path():
    return os.path.join(etc_dir(), "config.json")


def extra_sites_path():
    return os.path.join(etc_dir(), "blocklist-extra.txt")


def extra_words_path():
    return os.path.join(etc_dir(), "badwords-extra.txt")


def managed_path():
    return os.path.join(state_dir(), "managed.json")


def model_path():
    from . import MODEL_FILE
    return os.path.join(state_dir(), "models", MODEL_FILE)


def venv_python():
    return under_root("/opt/schoolshield-teacher/venv/bin/python")


def share_dir():
    """Папка со списками, которые идут с программой: blocklist.txt, badwords-teacher.txt."""
    env = os.environ.get("SCHOOLSHIELD_SHARE")
    candidates = [env] if env else []
    candidates.append("/usr/share/schoolshield-teacher/config")
    here = os.path.dirname(os.path.abspath(__file__))
    candidates.append(os.path.normpath(os.path.join(here, "..", "..", "config")))   # запуск из репозитория
    candidates.append(os.path.normpath(os.path.join(here, "config")))
    for c in candidates:
        if c and os.path.isfile(os.path.join(c, "blocklist.txt")):
            return c
    return candidates[1]


def user_config_dir():
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config")
    return os.path.join(base, "schoolshield")


def user_state_dir():
    base = os.environ.get("XDG_STATE_HOME") or os.path.join(os.path.expanduser("~"), ".local", "state")
    return os.path.join(base, "schoolshield")


def runtime_dir():
    return os.environ.get("XDG_RUNTIME_DIR") or os.path.join("/tmp", "schoolshield-%d" % os.getuid())
