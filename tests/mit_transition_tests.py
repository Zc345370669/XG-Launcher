"""Effective standard MIT and explicit third-party obligations, not universal IP warranty."""
import hashlib,json,unittest
from pathlib import Path
R=Path(__file__).resolve().parents[1]
class MitTests(unittest.TestCase):
 def test_standard_unmodified_grant(self):
  text=(R/'LICENSE').read_text();body=(R/'licenses/MicroCom.Runtime-LICENSE.txt').read_text();body=body[body.index('Permission is hereby granted'):]
  self.assertEqual(text,'MIT License\n\nCopyright (c) 2026 XG Launcher\n\n'+body)
 def test_manifest_self_consistent(self):
  d=json.loads((R/'licensing/APPLICATION-RELICENSING-CHECKLIST.json').read_text());self.assertEqual(d['decision'],'A');self.assertEqual(hashlib.sha256(json.dumps(d['rows'],sort_keys=True,separators=(',',':')).encode()).hexdigest(),d['manifest_sha256'])
  for x in d['rows']:self.assertEqual(hashlib.sha256((R/x['path']).read_bytes()).hexdigest(),x['sha256'])
 def test_current_mit_identity(self):
  d=json.loads((R/'licensing/status.json').read_text());self.assertEqual(d['active_application_license'],'MIT');self.assertFalse(d['personal_legal_identity_asserted']);self.assertFalse(d['past_gpl_grants_revoked']);self.assertNotIn('existing_document_license_carryover',d);self.assertIn('additionally MIT',d['historical_document_grants'])
 def test_branding_not_extra_code_restrictions(self):
  s=(R/'TRADEMARKS.md').read_text();self.assertIn('不能用本文件取消这些权利',s);self.assertIn('闭源再分发',s)
 def test_native_source_available(self):
  p=R/'third-party/native-sources/libgifcodec-d06d2a6d.tar.gz';self.assertEqual(hashlib.sha256(p.read_bytes()).hexdigest(),'73a1cf8711f4d753c62e977b4282c2c37b29a7c1eae05dfc2099daa7e98b7517');self.assertIn('MOZILLA PUBLIC LICENSE',(R/'licenses/MPL-1.1.txt').read_text())
 def test_java_no_old_example_gate(self):
  d=json.loads((R/'licensing/JAVA-PROVENANCE.json').read_text());self.assertFalse(d['current_module_old_python_reference_required']);self.assertTrue(d['maintainer_confirms_current_module_not_old_python'])
  for x in d['files']:self.assertEqual(hashlib.sha256((R/x['path']).read_bytes()).hexdigest(),x['sha256'])
if __name__=='__main__':unittest.main()
