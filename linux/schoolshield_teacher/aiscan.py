"""Локальный ИИ-фильтр экрана (необязательный модуль).

Модель Yahoo open_nsfw (BSD-2-Clause) в формате ONNX и ONNX Runtime работают на процессоре, ничего не уходит в интернет.
Модуль запускается отдельным процессом из виртуального окружения /opt/schoolshield-teacher/venv (туда ставится onnxruntime),
поэтому основная программа остаётся на одной стандартной библиотеке. Установка: `sudo schoolshield-teacher install-ai`.

Обработка кадра повторяет Windows-версию: 256x256, центральный квадрат 224, каналы BGR, вычитание среднего (104, 117, 123), NHWC.
"""

import hashlib
import io
import os
import subprocess
import sys
import time

from . import config, paths, MODEL_SHA256, MODEL_URL, ONNXRUNTIME_PIN

MEAN_BGR = (104.0, 117.0, 123.0)


def sha256_file(path):
    h = hashlib.sha256()
    with io.open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def model_ok():
    p = paths.model_path()
    try:
        return os.path.isfile(p) and sha256_file(p) == MODEL_SHA256
    except OSError:
        return False


def available():
    """Установлены ли модель и окружение (проверка без импорта onnxruntime)."""
    return os.path.isfile(paths.venv_python()) and os.path.isfile(paths.model_path())


class Engine(object):
    """Оценка картинки: вероятность (0..1), что она не для детей и не для работы."""

    def __init__(self, model_path=None):
        import numpy  # noqa: F401  (проверяем сразу, чтобы ошибка была понятной)
        import onnxruntime as ort
        opts = ort.SessionOptions()
        opts.intra_op_num_threads = 1       # школьные ПК скромные: держим нагрузку низкой
        opts.inter_op_num_threads = 1
        self.session = ort.InferenceSession(model_path or paths.model_path(), opts, providers=["CPUExecutionProvider"])
        self.input_name = self.session.get_inputs()[0].name

    @staticmethod
    def preprocess(img):
        import numpy as np
        from PIL import Image
        img = img.convert("RGB").resize((256, 256), Image.BILINEAR)
        arr = np.asarray(img, dtype="float32")[16:240, 16:240, ::-1]      # центр 224x224, RGB -> BGR
        arr = arr - np.array(MEAN_BGR, dtype="float32")
        return np.ascontiguousarray(arr[None, ...])                          # NHWC

    def score(self, img):
        out = self.session.run(None, {self.input_name: self.preprocess(img)})[0]
        return float(out[0][1])    # [sfw, nsfw]


def _active_window_id():
    try:
        out = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=5).stdout.decode()
        token = out.strip().split()[-1]
        if token.startswith("0x") and int(token, 16) != 0:
            return token
    except (OSError, subprocess.SubprocessError, IndexError, ValueError):
        pass
    return None


def _close_active(log):
    wid = _active_window_id()
    if not wid:
        return
    subprocess.run(["wmctrl", "-i", "-c", wid], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
    log("ИИ: неприемлемое изображение, активное окно %s закрыто" % wid)
    subprocess.run(["notify-send", "-i", "dialog-warning", "-t", "4000", "SchoolShield", "Неприемлемое изображение. Окно закрыто."],
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)


def run_loop():
    """Тело процесса-сканера."""
    from PIL import ImageGrab
    cfg = config.load()["ai"]
    engine = Engine()
    hits = 0
    interval = max(1.0, float(cfg.get("interval", 3.0)))
    need = max(1, int(cfg.get("confirmations", 2)))
    threshold = float(cfg.get("threshold", 0.93))

    def log(msg):
        sys.stderr.write(time.strftime("%H:%M:%S ") + msg + "\n")
        sys.stderr.flush()

    log("ИИ-фильтр экрана запущен (порог %.2f)" % threshold)
    while True:
        try:
            score = engine.score(ImageGrab.grab())
            hits = hits + 1 if score >= threshold else 0
            if hits >= need:
                hits = 0
                log("ИИ: оценка %.2f" % score)
                _close_active(log)
        except Exception as e:      # нет X-сервера, закрытый экран и т.п.
            log("ИИ: пропуск кадра: %s" % e)
        time.sleep(interval)


def start_in_background(cfg, log):
    """Запускает сканер, если он включён и установлен. Возвращает Popen или None."""
    if not cfg["ai"].get("enabled"):
        return None
    if os.environ.get("XDG_SESSION_TYPE", "").lower() == "wayland":
        log("ИИ-фильтр: в сеансе Wayland снимок экрана недоступен.")
        return None
    if not available():
        log("ИИ-фильтр включён, но не установлен: выполните `sudo schoolshield-teacher install-ai`")
        return None
    pkg_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    env = dict(os.environ)
    env["PYTHONPATH"] = pkg_root + os.pathsep + env.get("PYTHONPATH", "")
    return subprocess.Popen([paths.venv_python(), "-m", "schoolshield_teacher.aiscan"], env=env,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


# ----------------------------------------------------------------------------- установка (root)

def install_ai(log=print):
    """Создаёт окружение, ставит onnxruntime и скачивает модель с проверкой контрольной суммы. Нужен интернет."""
    import urllib.request
    venv = os.path.dirname(os.path.dirname(paths.venv_python()))
    os.makedirs(os.path.dirname(venv), exist_ok=True)
    if not os.path.isfile(paths.venv_python()):
        log("Создаю окружение Python: " + venv)
        r = subprocess.run([sys.executable or "python3", "-m", "venv", "--system-site-packages", venv])
        if r.returncode != 0:
            log("! Не удалось создать окружение. Установите пакет python3-venv: sudo apt install python3-venv")
            return False
    log("Устанавливаю %s (нужен интернет)…" % ONNXRUNTIME_PIN)
    r = subprocess.run([paths.venv_python(), "-m", "pip", "install", "--disable-pip-version-check", ONNXRUNTIME_PIN])
    if r.returncode != 0:
        log("! pip не смог установить onnxruntime. Проверьте интернет.")
        return False
    missing = []
    for mod in ("numpy", "PIL"):
        if subprocess.run([paths.venv_python(), "-c", "import " + mod], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode != 0:
            missing.append(mod)
    if missing:
        log("! Не хватает: %s. Установите: sudo apt install python3-numpy python3-pil" % ", ".join(missing))
        return False
    if not model_ok():
        os.makedirs(os.path.dirname(paths.model_path()), exist_ok=True)
        tmp = paths.model_path() + ".part"
        log("Скачиваю модель (23 МБ)…")
        with urllib.request.urlopen(MODEL_URL, timeout=60) as resp, io.open(tmp, "wb") as out:
            while True:
                chunk = resp.read(1 << 20)
                if not chunk:
                    break
                out.write(chunk)
        if sha256_file(tmp) != MODEL_SHA256:
            os.remove(tmp)
            log("! Контрольная сумма модели не совпала, файл удалён.")
            return False
        os.replace(tmp, paths.model_path())
        os.chmod(paths.model_path(), 0o644)
    log("ИИ-фильтр экрана готов. Включите его в Центре учителя (Защита 18+) или в /etc/schoolshield/config.json (ai.enabled).")
    return True


if __name__ == "__main__":
    run_loop()
