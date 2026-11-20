"""Historical grants and current MIT scope; no third-party relicense inference."""
import hashlib,json,unittest
from pathlib import Path
R=Path(__file__).resolve().parents[1];S=json.loads((R/'licensing/status.json').read_text());T=(R/'licensing/XGCL-1.0.txt').read_text()
class ScopeTests(unittest.TestCase):
 def test_current_project_mit(self):self.assertEqual(S['active_application_license'],'MIT');self.assertEqual(S['requested_license'],'MIT');self.assertFalse(S['entire_repository_relicensed'])
 def test_historical_gpl_unchanged(self):self.assertEqual(hashlib.sha256((R/'licensing/history/LICENSE-GPL-3.0.txt').read_bytes()).hexdigest(),'3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986')
 def test_legacy_terms_not_erased(self):self.assertIn('免费或收费的 Minecraft 整合包',T);self.assertIn('历史 GPL 副本仍按 GPL',T)
 def test_third_party_and_old_grants_not_relicensed(self):self.assertFalse(S['third_party_relicensed']);self.assertFalse(S['past_gpl_grants_revoked'])
 def test_former_target_withdrawn(self):self.assertTrue(S['former_target_withdrawn']);self.assertTrue(S['requested_license_is_osi_approved'])
 def test_resource_matches_current_license(self):self.assertIn('XGLauncher-MIT.txt',(R/'src/XGLauncher/XGLauncher.csproj').read_text());self.assertIn('<PackageLicenseExpression>MIT',(R/'Directory.Build.props').read_text())
 def test_readme_intro_and_current_license(self):
  t=(R/'README.md').read_text()
  for p in ['趁 ChatGPT 订阅到期前，让 AI 写着玩的 Minecraft Java 版启动器。','v0.1.0-alpha','标准 MIT License','第三方并不因此成为 MIT']:self.assertIn(p,t)
 def test_release_provides_native_source_and_notices(self):
  self.assertIn('Copy-Item third-party',(R/'build-windows.ps1').read_text());self.assertIn('third-party',(R/'scripts/publish-portable-windows.sh').read_text())
if __name__=='__main__':unittest.main()
