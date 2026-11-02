import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
REPO_CONFIG = os.path.normpath(os.path.join(HERE, "..", "..", "config"))
os.environ["SCHOOLSHIELD_SHARE"] = REPO_CONFIG

from schoolshield_teacher import textguard, lists, policies, config  # noqa: E402


class TextGuardTests(unittest.TestCase):
    def setUp(self):
        self.words = lists.load_badwords(REPO_CONFIG)

    def bad(self, text):
        return lists.is_bad_text(self.words, text)

    def test_porn_markers(self):
        for t in ["Free porn videos - Google Chrome", "pornhub", "ХЕНТАЙ аниме", "XXX", "Порно видео"]:
            self.assertTrue(self.bad(t), t)

    def test_obfuscation(self):
        for t in ["p o r n o", "p.o.r.n", "p0rn", "пoрнo", "п о р н о", "x v i d e o s"]:
            self.assertTrue(self.bad(t), t)

    def test_teacher_words_do_not_close_lessons(self):
        for t in ["Sex education for teachers", "Урок биологии: размножение и секс", "Nude descending a staircase - Duchamp",
                  "Обнажённая маха Гойя - Википедия", "Эротика в литературе XIX века", "Scunthorpe", "Essex County"]:
            self.assertFalse(self.bad(t), t)

    def test_collapse(self):
        self.assertEqual(textguard.collapse("p o r n o"), "porno")
        self.assertEqual(textguard.collapse("a b"), "a b")


class ListsTests(unittest.TestCase):
    def setUp(self):
        self.bl = lists.load_blocklist(REPO_CONFIG)

    def test_big_adult_list_stops_before_student_only_part(self):
        d = self.bl.concrete_domains()
        self.assertGreater(len(d), 700)
        self.assertIn("pornhub.com", d)
        self.assertNotIn("proxysite.com", d)
        self.assertEqual(len(d), len(set(d)))
        self.assertTrue(all("*" not in x and "/" not in x for x in d))

    def test_hosts(self):
        self.assertTrue(self.bl.is_blocked_host("www.pornhub.com"))
        self.assertTrue(self.bl.is_blocked_host("de.m.pornhub.com"))
        self.assertFalse(self.bl.is_blocked_host("ru.wikipedia.org"))
        self.assertFalse(self.bl.is_blocked_host("com"))

    def test_wildcard_mirror(self):
        bl = lists.Blocklist()
        bl.add("youporn.*")
        self.assertTrue(bl.is_blocked_host("www.youporn.xyz"))
        self.assertTrue(bl.is_blocked_host("youporn.co.uk"))
        self.assertFalse(bl.is_blocked_host("notyouporn.xyz"))


class PolicyTests(unittest.TestCase):
    def test_chromium_limit_and_flags(self):
        domains = ["d%d.com" % i for i in range(2000)]
        p = policies.chromium_policy(domains, edge=True)
        self.assertEqual(len(p["URLBlocklist"]), policies.POLICY_LIMIT)
        self.assertTrue(p["ForceGoogleSafeSearch"])
        self.assertEqual(p["ForceBingSafeSearch"], 2)
        self.assertNotIn("ForceBingSafeSearch", policies.chromium_policy(domains))

    def test_firefox_merge_keeps_foreign_policies(self):
        existing = {"policies": {"DisableTelemetry": True, "WebsiteFilter": {"Block": ["*://*.example.org/*"]}}}
        doc = policies.firefox_merge(existing, ["a.com", "b.com/path"])
        self.assertTrue(doc["policies"]["DisableTelemetry"])
        blocks = doc["policies"]["WebsiteFilter"]["Block"]
        self.assertIn("*://*.example.org/*", blocks)
        self.assertIn("*://*.a.com/*", blocks)
        self.assertEqual(len([b for b in blocks if "b.com" in b]), 0)
        self.assertTrue(policies.is_ours(doc))

    def test_resolved_and_dispatcher(self):
        for prov in config.DNS_PROVIDERS:
            r = policies.resolved_dropin(prov)
            self.assertIn("DNS=", r)
            self.assertIn("DNSOverTLS=opportunistic", r)
            d = policies.dispatcher_script(prov)
            self.assertTrue(d.startswith("#!/bin/sh"))
            self.assertIn(config.DNS_PROVIDERS[prov]["v4"][0], d)
            self.assertIn("tun*", d)

    def test_hosts_block_roundtrip(self):
        original = "127.0.0.1 localhost\n127.0.1.1 mint\n"
        block = policies.hosts_block(["a.com", "b.org", "x.*", "c.com/p"])
        once = policies.replace_hosts_block(original, block)
        twice = policies.replace_hosts_block(once, block)
        self.assertEqual(once, twice)
        self.assertIn("0.0.0.0 www.a.com", once)
        self.assertNotIn("x.*", once)
        self.assertNotIn("c.com/p", once)
        self.assertEqual(policies.replace_hosts_block(once, None), original)


class ApplyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        os.environ["SCHOOLSHIELD_ROOT"] = self.tmp
        # модуль paths читает переменную при импорте: перезагружаем
        import importlib
        from schoolshield_teacher import paths, apply as apply_mod
        importlib.reload(paths)
        importlib.reload(apply_mod)
        self.paths, self.apply = paths, apply_mod
        os.makedirs(os.path.join(self.tmp, "etc"))
        with open(os.path.join(self.tmp, "etc", "hosts"), "w") as f:
            f.write("127.0.0.1 localhost\n")
        os.makedirs(os.path.join(self.tmp, "usr/lib/firefox/distribution"))
        with open(os.path.join(self.tmp, "usr/lib/firefox/distribution/policies.json"), "w") as f:
            json.dump({"policies": {"DisableTelemetry": True}}, f)
        self.log = []

    def tearDown(self):
        del os.environ["SCHOOLSHIELD_ROOT"]
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_apply_then_remove_restores_everything(self):
        self.assertTrue(self.apply.apply_all(self.log.append))
        st = self.apply.status()
        self.assertTrue(st["applied"])
        self.assertTrue(st["hosts"])
        self.assertTrue(st["dns_configured"])
        self.assertGreaterEqual(len(st["browsers_chromium"]), 5)
        self.assertIn("/usr/lib/firefox/distribution", st["browsers_firefox"])
        ff = json.load(open(os.path.join(self.tmp, "usr/lib/firefox/distribution/policies.json")))
        self.assertTrue(ff["policies"]["DisableTelemetry"])
        self.assertGreater(len(ff["policies"]["WebsiteFilter"]["Block"]), 500)

        # повторное применение ничего не копит
        self.assertTrue(self.apply.apply_all(self.log.append))
        hosts = open(os.path.join(self.tmp, "etc", "hosts")).read()
        self.assertEqual(hosts.count("# >>> SchoolShield"), 1)
        ff2 = json.load(open(os.path.join(self.tmp, "usr/lib/firefox/distribution/policies.json")))
        self.assertEqual(len(ff2["policies"]["WebsiteFilter"]["Block"]), len(ff["policies"]["WebsiteFilter"]["Block"]))

        self.assertTrue(self.apply.remove_all(self.log.append))
        self.assertEqual(open(os.path.join(self.tmp, "etc", "hosts")).read(), "127.0.0.1 localhost\n")
        ff3 = json.load(open(os.path.join(self.tmp, "usr/lib/firefox/distribution/policies.json")))
        self.assertEqual(ff3, {"policies": {"DisableTelemetry": True}})
        self.assertFalse(self.apply.status()["applied"])
        self.assertFalse(self.apply.status()["dns_configured"])

    def test_extra_lists(self):
        sites = os.path.join(self.tmp, "sites.txt")
        words = os.path.join(self.tmp, "words.txt")
        with open(sites, "w") as f:
            f.write("# мои\nmy-bad-site.example\n")
        with open(words, "w") as f:
            f.write("\\bмоёслово\\b\n")
        self.assertTrue(self.apply.set_lists(sites, words, self.log.append))
        hosts = open(os.path.join(self.tmp, "etc", "hosts")).read()
        self.assertIn("0.0.0.0 my-bad-site.example", hosts)
        bad = lists.load_badwords(REPO_CONFIG, self.paths.extra_words_path())
        self.assertTrue(lists.is_bad_text(bad, "МоёСлово здесь"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
