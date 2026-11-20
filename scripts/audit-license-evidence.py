"""Record evidence without generating or editing license text. Run after package restore and upstream verification."""
from pathlib import Path
import hashlib,json,re
root=Path(__file__).resolve().parents[1]/'licenses'
packages=Path.home()/'.nuget/packages'; known={}
for p in packages.glob('*/*/*'):
 if p.is_file() and re.match(r'(licen[cs]e|third.party.notices|notice|copyright)(\.|$)',p.name,re.I):
  known.setdefault(hashlib.sha256(p.read_bytes()).hexdigest(),[]).append(str(p))
up={x['file']:x for x in json.loads((root/'UPSTREAM-PROVENANCE.json').read_text())}
original={x['file']:x for x in json.loads((root/'ORIGINAL-VERIFICATION.json').read_text())}
rows=[]
for p in sorted(root.iterdir()):
 if not p.is_file() or p.name=='LICENSE-EVIDENCE.json':continue
 sha=hashlib.sha256(p.read_bytes()).hexdigest();scope='windows-runtime-dependency'
 if any(s in p.name for s in ['Linux','macOS','WebAssembly']):scope='other-platform-reference'
 if any(s in p.name for s in ['NSIS','BuildServices','ILLink']):scope='build-or-installer-reference'
 if p.name.startswith('Microsoft.Extensions.'):scope='legacy-reference-not-published'
 if p.name in up:status='verified-upstream-reference';source=up[p.name]['source'];scope='on-demand-component-reference'
 elif p.name in original:status=original[p.name]['status'];source=original[p.name].get('url',original[p.name].get('repository'))
 elif sha in known:status='verified-package-original';source=known[sha][0]
 elif p.suffix=='.json' or p.name in ['PROJECT-NOTICE.md','DEPENDENCY-INVENTORY.md']:status='generated-metadata-not-license';source='project metadata';scope='metadata'
 else:status='unverified-reference';source='phase12 document; original not independently confirmed'
 rows.append({'file':p.name,'sha256':sha,'bytes':p.stat().st_size,'status':status,'scope':scope,'source':source})
(root/'LICENSE-EVIDENCE.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2))
print('Documents:',len(rows))
for x in rows:
 if 'unverified' in x['status'] or 'version-reference' in x['status']:print(x['file'],x['status'],x['scope'])
