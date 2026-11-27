"""Offline release checks; no real account data, network or user game files."""
import hashlib
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LICENSES = ROOT / 'licenses'

class LicenseIntegrityTests(unittest.TestCase):
    def test_original_license_documents(self):
        evidence = json.loads((LICENSES / 'LICENSE-EVIDENCE.json').read_text())
        for row in evidence:
            path = LICENSES / row['file']
            self.assertTrue(path.is_file(), row['file'])
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), row['sha256'], row['file'])

    def test_exact_locked_dependency_coverage(self):
        expected = set()
        for path in ROOT.rglob('packages*.lock.json'):
            if any(p in path.parts for p in ('bin', 'obj')):
                continue
            for graph in json.loads(path.read_text())['dependencies'].values():
                expected.update((k, v['resolved']) for k, v in graph.items() if v.get('type') != 'Project')
        rows = json.loads((LICENSES / 'LOCKED-PACKAGE-LICENSES.json').read_text())
        self.assertEqual(expected, {(r['name'], r['version']) for r in rows})
        for row in rows:
            self.assertTrue(row['hash_matches_lock'], row['name'])
            self.assertIn(row['computed_nuget_content_hash'], row['content_hashes'])
            self.assertTrue(row['documents'], row['name'])
            self.assertTrue(all((LICENSES / d).is_file() for d in row['documents']), row['name'])

    def test_optional_nsis_pinned_and_zlib(self):
        self.assertIn('v3.11', (ROOT / 'build-windows.ps1').read_text())
        workflow = (ROOT / '.github/workflows/build-windows.yml').read_text()
        self.assertNotIn('choco install nsis', workflow)
        self.assertIn('dist/win-x64/XG-Launcher/XGLauncher.exe', workflow)
        self.assertIn('if ($BuildInstaller)', (ROOT / 'build-windows.ps1').read_text())
        self.assertIn('SetCompressor /FINAL zlib', (ROOT / 'installer/XGLauncher.nsi').read_text())
        self.assertIn('SPECIAL EXCEPTION', (LICENSES / 'NSIS-COPYRIGHT.txt').read_text().upper())

    def test_gpl_preserved(self):
        self.assertEqual(hashlib.sha256((ROOT / 'licensing/history/LICENSE-GPL-3.0.txt').read_bytes()).hexdigest(),
            '3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986')
        self.assertTrue((ROOT / 'LICENSE').read_text().startswith('MIT License'))

    def test_asset_status_not_falsely_cleared(self):
        rows = json.loads((LICENSES / 'ASSET-PROVENANCE.json').read_text())
        self.assertEqual(len(rows), 3)
        for row in rows:
            self.assertEqual(row['rights_status'], 'maintainer-declared-ai-origin-not-independently-verified')
            self.assertEqual(hashlib.sha256((ROOT / row['path']).read_bytes()).hexdigest(), row['sha256'])

    def test_legacy_microsoft_not_misrepresented(self):
        rows = json.loads((LICENSES / 'LOCKED-PACKAGE-LICENSES.json').read_text())
        self.assertFalse(any(r['name'].startswith('Microsoft.Extensions.') for r in rows))
        self.assertFalse(any(LICENSES.glob('Microsoft.Extensions.*-THIRD-PARTY-NOTICES.TXT')))

if __name__ == '__main__':
    unittest.main()
