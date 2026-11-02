"""Охрана сеанса: закрывает окна, в заголовке которых порно-слова (как охрана Windows-версии).

Работает в сеансе X11 (Linux Mint Cinnamon по умолчанию). В сеансе Wayland чужие окна не видны, там остаются DNS-фильтр,
политики браузеров и /etc/hosts. Окно закрывается вежливо (wmctrl -c); если оно не закрылось за 2 секунды, завершается его процесс.
"""

import io
import os
import re
import signal
import subprocess
import sys
import time

from . import config, lists, paths

_WMCTRL_LINE = re.compile(r"^(0x[0-9a-fA-F]+)\s+(-?\d+)\s+(\d+)\s+(\S+)\s?(.*)$")


def parse_wmctrl(output):
    """Вывод `wmctrl -lp` -> список (id, desktop, pid, title). Окна «на всех рабочих столах» (-1) тоже учитываются."""
    res = []
    for line in output.splitlines():
        m = _WMCTRL_LINE.match(line)
        if m:
            res.append((m.group(1), int(m.group(2)), int(m.group(3)), m.group(5).strip()))
    return res


class Guard(object):
    def __init__(self, cfg=None, patterns=None, runner=None, clock=time.time):
        self.cfg = cfg or config.load()
        self.patterns = patterns if patterns is not None else lists.load_badwords(paths.share_dir(), paths.extra_words_path())
        self._runner = runner or self._real_run
        self._clock = clock
        self.own_pid = os.getpid()
        self.closing = {}        # id окна -> когда впервые закрыли
        self.cache = {}          # id окна -> (заголовок, результат)
        self.strikes = 0
        self.log_lines = []

    # ------------------------------------------------------------------ внешние команды
    @staticmethod
    def _real_run(cmd):
        try:
            r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
            return r.returncode, r.stdout.decode("utf-8", "replace")
        except (OSError, subprocess.SubprocessError):
            return 127, ""

    def log(self, msg):
        line = time.strftime("%H:%M:%S ") + msg
        self.log_lines.append(line)
        del self.log_lines[:-200]
        try:
            d = paths.user_state_dir()
            os.makedirs(d, exist_ok=True)
            with io.open(os.path.join(d, "guard.log"), "a", encoding="utf-8") as f:
                f.write(line + "\n")
        except OSError:
            pass

    def notify(self, text):
        if self.cfg["guard"].get("notify"):
            self._runner(["notify-send", "-i", "dialog-warning", "-t", "4000", "SchoolShield", text])

    # ------------------------------------------------------------------ один проход
    def is_bad(self, title):
        return lists.is_bad_text(self.patterns, title)

    def tick(self):
        """Возвращает список закрытых окон (для тестов)."""
        code, out = self._runner(["wmctrl", "-lp"])
        if code != 0:
            return []
        now = self._clock()
        acted = []
        present = set()
        for wid, _desk, pid, title in parse_wmctrl(out):
            present.add(wid)
            if pid == self.own_pid:
                continue
            cached = self.cache.get(wid)
            if cached and cached[0] == title:
                bad = cached[1]
            else:
                bad = self.is_bad(title)
                self.cache[wid] = (title, bad)
            if not bad:
                continue
            first = self.closing.get(wid)
            if first is None:
                self.closing[wid] = now
                self.strikes += 1
                self.log("Окно 18+ закрыто: «%s» (pid %d)" % (title, pid))
                self.notify("Материал 18+. Окно закрыто.")
                self._runner(["wmctrl", "-i", "-c", wid])
                acted.append(wid)
            elif now - first > 2:
                self.closing[wid] = now
                self._kill(pid, title)
                acted.append(wid)
        for wid in [w for w in self.closing if w not in present]:
            del self.closing[wid]
        for wid in [w for w in self.cache if w not in present]:
            del self.cache[wid]
        return acted

    def _kill(self, pid, title):
        if pid <= 1:
            return
        try:
            os.kill(pid, signal.SIGTERM)
            self.log("Процесс %d завершён: окно не закрылось («%s»)" % (pid, title))
        except OSError:
            pass

    def run_forever(self, interval=0.7):
        if os.environ.get("XDG_SESSION_TYPE", "").lower() == "wayland":
            self.log("Сеанс Wayland: окна других программ недоступны, охрана окон не работает (DNS и политики браузеров действуют).")
        while True:
            try:
                self.tick()
            except Exception as e:     # охрана не должна падать из-за одной ошибки
                self.log("Ошибка цикла: %s" % e)
            time.sleep(interval)


def pidfile():
    return os.path.join(paths.runtime_dir(), "schoolshield-guard.pid")


def is_running():
    try:
        with io.open(pidfile()) as f:
            pid = int(f.read().strip())
        os.kill(pid, 0)
        return True
    except (IOError, OSError, ValueError):
        return False


def main():
    """Точка входа `schoolshield-teacher guard` (запускается из автозапуска сеанса)."""
    if is_running():
        return 0
    cfg = config.load()
    if not cfg["guard"].get("enabled", True):
        return 0
    try:
        os.makedirs(paths.runtime_dir(), exist_ok=True)
        with io.open(pidfile(), "w") as f:
            f.write(str(os.getpid()))
    except OSError:
        pass
    g = Guard(cfg)
    g.log("Охрана 18+ запущена (версия %s)" % __import__("schoolshield_teacher").VERSION)
    ai = None
    try:
        from . import aiscan
        ai = aiscan.start_in_background(cfg, g.log)
    except Exception as e:
        g.log("ИИ-фильтр экрана не запущен: %s" % e)
    try:
        g.run_forever()
    finally:
        if ai is not None:
            ai.terminate()
        try:
            os.remove(pidfile())
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
