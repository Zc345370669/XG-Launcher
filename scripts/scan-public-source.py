"""Local, offline release gate. Reports locations/categories, never matched values.
Run from repository root: python scripts/scan-public-source.py . --report public-scan.json
Review policy is hash-scoped to exact upstream attribution and synthetic fixtures.
This is a heuristic gate, not a proof that arbitrary encrypted/obfuscated secrets cannot exist.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

RULES = {
    'private-key': rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----',
    'service-token': rb'(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{25,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30,}|sk-[A-Za-z0-9]{30,})',
    'phone': rb'(?<![A-Za-z0-9])\+[0-9]{1,3}[ .-][0-9][0-9 .()-]{6,}[0-9]',
    'jwt': rb'eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{8,}',
    'email': rb'(?i)[a-z0-9.!#$%&*+/=?^_`{|}~-]+@[a-z0-9.-]+\.[a-z]{2,}',
    'personal-path': rb'(?i)(?:[A-Z]:[\\/](?:Users|Documents and Settings)[\\/][A-Za-z0-9_. -]+|/(?:home|Users)/[A-Za-z0-9_.-]+|/workspace/scratch/[A-Za-z0-9]+)',
    'credential-literal': rb'''(?i)(?:client_secret|access_token|refresh_token|xsts_token|api_key|password|code_verifier)\s*["']?\s*[:=]\s*["'][^"'\r\n]{5,}["']''',
}
IGNORED = {'bin', 'obj', '.git', '.vs', '.idea', 'dist', 'data', 'minecraft', '.minecraft', '.xg-java-downloads', 'runtimes', 'backups', 'cache', 'logs', '__pycache__', '.pytest_cache'}
BANNED_EXTENSIONS = {'.exe', '.dll', '.pdb', '.pfx', '.p12', '.pem', '.key', '.zip', '.7z', '.bak', '.tmp', '.log', '.pyc'}

def scan(root, policy, strict=False, report_path=None):
    reviewed = {(r['path'], r['rule'], r['encoding'], r['digest']): r['reason'] for r in policy}
    findings, files = [], []
    for path in sorted(root.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(root).as_posix()
        if report_path and path.resolve() == report_path.resolve():
            continue
        ignored = any(part in IGNORED for part in path.relative_to(root).parts)
        if ignored:
            if strict:
                findings.append({'path': relative, 'rule': 'private-layout', 'status': 'blocked'})
            continue
        data = path.read_bytes()
        files.append({'path': relative, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
        if path.suffix.lower() in BANNED_EXTENSIONS or path.name.startswith('.env') and path.name != '.env.example' or any(name in path.name.lower() for name in ('microsoft-msal.bin', 'oauth-diagnostic', 'token-cache')) or path.name.lower() in ('accounts.json', 'tokens.json'):
            findings.append({'path': relative, 'rule': 'private-layout', 'status': 'blocked'})
        # Text sources plus printable UTF16 strings in resource/binary payloads.
        for encoding in ['utf-8', 'utf-16-le']:
            text = data.decode(encoding, errors='ignore')
            for rule, pattern in RULES.items():
                for match in re.finditer(pattern.decode(), text):
                    digest = hashlib.sha256(match.group().encode()).hexdigest()
                    reason = reviewed.get((relative, rule, encoding, digest))
                    findings.append({'path': relative, 'rule': rule, 'encoding': encoding,
                        'line': text.count('\n', 0, match.start()) + 1,
                        'digest': digest, 'status': 'reviewed' if reason else 'blocked',
                        'review': reason or 'requires private human review; value omitted'})
    return {'files': files, 'findings': findings, 'blocked': sum(x['status'] == 'blocked' for x in findings),
        'reviewed': sum(x['status'] == 'reviewed' for x in findings)}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=Path)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--strict-layout', action='store_true')
    args = parser.parse_args()
    policy = json.loads((args.root / 'scripts/public-scan-policy.json').read_text())
    result = scan(args.root, policy, args.strict_layout, args.report)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2))
    print(f"Files scanned: {len(result['files'])}; reviewed matches: {result['reviewed']}; blocking findings: {result['blocked']}")
    raise SystemExit(1 if result['blocked'] else 0)
