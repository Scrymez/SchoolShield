"""Центр учителя: локальный веб-сервер (только 127.0.0.1) и окно. Интерфейс — ui/index.html.

Защита от чужих: случайный токен сеанса (без него сервер отвечает 403), проверка заголовка Host (против DNS rebinding) и
проверка Origin у POST-запросов. Токен лежит в $XDG_RUNTIME_DIR (доступ только владельцу).
"""

import io
import json
import mimetypes
import os
import secrets
import shutil
import socket
import subprocess
import tempfile
import threading
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from . import apply as apply_mod, apps, config, guard, paths, system, updater, aiscan, VERSION, AUTHOR, SITE_URL

UI_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui")
PKEXEC_BIN = "/usr/bin/schoolshield-teacher"
DEFAULT_PORT = 47831


class Center(object):
    """Состояние одного запущенного Центра учителя."""

    def __init__(self, port):
        self.port = port
        self.token = secrets.token_urlsafe(24)
        self.last_beat = time.time()
        self.apps_cache = None
        self.apps_time = 0.0
        self.lock = threading.Lock()

    def apps(self, refresh=False):
        with self.lock:
            if refresh or self.apps_cache is None or time.time() - self.apps_time > 300:
                self.apps_cache = apps.load_apps()
                self.apps_time = time.time()
            return self.apps_cache

    def app_by_id(self, aid):
        for a in self.apps():
            if a["id"] == aid:
                return a
        return None

    def url(self):
        return "http://127.0.0.1:%d/?t=%s" % (self.port, self.token)


