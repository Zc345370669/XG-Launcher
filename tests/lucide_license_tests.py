import hashlib,json,tarfile,unittest,xml.etree.ElementTree as E
from pathlib import Path
R=Path(__file__).resolve().parents[1]
class LucideTests(unittest.TestCase):
 def test_pinned_official_resource_hashes(self):
  d=json.loads((R/'licensing/LUCIDE-ASSETS.json').read_text());self.assertEqual(len(d['assets']),37)
  for x in d['assets']:self.assertIn(d['commit'],x['source']);self.assertEqual(hashlib.sha256((R/x['path']).read_bytes()).hexdigest(),x['sha256'])
 def test_official_transparent_outline(self):
  for p in(R/'src/XGLauncher/Assets/Lucide').glob('*.svg'):
   e=E.fromstring(p.read_bytes());self.assertEqual(e.get('viewBox'),'0 0 24 24');self.assertEqual(e.get('fill'),'none');self.assertEqual(e.get('stroke'),'currentColor');self.assertEqual(e.get('stroke-width'),'2')
 def test_no_old_manual_geometry_outside_renderer(self):
  for p in(R/'src/XGLauncher').glob('*.cs'):
   if p.name!='LucideIcon.cs':self.assertNotIn('Geometry.Parse(',p.read_text());self.assertNotIn('string[] Symbols',p.read_text())
 def test_full_lucide_and_feather_notice(self):
  t=(R/'licenses/Lucide-LICENSE.txt').read_text();self.assertIn('ISC License',t);self.assertIn('The MIT License (MIT)',t);self.assertIn('2013-present Cole Bemis',t)
 def test_corresponding_native_source_archive_safe_and_complete(self):
  with tarfile.open(R/'third-party/native-sources/libgifcodec-d06d2a6d.tar.gz')as t:
   self.assertEqual(len(t.getmembers()),9);self.assertIn('LICENSE.md',t.getnames())
   for m in t.getmembers():self.assertFalse(m.name.startswith('/'));self.assertNotIn('..',Path(m.name).parts);self.assertFalse(m.issym()or m.islnk())
 def test_global_render_and_state_style(self):
  s=(R/'src/XGLauncher/LucideIcon.cs').read_text();self.assertIn('PenLineCap.Round',s);self.assertIn('PenLineJoin.Round',s);self.assertIn('DtdProcessing.Prohibit',s)
if __name__=='__main__':unittest.main()
