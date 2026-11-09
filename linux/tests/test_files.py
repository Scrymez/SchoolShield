import json
import os
import shutil
import sys
import tempfile
import threading
import time
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from schoolshield_teacher import devices, fileops  # noqa: E402


def write(path, text="x"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write(text)


class FileOpsTests(unittest.TestCase):
    def setUp(self):
        self.root = os.path.realpath(tempfile.mkdtemp(prefix="ss-fo-"))
        self.a = os.path.join(self.root, "a")
        self.b = os.path.join(self.root, "b")
        write(os.path.join(self.a, "doc.txt"), "hello")
        write(os.path.join(self.a, "sub", "x.txt"), "x")
        write(os.path.join(self.a, "sub", "deep", "y.txt"), "yy")
        os.makedirs(self.b)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def run_job(self, sources, target, move=False):
        job = fileops.Job(sources, target, move)
        job.run()
        return job

    def test_unique_names(self):
        self.assertEqual(fileops.unique_path(self.a, "new.txt"), os.path.join(self.a, "new.txt"))
        self.assertEqual(fileops.unique_path(self.a, "doc.txt"), os.path.join(self.a, "doc (2).txt"))
        self.assertEqual(fileops.unique_path(self.a, "sub"), os.path.join(self.a, "sub (2)"))
        write(os.path.join(self.a, "doc (2).txt"))
        self.assertEqual(fileops.unique_path(self.a, "doc.txt"), os.path.join(self.a, "doc (3).txt"))

    def test_copy_keeps_source_and_never_overwrites(self):
        j = self.run_job([os.path.join(self.a, "doc.txt"), os.path.join(self.a, "sub")], self.b)
        self.assertTrue(j.finished)
        self.assertEqual(j.errors, [])
        self.assertEqual(j.done, 2)
        self.assertEqual(open(os.path.join(self.b, "doc.txt")).read(), "hello")
        self.assertTrue(os.path.exists(os.path.join(self.b, "sub", "deep", "y.txt")))
        self.assertTrue(os.path.exists(os.path.join(self.a, "doc.txt")))
        self.run_job([os.path.join(self.a, "doc.txt")], self.b)
        self.assertTrue(os.path.exists(os.path.join(self.b, "doc (2).txt")))
        self.assertEqual(open(os.path.join(self.b, "doc.txt")).read(), "hello")
        self.assertEqual(j.percent, 100)

    def test_move_same_disk_removes_source(self):
        c = os.path.join(self.root, "c")
        os.makedirs(c)
        j = self.run_job([os.path.join(self.a, "sub")], c, move=True)
        self.assertEqual(j.errors, [])
        self.assertFalse(os.path.exists(os.path.join(self.a, "sub")))
        self.assertTrue(os.path.exists(os.path.join(c, "sub", "deep", "y.txt")))

    def test_move_into_own_folder_is_a_noop(self):
        j = self.run_job([os.path.join(self.a, "doc.txt")], self.a, move=True)
        self.assertEqual(j.errors, [])
        self.assertTrue(os.path.exists(os.path.join(self.a, "doc.txt")))
        self.assertFalse(os.path.exists(os.path.join(self.a, "doc (2).txt")))

    def test_folder_into_itself_refused(self):
        j = self.run_job([self.a], os.path.join(self.a, "sub"))
        self.assertEqual(len(j.errors), 1)
        self.assertIn("внутрь самой себя", j.errors[0])

    def test_missing_source_reported_others_done(self):
        j = self.run_job([os.path.join(self.a, "nope.txt"), os.path.join(self.a, "doc.txt")], self.b)
        self.assertEqual(len(j.errors), 1)
        self.assertEqual(j.done, 1)

    def test_cancel_leaves_no_half_file(self):
        big = os.path.join(self.a, "big.bin")
        with open(big, "wb") as f:
            f.write(b"\0" * (80 << 20))
        job = fileops.Job([big], self.b, False)
        t = threading.Thread(target=job.run)
        t.start()
        deadline = time.time() + 10
        while job.done_bytes < (4 << 20) and not job.finished and time.time() < deadline:
            time.sleep(0.002)
        job.cancelled = True
        t.join()
        self.assertFalse(os.path.exists(os.path.join(self.b, "big.bin")))
        self.assertTrue(os.path.exists(big))

    def test_jobs_registry(self):
        jobs = fileops.Jobs()
        job = jobs.start([os.path.join(self.a, "doc.txt")], self.b, False)
        for _ in range(200):
            if job.finished:
                break
            time.sleep(0.01)
        self.assertTrue(jobs.get(job.id).snapshot()["finished"])
        self.assertIsNone(jobs.get("nope"))

    def test_names_and_protection(self):
        self.assertIsNone(fileops.valid_name("a/b"))
        self.assertIsNone(fileops.valid_name(".."))
        self.assertEqual(fileops.valid_name("  Отчёт 5А.docx "), "Отчёт 5А.docx")
        self.assertTrue(fileops.is_protected("/"))
        self.assertTrue(fileops.is_protected(os.path.expanduser("~")))
        self.assertFalse(fileops.is_protected(self.a))
        path, err = fileops.rename(os.path.join(self.a, "doc.txt"), "renamed.txt")
        self.assertIsNone(err)
        self.assertTrue(os.path.exists(path))
        _, err = fileops.rename(path, "sub")
        self.assertIsNotNone(err)
        new, err = fileops.make_folder(self.a, "Новая папка")
        self.assertIsNone(err)
        new2, _ = fileops.make_folder(self.a, "Новая папка")
        self.assertNotEqual(new, new2)

    def test_trash_never_deletes_forever_without_consent(self):
        target = os.path.join(self.a, "keep.txt")
        write(target)
        # no gio / trash-put on the test machine: the file must stay
        import unittest.mock as mock
        with mock.patch("shutil.which", return_value=None):
            ok, errors = fileops.to_trash([target])
        self.assertEqual(ok, 0)
        self.assertTrue(errors)
        self.assertTrue(os.path.exists(target))
        ok, errors = fileops.to_trash([target], permanent=True)
        self.assertEqual((ok, errors), (1, []))
        self.assertFalse(os.path.exists(target))
        ok, errors = fileops.to_trash(["/"], permanent=True)
        self.assertEqual(ok, 0)


LSBLK = json.dumps({"blockdevices": [
    {"name": "sda", "path": "/dev/sda", "type": "disk", "rm": False, "hotplug": False, "tran": "sata", "model": "SSD", "size": 500107862016, "children": [
        {"name": "sda1", "path": "/dev/sda1", "type": "part", "fstype": "ext4", "label": None, "size": 400000000000, "mountpoints": ["/"], "rm": False, "hotplug": False}]},
    {"name": "sdb", "path": "/dev/sdb", "type": "disk", "rm": True, "hotplug": True, "tran": "usb", "model": "Cruzer Blade ", "size": 16000000000, "children": [
        {"name": "sdb1", "path": "/dev/sdb1", "type": "part", "fstype": "vfat", "label": "УРОКИ", "size": 15990000000, "mountpoints": ["/media/user/УРОКИ"], "rm": True, "hotplug": True},
        {"name": "sdb2", "path": "/dev/sdb2", "type": "part", "fstype": "ntfs", "label": None, "size": 10000000, "mountpoints": [None], "rm": True, "hotplug": True}]},
    {"name": "sr0", "path": "/dev/sr0", "type": "rom", "fstype": None, "rm": True, "hotplug": False, "size": 0},
    {"name": "sdc", "path": "/dev/sdc", "type": "disk", "rm": False, "hotplug": False, "tran": "usb", "model": "External HDD", "size": 1000000000000, "children": [
        {"name": "sdc1", "path": "/dev/sdc1", "type": "part", "fstype": "ext4", "label": "Архив", "size": 999000000000, "mountpoints": ["/media/user/Архив"], "rm": False, "hotplug": False}]},
    {"name": "sdd", "path": "/dev/sdd", "type": "disk", "rm": False, "hotplug": False, "tran": "sata", "size": 100, "children": [
        {"name": "sdd1", "path": "/dev/sdd1", "type": "part", "fstype": "swap", "size": 100, "mountpoints": ["[SWAP]"]}]},
]})


class DeviceTests(unittest.TestCase):
    def test_lsblk_finds_flash_drives_and_external_disks(self):
        parts = devices.parse_lsblk(LSBLK)
        by = {p["device"]: p for p in parts}
        self.assertFalse(by["/dev/sda1"]["removable"])
        self.assertTrue(by["/dev/sdb1"]["removable"])
        self.assertEqual(by["/dev/sdb1"]["label"], "УРОКИ")
        self.assertEqual(by["/dev/sdb1"]["mountpoint"], "/media/user/УРОКИ")
        self.assertIsNone(by["/dev/sdb2"]["mountpoint"])
        self.assertEqual(by["/dev/sdb1"]["disk"], "/dev/sdb")
        self.assertTrue(by["/dev/sdc1"]["removable"])          # a USB hard disk counts as removable (tran=usb)
        self.assertNotIn("/dev/sdd1", by)                      # swap is not a drive for the teacher
        self.assertNotIn("/dev/sr0", by)                       # an empty drive has no file system
        self.assertEqual(devices.parse_lsblk("not json"), [])

    def test_brightnessctl(self):
        self.assertEqual(devices.parse_brightnessctl("intel_backlight,backlight,4800,50%,9600\n"), 50)
        self.assertIsNone(devices.parse_brightnessctl("garbage"))

    XRANDR = """Screen 0: minimum 320 x 200, current 1920 x 1080, maximum 16384 x 16384
eDP-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 344mm x 193mm
   1920x1080     60.01*+  59.97
   1600x900      60.00
   1280x720      60.00
HDMI-1 connected (normal left inverted right x axis y axis)
   1920x1080     60.00 +  50.00
   1280x720      60.00
   1024x768      60.00
DP-1 disconnected (normal left inverted right x axis y axis)
"""

    def test_xrandr_modes(self):
        outs = devices.parse_xrandr(self.XRANDR)
        self.assertEqual([o["name"] for o in outs], ["eDP-1", "HDMI-1"])
        self.assertTrue(outs[0]["internal"] and not outs[1]["internal"])
        self.assertIn((1024, 768), outs[1]["modes"])
        cmds, err = devices.plan_display(outs, "clone")
        self.assertIsNone(err)
        self.assertIn("1920x1080", cmds[0])
        self.assertIn("--same-as", cmds[0])
        cmds, _ = devices.plan_display(outs, "extend")
        self.assertIn("--right-of", cmds[0])
        cmds, _ = devices.plan_display(outs, "external")
        self.assertEqual(cmds[0][2], "HDMI-1")
        self.assertIn("--off", cmds[0])
        cmds, _ = devices.plan_display(outs, "internal")
        self.assertIn("HDMI-1", cmds[0])
        self.assertIn("--off", cmds[0])
        self.assertIsNone(devices.plan_display(outs, "nonsense")[0])
        self.assertIsNone(devices.plan_display(outs[:1], "clone")[0])      # no projector connected

    def test_settings_names_are_a_fixed_list(self):
        self.assertFalse(devices.open_settings("rm -rf /"))
        self.assertIn("sound", devices.SETTINGS)


if __name__ == "__main__":
    unittest.main()
