import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / 'tools/ci/repository-safety.py'
spec = importlib.util.spec_from_file_location('android_inventory', SOURCE)
inventory_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inventory_module)


class AndroidNativeInventoryTests(unittest.TestCase):
    def candidate(self, manifest=True, tampered=False):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        name = 'mobile/android/app/src/main/res/mipmap-hdpi/ic_launcher.png'
        content = b'\x89PNG\r\n\x1a\n\x80approved-test-image'
        path = root / name
        path.parent.mkdir(parents=True)
        path.write_bytes(content + (b'tampered' if tampered else b''))
        if manifest:
            (root / 'mobile/native-source-checksums.json').write_text(json.dumps({'files': {name: hashlib.sha256(content).hexdigest()}}))
        return temp, root

    def test_exact_reviewed_native_image_is_allowed(self):
        temp, root = self.candidate()
        with temp: self.assertEqual([], inventory_module.inventory(root)['errors'])

    def test_native_image_needs_reviewed_hash(self):
        for manifest, tampered in [(False, False), (True, True)]:
            temp, root = self.candidate(manifest, tampered)
            with temp: self.assertTrue(any('unapproved binary' in e for e in inventory_module.inventory(root)['errors']))

    def test_arbitrary_jar_remains_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / 'unreviewed.jar').write_bytes(b'PK\x03\x04\x80')
            self.assertTrue(any('unapproved binary' in e for e in inventory_module.inventory(root)['errors']))
