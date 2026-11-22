"""Build an offline redistributable notices/source archive from reviewed repository files only."""
from pathlib import Path
import sys,zipfile
root=Path(__file__).resolve().parent.parent
out=Path(sys.argv[1]).resolve();out.mkdir(parents=True,exist_ok=True)
files=[root/name for name in ['LICENSE','LICENSE.md','NOTICE.md','THIRD-PARTY-NOTICES.md','THIRD_PARTY_NOTICES.md','AI-DEVELOPMENT.md','DISCLAIMER.md','TRADEMARKS.md','PUBLICATION_STATUS.md','README.md','RELEASE_NOTES.md']]
for folder in ['licenses','licensing','third-party','docs']:
 files.extend(p for p in (root/folder).rglob('*') if p.is_file())
with zipfile.ZipFile(out/'Compliance.zip','w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
 for path in sorted(files):archive.write(path,path.relative_to(root))
print('Embedded compliance files:',len(files))
