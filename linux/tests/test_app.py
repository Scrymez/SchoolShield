import json
import os
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
os.environ["SCHOOLSHIELD_SHARE"] = os.path.normpath(os.path.join(HERE, "..", "..", "config"))

from schoolshield_teacher import apps, guard, system, lists, updater, textguard  # noqa: E402

DESKTOP = """[Desktop Entry]
Type=Application
Name=Writer
Name[ru]=Текстовый процессор
GenericName=Word Processor
Exec=libreoffice --writer %U
Icon=libreoffice-writer
Categories=Office;WordProcessor;
"""


class AppsTests(unittest.TestCase):
    def test_parse_and_classify(self):
        with tempfile.TemporaryDirectory() as d:
            for name, body in (("writer.desktop", DESKTOP),
                               ("hidden.desktop", "[Desktop Entry]\nType=Application\nName=Hidden\nExec=x\nNoDisplay=true\n"),
                               ("wine-np.desktop", "[Desktop Entry]\nType=Application\nName=Notepad++\nExec=env WINEPREFIX=/x wine notepad++.exe\nCategories=Development;\n"),
                               ("schoolshield-teacher.desktop", "[Desktop Entry]\nType=Application\nName=self\nExec=x\n"),
                               ("game.desktop", "[Desktop Entry]\nType=Application\nName=Game\nExec=game\nCategories=Game;\n")):
                with open(os.path.join(d, name), "w", encoding="utf-8") as f:
                    f.write(body)
            found = {a["id"]: a for a in apps.load_apps([d])}
            self.assertIn("writer", found)
            self.assertEqual(found["writer"]["category"], "office")
            self.assertNotIn("hidden", found)
            self.assertNotIn("schoolshield-teacher", found)
            self.assertTrue(found["wine-np"]["windows"])
            self.assertEqual(found["wine-np"]["category"], "study")
            self.assertEqual(found["game"]["category"], "other")      # игры не скрываются: ограничений нет

    def test_strip_exec(self):
        self.assertEqual(apps.strip_exec("libreoffice --writer %U"), ["libreoffice", "--writer"])
        self.assertEqual(apps.strip_exec('sh -c "echo 100%% done" %f'), ["sh", "-c", "echo 100% done"])

    def test_kind_of(self):
        self.assertEqual(apps.kind_of("/a/План.DOCX"), "docs")
        self.assertEqual(apps.kind_of("b.xlsx"), "sheets")
        self.assertEqual(apps.kind_of("c.pptx"), "slides")
        self.assertIsNone(apps.kind_of("d.exe"))

    def test_scan_and_list(self):
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "sub", ".hidden"))
            for n in ("a.docx", "sub/b.pdf", "sub/.hidden/c.docx", "x.bin"):
                with open(os.path.join(d, n), "w") as f:
                    f.write("x")
            res = apps.scan_files(["docs", "pdf"], roots=[d])
            self.assertEqual(sorted(r["name"] for r in res), ["a.docx", "b.pdf"])
            entries = apps.list_dir(d)
            self.assertEqual([e["name"] for e in entries][0], "sub")      # папки сначала
            self.assertIsNone(apps.list_dir(os.path.join(d, "nope")))


class GuardTests(unittest.TestCase):
    OUT = ("0x04600003  0 1111  host Free porn videos - Mozilla Firefox\n"
           "0x04800007  0 2222  host Урок: квадратные уравнения - LibreOffice Writer\n"
           "0x05000001 -1 3333  host Sex education for teachers\n"
           "0x05200001  1 4444  host p o r n o\n")

    def test_parse(self):
        wins = guard.parse_wmctrl(self.OUT)
        self.assertEqual(len(wins), 4)
        self.assertEqual(wins[0], ("0x04600003", 0, 1111, "Free porn videos - Mozilla Firefox"))
        self.assertEqual(wins[2][1], -1)

    def test_closes_only_adult_windows(self):
        calls = []
        now = [1000.0]

        def runner(cmd):
            calls.append(cmd)
            return (0, self.OUT) if cmd[0] == "wmctrl" and cmd[1] == "-lp" else (0, "")

        cfg = {"guard": {"enabled": True, "notify": False}}
        g = guard.Guard(cfg=cfg, patterns=lists.load_badwords(os.environ["SCHOOLSHIELD_SHARE"]), runner=runner, clock=lambda: now[0])
        killed = []
        g._kill = lambda pid, title: killed.append(pid)
        g.log = lambda m: None
        acted = g.tick()
        self.assertEqual(sorted(acted), ["0x04600003", "0x05200001"])
        closed = [c[3] for c in calls if c[:3] == ["wmctrl", "-i", "-c"]]
        self.assertEqual(sorted(closed), ["0x04600003", "0x05200001"])
        self.assertEqual(killed, [])
        now[0] += 3          # окна не закрылись: убиваем процесс
        g.tick()
        self.assertEqual(sorted(killed), [1111, 4444])


