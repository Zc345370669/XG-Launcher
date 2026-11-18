import hashlib,json,unittest
from pathlib import Path
R=Path(__file__).resolve().parents[1]
class FinalAuditTests(unittest.TestCase):
 def test_mit_decision_qualified_scope(self):
  d=json.loads((R/'licensing/FINAL-REVIEW.json').read_text());self.assertEqual(d['decision'],'A');self.assertEqual(d['application_license'],'MIT');self.assertFalse(d['entire_repository_relicensed']);self.assertEqual(d['blocking_items'],[]);self.assertFalse(d['third_party_auto_authorized_by_declaration'])
 def test_gpl_history_remains(self):self.assertEqual(hashlib.sha256((R/'licensing/history/LICENSE-GPL-3.0.txt').read_bytes()).hexdigest(),'3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986')
 def test_complete_icon_notices(self):
  t=(R/'licenses/Lucide-LICENSE.txt').read_text()
  for x in ['ISC License','2013-present Cole Bemis','The MIT License (MIT)']:self.assertIn(x,t)
 def test_logo_qualified_source_retained(self):
  for x in json.loads((R/'licenses/ASSET-PROVENANCE.json').read_text()):self.assertEqual(x['rights_status'],'maintainer-declared-ai-origin-not-independently-verified')
if __name__=='__main__':unittest.main()
