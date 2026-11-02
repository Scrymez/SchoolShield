"""Включение и отключение фильтра 18+. Запускается от root: через pkexec из Центра учителя, из postinst пакета или вручную с sudo.

Меняет только то, что относится к 18+: политики браузеров, семейный DNS, блок в /etc/hosts. Установка программ,
учётные записи, терминал, флешки и всё остальное не затрагиваются.
"""

import io
import json
import os
import shutil
import subprocess

from . import config, lists, paths, policies, VERSION

HOSTS = "/etc/hosts"
RESOLVED_DROPIN = "/etc/systemd/resolved.conf.d/schoolshield.conf"
DISPATCHER = "/etc/NetworkManager/dispatcher.d/90-schoolshield-dns"


def _run(cmd, log, timeout=40):
    if paths.ROOT:
        log("[песочница] " + " ".join(cmd))
        return 0
    try:
        r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode
    except (OSError, subprocess.SubprocessError) as e:
        log("! %s: %s" % (cmd[0], e))
        return 127


def _read(path):
    try:
        with io.open(path, encoding="utf-8") as f:
            return f.read()
    except (IOError, OSError):
        return None


def _write(path, text, mode=0o644):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".ss-tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        f.write(text)
    os.chmod(tmp, mode)
    os.replace(tmp, path)


def _load_managed():
    raw = _read(paths.managed_path())
    try:
        data = json.loads(raw) if raw else {}
    except ValueError:
        data = {}
    data.setdefault("files", [])
    data.setdefault("backups", {})
    return data


def _save_managed(data):
    _write(paths.managed_path(), json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True) + "\n")


def _track(data, path):
    if path not in data["files"]:
        data["files"].append(path)


