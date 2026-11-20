"""Fail the release build if the promised single EXE payload differs from publish output."""
from pathlib import Path
import sys
import zipfile

folder = Path(sys.argv[1])
if sorted(p.name for p in folder.iterdir()) != ['XGLauncher.exe']:
    raise SystemExit('Publish folder must contain only XGLauncher.exe')
exe = folder / 'XGLauncher.exe'
if exe.stat().st_size < 1024 * 1024 or exe.read_bytes()[:2] != b'MZ':
    raise SystemExit('Missing published Windows executable')
if len(sys.argv) > 2:
    with zipfile.ZipFile(sys.argv[2], 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(exe, 'XG-Launcher/XGLauncher.exe')
    with zipfile.ZipFile(sys.argv[2]) as archive:
        assert archive.namelist() == ['XG-Launcher/XGLauncher.exe']
print('PASS Windows single EXE publish layout')
