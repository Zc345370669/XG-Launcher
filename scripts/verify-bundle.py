"""Verify the .NET 6+ compressed apphost bundle against the latest publish assemblies."""
import hashlib
import io
from pathlib import Path
import struct
import sys
import zlib

exe, launcher, core = map(Path, sys.argv[1:])
data = exe.read_bytes()
pe = struct.unpack_from('<I', data, 0x3c)[0]
assert data[pe:pe + 4] == b'PE\0\0'
assert struct.unpack_from('<H', data, pe + 4)[0] == 0x8664
signature = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
pos = data.index(signature)
offset = struct.unpack_from('<q', data, pos - 8)[0]
stream = io.BytesIO(data)
stream.seek(offset)
def read(fmt):
    return struct.unpack(fmt, stream.read(struct.calcsize(fmt)))
def string():
    n = shift = 0
    while True:
        b = stream.read(1)[0]
        n |= (b & 127) << shift
        if b < 128:
            break
        shift += 7
    return stream.read(n).decode('utf-8')
major, minor, count = read('<iii')
assert major >= 6
bundle = string()
read('<qqqqQ')
entries = {}
for _ in range(count):
    start, size, compressed = read('<qqq')
    kind, = read('<B')
    name = string()
    entries[name] = (start, size, compressed, kind)
for assembly in (launcher, core):
    start, size, compressed, kind = entries[assembly.name]
    payload = data[start:start + (compressed or size)]
    if compressed:
        payload = zlib.decompress(payload, -15)
    assert len(payload) == size
    assert payload == assembly.read_bytes(), 'Bundled assembly differs: ' + assembly.name
    print('PASS bundled latest assembly', assembly.name, hashlib.sha256(payload).hexdigest())
print('PASS Windows x64 PE, .NET bundle version', major, minor, 'entries', count, 'bundle ID', bundle)
print('EXE SHA256', hashlib.sha256(data).hexdigest())
