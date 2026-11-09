"""Файлы для Центра учителя: копирование и перемещение в фоне (с полоской хода и отменой), корзина, переименование, новая папка.

Копирование идёт в отдельном потоке, окно только читает счётчики, поэтому ничего не зависает. Занятое имя превращается
в «Имя (2).ext»: ничего не перезаписывается и ничего не спрашивается. Без сторонних модулей.
"""

import os
import shutil
import subprocess
import threading
import time
import uuid

CHUNK = 1 << 20
FORBIDDEN_NAME_CHARS = "/\0"

# Эти папки нельзя удалить, переименовать или перенести целиком (случайный жест не должен снести систему или «Домой»).
PROTECTED = {"/", "/home", "/media", "/mnt", "/run", "/usr", "/etc", "/var", "/boot", "/bin", "/sbin", "/lib", "/lib64", "/opt", "/root", "/dev", "/proc", "/sys"}


def home():
    return os.path.realpath(os.path.expanduser("~"))


def is_protected(path):
    try:
        p = os.path.realpath(path)
    except (OSError, ValueError):
        return True
    return p in PROTECTED or p == home() or p.rstrip("/") in ("/media/" + os.environ.get("USER", "-"), "/run/media/" + os.environ.get("USER", "-"))


def unique_path(folder, name):
    """«Отчёт.docx» в папке, где он уже есть, даёт «Отчёт (2).docx», затем «(3)»..."""
    path = os.path.join(folder, name)
    if not os.path.lexists(path):
        return path
    is_dir = os.path.isdir(path) and not os.path.islink(path)
    stem, ext = (name, "") if is_dir else os.path.splitext(name)
    for i in range(2, 10000):
        candidate = os.path.join(folder, "%s (%d)%s" % (stem, i, ext))
        if not os.path.lexists(candidate):
            return candidate
    return os.path.join(folder, "%s (%s)%s" % (stem, uuid.uuid4().hex[:6], ext))


def is_inside(path, parent):
    """path — это сама папка parent или что-то внутри неё?"""
    p = os.path.realpath(path).rstrip("/")
    r = os.path.realpath(parent).rstrip("/")
    return p == r or p.startswith(r + "/")


def same_device(a, b):
    """Оба пути на одном диске: перемещение тогда — просто переименование (мгновенно, любого размера)."""
    try:
        return os.stat(a).st_dev == os.stat(b).st_dev
    except OSError:
        return False


def size_of(path):
    try:
        if os.path.islink(path) or os.path.isfile(path):
            return os.lstat(path).st_size
        total = 0
        for cur, _dirs, files in os.walk(path):
            for f in files:
                try:
                    total += os.lstat(os.path.join(cur, f)).st_size
                except OSError:
                    pass
        return total
    except OSError:
        return 0


class Cancelled(Exception):
    pass


class Job(object):
    """Одно копирование или перемещение. Run() выполняется в потоке; snapshot() читает окно."""

    def __init__(self, sources, target, move):
        self.id = uuid.uuid4().hex[:12]
        self.sources = list(sources)
        self.target = target
        self.move = bool(move)
        self.total_bytes = 0
        self.done_bytes = 0
        self.total = 0
        self.done = 0
        self.current = ""
        self.cancelled = False
        self.finished = False
        self.errors = []
        self.created = []
        self.started = time.time()

    @property
    def percent(self):
        if self.total_bytes <= 0:
            return 100 if self.finished else 0
        return min(100, int(self.done_bytes * 100 / self.total_bytes))

    def snapshot(self):
        return {"id": self.id, "move": self.move, "percent": self.percent, "done": self.done, "total": self.total, "current": self.current,
                "finished": self.finished, "cancelled": self.cancelled, "errors": self.errors[:5], "error_count": len(self.errors),
                "bytes": self.total_bytes, "created": self.created[:20]}

    def run(self):
        try:
            todo = []
            for s in self.sources:
                if not os.path.lexists(s):
                    self.errors.append("Не найдено: " + os.path.basename(s))
                elif os.path.isdir(s) and not os.path.islink(s) and is_inside(self.target, s):
                    self.errors.append("Папку «%s» нельзя поместить внутрь самой себя." % os.path.basename(s))
                elif self.move and is_protected(s):
                    self.errors.append("Папку «%s» нельзя переносить." % os.path.basename(s))
                else:
                    todo.append(s)
            self.total = len(todo)
            self.total_bytes = sum(size_of(s) for s in todo)
            try:
                os.makedirs(self.target, exist_ok=True)
            except OSError as e:
                self.errors.append("Не удалось открыть папку назначения: %s" % e)
                return
            for s in todo:
                if self.cancelled:
                    break
                name = os.path.basename(s.rstrip("/"))
                self.current = name
                try:
                    if self.move and os.path.dirname(s.rstrip("/")) == self.target.rstrip("/"):
                        self.done += 1               # уже лежит в этой папке
                        self.done_bytes += size_of(s)
                        continue
                    dst = unique_path(self.target, name)
                    if self.move and same_device(s, self.target):
                        n = size_of(s)
                        os.rename(s, dst)
                        self.done_bytes += n
                    else:
                        self._copy_tree(s, dst)
                        if self.move and not self.cancelled:
                            if os.path.isdir(s) and not os.path.islink(s):
                                shutil.rmtree(s)
                            else:
                                os.remove(s)
                    self.created.append(dst)
                    self.done += 1
                except Cancelled:
                    break
                except (OSError, shutil.Error) as e:
                    self.errors.append("%s: %s" % (name, e))
        finally:
            self.finished = True

    def _copy_tree(self, src, dst):
        if os.path.islink(src):
            os.symlink(os.readlink(src), dst)
            return
        if os.path.isdir(src):
            os.makedirs(dst, exist_ok=True)
            for entry in sorted(os.listdir(src)):
                if self.cancelled:
                    raise Cancelled()
                self.current = entry
                try:
                    self._copy_tree(os.path.join(src, entry), os.path.join(dst, entry))
                except Cancelled:
                    raise
                except (OSError, shutil.Error) as e:
                    self.errors.append("%s: %s" % (entry, e))
            try:
                shutil.copystat(src, dst)
            except OSError:
                pass
            return
        self._copy_file(src, dst)

    def _copy_file(self, src, dst):
        created = ok = False
        try:
            with open(src, "rb") as i:
                fd = os.open(dst, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o644)
                created = True
                with os.fdopen(fd, "wb") as o:
                    while True:
                        buf = i.read(CHUNK)
                        if not buf:
                            break
                        if self.cancelled:
                            raise Cancelled()
                        o.write(buf)
                        self.done_bytes += len(buf)
            ok = True
        finally:
            if created and not ok:
                try:
                    os.remove(dst)                       # половину файла не оставляем
                except OSError:
                    pass
        try:
            shutil.copystat(src, dst)
        except OSError:
            pass


