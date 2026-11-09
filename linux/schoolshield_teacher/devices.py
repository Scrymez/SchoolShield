"""Устройства для Центра учителя: флешки и диски, яркость, проектор, окна настроек. Вызовы стандартных утилит Linux Mint, без shell."""

import json
import os
import re
import shutil
import subprocess


def _run(cmd, timeout=15):
    try:
        r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace"), r.stderr.decode("utf-8", "replace")
    except (OSError, subprocess.SubprocessError):
        return 127, "", ""


# ----------------------------------------------------------------------------- флешки и диски

def parse_lsblk(text):
    """Разбор `lsblk -J -b`: плоский список разделов с файловой системой (флешки, внешние диски, DVD)."""
    try:
        tree = json.loads(text)
    except ValueError:
        return []
    out = []

    def walk(node, parent):
        kids = node.get("children") or []
        mounts = node.get("mountpoints") or ([node.get("mountpoint")] if node.get("mountpoint") else [])
        mounts = [m for m in mounts if m]
        fs = node.get("fstype")
        removable = bool(node.get("rm")) or bool(node.get("hotplug")) or (parent or {}).get("tran") == "usb" or node.get("tran") == "usb"
        if fs and node.get("type") in ("part", "disk", "rom", "loop") and not (fs in ("swap", "crypto_LUKS", "LVM2_member")):
            out.append({
                "device": node.get("path") or "/dev/" + str(node.get("name")),
                "name": node.get("name"),
                "label": node.get("label") or "",
                "size": int(node.get("size") or 0),
                "fstype": fs,
                "mountpoint": mounts[0] if mounts else None,
                "removable": removable,
                "disk": (parent or {}).get("path") or node.get("path"),
                "model": ((parent or {}).get("model") or node.get("model") or "").strip(),
            })
        for k in kids:
            walk(k, node if node.get("type") == "disk" else parent)

    for dev in tree.get("blockdevices", []):
        walk(dev, None)
    return out


def _lsblk():
    for cols in ("NAME,PATH,LABEL,SIZE,FSTYPE,MOUNTPOINTS,RM,HOTPLUG,TRAN,TYPE,MODEL", "NAME,PATH,LABEL,SIZE,FSTYPE,MOUNTPOINT,RM,HOTPLUG,TRAN,TYPE,MODEL"):
        code, out, _ = _run(["lsblk", "-J", "-b", "-o", cols])
        if code == 0 and out.strip():
            return out
    return ""


def drives():
    """Флешки и внешние диски (подключённые и ещё нет). Системные диски не показываются здесь: они есть в «Местах»."""
    res = []
    for p in parse_lsblk(_lsblk()):
        if not p["removable"]:
            continue
        free = total = None
        if p["mountpoint"]:
            try:
                u = shutil.disk_usage(p["mountpoint"])
                free, total = u.free, u.total
            except OSError:
                pass
        title = p["label"] or p["model"] or os.path.basename(p["device"])
        res.append({"device": p["device"], "disk": p["disk"], "title": title, "path": p["mountpoint"], "mounted": bool(p["mountpoint"]),
                    "free": free, "total": total or p["size"], "fstype": p["fstype"]})
    return res


def mount(device):
    """Подключает флешку (udisksctl). Возвращает (папка или None, сообщение)."""
    if not re.match(r"^/dev/[A-Za-z0-9_\-/]+$", device or ""):
        return None, "Недопустимое устройство."
    code, out, err = _run(["udisksctl", "mount", "-b", device, "--no-user-interaction"], 40)
    m = re.search(r" at (.+?)\.?\s*$", out.strip())
    if code == 0 and m:
        return m.group(1), None
    return None, (err or out or "Не удалось подключить флешку.").strip()


def eject(device, disk=None):
    """Безопасное извлечение: отключить разделы, затем выключить питание диска."""
    if not re.match(r"^/dev/[A-Za-z0-9_\-/]+$", device or ""):
        return False, "Недопустимое устройство."
    errors = []
    code, out, err = _run(["udisksctl", "unmount", "-b", device, "--no-user-interaction"], 60)
    if code != 0 and "not mounted" not in (err + out).lower():
        # запасной путь: gio
        code2, out2, err2 = _run(["gio", "mount", "-u", device], 60)
        if code2 != 0:
            errors.append((err or out or err2 or out2).strip())
            return False, "Не удалось извлечь. Закройте файлы с флешки (Writer, проигрыватель) и попробуйте ещё раз."
    if disk and re.match(r"^/dev/[A-Za-z0-9_\-/]+$", disk):
        _run(["udisksctl", "power-off", "-b", disk, "--no-user-interaction"], 30)
    return True, "Флешку можно вынимать."


# ----------------------------------------------------------------------------- яркость

def parse_brightnessctl(text):
    """`brightnessctl -m info` -> процент. Строка: имя,класс,текущая,процент%,максимум."""
    for line in text.splitlines():
        parts = line.split(",")
        if len(parts) >= 5:
            m = re.match(r"(\d+)%", parts[3])
            if m:
                return int(m.group(1))
    return None


def _sysfs_backlight():
    for d in sorted(os.listdir("/sys/class/backlight")) if os.path.isdir("/sys/class/backlight") else []:
        base = os.path.join("/sys/class/backlight", d)
        try:
            cur = int(open(os.path.join(base, "brightness")).read().strip())
            mx = int(open(os.path.join(base, "max_brightness")).read().strip())
            if mx > 0:
                return base, cur, mx
        except (OSError, ValueError):
            continue
    return None