class SystemTests(unittest.TestCase):
    def test_wifi_parse(self):
        out = ("*:Школа\\:Учителя:88:WPA2\n :Школа\\:Учителя:60:WPA2\n :Кафе:42:\n :Hidden:70:WPA2\n::50:WPA2\n :Кафе:30:--\n")
        nets = system.parse_wifi_list(out)
        self.assertEqual(nets[0]["ssid"], "Школа:Учителя")
        self.assertTrue(nets[0]["active"])
        self.assertEqual([n["ssid"] for n in nets], ["Школа:Учителя", "Hidden", "Кафе"])
        self.assertFalse([n for n in nets if n["ssid"] == "Кафе"][0]["secure"])

    def test_wifi_connect_rejects_option_injection(self):
        ok, _ = system.wifi_connect("--help")
        self.assertFalse(ok)


class UpdaterTests(unittest.TestCase):
    def test_versions(self):
        self.assertTrue(updater.version_tuple("2.10.0") > updater.version_tuple("2.9.9"))
        self.assertFalse(updater.DEB_RE.match("../../etc/passwd"))

    def test_bad_signature_rejected(self):
        self.assertFalse(updater.verify(b"data", b"not a signature"))


class ServerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        from schoolshield_teacher import server, demo
        demo.install()
        cls.center, cls.httpd = server.start_server(0 or 47999)

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()

    def get(self, path, token=True, host=None, method="GET", body=None, origin=None):
        url = "http://127.0.0.1:%d%s" % (self.center.port, path)
        headers = {}
        if token:
            headers["X-Token"] = self.center.token
        if host:
            headers["Host"] = host
        if origin:
            headers["Origin"] = origin
        data = None
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=5) as r:
                return r.status, r.read()
        except urllib.error.HTTPError as e:
            return e.code, e.read()

    def test_requires_token_and_host(self):
        self.assertEqual(self.get("/api/state", token=False)[0], 403)
        self.assertEqual(self.get("/api/state", host="evil.example:80")[0], 403)
        self.assertEqual(self.get("/api/state")[0], 200)

    def test_page_gets_token(self):
        code, body = self.get("/?t=" + self.center.token, token=False)
        self.assertEqual(code, 200)
        self.assertIn(self.center.token.encode(), body)
        self.assertNotIn(b"__TOKEN__", body)

    def test_apps_and_docs(self):
        code, body = self.get("/api/apps")
        apps_list = json.loads(body)
        self.assertGreater(len(apps_list), 20)
        self.assertTrue(any(a["windows"] for a in apps_list))
        code, body = self.get("/api/docs?kinds=docs,pdf")
        self.assertTrue(all(d["kind"] in ("docs", "pdf") for d in json.loads(body)))
        code, body = self.get("/api/docs?kinds=exe")
        self.assertEqual(json.loads(body), [])

    def test_post_rules(self):
        self.assertEqual(self.get("/api/launch", method="POST", body={"id": "firefox"}, origin="http://evil.example")[0], 403)
        code, body = self.get("/api/launch", method="POST", body={"id": "firefox"})
        self.assertTrue(json.loads(body)["ok"])
        code, body = self.get("/api/launch", method="POST", body={"id": "no-such-app"})
        self.assertFalse(json.loads(body)["ok"])
        code, body = self.get("/api/open-url", method="POST", body={"url": "file:///etc/passwd"})
        self.assertFalse(json.loads(body)["ok"])
        code, body = self.get("/api/open-url", method="POST", body={"url": "https://ru.wikipedia.org"})
        self.assertTrue(json.loads(body)["ok"])

    def test_icon_and_protection(self):
        code, body = self.get("/icon/firefox")
        self.assertEqual(code, 200)
        self.assertIn(b"<svg", body)
        self.assertEqual(self.get("/icon/does-not-exist")[0], 404)
        code, body = self.get("/api/protection")
        st = json.loads(body)
        self.assertTrue(st["applied"])
        self.assertIn("dns_choices", st)

    def test_files_listing(self):
        with tempfile.TemporaryDirectory() as d:
            open(os.path.join(d, "a.txt"), "w").close()
            code, body = self.get("/api/files?path=" + urllib.request.quote(d))
            self.assertEqual([e["name"] for e in json.loads(body)["entries"]], ["a.txt"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
