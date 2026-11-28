#!/usr/bin/env python3
"""Run a compiled ZX81 .p on a Z80 emulator and print the screen.
usage: run81.py file.p [--basic] [--keys "frame:char,frame:char,..."] [--frames N] [--break F] [--snap f1,f2]
                       [--rom zx81.rom | --stub] [--expect saved.txt] [--ramtop ADDR] [--hw16 | --hw32]
--expect compares the screen, printer output and events with a saved run (tests/expected).
--ramtop ADDR (hex, e.g. C000) sets RAMTOP and puts the stack just below it, as after
POKE RAMTOP + NEW on a machine with a 32K/64K RAM pack (default 8000).
--hw16 makes the first 20000 instructions see a plain 16K ZX81: 8000h-BFFFh reads as the
ROM (writes are lost) and C000h-FFFFh is the same memory as 4000h-7FFFh. That is enough
to test the start-up RAM check of programs compiled for 32K/64K. --hw32 is a 32K pack:
RAM up to BFFFh, with C000h-FFFFh the same memory as 4000h-7FFFh.
--basic runs the program as BASIC in the ROM's own interpreter (needs the real ROM) instead
of calling the machine code at 16514, e.g. to compare a compiled program with the original.
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

def screen_rows(mem, dfile):
    """The 24 screen lines and whether the display file is sound. Handles a full display
    (24 x 32 characters + 76h) and a collapsed one (each line only as long as its text,
    as a 1K/2K ZX81 keeps it): each line runs up to its 76h, at most 32 characters."""
    rows, ok = [], mem[dfile] == 0x76
    a = dfile + 1
    for r in range(24):
        line = []
        while mem[a] != 0x76 and len(line) < 33:
            line.append(show(mem[a])); a += 1
        if len(line) > 32: ok = False; line = line[:32]
        rows.append(''.join(line).ljust(32))
        a += 1
    return rows, ok

def find_rom(path=None):
    for p in (path, os.environ.get('ZX81_ROM'), os.path.join(os.path.dirname(os.path.abspath(__file__)), 'zx81.rom')):
        if p and os.path.exists(p): return open(p, 'rb').read()
    return None

def run(pfile, keys=(), frames=3000, quiet=False, breakframe=None, snaps=(), rom=None, stub=False, expect=None,
        ramtop=0x8000, hw16=False, hw32=False, basic=False):
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
    w16(0x4004, ramtop)                         # RAMTOP (not in the .p)
    romcopy = bytes(mem[0:0x3F00])
    m.iy = 0x4000
    w16(ramtop - 0x12, 0x3FF8)                  # return address of RAND USR
    m.sp = ramtop - 0x12
    m.pc = 16514
    if basic:
        # run the program as BASIC, in the ROM's interpreter (from its first line), to
        # compare with the compiled version: enter the ROM where it starts the next line
        # (the loop at 066Ch: HL = the next line, DE = the text of this one, PPC = its number)
        mem[0x4000] = 0xFF                      # ERR_NR: no error
        mem[0x4001] = 0x80                      # FLAGS
        first = 0x407D
        mem[0x4007] = mem[first + 1]; mem[0x4008] = mem[first]
        m.de = first + 4
        m.hl = first + 4 + r16(first + 2)
        m.pc = 0x066C
    if hw16 or hw32:
        # plain 16K machine (or 32K): step one instruction at a time and fix the memory up after each
        if hw16: m.set_memory_block(0x8000, bytes(romimg))
        m.set_memory_block(0xC000, bytes(mem[0x4000:0x8000]))
        for _ in range(20000):
            lo = bytes(mem[0x4000:0x8000]); hi = bytes(mem[0xC000:0x10000])
            m.ticks_to_stop = 1; m.run()
            if hw16 and bytes(mem[0x8000:0xC000]) != bytes(romimg): m.set_memory_block(0x8000, bytes(romimg))   # ROM: writes lost
            nlo = bytes(mem[0x4000:0x8000]); nhi = bytes(mem[0xC000:0x10000])
            if nhi != hi: m.set_memory_block(0x4000, nhi)                                            # a write up there lands below
            elif nlo != lo: m.set_memory_block(0xC000, nlo)
            if 0x3FF8 <= m.pc <= 0x3FF9: break
    traps = {}
    if realrom:
        for a in (0x0008, 0x0871, 0x0869, 0x02FB, 0x0343, 0x03C3): m.set_breakpoint(a)
        if basic: m.set_breakpoint(0x06AE)     # the interpreter ran off the end of the program
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
            elif basic and pc == 0x06AE: finished = True
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
    global last_events, last_printer
    last_events = list(events); last_printer = list(printer)
    bad = bytes(mem[0:0x3F00]) != romcopy
    dfile = mem[0x400C] | mem[0x400D] << 8
    rows, ok_layout = screen_rows(mem, dfile)
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
    basic = '--basic' in args
    if basic: args.remove('--basic')
    snaps = ()
    if '--snap' in args:
        i = args.index('--snap'); snaps = [int(x) for x in args[i + 1].split(',')]; del args[i:i + 2]
    rom = None; stub = False; ramtop = 0x8000; hw16 = False
    if '--ramtop' in args:
        i = args.index('--ramtop'); ramtop = int(args[i + 1], 16); del args[i:i + 2]
    if '--hw16' in args:
        args.remove('--hw16'); hw16 = True
    hw32 = '--hw32' in args
    if hw32: args.remove('--hw32')
    if '--rom' in args:
        i = args.index('--rom'); rom = args[i + 1]; del args[i:i + 2]
    if '--stub' in args:
        args.remove('--stub'); stub = True
    expect = None
    if '--expect' in args:
        i = args.index('--expect'); expect = args[i + 1]; del args[i:i + 2]
    run(args[0], keys, frames, breakframe=brk, snaps=snaps, rom=rom, stub=stub, expect=expect, ramtop=ramtop, hw16=hw16, hw32=hw32, basic=basic)