def get_brightness():
    """Яркость 0..100 или None (у обычного монитора регулятора нет)."""
    if shutil.which("brightnessctl"):
        code, out, _ = _run(["brightnessctl", "-m", "info"])
        v = parse_brightnessctl(out) if code == 0 else None
        if v is not None:
            return v
    info = _sysfs_backlight()
    if info:
        return int(round(info[1] * 100.0 / info[2]))
    return None


def set_brightness(percent):
    percent = max(5, min(100, int(percent)))
    if shutil.which("brightnessctl"):
        code, _, _ = _run(["brightnessctl", "set", "%d%%" % percent])
        if code == 0:
            return True
    # без brightnessctl запись в sysfs обычно запрещена; пробуем и сообщаем
    info = _sysfs_backlight()
    if info:
        try:
            with open(os.path.join(info[0], "brightness"), "w") as f:
                f.write(str(int(info[2] * percent / 100.0)))
            return True
        except OSError:
            return False
    return False


# ----------------------------------------------------------------------------- проектор (xrandr)

_OUT_RE = re.compile(r"^(\S+) (connected|disconnected)(?: primary)?(?: (\d+)x(\d+)\+(\d+)\+(\d+))?")
_MODE_RE = re.compile(r"^\s+(\d+)x(\d+)")


def parse_xrandr(text):
    """`xrandr --query` -> [{name, connected, internal, modes:[(w,h)...], active}]"""
    outs = []
    cur = None
    for line in text.splitlines():
        m = _OUT_RE.match(line)
        if m:
            cur = {"name": m.group(1), "connected": m.group(2) == "connected", "modes": [], "active": m.group(3) is not None,
                   "internal": bool(re.match(r"(eDP|LVDS|DSI)", m.group(1)))}
            outs.append(cur)
            continue
        m = _MODE_RE.match(line)
        if m and cur is not None:
            mode = (int(m.group(1)), int(m.group(2)))
            if mode not in cur["modes"]:
                cur["modes"].append(mode)
    return [o for o in outs if o["connected"]]


def plan_display(outputs, mode):
    """Команды xrandr для режима: internal (только экран ноутбука), external (только проектор), clone (повторить), extend (два экрана)."""
    if len(outputs) < 2:
        return None, "Проектор не найден. Подключите его кабелем и подождите несколько секунд."
    internal = next((o for o in outputs if o["internal"]), outputs[0])
    others = [o for o in outputs if o is not internal]
    ext = others[0]
    cmds = []
    if mode == "internal":
        cmds.append(["xrandr", "--output", internal["name"], "--auto", "--primary"] + sum([["--output", o["name"], "--off"] for o in others], []))
    elif mode == "external":
        cmds.append(["xrandr", "--output", ext["name"], "--auto", "--primary", "--output", internal["name"], "--off"])
    elif mode == "extend":
        cmds.append(["xrandr", "--output", internal["name"], "--auto", "--primary", "--output", ext["name"], "--auto", "--right-of", internal["name"]])
    elif mode == "clone":
        common = [m for m in internal["modes"] if m in ext["modes"]]
        if common:
            w, h = max(common, key=lambda m: m[0] * m[1])
            cmds.append(["xrandr", "--output", internal["name"], "--mode", "%dx%d" % (w, h), "--primary", "--output", ext["name"], "--mode", "%dx%d" % (w, h),
                         "--same-as", internal["name"]])
        else:
            cmds.append(["xrandr", "--output", internal["name"], "--auto", "--primary", "--output", ext["name"], "--auto", "--same-as", internal["name"]])
    else:
        return None, "Неизвестный режим."
    return cmds, None


def display_status():
    if not shutil.which("xrandr"):
        return {"available": False, "outputs": 0}
    code, out, _ = _run(["xrandr", "--query"])
    outs = parse_xrandr(out) if code == 0 else []
    return {"available": bool(outs), "outputs": len(outs), "names": [o["name"] for o in outs]}


def set_display(mode):
    if not shutil.which("xrandr"):
        return False, "Не найдена программа xrandr."
    code, out, _ = _run(["xrandr", "--query"])
    cmds, error = plan_display(parse_xrandr(out) if code == 0 else [], mode)
    if error:
        return False, error
    for c in cmds:
        code, _, err = _run(c, 20)
        if code != 0:
            return False, "Не получилось: " + (err.strip().splitlines() or ["ошибка xrandr"])[-1]
    return True, "Готово."


# ----------------------------------------------------------------------------- окна настроек

SETTINGS = {
    "sound": [["cinnamon-settings", "sound"], ["pavucontrol"]],
    "display": [["cinnamon-settings", "display"], ["arandr"]],
    "network": [["cinnamon-settings", "network"], ["nm-connection-editor"]],
    "printers": [["system-config-printer"], ["cinnamon-settings", "printers"]],
    "bluetooth": [["blueman-manager"], ["cinnamon-settings", "bluetooth"]],
    "datetime": [["cinnamon-settings", "calendar"]],
    "mouse": [["cinnamon-settings", "mouse"]],
    "keyboard": [["cinnamon-settings", "keyboard"]],
    "all": [["cinnamon-settings"]],
    "software": [["mintinstall"]],
    "updates": [["mintupdate"]],
    "monitor": [["gnome-system-monitor"], ["mate-system-monitor"]],
}


def open_settings(name):
    """Открывает окно настроек по короткому имени (только из списка выше). Возвращает True, если что-то запустилось."""
    for cmd in SETTINGS.get(name, []):
        if shutil.which(cmd[0]):
            try:
                subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
                return True
            except OSError:
                continue
    return False


def internet_state():
    """full | limited | portal | none | unknown (NetworkManager сам проверяет, пускает ли сеть в интернет)."""
    code, out, _ = _run(["nmcli", "networking", "connectivity", "check"], 8)
    return out.strip() if code == 0 and out.strip() else "unknown"
