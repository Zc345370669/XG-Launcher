"""Embed the original compliance material without rewriting upstream bytes."""
from pathlib import Path
import sys
import zipfile

root = Path(__file__).resolve().parents[1]
out = Path(sys.argv[1]).resolve()
out.mkdir(parents=True, exist_ok=True)
notices = ['LICENSE', 'LICENSE.md', 'NOTICE.md', 'THIRD-PARTY-NOTICES.md',
           'THIRD_PARTY_NOTICES.md', 'AI-DEVELOPMENT.md', 'DISCLAIMER.md',
           'TRADEMARKS.md', 'PUBLICATION_STATUS.md', 'README.md', 'RELEASE_NOTES.md']
files = [root / name for name in notices]
for directory in ['licenses', 'licensing', 'third-party', 'docs']:
    files.extend(p for p in (root / directory).rglob('*') if p.is_file())
with zipfile.ZipFile(out / 'Compliance.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(files):
        if any(part in {'bin', 'obj', '.git', '__pycache__'} for part in path.relative_to(root).parts):
            continue
        if path.is_symlink():
            raise ValueError('Compliance source must not be a link')
        archive.write(path, path.relative_to(root).as_posix())
print('Prepared embedded compliance archive')