def _connected_interfaces(log):
    if paths.ROOT:
        return []
    try:
        out = subprocess.run(["nmcli", "-t", "-f", "DEVICE,STATE", "device"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)
        names = []
        for line in out.stdout.decode("utf-8", "replace").splitlines():
            dev, _, state = line.partition(":")
            if state.strip() == "connected" and dev:
                names.append(dev)
        return names
    except (OSError, subprocess.SubprocessError) as e:
        log("! nmcli: %s" % e)
        return []


# ----------------------------------------------------------------------------- применить

def apply_all(log=print):
    """Возвращает True, если всё применилось (предупреждения не считаются ошибкой)."""
    cfg = config.load()
    share = paths.share_dir()
    bl = lists.load_blocklist(share, paths.extra_sites_path())
    domains = bl.concrete_domains()
    if not domains:
        log("! Не найден список сайтов (%s). Проверьте установку пакета." % os.path.join(share, "blocklist.txt"))
        return False
    log("Версия %s, закрытых сайтов 18+ в списке: %d" % (VERSION, len(domains)))
    managed = _load_managed()
    ok = True

    if cfg["browser_policies"]:
        _apply_browsers(domains, managed, log)
    if cfg["hosts_block"]:
        _apply_hosts(domains, managed, log)
    if cfg["dns"] != "None":
        ok = _apply_dns(cfg["dns"], managed, log) and ok
    else:
        log("DNS-фильтр выключен в настройках (dns = None).")

    managed["version"] = VERSION
    _save_managed(managed)
    log("Готово. Браузеры подхватят политики после перезапуска.")
    return ok


def _apply_browsers(domains, managed, log):
    n = 0
    for directory, edge in policies.CHROMIUM_POLICY_DIRS:
        path = os.path.join(paths.under_root(directory), policies.POLICY_FILE)
        _write(path, policies.dumps(policies.chromium_policy(domains, edge)))
        _track(managed, path)
        n += 1
    log("Политики Chrome / Chromium / Edge / Brave / Vivaldi: SafeSearch и %d закрытых сайтов (%d папок)." % (min(len(domains), policies.POLICY_LIMIT), n))

    for directory in policies.FIREFOX_POLICY_DIRS:
        real = paths.under_root(directory)
        if directory != policies.FIREFOX_POLICY_DIRS[0] and not os.path.isdir(os.path.dirname(real)):
            continue    # такого Firefox на этом ПК нет
        path = os.path.join(real, "policies.json")
        backup = path + ".schoolshield-backup"
        raw = _read(path)
        base = {}
        if raw:
            try:
                existing = json.loads(raw)
            except ValueError:
                existing = None
            if existing is not None and not policies.is_ours(existing):
                if not os.path.exists(backup):
                    shutil.copy2(path, backup)
                base = existing
            elif os.path.exists(backup):
                try:
                    base = json.loads(_read(backup) or "{}")
                except ValueError:
                    base = {}
        _write(path, policies.dumps(policies.firefox_merge(base, domains)))
        _track(managed, path)
        if os.path.exists(backup):
            managed["backups"][path] = backup
        log("Политики Firefox: " + path)


def _apply_hosts(domains, managed, log):
    path = paths.under_root(HOSTS)
    current = _read(path)
    if current is None:
        log("! Нет %s" % path)
        return
    backup = path + ".schoolshield-backup"
    if policies.HOSTS_BEGIN[:20] not in current and not os.path.exists(backup):
        shutil.copy2(path, backup)
    _write(path, policies.replace_hosts_block(current, policies.hosts_block(domains)))
    log("/etc/hosts: крупные сайты 18+ направлены на 0.0.0.0 (вместе с www).")


def _apply_dns(provider, managed, log):
    if provider not in config.DNS_PROVIDERS:
        log("! Неизвестный DNS-провайдер: %s" % provider)
        return False
    if not paths.ROOT and _run(["systemctl", "is-active", "--quiet", "systemd-resolved"], log) != 0:
        log("! systemd-resolved не работает: семейный DNS не применён (политики браузеров и /etc/hosts остаются).")
        return False
    _write(paths.under_root(RESOLVED_DROPIN), policies.resolved_dropin(provider))
    _track(managed, paths.under_root(RESOLVED_DROPIN))
    _write(paths.under_root(DISPATCHER), policies.dispatcher_script(provider), 0o755)
    _track(managed, paths.under_root(DISPATCHER))
    _run(["systemctl", "restart", "systemd-resolved"], log)
    for iface in _connected_interfaces(log):
        _run(["sh", DISPATCHER, iface, "up"], log)
    log("Семейный DNS-фильтр: %s (скрипт для NetworkManager заменяет DNS при каждом подключении)." % provider)
    return True


# ----------------------------------------------------------------------------- отключить

def remove_all(log=print):
    managed = _load_managed()
    for path in list(managed["files"]):
        backup = managed["backups"].get(path)
        try:
            if backup and os.path.exists(backup):
                os.replace(backup, path)
                log("Восстановлено: " + path)
            elif os.path.exists(path):
                os.remove(path)
                log("Удалено: " + path)
        except OSError as e:
            log("! %s: %s" % (path, e))

    hosts = paths.under_root(HOSTS)
    current = _read(hosts)
    if current is not None and policies.HOSTS_BEGIN[:20] in current:
        _write(hosts, policies.replace_hosts_block(current, None))
        log("/etc/hosts очищен от блока SchoolShield.")

    had_dns = any(f.endswith("schoolshield.conf") for f in managed["files"])
    for iface in _connected_interfaces(log):
        _run(["resolvectl", "revert", iface], log)
    if had_dns:
        _run(["systemctl", "restart", "systemd-resolved"], log)
        log("Прежние настройки DNS возвращены.")
    try:
        os.remove(paths.managed_path())
    except OSError:
        pass
    log("Фильтр 18+ отключён. Программа осталась на месте.")
    return True


# ----------------------------------------------------------------------------- списки администратора

def set_lists(sites_file, words_file, log=print):
    """Сохраняет «мои добавки» (сайты и слова) и сразу применяет их."""
    os.makedirs(paths.etc_dir(), exist_ok=True)
    for src, dst, what in ((sites_file, paths.extra_sites_path(), "сайты"), (words_file, paths.extra_words_path(), "слова")):
        if not src:
            continue
        text = _read(src)
        if text is None:
            log("! Не прочитан файл: " + src)
            return False
        if len(text) > 400000:
            log("! Слишком большой список (%s)." % what)
            return False
        _write(dst, text.replace("\r\n", "\n"))
        log("Сохранено (%s): %s" % (what, dst))
    return apply_all(log)


# ----------------------------------------------------------------------------- состояние (root не нужен)

def status():
    cfg = config.load()
    managed = _load_managed()
    chromium = [d for d, _ in policies.CHROMIUM_POLICY_DIRS if os.path.exists(os.path.join(paths.under_root(d), policies.POLICY_FILE))]
    firefox = []
    for d in policies.FIREFOX_POLICY_DIRS:
        raw = _read(os.path.join(paths.under_root(d), "policies.json"))
        try:
            if raw and policies.is_ours(json.loads(raw)):
                firefox.append(d)
        except ValueError:
            pass
    hosts = _read(paths.under_root(HOSTS)) or ""
    dns_file = os.path.exists(paths.under_root(RESOLVED_DROPIN))
    return {
        "version": VERSION,
        "applied": bool(managed["files"]),
        "browsers_chromium": chromium,
        "browsers_firefox": firefox,
        "hosts": policies.HOSTS_BEGIN[:20] in hosts,
        "dns_provider": cfg["dns"],
        "dns_configured": dns_file,
        "dns_live": _dns_live(cfg["dns"]) if dns_file else False,
    }


def _dns_live(provider):
    """Действительно ли системный резолвер уже использует семейный фильтр (смотрим resolvectl)."""
    if paths.ROOT or provider not in config.DNS_PROVIDERS:
        return False
    try:
        out = subprocess.run(["resolvectl", "dns"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10).stdout.decode("utf-8", "replace")
    except (OSError, subprocess.SubprocessError):
        return False
    return any(ip in out for ip in config.DNS_PROVIDERS[provider]["v4"])
