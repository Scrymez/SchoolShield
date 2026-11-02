"""schoolshield-teacher: Центр учителя и фильтр 18+ для Linux Mint.

  center [--browser]      Центр учителя (окно; --browser — во вкладке браузера)
  guard                   охрана сеанса (запускается автоматически при входе)
  apply | remove          включить / отключить фильтр 18+ (нужен root: sudo или pkexec)
  status [--json]         что сейчас включено
  set-lists SITES WORDS   сохранить «мои добавки» и применить (root)
  set-config JSON         изменить dns, ai_enabled, ai_threshold, hosts_block и применить (root)
  install-ai              установить локальный ИИ-фильтр экрана (root, нужен интернет)
  update [--install]      проверить обновление (с --install: скачать, проверить подпись, установить)
  install-deb DEB SIG     (root) проверить подпись и установить пакет
  version
"""

import io
import json
import os
import subprocess
import sys
import time

from . import VERSION, config, paths


def _need_root():
    if paths.ROOT or os.geteuid() == 0:
        return True
    sys.stderr.write("Нужны права администратора. Выполните: sudo schoolshield-teacher %s\n" % " ".join(sys.argv[1:]))
    return False


def _center(argv):
    from . import server, window
    browser = "--browser" in argv or not window.available()
    running = server.find_running()
    if running:
        url = "http://127.0.0.1:%d/?t=%s" % (running["port"], running["token"])
        subprocess.call(["wmctrl", "-a", "Центр учителя"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if browser:
            subprocess.Popen(["xdg-open", url], start_new_session=True)
        return 0
    if os.environ.get("SCHOOLSHIELD_DEMO"):
        from . import demo
        demo.install()
    center, httpd = server.start_server()
    if os.environ.get("SCHOOLSHIELD_DEMO"):
        print(center.url(), flush=True)
        try:
            while True:
                time.sleep(3600)
        except KeyboardInterrupt:
            return 0
    if not browser and window.run(center.url()):
        httpd.shutdown()
        return 0
    # Запасной путь: обычный браузер. Сервер живёт, пока вкладка присылает «пульс».
    subprocess.Popen(["xdg-open", center.url()], start_new_session=True)
    started = time.time()
    center.last_beat = time.time()
    try:
        while True:
            time.sleep(5)
            idle = time.time() - center.last_beat
            if idle > 45 and time.time() - started > 60:
                break
    except KeyboardInterrupt:
        pass
    httpd.shutdown()
    return 0


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    cmd = argv[0] if argv else "center"
    rest = argv[1:]

    if cmd in ("version", "--version", "-V"):
        print(VERSION)
        return 0
    if cmd == "center":
        return _center(rest)
    if cmd == "guard":
        from . import guard
        return guard.main()
    if cmd == "status":
        from . import apply
        st = apply.status()
        if "--json" in rest:
            print(json.dumps(st, ensure_ascii=False, indent=2))
        else:
            print("Версия %s; применено: %s" % (st["version"], "да" if st["applied"] else "нет"))
            print("Браузеры (Chromium): %s" % (", ".join(st["browsers_chromium"]) or "—"))
            print("Браузеры (Firefox): %s" % (", ".join(st["browsers_firefox"]) or "—"))
            print("/etc/hosts: %s" % ("блок есть" if st["hosts"] else "нет"))
            print("DNS %s: настроен=%s, работает=%s" % (st["dns_provider"], st["dns_configured"], st["dns_live"]))
        return 0
    if cmd == "apply":
        if not _need_root():
            return 1
        from . import apply
        return 0 if apply.apply_all() else 2
    if cmd == "remove":
        if not _need_root():
            return 1
        from . import apply
        return 0 if apply.remove_all() else 2
    if cmd == "set-lists":
        if not _need_root() or len(rest) < 2:
            return 1
        from . import apply
        return 0 if apply.set_lists(rest[0], rest[1]) else 2
    if cmd == "set-config":
        if not _need_root() or not rest:
            return 1
        return _set_config(rest[0])
    if cmd == "install-ai":
        if not _need_root():
            return 1
        from . import aiscan
        return 0 if aiscan.install_ai() else 2
    if cmd == "update":
        from . import updater
        info = updater.check()
        print(json.dumps(info, ensure_ascii=False, indent=2))
        if "--install" in rest and info.get("available"):
            deb, sig = updater.download(info, os.path.join(paths.user_state_dir(), "updates"))
            exe = sys.argv[0] if os.path.isabs(sys.argv[0]) else "/usr/bin/schoolshield-teacher"
            return subprocess.call(["pkexec", exe, "install-deb", deb, sig])
        return 0
    if cmd == "install-deb":
        if not _need_root() or len(rest) < 2:
            return 1
        from . import updater
        return 0 if updater.install_deb(rest[0], rest[1]) else 2
    sys.stderr.write(__doc__)
    return 64


def _set_config(raw):
    from . import apply
    try:
        patch = json.loads(raw)
    except ValueError:
        sys.stderr.write("Неверный JSON.\n")
        return 1
    try:
        with io.open(paths.config_path(), encoding="utf-8") as f:
            current = json.load(f)
    except (IOError, OSError, ValueError):
        current = {}
    if "dns" in patch:
        if patch["dns"] not in list(config.DNS_PROVIDERS) + ["None"]:
            sys.stderr.write("Неизвестный DNS.\n")
            return 1
        current["dns"] = patch["dns"]
    if "hosts_block" in patch:
        current["hosts_block"] = bool(patch["hosts_block"])
    ai = current.setdefault("ai", {})
    if "ai_enabled" in patch:
        ai["enabled"] = bool(patch["ai_enabled"])
    if "ai_threshold" in patch:
        try:
            ai["threshold"] = min(0.99, max(0.5, float(patch["ai_threshold"])))
        except (TypeError, ValueError):
            sys.stderr.write("Неверный порог.\n")
            return 1
    config.save(current)
    return 0 if apply.apply_all() else 2


if __name__ == "__main__":
    sys.exit(main())
