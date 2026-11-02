"""Окно Центра учителя: GTK 3 и встроенный WebKit (пакеты python3-gi и gir1.2-webkit2-4.1 или 4.0, они есть в Linux Mint).

Если их нет, вызывающий код открывает Центр в обычном браузере.
"""

import os
import subprocess


def available():
    try:
        import gi    # noqa: F401
        return True
    except ImportError:
        return False


def run(url, title="Центр учителя", icon_name="schoolshield-teacher"):
    """Показывает окно и ждёт его закрытия. Возвращает False, если окно создать не удалось."""
    os.environ.setdefault("WEBKIT_DISABLE_COMPOSITING_MODE", "1")     # пустое окно на части видеокарт и виртуальных машин
    os.environ.setdefault("WEBKIT_DISABLE_DMABUF_RENDERER", "1")
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        try:
            gi.require_version("WebKit2", "4.1")
        except ValueError:
            gi.require_version("WebKit2", "4.0")
        from gi.repository import Gtk, WebKit2
    except (ImportError, ValueError):
        return False

    base = url.split("?", 1)[0].rstrip("/")
    win = Gtk.Window(title=title)
    win.set_default_size(1360, 860)
    win.set_position(Gtk.WindowPosition.CENTER)
    try:
        win.set_icon_name(icon_name)
    except Exception:
        pass
    view = WebKit2.WebView()
    settings = view.get_settings()
    settings.set_property("enable-developer-extras", False)
    settings.set_property("enable-javascript", True)

    def on_policy(_view, decision, dtype):
        if dtype in (WebKit2.PolicyDecisionType.NAVIGATION_ACTION, WebKit2.PolicyDecisionType.NEW_WINDOW_ACTION):
            uri = decision.get_navigation_action().get_request().get_uri()
            if not uri.startswith(base):
                decision.ignore()
                try:
                    subprocess.Popen(["xdg-open", uri], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
                except OSError:
                    pass
                return True
        return False

    view.connect("decide-policy", on_policy)
    view.connect("create", lambda *a: None)
    win.connect("destroy", Gtk.main_quit)
    win.add(view)
    view.load_uri(url)
    win.show_all()
    Gtk.main()
    return True
