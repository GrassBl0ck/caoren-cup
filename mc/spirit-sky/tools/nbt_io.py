"""Small typed NBT writer for offline, pre-26.1 Anvil worlds. Standard library only."""
import struct

def byte(v): return (1, v)
def integer(v): return (3, v)
def long(v): return (4, v)
def double(v): return (6, v)
def string(v): return (8, v)
def list_of(kind, values): return (9, (kind, values))
def compound(v): return (10, v)
def longs(v): return (12, v)

def utf(s):
    data = s.encode('utf-8')
    return struct.pack('>H', len(data)) + data

def payload(kind, v):
    if kind in (1, 2, 3, 4, 5, 6):
        return struct.pack({1:'>b', 2:'>h', 3:'>i', 4:'>q', 5:'>f', 6:'>d'}[kind], v)
    if kind == 7: return struct.pack('>i', len(v)) + bytes(v)
    if kind == 8: return utf(v)
    if kind == 9:
        t, values = v
        return bytes([t]) + struct.pack('>i', len(values)) + b''.join(payload(t, x) for x in values)
    if kind == 10:
        return b''.join(bytes([t]) + utf(k) + payload(t, val) for k, (t, val) in v.items()) + b'\0'
    if kind in (11, 12):
        fmt = '>i' if kind == 11 else '>q'
        return struct.pack('>i', len(v)) + b''.join(struct.pack(fmt, x) for x in v)
    raise ValueError(kind)

def encode(root): return b'\x0a\x00\x00' + payload(10, root)

def packed(values, bits):
    per = 64 // bits
    out = []
    for start in range(0, len(values), per):
        n = sum(v << (i * bits) for i, v in enumerate(values[start:start + per]))
        out.append(n if n < 2**63 else n - 2**64)
    return out
