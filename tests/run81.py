#!/usr/bin/env python3
"""Run a compiled ZX81 .p on a Z80 emulator and print the screen.
usage: run81.py file.p [--keys "frame:char,frame:char,..."] [--frames N] [--break F] [--snap f1,f2]
                       [--rom zx81.rom | --stub] [--expect saved.txt]
--expect compares the screen, printer output and events with a saved run (tests/expected).
Keys: char is a ZX81 key char (A-Z 0-9 space), NL = newline, RUB = rubout. Each press lasts 5 frames.

ROM: with the real ZX81 ROM (--rom file, or tests/zx81.rom, or $ZX81_ROM) the ROM's own
floating-point calculator, STR$, VAL etc. run for real. Some ROM entry points are caught
instead of run: KEYBOARD/DECODE (keys come from --keys), FAST/SLOW, the printer
(COPY-BUFF and COPY: lines are shown as printer output), SAVE, LOAD and NEW (reported),
and RST 08h errors (reported as the ZX81 report code). Without a ROM a small stub is used,
which is enough for programs that don't use floating point, VAL, LPRINT or tape.
"""
import sys, os, z80

CHARS = " ??????????\"£$:?()><=+-*/;,.0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"
BLOCK = {0: ' ', 1: '▘', 2: '▝', 3: '▀', 4: '▖', 5: '▌', 6: '▞', 7: '▛', 8: '▒'}
INVBLOCK = {0x80: '█', 0x81: '▟', 0x82: '▙', 0x83: '▄', 0x84: '▜', 0x85: '▐', 0x86: '▚', 0x87: '▗'}

def zxcode(ch):
    if ch == 'NL': return 0x76
    if ch == 'RUB': return 0x77
    if ch == 'SPACE': return 0
    if ch == 'MINUS': return 0x16
    return CHARS.index(ch, 1) if ch != ' ' else 0

def show(code):
    if code in BLOCK: return BLOCK[code]
    if code in INVBLOCK: return INVBLOCK[code]
    if code < 0x40: return CHARS[code]
    if 0x80 <= code < 0xC0:
        c = CHARS[code - 0x80]
        return c.lower() if c.isalpha() else '~'
    return '#'

def dump(mem, title):
    dfile = mem[0x400C] | mem[0x400D] << 8
    print('--', title)
    for r in range(24):
        base = dfile + 1 + r * 33
        print('|' + ''.join(show(mem[base + c]) for c in range(32)) + '|')

REPORTS = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'

def find_rom(path=None):
    for p in (path, os.environ.get('ZX81_ROM'), os.path.join(os.path.dirname(os.path.abspath(__file__)), 'zx81.rom')):
        if p and os.path.exists(p): return open(p, 'rb').read()
    return None

