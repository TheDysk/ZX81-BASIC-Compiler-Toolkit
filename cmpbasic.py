#!/usr/bin/env python3
"""Compare a compiled program with the same BASIC run by the ZX81 ROM's own interpreter.
usage: cmpbasic.py compiled.p basic.p [--frames N] [--keys ...]
basic.p is the listing as ordinary (interpreted) BASIC, e.g. made by ToolTest --mkp.
Both are run with the real ROM (tests/zx81.rom or $ZX81_ROM); the screens (rows 0-21:
the interpreter puts its report on the bottom lines), the printer output and any error
report must be the same. Prints one line: 'same as the ZX81's BASIC' or the differences.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run81

def main():
    args = sys.argv[1:]
    frames = 3000
    if '--frames' in args:
        i = args.index('--frames'); frames = int(args[i + 1]); del args[i:i + 2]
    keys = []
    if '--keys' in args:
        i = args.index('--keys'); spec = args[i + 1]; del args[i:i + 2]
        for item in spec.split(','):
            f, ch = item.split(':', 1); keys.append((int(f), ch))
    comp, basic = args[0], args[1]
    rows_c, fin_c, bad_c, ok_c, _ = run81.run(comp, keys, frames, quiet=True)
    ev_c, pr_c = run81.last_events, run81.last_printer
    rows_b, fin_b, bad_b, ok_b, _ = run81.run(basic, keys, frames, quiet=True, basic=True)
    ev_b, pr_b = run81.last_events, run81.last_printer
    problems = []
    for r in range(22):
        if rows_c[r] != rows_b[r]:
            problems.append('row %2d compiled: |%s|' % (r, rows_c[r]))
            problems.append('       ZX81 BASIC: |%s|' % rows_b[r])
    if pr_c != pr_b: problems.append('printer output differs: %r / %r' % (pr_c, pr_b))
    if ev_c != ev_b: problems.append('events differ: compiled %r, ZX81 BASIC %r' % (ev_c, ev_b))
    if bad_c: problems.append('the compiled program corrupted the ROM area')
    if not ok_c: problems.append('the compiled program corrupted the display file')
    if not fin_c: problems.append('the compiled program did not finish in %d frames' % frames)
    name = os.path.basename(comp)
    if problems:
        print(name + ': DIFFERENT from the ZX81\'s BASIC')
        for p in problems: print('  ' + p)
    else:
        print(name + ': same as the ZX81\'s BASIC' + (' (ending with report %s)' % ev_c[0].split()[-1] if ev_c else ''))

main()
