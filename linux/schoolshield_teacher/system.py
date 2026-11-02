"""Система: громкость, Wi-Fi, батарея, питание. Только вызовы стандартных утилит Linux Mint, без оболочки (shell)."""

import glob
import os
import re
import subprocess


def _out(cmd, timeout=10):
    try:
        r = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    except (OSError, subprocess.SubprocessError):
        return 127, ""


# ----------------------------------------------------------------------------- громкость

def get_volume():
    """Громкость 0..100 и состояние «без звука» (pactl работает и с PulseAudio, и с PipeWire)."""
    code, out = _out(["pactl", "get-sink-volume", "@DEFAULT_SINK@"])
    vol = None
    if code == 0:
        m = re.search(r"(\d+)%", out)
        if m:
            vol = int(m.group(1))
    code, out = _out(["pactl", "get-sink-mute", "@DEFAULT_SINK@"])
    muted = code == 0 and "yes" in out.lower()
    return {"volume": vol, "muted": muted, "available": vol is not None}


def set_volume(percent):
    percent = max(0, min(100, int(percent)))
    code, _ = _out(["pactl", "set-sink-volume", "@DEFAULT_SINK@", "%d%%" % percent])
    return code == 0


def set_mute(muted):
    code, _ = _out(["pactl", "set-sink-mute", "@DEFAULT_SINK@", "1" if muted else "0"])
    return code == 0


# ----------------------------------------------------------------------------- Wi-Fi

def _split_nmcli(line):
    """Разбор строки `nmcli -t`: поля разделены ':', экранированные '\\:' остаются двоеточием."""
    parts, cur, i = [], "", 0
    while i < len(line):
        c = line[i]
        if c == "\\" and i + 1 < len(line):
            cur += line[i + 1]
            i += 2
            continue
        if c == ":":
            parts.append(cur)
            cur = ""
        else:
            cur += c
        i += 1
    parts.append(cur)
    return parts


def parse_wifi_list(output):
    nets = {}
    for line in output.splitlines():
        f = _split_nmcli(line)
        if len(f) < 4 or not f[1]:
            continue
        in_use, ssid, signal, security = f[0] == "*", f[1], f[2], f[3]
        try:
            sig = int(signal)
        except ValueError:
            sig = 0
        cur = nets.get(ssid)
        if cur is None or sig > cur["signal"] or in_use:
            nets[ssid] = {"ssid": ssid, "signal": sig, "secure": security not in ("", "--"), "active": in_use or (cur or {}).get("active", False)}
    return sorted(nets.values(), key=lambda n: (not n["active"], -n["signal"]))


def wifi_list():
    code, out = _out(["nmcli", "-t", "-f", "IN-USE,SSID,SIGNAL,SECURITY", "device", "wifi", "list"], 20)
    return parse_wifi_list(out) if code == 0 else []


def wifi_connect(ssid, password=None):
    if not ssid or ssid.startswith("-"):
        return False, "Недопустимое имя сети."
    cmd = ["nmcli", "device", "wifi", "connect", ssid]
    if password:
        cmd += ["password", password]
    code, out = _out(cmd, 60)
    return code == 0, out.strip()


def wifi_enabled():
    code, out = _out(["nmcli", "radio", "wifi"])
    return code == 0 and "enabled" in out


# ----------------------------------------------------------------------------- батарея и сеанс

def battery():
    for cap in glob.glob("/sys/class/power_supply/BAT*/capacity"):
        try:
            level = int(open(cap).read().strip())
            status = open(os.path.join(os.path.dirname(cap), "status")).read().strip()
            return {"present": True, "percent": level, "charging": status in ("Charging", "Full")}
        except (OSError, ValueError):
            continue
    return {"present": False}


def power(action):
    """poweroff | reboot | logout | lock"""
    if action == "poweroff":
        cmd = ["systemctl", "poweroff"]
    elif action == "reboot":
        cmd = ["systemctl", "reboot"]
    elif action == "lock":
        cmd = ["cinnamon-screensaver-command", "--lock"]
    elif action == "logout":
        cmd = ["cinnamon-session-quit", "--logout", "--no-prompt"]
    else:
        return False
    code, _ = _out(cmd, 15)
    if code == 127 and action == "logout":
        code, _ = _out(["loginctl", "terminate-session", os.environ.get("XDG_SESSION_ID", "")], 15)
    return code == 0


def uname():
    try:
        return os.uname().nodename
    except AttributeError:
        return ""
