#!/usr/bin/env python3
"""Make large test .p files for 32K / 64K RAM packs (BASIC programs, not compiled ones).
usage: mkbig.py base.p out.p ARRAY_ELEMENTS [--code ADDR] [--bigrem N]
base.p is a small BASIC .p (made with the toolset from a listing). This adds a saved
numeric array A(n) to its variables (5 bytes per element), so the file grows past 16K.
--code ADDR puts a tiny machine code routine (LD BC,42 / RET) at ADDR inside the array.
--bigrem N adds N bytes to the first line (a REM), pushing the display file up."""
import sys, struct
args = sys.argv[1:]
base, out, n = args[0], args[1], int(args[2])
code = int(args[args.index('--code') + 1]) if '--code' in args else None
bigrem = int(args[args.index('--bigrem') + 1]) if '--bigrem' in args else 0
d = bytearray(open(base, 'rb').read())
B = 0x4009
w = lambda a: d[a - B] | d[a - B + 1] << 8
def setw(a, v): d[a - B] = v & 255; d[a - B + 1] = v >> 8
if bigrem:
    # grow line 1 (assumed to be a REM) by bigrem bytes of 00h and move everything after it
    ln_len = d[0x407D + 2 - B] | d[0x407D + 3 - B] << 8
    at = 0x407D + 4 + ln_len - 1 - B          # before its 76h
    d[at:at] = bytes(bigrem)
    setw(0x407D + 2, ln_len + bigrem)
    for sv in (0x400C, 0x400E, 0x4010, 0x4012, 0x4014, 0x4016, 0x4018, 0x401A, 0x401C, 0x4029):
        v = w(sv)
        if v >= 0x407D + 4 + ln_len - 1: setw(sv, v + bigrem)
vars_ = w(0x4010); eline = w(0x4014)
body = bytes([1]) + struct.pack('<H', n) + bytes(5 * n)          # 1 dimension of n, then values
arr = bytearray(bytes([0x86]) + struct.pack('<H', len(body)) + body)   # 0x86 = numeric array A
# fill A(i) = i (ZX81 floats) for the first few, so the browser shows real values
def enc(v):
    if v == 0: return bytes(5)
    import math
    e = math.frexp(v)[1]; m = int(v / 2 ** e * 2 ** 32)
    return bytes([e + 128, (m >> 24) & 0x7F, (m >> 16) & 255, (m >> 8) & 255, m & 255])
for i in range(1, min(n, 10) + 1):
    o = 3 + 3 + (i - 1) * 5
    arr[o:o + 5] = enc(i)
ins = eline - 1 - B                           # before the 80h end-of-variables marker
d[ins:ins] = arr
grow = len(arr)
for sv in (0x4014, 0x4016, 0x4018, 0x401A, 0x401C):
    v = w(sv)
    if v >= eline - 1: setw(sv, v + grow)
if code:
    off = code - B
    assert vars_ - B < off < len(d) - 3, 'code address must be inside the array'
    d[off:off + 4] = bytes([0x01, 42, 0, 0xC9])
open(out, 'wb').write(d)
print('%s: %d bytes, VARS %04X, E_LINE %04X, D_FILE %04X' % (out, len(d), w(0x4010), w(0x4014), w(0x400C)))