def _pkexec(args, timeout=900):
    """Команды, меняющие систему, выполняются от root через pkexec: система сама спросит пароль администратора."""
    exe = PKEXEC_BIN if os.path.exists(PKEXEC_BIN) else shutil.which("schoolshield-teacher") or PKEXEC_BIN
    try:
        r = subprocess.run(["pkexec", exe] + args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
        text = r.stdout.decode("utf-8", "replace")
        return r.returncode == 0, text.strip().splitlines()[-60:]
    except FileNotFoundError:
        return False, ["Не найден pkexec (пакет policykit-1). Выполните в терминале: sudo schoolshield-teacher " + " ".join(args)]
    except subprocess.SubprocessError as e:
        return False, [str(e)]


def make_handler(center):
    allowed_hosts = {"127.0.0.1:%d" % center.port, "localhost:%d" % center.port}

    class Handler(BaseHTTPRequestHandler):
        server_version = "SchoolShieldCenter/" + VERSION
        protocol_version = "HTTP/1.1"

        def log_message(self, fmt, *args):      # тишина в консоли
            pass

        # ------------------------------------------------------------ ответы
        def _send(self, code, body, ctype="application/json; charset=utf-8", extra=None):
            if isinstance(body, (dict, list)):
                body = json.dumps(body, ensure_ascii=False).encode("utf-8")
            elif isinstance(body, str):
                body = body.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("Referrer-Policy", "no-referrer")
            for k, v in (extra or {}).items():
                self.send_header(k, v)
            self.end_headers()
            self.wfile.write(body)

        def _deny(self, code=403, msg="Forbidden"):
            self._send(code, {"error": msg})

        def _authorised(self, query):
            if self.headers.get("Host", "") not in allowed_hosts:
                return False
            token = self.headers.get("X-Token") or (query.get("t") or [""])[0]
            return secrets.compare_digest(token, center.token)

        def _json_body(self):
            try:
                n = int(self.headers.get("Content-Length", "0"))
                if n <= 0 or n > 1000000:
                    return {}
                data = json.loads(self.rfile.read(n).decode("utf-8"))
                return data if isinstance(data, dict) else {}
            except (ValueError, OSError):
                return {}

        # ------------------------------------------------------------ GET
        def do_GET(self):
            parsed = urllib.parse.urlparse(self.path)
            query = urllib.parse.parse_qs(parsed.query)
            if not self._authorised(query):
                return self._deny()
            path = parsed.path
            try:
                if path == "/":
                    with io.open(os.path.join(UI_DIR, "index.html"), "rb") as f:
                        html = f.read().replace(b"__TOKEN__", center.token.encode("ascii"))
                    return self._send(200, html, "text/html; charset=utf-8")
                if path == "/api/ping":
                    return self._send(200, {"ok": True, "version": VERSION})
                if path == "/api/state":
                    return self._send(200, self._state())
                if path == "/api/apps":
                    refresh = (query.get("refresh") or ["0"])[0] == "1"
                    return self._send(200, [{k: a[k] for k in ("id", "name", "generic", "category", "windows")}
                                            for a in center.apps(refresh)])
                if path.startswith("/icon/"):
                    return self._icon(urllib.parse.unquote(path[6:]))
                if path == "/api/files":
                    return self._files((query.get("path") or [""])[0])
                if path == "/api/docs":
                    kinds = [k for k in (query.get("kinds") or [""])[0].split(",") if k in apps.KINDS]
                    return self._send(200, apps.scan_files(kinds) if kinds else [])
                if path == "/api/folders":
                    return self._send(200, apps.user_folders())
                if path == "/api/system":
                    return self._send(200, {"volume": system.get_volume(), "battery": system.battery(), "wifi_enabled": system.wifi_enabled()})
                if path == "/api/wifi":
                    return self._send(200, system.wifi_list())
                if path == "/api/protection":
                    return self._send(200, self._protection())
                if path == "/api/protection/lists":
                    return self._send(200, {"sites": _read(paths.extra_sites_path()), "words": _read(paths.extra_words_path())})
                if path == "/api/update/check":
                    return self._send(200, updater.check())
                if path == "/api/guard/log":
                    return self._send(200, {"lines": _tail(os.path.join(paths.user_state_dir(), "guard.log"), 60)})
            except Exception as e:      # одна ошибка не должна ронять сервер
                return self._send(500, {"error": str(e)})
            self._deny(404, "Not found")

        # ------------------------------------------------------------ POST
        def do_POST(self):
            parsed = urllib.parse.urlparse(self.path)
            if not self._authorised(urllib.parse.parse_qs(parsed.query)):
                return self._deny()
            origin = self.headers.get("Origin")
            if origin and origin not in ("http://127.0.0.1:%d" % center.port, "http://localhost:%d" % center.port):
                return self._deny()
            body = self._json_body()
            path = parsed.path
            try:
                if path == "/api/heartbeat":
                    center.last_beat = time.time()
                    return self._send(200, {"ok": True})
                if path == "/api/launch":
                    app = center.app_by_id(str(body.get("id", "")))
                    return self._send(200, {"ok": bool(app and apps.launch(app))})
                if path == "/api/open":
                    p = str(body.get("path", ""))
                    return self._send(200, {"ok": os.path.exists(p) and apps.open_path(p)})
                if path == "/api/open-url":
                    u = str(body.get("url", ""))
                    ok = u.startswith(("http://", "https://")) and apps.open_path(u)
                    return self._send(200, {"ok": bool(ok)})
                if path == "/api/volume":
                    if "muted" in body:
                        return self._send(200, {"ok": system.set_mute(bool(body["muted"]))})
                    return self._send(200, {"ok": system.set_volume(int(body.get("value", 0)))})
                if path == "/api/wifi/connect":
                    ok, msg = system.wifi_connect(str(body.get("ssid", "")), body.get("password") or None)
                    return self._send(200, {"ok": ok, "message": msg})
                if path == "/api/power":
                    return self._send(200, {"ok": system.power(str(body.get("action", "")))})
                if path == "/api/prefs":
                    return self._send(200, config.save_prefs(body))
                if path == "/api/protection/apply":
                    ok, lines = _pkexec(["apply"])
                    return self._send(200, {"ok": ok, "lines": lines, "status": self._protection()})
                if path == "/api/protection/remove":
                    ok, lines = _pkexec(["remove"])
                    return self._send(200, {"ok": ok, "lines": lines, "status": self._protection()})
                if path == "/api/protection/lists":
                    return self._send(200, self._save_lists(body))
                if path == "/api/protection/ai":
                    ok, lines = _pkexec(["install-ai"])
                    return self._send(200, {"ok": ok, "lines": lines, "status": self._protection()})
                if path == "/api/protection/settings":
                    return self._send(200, self._save_settings(body))
                if path == "/api/update/install":
                    return self._send(200, self._install_update())
            except Exception as e:
                return self._send(500, {"error": str(e)})
            self._deny(404, "Not found")

        # ------------------------------------------------------------ обработчики
        def _state(self):
            cfg = config.load()
            return {
                "version": VERSION, "author": AUTHOR, "site": SITE_URL, "title": cfg["title"], "host": system.uname(),
                "user": os.environ.get("USER", ""), "links": cfg["links"], "bells": cfg["bells"], "prefs": config.load_prefs(),
                "wayland": os.environ.get("XDG_SESSION_TYPE", "").lower() == "wayland",
                "sections": [{"id": s[0], "name": s[1]} for s in apps.SECTIONS] + [{"id": apps.OTHER[0], "name": apps.OTHER[1]}],
            }

        def _protection(self):
            st = apply_mod.status()
            cfg = config.load()
            st["guard_running"] = guard.is_running()
            st["ai_enabled"] = bool(cfg["ai"].get("enabled"))
            st["ai_installed"] = aiscan.available()
            st["ai_threshold"] = cfg["ai"].get("threshold")
            st["dns_choices"] = ["AdGuardFamily", "Cloudflare", "CleanBrowsing", "Yandex", "None"]
            st["hosts_block"] = bool(cfg["hosts_block"])
            st["session_x11"] = os.environ.get("XDG_SESSION_TYPE", "").lower() != "wayland"
            return st

        def _save_lists(self, body):
            tmp = tempfile.mkdtemp(prefix="ss-lists-", dir=paths.runtime_dir() if os.path.isdir(paths.runtime_dir()) else None)
            try:
                files = {}
                for key in ("sites", "words"):
                    p = os.path.join(tmp, key + ".txt")
                    with io.open(p, "w", encoding="utf-8") as f:
                        f.write(str(body.get(key, ""))[:300000])
                    files[key] = p
                os.chmod(tmp, 0o755)
                for p in files.values():
                    os.chmod(p, 0o644)
                ok, lines = _pkexec(["set-lists", files["sites"], files["words"]])
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
            return {"ok": ok, "lines": lines}

        def _save_settings(self, body):
            ok, lines = _pkexec(["set-config", json.dumps({k: body[k] for k in ("dns", "ai_enabled", "ai_threshold", "hosts_block") if k in body})])
            return {"ok": ok, "lines": lines, "status": self._protection()}

        def _install_update(self):
            info = updater.check()
            if not info.get("available"):
                return {"ok": False, "lines": ["Новой версии нет."]}
            deb, sig = updater.download(info, os.path.join(paths.user_state_dir(), "updates"), lambda m: None)
            ok, lines = _pkexec(["install-deb", deb, sig])
            return {"ok": ok, "lines": lines}

        def _icon(self, aid):
            app = center.app_by_id(aid)
            path = apps.find_icon(app["icon"]) if app else None
            if not path:
                return self._deny(404, "No icon")
            ctype = mimetypes.guess_type(path)[0] or "image/png"
            with io.open(path, "rb") as f:
                data = f.read(2000000)
            self._send(200, data, ctype, {"Cache-Control": "max-age=3600"})

        def _files(self, path):
            if not path:
                return self._send(200, {"path": "", "entries": []})
            path = os.path.abspath(os.path.expanduser(path))
            entries = apps.list_dir(path)
            if entries is None:
                return self._send(200, {"path": path, "entries": [], "error": "Папка недоступна."})
            return self._send(200, {"path": path, "parent": os.path.dirname(path), "entries": entries[:3000]})

    return Handler


def _read(path):
    try:
        with io.open(path, encoding="utf-8") as f:
            return f.read()
    except (IOError, OSError):
        return ""


def _tail(path, n):
    return _read(path).splitlines()[-n:]


# ----------------------------------------------------------------------------- запуск

def _runtime_file():
    return os.path.join(paths.runtime_dir(), "schoolshield-center.json")


def find_running():
    """Уже запущенный Центр этого пользователя: {port, token} или None."""
    try:
        with io.open(_runtime_file(), encoding="utf-8") as f:
            info = json.load(f)
        import urllib.request
        req = urllib.request.Request("http://127.0.0.1:%d/api/ping" % info["port"], headers={"X-Token": info["token"]})
        with urllib.request.urlopen(req, timeout=1.5) as r:
            if r.status == 200:
                return info
    except Exception:
        pass
    return None


def _bind(port):
    center = Center(port)
    return ThreadingHTTPServer(("127.0.0.1", port), make_handler(center)), center


def start_server(port=DEFAULT_PORT):
    """Запускает сервер в потоке. Возвращает (center, httpd)."""
    try:
        httpd, center = _bind(port)
    except OSError:
        probe = socket.socket()
        probe.bind(("127.0.0.1", 0))
        free = probe.getsockname()[1]
        probe.close()
        httpd, center = _bind(free)
    httpd.daemon_threads = True
    threading.Thread(target=httpd.serve_forever, name="center-http", daemon=True).start()
    try:
        os.makedirs(paths.runtime_dir(), exist_ok=True)
        fd = os.open(_runtime_file(), os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
        with os.fdopen(fd, "w") as f:
            json.dump({"port": center.port, "token": center.token, "pid": os.getpid()}, f)
    except OSError:
        pass
    return center, httpd
