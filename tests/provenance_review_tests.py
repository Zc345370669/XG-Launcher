"""Evidence consistency tests; these do not prove copyright or commercial rights."""
import hashlib,json,unittest
from pathlib import Path
R=Path(__file__).resolve().parents[1]
class ProvenanceTests(unittest.TestCase):
 def test_rewritten_java_sources_match_record(self):
  d=json.loads((R/'licensing/JAVA-PROVENANCE.json').read_text())
  self.assertTrue(d['business_logic_modified_in_review'])
  for x in d['files']:self.assertEqual(hashlib.sha256((R/x['path']).read_bytes()).hexdigest(),x['sha256'])
 def test_missing_original_not_falsely_cleared(self):
  d=json.loads((R/'licensing/JAVA-PROVENANCE.json').read_text())
  self.assertFalse(d['original_example_available']);self.assertFalse(d['historical_rights_resolved'])
  self.assertEqual(d['status'],'native-reimplementation-with-qualified-provenance')
 def test_complete_lucide_and_feather_text(self):
  b=(R/'licenses/Lucide-LICENSE.txt').read_bytes();s=b.decode()
  for t in ['ISC License','Lucide Icons and Contributors','The MIT License (MIT)','2013-present Cole Bemis','icons listed above','IN NO EVENT','copies or substantial portions']:self.assertIn(t,s)
  d=json.loads((R/'licensing/ICON-PROVENANCE.json').read_text())['official_license']
  self.assertEqual(len(b),d['bytes']);self.assertEqual(hashlib.sha256(b).hexdigest(),d['sha256']);self.assertIn(d['commit'],d['source'])
 def test_current_official_icon_origin(self):
  d=json.loads((R/'licensing/ICON-PROVENANCE.json').read_text())
  self.assertTrue(d['actual_icon_version_confirmed']);self.assertTrue(d['upstream_source_of_each_geometry_confirmed'])
  for x in d['reviewed_files']:self.assertEqual(hashlib.sha256((R/x['path']).read_bytes()).hexdigest(),x['sha256'])
 def test_project_mit_does_not_relicense_third_party(self):
  d=json.loads((R/'licensing/status.json').read_text());self.assertFalse(d['entire_repository_relicensed'])
  self.assertEqual(d['active_application_license'],'MIT');self.assertEqual(d['publication_clearance'],'project_owned_mit_with_documented_third_party_obligations')
 def test_notice_visible_and_resource_included(self):
  self.assertIn('Lucide-LICENSE.txt',(R/'THIRD-PARTY-NOTICES.md').read_text())
  self.assertIn('../../licenses/*',(R/'src/XGLauncher/XGLauncher.csproj').read_text())
  self.assertIn('旧 Java 示例',(R/'licensing/PROVENANCE-REVIEW.md').read_text())
if __name__=='__main__':unittest.main()
