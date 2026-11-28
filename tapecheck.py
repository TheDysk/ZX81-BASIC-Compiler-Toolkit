#!/usr/bin/env python3
# Decode a .wav and a .tzx made by the toolset and check they hold name + .p bytes.
# usage: tapecheck.py file.p file.wav file.tzx NAME
import struct, wave, sys
pf = open(sys.argv[1],'rb').read()
CH = " ??????????\"£$:?()><=+-*/;,.0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
name = bytes(CH.index(c, 1) if c != ' ' else 0 for c in sys.argv[4])
name = name[:-1] + bytes([name[-1] | 0x80])
expect = name + pf
# ---- WAV: count pulses between gaps ----
w = wave.open(sys.argv[2]); n = w.getnframes(); fr = w.readframes(n); rate = w.getframerate()
s = struct.unpack('<%dh' % n, fr)
bits = []; pulses = 0; quiet = 0; prev = 0
for v in s:
    if v > 0 and prev <= 0: pulses += 1
    if v == 0: quiet += 1
    else:
        if quiet > rate * 800e-6 and pulses > 0: pass
        quiet = 0 if v != 0 else quiet
    if v == 0 and quiet == int(rate*800e-6) and pulses:
        bits.append({4:0, 9:1}.get(pulses, '?')); pulses = 0
    prev = v
out = bytes(int(''.join(map(str, bits[i:i+8])), 2) for i in range(0, len(bits)//8*8, 8))
print('WAV: %.1f s, %d bytes decoded, identical: %s' % (n/rate, len(out), out == expect))
# ---- TZX ----
t = open(sys.argv[3],'rb').read()
assert t[:8] == b'ZXTape!\x1a'
p = 10; blocks = []
while p < len(t):
    bid = t[p]; p += 1
    if bid == 0x30: l = t[p]; blocks.append(('text', t[p+1:p+1+l].decode())); p += 1 + l
    elif bid == 0x20: blocks.append(('pause', struct.unpack('<H', t[p:p+2])[0])); p += 2
    elif bid == 0x19:
        blen = struct.unpack('<I', t[p:p+4])[0]; b = t[p+4:p+4+blen]; p += 4 + blen
        pause, totp, npp, asp, totd, npd, asd = struct.unpack('<HIBBIBB', b[:14])
        q = 14 + asd * (1 + 2*npd)
        syms = [struct.unpack('<B%dH' % npd, b[14+k*(1+2*npd):14+(k+1)*(1+2*npd)]) for k in range(asd)]
        stream = b[q:]
        data = stream[: (totd+7)//8]
        blocks.append(('gdb', totd, npd, asd, [sum(1 for x in sy[1:] if x)//2 for sy in syms], data == expect, len(stream) == (totd+7)//8))
    else: print('unknown block', bid); break
print('TZX blocks:', blocks)