class Jobs(object):
    """Список заданий окна: запуск, чтение хода, отмена."""

    def __init__(self):
        self.items = {}
        self.lock = threading.Lock()

    def start(self, sources, target, move):
        job = Job(sources, target, move)
        with self.lock:
            now = time.time()
            for k in [k for k, j in self.items.items() if j.finished and now - j.started > 900]:
                del self.items[k]
            self.items[job.id] = job
        threading.Thread(target=job.run, name="fileop-" + job.id, daemon=True).start()
        return job

    def get(self, job_id):
        with self.lock:
            return self.items.get(job_id)


# ----------------------------------------------------------------------------- мелкие операции

def valid_name(name):
    name = (name or "").strip()
    if not name or name in (".", "..") or any(c in name for c in FORBIDDEN_NAME_CHARS) or len(name.encode("utf-8")) > 255:
        return None
    return name


def make_folder(parent, name="Новая папка"):
    clean = valid_name(name)
    if not clean:
        return None, "Недопустимое имя папки."
    path = unique_path(parent, clean)
    try:
        os.makedirs(path)
    except OSError as e:
        return None, "Не удалось создать папку: %s" % e
    return path, None


def rename(path, new_name):
    clean = valid_name(new_name)
    if not clean:
        return None, "В имени нельзя использовать знак «/»."
    if is_protected(path):
        return None, "Эту папку переименовывать нельзя."
    dst = os.path.join(os.path.dirname(path.rstrip("/")), clean)
    if dst == path:
        return path, None
    if os.path.lexists(dst):
        return None, "Такое имя уже есть. Выберите другое."
    try:
        os.rename(path, dst)
    except OSError as e:
        return None, "Не удалось переименовать: %s" % e
    return dst, None


def to_trash(paths, permanent=False):
    """В корзину (gio trash). permanent=True: удалить насовсем (окно заранее предупреждает об этом, например на флешке).

    Возвращает (сколько убрано, список ошибок). Без разрешения файл никогда не удаляется насовсем.
    """
    ok, errors = 0, []
    for p in paths:
        if not os.path.lexists(p):
            errors.append("Не найдено: " + os.path.basename(p))
            continue
        if is_protected(p):
            errors.append("Эту папку удалять нельзя: " + os.path.basename(p))
            continue
        done = False
        if not permanent:
            for cmd in (["gio", "trash", p], ["trash-put", p]):
                if shutil.which(cmd[0]):
                    try:
                        r = subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, timeout=120)
                        if r.returncode == 0:
                            done = True
                            break
                    except (OSError, subprocess.SubprocessError):
                        pass
            if not done:
                errors.append("Не удалось убрать в корзину: " + os.path.basename(p))
                continue
        else:
            try:
                if os.path.isdir(p) and not os.path.islink(p):
                    shutil.rmtree(p)
                else:
                    os.remove(p)
                done = True
            except OSError as e:
                errors.append("%s: %s" % (os.path.basename(p), e))
        if done:
            ok += 1
    return ok, errors
