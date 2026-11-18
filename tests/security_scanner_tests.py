"""Synthetic inputs only, isolated temporary directories; no real secrets."""
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('public_scanner', Path(__file__).parents[1] / 'scripts/scan-public-source.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class ScannerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def check_block(self, name, text):
        (self.root / name).write_text(text)
        result = module.scan(self.root, [])
        self.assertGreater(result['blocked'], 0)
        # Reports contain digests and locations, never matched values.
        self.assertNotIn(text, str(result))
        return result

    def test_service_token(self):
        self.check_block('sample.txt', 'gh' + 'p_' + 'A' * 32)

    def test_private_key(self):
        self.check_block('sample.txt', '-----BEGIN ' + 'PRIVATE KEY-----')

    def test_private_file(self):
        self.check_block('.env', 'fixture configuration')

    def test_private_path(self):
        self.check_block('sample.txt', '/home/' + 'fixture-person/log')

    def test_unknown_email(self):
        self.check_block('sample.txt', 'fixture-person' + '@example.invalid')

    def test_exact_review_scope(self):
        text = 'fixture-person' + '@example.invalid'
        (self.root / 'sample.txt').write_text(text)
        policy = [{'path': 'sample.txt', 'rule': 'email', 'encoding': 'utf-8',
                   'digest': hashlib.sha256(text.encode()).hexdigest(), 'reason': 'synthetic test fixture'}]
        self.assertEqual(module.scan(self.root, policy)['blocked'], 0)
        (self.root / 'sample.txt').write_text('changed-' + text)
        self.assertGreater(module.scan(self.root, policy)['blocked'], 0)

    def test_strict_private_layout(self):
        (self.root / 'bin').mkdir()
        (self.root / 'bin/generated.txt').write_text('fixture')
        self.assertEqual(module.scan(self.root, [], strict=False)['blocked'], 0)
        self.assertGreater(module.scan(self.root, [], strict=True)['blocked'], 0)

if __name__ == '__main__':
    unittest.main()