def run(pfile, keys=(), frames=3000, quiet=False, breakframe=None, snaps=(), rom=None, stub=False, expect=None):
    data = open(pfile, 'rb').read()
    m = z80.Z80Machine()
    mem = m.memory
    realrom = None if stub else find_rom(rom)
    romimg = bytearray(0x4000)
    if realrom:
        romimg[0:0x2000] = realrom[:0x2000]; romimg[0x2000:0x4000] = realrom[:0x2000]
    def put(addr, bs):
        romimg[addr:addr+len(bs)] = bytes(bs)
    # the keyboard and FAST/SLOW are always replaced (keys come from --keys)
    put(0x02BB, [0x2A, 0x00, 0x3F, 0xC9])       # KEYBOARD: LD HL,(3F00h) ; RET
    put(0x07BD, [0x21, 0x02, 0x3F, 0x37, 0xC9]) # DECODE: LD HL,3F02h ; SCF ; RET
    put(0x0F23, [0xC9]); put(0x0F2B, [0xC9])    # FAST / SLOW
    put(0x3FF8, [0x18, 0xFE])                   # JR $ : "returned to BASIC"
    if realrom:
        put(0x02EC, [0x00])                     # SET-FAST: no HALT (no display interrupts here)
    m.set_memory_block(0, bytes(romimg))
    m.set_memory_block(0x3F00, b'\xff\xff\x00')
    m.set_memory_block(0x4009, data)
    def w16(a, v): mem[a] = v & 255; mem[a + 1] = (v >> 8) & 255
    def r16(a): return mem[a] | mem[a + 1] << 8
    w16(0x4004, 0x8000)                         # RAMTOP (not in the .p)
    romcopy = bytes(mem[0:0x3F00])
    m.iy = 0x4000
    w16(0x7FEE, 0x3FF8)                         # return address of RAND USR
    m.sp = 0x7FEE
    m.pc = 16514
    traps = {}
    if realrom:
        for a in (0x0008, 0x0871, 0x0869, 0x02FB, 0x0343, 0x03C3): m.set_breakpoint(a)
    printer = []; events = []
    space = [False]
    m.set_input_callback(lambda port: 0xFE if space[0] and (port >> 8) == 0x7F else 0xFF)
    keytab = {}
    for f, ch in keys:
        for k in range(f, f + 5): keytab[k] = ch
    frame = 0
    finished = False
    def ret():
        m.pc = r16(m.sp); m.sp += 2
    def zxname(a):
        s = ''
        while True:
            c = mem[a]; s += CHARS[c & 0x3F] if (c & 0x3F) < 64 else '?'; a += 1
            if c & 0x80 or len(s) > 64: return s
    while frame < frames and not finished:
        if frame in snaps: dump(mem, 'frame %d' % frame)
        ch = keytab.get(frame)
        if ch is not None:
            mem[0x3F00] = 0xFE; mem[0x3F01] = 0xFD; mem[0x3F02] = zxcode(ch)
        else:
            mem[0x3F00] = 0xFF; mem[0x3F01] = 0xFF
        space[0] = breakframe is not None and frame >= breakframe
        budget = 16000               # CPU time per frame in SLOW mode (approx)
        while budget > 0 and not finished:
            m.ticks_to_stop = budget
            m.run()
            if realrom and mem[0] != romimg[0]:
                # the ROM's skip-constants routine copies to address 0 (DE = 0) on purpose;
                # a real ZX81 ignores writes to ROM, so put the bytes back
                m.set_memory_block(0, bytes(romimg[0:5]))
            budget = m.ticks_to_stop if m.pc in (0x0008, 0x0871, 0x0869, 0x02FB, 0x0343, 0x03C3) and realrom else 0
            pc = m.pc
            if 0x3FF8 <= pc <= 0x3FF9: finished = True
            elif not realrom: pass
            elif pc == 0x0008:
                code = mem[r16(m.sp)]
                events.append('report %s' % REPORTS[code + 1]); finished = True
            elif pc == 0x0871:                    # COPY-BUFF: one printer line
                printer.append(''.join(show(mem[0x403C + i]) for i in range(32)))
                for i in range(32): mem[0x403C + i] = 0
                mem[0x405C] = 0x76; mem[0x4038] = 0xBC
                ret()
            elif pc == 0x0869:                    # COPY: the screen
                dfile = r16(0x400C)
                for r in range(22): printer.append(''.join(show(mem[dfile + 1 + r * 33 + c]) for c in range(32)))
                ret()
            elif pc == 0x02FB:
                events.append('SAVE "%s" (%d bytes)' % (zxname(m.de), r16(0x4014) - 0x4009)); ret()
            elif pc == 0x0343:
                events.append('LOAD "%s"' % ('' if m.f & 1 else zxname(m.de))); finished = True
            elif pc == 0x03C3:
                events.append('NEW'); finished = True
        if finished: break
        fr = mem[0x4034] | mem[0x4035] << 8
        fr = (fr - 1) & 0xFFFF
        mem[0x4034] = fr & 0xFF; mem[0x4035] = fr >> 8
        frame += 1
    bad = bytes(mem[0:0x3F00]) != romcopy
    dfile = mem[0x400C] | mem[0x400D] << 8
    rows = []
    ok_layout = mem[dfile] == 0x76
    for r in range(24):
        base = dfile + 1 + r * 33
        rows.append(''.join(show(mem[base + c]) for c in range(32)))
        if mem[base + 32] != 0x76: ok_layout = False
    out = ['+' + '-' * 32 + '+'] + ['|' + r + '|' for r in rows] + ['+' + '-' * 32 + '+']
    if printer: out += ['printer:'] + [':' + r + ':' for r in printer]
    out += ['event: ' + e for e in events]
    status = ' '.join(['finished' if finished else 'still running', 'after', str(frame), 'frames;',
              'ROM CORRUPTED' if bad else 'rom ok', '; display ok' if ok_layout else '; DISPLAY FILE CORRUPTED',
              '; SP=%04X' % m.sp, '' if realrom else '; (stub ROM)'])
    if expect:
        # compare the screen, printer output and events with a saved result
        want = open(expect, encoding='utf-8').read().rstrip('\n').split('\n')
        same = want == out
        if not same and not quiet:
            for k in range(max(len(want), len(out))):
                a = want[k] if k < len(want) else ''; b = out[k] if k < len(out) else ''
                if a != b: print('expected:', a); print('got:     ', b)
        status += '; matches expected' if same else '; DIFFERS FROM EXPECTED'
    if not quiet:
        if not expect: print('\n'.join(out))
        print(status)
    return rows, finished, bad, ok_layout, m

if __name__ == '__main__':
    args = sys.argv[1:]
    keys = []; frames = 3000; brk = None
    if '--keys' in args:
        i = args.index('--keys'); spec = args[i + 1]; del args[i:i + 2]
        for item in spec.split(','):
            f, ch = item.split(':', 1); keys.append((int(f), ch))
    if '--frames' in args:
        i = args.index('--frames'); frames = int(args[i + 1]); del args[i:i + 2]
    if '--break' in args:
        i = args.index('--break'); brk = int(args[i + 1]); del args[i:i + 2]
    snaps = ()
    if '--snap' in args:
        i = args.index('--snap'); snaps = [int(x) for x in args[i + 1].split(',')]; del args[i:i + 2]
    rom = None; stub = False
    if '--rom' in args:
        i = args.index('--rom'); rom = args[i + 1]; del args[i:i + 2]
    if '--stub' in args:
        args.remove('--stub'); stub = True
    expect = None
    if '--expect' in args:
        i = args.index('--expect'); expect = args[i + 1]; del args[i:i + 2]
    run(args[0], keys, frames, breakframe=brk, snaps=snaps, rom=rom, stub=stub, expect=expect)
