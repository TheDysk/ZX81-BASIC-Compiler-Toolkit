#!/bin/sh
# Rebuild everything and test the toolset.
#   1. compiler: compile the test listings, cross-check the assembler, run them on a
#      Z80 emulator; programs with known results are compared with tests/expected.
#      t1-t12 test float islands (fast whole numbers); f1-f6 test full floating point,
#      whose results are compared with the same BASIC run by the ROM's own interpreter
#   2. decompiler: rebuild the BASIC of every compiled test program (must recompile to
#      identical machine code), and turn each listing back into an identical .p
#   3. tape: make .wav and .tzx, decode them with an independent decoder (tapecheck.py)
#      and with the toolset's own decoders (tests/cs/TapeTest.cs: also damaged audio,
#      other .wav formats, .p81 and ZX80 .o conversion)
# Optional: give a folder of other .p files to round-trip them too:  tests/run_tests.sh ~/zx81files
# Needs: mono (mcs), python3, pip install z80.
# The floating-point, VAL, printer and tape tests run the real ZX81 ROM, which is not
# included: copy zx81.rom (8K, e.g. from the EightyOne emulator) to tests/zx81.rom or
# set ZX81_ROM=path. Without it those programs are only compiled and decompiled.
set -e
cd "$(dirname "$0")/.."
./build.sh
ROM=""
if [ -f tests/zx81.rom ] || [ -n "$ZX81_ROM" ]; then ROM=yes; fi
echo "== compiler"
for f in t1 t2 t3 t4 t5 t6 t7 t8 t9 t10 t11 t12; do
  ram=16; [ $f = t11 ] && ram=32; [ $f = t12 ] && ram=64     # t11/t12: 32K and 64K RAM packs
  mono build/zx81c.exe --float islands --ram $ram tests/$f.bas tests/$f.p --asm tests/$f.asm > /dev/null
  echo "$f: $(python3 tests/asmcheck.py tests/$f.asm tests/$f.p | tail -1)"
done
mono build/zx81c.exe --float islands --no-break --maxstr 40 --no-autorun tests/t5.bas tests/t5opt.p > /dev/null
mono build/zx81c.exe examples/fortress.txt examples/fortress.p > /dev/null
mono build/zx81c.exe examples/clock.txt examples/clock.p > /dev/null
echo "-- strict integer mode: whole-number programs compile identically, floating point is refused"
printf '#STRICT_INT\n' | cat - tests/t1.bas > /tmp/zx81strict1.bas
mono build/zx81c.exe /tmp/zx81strict1.bas /tmp/zx81strict1.p > /dev/null   # (integer only makes the same code as float islands)
cmp -s /tmp/zx81strict1.p tests/t1.p && echo "t1 with #STRICT_INT: identical" || echo "t1 with #STRICT_INT: DIFFERENT"
n=$(mono build/zx81c.exe --strict-int tests/t7.bas /tmp/zx81strict7.p | grep -c "strict integer mode") || true
echo "t7 with --strict-int: $n floating-point lines refused"
echo "-- labels (@label:), mixed numbering, label errors, renumbering"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/labeltest.exe src/Core/*.cs tests/cs/LabelTest.cs
mono /tmp/labeltest.exe | grep -v "^ok    "
echo "-- BASIC only output (not compiled): labels, LET / THEN GOTO put right, ZX81 rules, 1K / 2K"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/basiconlytest.exe src/Core/*.cs tests/cs/BasicOnlyTest.cs
mono /tmp/basiconlytest.exe tests/t1.bas | grep -v "^ok    "
# zx81c --basic-only makes the same bytes as tokenising the listing directly, for every test listing
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
n=0; bad=""
for f in t1 t2 t3 t4 t5 t6 t7 t8 t9 t10 f1 f2 f3 f4 f5 f6; do
  mono build/zx81c.exe --basic-only --no-autorun tests/$f.bas /tmp/zx81bo_$f.p > /dev/null
  mono /tmp/tooltest.exe --mkp tests/$f.bas /tmp/zx81mkp_$f.p
  if cmp -s /tmp/zx81bo_$f.p /tmp/zx81mkp_$f.p; then n=$((n+1)); else bad="$bad $f"; fi
done
echo "zx81c --basic-only = the listing tokenised directly: $n of 16 identical${bad:+ - DIFFERENT:$bad}"
# t11 (arrays for a 32K pack) is too big for ordinary BASIC, which only uses 16K
code=0; mono build/zx81c.exe --basic-only tests/t11.bas /tmp/zx81bo_t11.p > /tmp/zx81bo.out || code=$?
[ $code = 1 ] && grep -q "doesn't fit a ZX81 with 16K" /tmp/zx81bo.out && echo "t11 refused as too big for BASIC: ok" || echo "t11 too big: FAIL"
echo "-- --run starts the emulator set up in the INI file (a stand-in script here)"
printf '#!/bin/sh\necho "$@" > /tmp/zx81emu.out\n' > /tmp/zx81emu.sh; chmod +x /tmp/zx81emu.sh; rm -f /tmp/zx81emu.out
printf '[Emulator]\nPath=/tmp/zx81emu.sh\nArguments=-q "{file}"\n' > /tmp/zx81test.ini
ZX81TOOLSET_INI=/tmp/zx81test.ini mono build/zx81c.exe tests/t1.bas /tmp/zx81run.p --run > /dev/null
sleep 1; echo "emulator was given: $(cat /tmp/zx81emu.out 2>/dev/null)"
echo "-- running (whole-number programs: stub ROM is enough)"
for f in t1 t4 t5; do python3 tests/run81.py tests/$f.p --frames 600 --stub | tail -1; done
# interactive test: typed INPUT, keys for INKEY$/PAUSE, then BREAK
python3 tests/run81.py tests/t3.p --frames 400 --break 300 --stub \
  --keys "20:B,30:O,40:B,50:RUB,60:B,70:NL,90:MINUS,100:4,110:2,120:NL,140:K,180:Q" | tail -1
if [ -n "$ROM" ]; then
  echo "-- running with the real ZX81 ROM (floating point, VAL, printer, tape, errors)"
  for f in t2 t7 t8 t10; do
    echo "$f: $(python3 tests/run81.py tests/$f.p --frames 3000 --expect tests/expected/$f.txt | tail -1)"
  done
  echo "t9: $(python3 tests/run81.py tests/t9.p --frames 50 | grep event)"
  echo "-- 32K / 64K RAM packs (the emulator has 64K of RAM; --hw16/--hw32 pretend it hasn't)"
  for f in t11 t12; do
    echo "$f: $(python3 tests/run81.py tests/$f.p --frames 3000 --expect tests/expected/$f.txt | tail -1)"
  done
  echo "t11, RAMTOP raised to C000h: $(python3 tests/run81.py tests/t11.p --frames 3000 --ramtop C000 | sed -n 2p)"
  echo "t11 on a 16K ZX81:  $(python3 tests/run81.py tests/t11.p --frames 50 --hw16 | sed -n 2p)"
  echo "t11 on a 32K ZX81:  $(python3 tests/run81.py tests/t11.p --frames 3000 --hw32 | sed -n 2p)"
  echo "t12 on a 32K ZX81:  $(python3 tests/run81.py tests/t12.p --frames 50 --hw32 | sed -n 2p)"
  echo "t1 (16K) on a 16K ZX81: $(python3 tests/run81.py tests/t1.p --frames 50 --hw16 | tail -1)"
else
  echo "-- no ZX81 ROM (tests/zx81.rom): skipping the floating point / printer / tape runs"
fi
echo "== full floating point (the default)"
for f in f1 f2 f3 f4 f5 f6; do
  mono build/zx81c.exe tests/$f.bas tests/$f.p --asm tests/$f.asm > /dev/null
  echo "$f: $(python3 tests/asmcheck.py tests/$f.asm tests/$f.p | tail -1)"
done
# the float-islands test programs compiled in full floating point too (5-byte numbers:
# t11's arrays then need a 64K pack, and t12's don't fit at all)
for f in t1 t2 t3 t4 t5 t6 t7 t8 t9 t10 t11; do
  ram=16; [ $f = t11 ] && ram=64
  mono build/zx81c.exe --float full --ram $ram tests/$f.bas tests/full_$f.p > /dev/null
done
mono build/zx81c.exe --float full examples/fortress.txt tests/full_fortress.p > /dev/null
mono build/zx81c.exe --float full examples/clock.txt tests/full_clock.p > /dev/null
echo "-- --float / directives / --? "
printf '10 PRINT 7/2\n' > /tmp/zx81half.bas
for m in full islands int; do mono build/zx81c.exe --float $m /tmp/zx81half.bas /tmp/zx81half_$m.p | grep -o "numbers: [a-z ]*"; done
printf '#FLOAT_ISLANDS\n10 PRINT 7/2\n' > /tmp/zx81half2.bas
mono build/zx81c.exe /tmp/zx81half2.bas /tmp/zx81half2.p > /dev/null
cmp -s /tmp/zx81half2.p /tmp/zx81half_islands.p && echo "#FLOAT_ISLANDS = --float islands: ok" || echo "#FLOAT_ISLANDS: DIFFERENT"
cmp -s /tmp/zx81half_int.p /tmp/zx81half_islands.p && echo "--float int, no floating point used = float islands code: ok" || echo "--float int: DIFFERENT"
mono build/zx81c.exe --? | grep -c -- "--float" | sed 's/^/zx81c --? lines mentioning --float: /'
mono build/zx81dc.exe --? | grep -c "FULL_FLOAT" | sed 's/^/zx81dc --? lines mentioning #FULL_FLOAT: /'
if [ -n "$ROM" ]; then
  echo "-- compiled in full floating point vs the same BASIC in the ZX81 ROM's interpreter"
  mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
  for f in f1 f2 f3 f4 f5; do
    mono /tmp/tooltest.exe --mkp tests/$f.bas /tmp/zx81basic_$f.p
    python3 tests/cmpbasic.py tests/$f.p /tmp/zx81basic_$f.p --frames 3000
  done
  for f in t1 t10; do
    mono /tmp/tooltest.exe --mkp tests/$f.bas /tmp/zx81basic_$f.p
    python3 tests/cmpbasic.py tests/full_$f.p /tmp/zx81basic_$f.p --frames 3000
  done
  echo "-- BASIC only output run by the ROM's own BASIC (16K, and 1K with a collapsed screen)"
  mono build/zx81c.exe tests/b1.bas /tmp/zx81b1.p > /dev/null
  mono build/zx81c.exe --ram 1 tests/b1.bas /tmp/zx81b1k.p > /dev/null
  echo "b1 (16K): $(python3 tests/run81.py /tmp/zx81b1.p --basic --frames 2000 --expect tests/expected/b1.txt | tail -1)"
  echo "b1 (1K):  $(python3 tests/run81.py /tmp/zx81b1k.p --basic --frames 2000 --ramtop 0x4400 --expect tests/expected/b1.txt | tail -1)"
  echo "f6 (INPUT 2.5, 4, -1.2): $(python3 tests/run81.py tests/f6.p --frames 600 --keys "20:2,30:.,40:5,50:NL,70:4,80:NL,200:MINUS,220:1,240:.,260:2,280:NL" --expect tests/expected/f6.txt | tail -1)"
fi
echo "== decompiler"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
mono /tmp/tooltest.exe tests/t1.p tests/t2.p tests/t3.p tests/t4.p tests/t5.p tests/t5opt.p tests/t6.p \
  tests/t7.p tests/t8.p tests/t9.p tests/t10.p tests/t11.p tests/t12.p examples/fortress.p examples/clock.p
echo "-- full floating point programs"
mono /tmp/tooltest.exe tests/f1.p tests/f2.p tests/f3.p tests/f4.p tests/f5.p tests/f6.p tests/full_t1.p tests/full_t2.p \
  tests/full_t3.p tests/full_t4.p tests/full_t5.p tests/full_t6.p tests/full_t7.p tests/full_t8.p tests/full_t9.p \
  tests/full_t10.p tests/full_t11.p tests/full_fortress.p tests/full_clock.p
echo "-- control files (.ctl): LABEL, COMMENT, CODE and DATA"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/ctltest.exe src/Core/*.cs tests/cs/CtlTest.cs
mono /tmp/ctltest.exe | grep -v "^ok    "
rm -rf /tmp/zx81ctl; mkdir -p /tmp/zx81ctl
printf '1 REM \\3e\\05\\c3\\8a\\40\\00\\00\\00\\c9\\01\\02\\03\\04\\cd\\93\\40\\c9\\21\\00\\00\\c9\n10 RAND USR 16514\n' > /tmp/zx81ctl/m.txt
mono /tmp/tooltest.exe --mkp /tmp/zx81ctl/m.txt /tmp/zx81ctl/m.p
printf 'LABEL 4082h start\nCODE 408Fh\nDATA 408Bh 4\nLABEL 4093h helper\nCOMMENT 408Fh not reached by USR\n' > /tmp/zx81ctl/m.ctl
mono build/zx81dc.exe /tmp/zx81ctl/m.p --txt /dev/null --basic /dev/null --asm /tmp/zx81ctl/m.asm > /tmp/zx81ctl/out.txt
grep -q "Control file /tmp/zx81ctl/m.ctl" /tmp/zx81ctl/out.txt && grep -q "CALL helper" /tmp/zx81ctl/m.asm && grep -q "; not reached by USR" /tmp/zx81ctl/m.asm && echo "zx81dc uses program.ctl next to the program: ok" || echo "zx81dc sidecar: FAIL"
printf 'LABEL 4082h other\n' > /tmp/zx81ctl/other.ctl
mono build/zx81dc.exe /tmp/zx81ctl/m.p --ctl /tmp/zx81ctl/other.ctl --txt /dev/null --basic /dev/null --asm /tmp/zx81ctl/m2.asm > /dev/null
grep -q "^other:" /tmp/zx81ctl/m2.asm && ! grep -q "helper" /tmp/zx81ctl/m2.asm && echo "zx81dc --ctl file: ok" || echo "zx81dc --ctl: FAIL"
echo "-- .P Toolkit screen and tile editors: bytes written back, system variables kept right"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/edittest.exe src/Core/*.cs tests/cs/EditTest.cs
mono /tmp/edittest.exe | grep -v "^ok    "
echo "-- large BASIC files for 32K / 64K packs (saved arrays past 32K; display file above 32K)"
printf '10 REM BIG\n20 PRINT A(1);" ";A(10)\n30 RAND USR 40000\n' > /tmp/zx81big.txt
mono /tmp/tooltest.exe --mkp /tmp/zx81big.txt /tmp/zx81bigbase.p
python3 tests/mkbig.py /tmp/zx81bigbase.p /tmp/zx81big32.p 5000 --code 40000 > /dev/null
python3 tests/mkbig.py /tmp/zx81bigbase.p /tmp/zx81big64.p 9000 > /dev/null
python3 tests/mkbig.py /tmp/zx81bigbase.p /tmp/zx81bigbad.p 10 --bigrem 17000 > /dev/null
mono /tmp/tooltest.exe /tmp/zx81big32.p /tmp/zx81big64.p /tmp/zx81bigbad.p
for f in big32 big64 bigbad; do
  echo "$f: $(mono build/zx81dc.exe /tmp/zx81$f.p --report --txt /dev/null --asm /dev/null | grep 'RAM needed' | cut -c1-40) / $(mono build/zx81dc.exe /tmp/zx81$f.p --report --txt /dev/null --asm /dev/null | grep -c Warning) warning(s)"
done
if [ -n "$1" ]; then mono /tmp/tooltest.exe $(find "$1" -iname "*.p") | tail -15; fi
echo "== tape"
mono /tmp/tooltest.exe --tape tests/t1.p
python3 tests/tapecheck.py tests/t1.p /tmp/zx81test.wav /tmp/zx81test.tzx TEST
echo "-- decoding: .wav / .tzx / .p81 -> .p byte for byte (many kinds of recording), damaged audio, ZX80 .o"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tapetest.exe src/Core/*.cs tests/cs/TapeTest.cs
mono /tmp/tapetest.exe tests/t1.p tests/t7.p examples/fortress.p examples/clock.p | grep -v "^ok    "
echo "-- zx81dc reads tapes: .p -> .wav/.tzx/.p81 -> zx81dc --extract -> .p (byte for byte)"
rm -rf /tmp/zx81tapes; mkdir -p /tmp/zx81tapes
mono /tmp/tapetest.exe --write-tapes /tmp/zx81tapes tests/t1.p examples/clock.p
for f in t1 clock; do
  src=tests/$f.p; [ -f $src ] || src=examples/$f.p
  for e in wav tzx p81; do
    mono build/zx81dc.exe /tmp/zx81tapes/$f.$e --extract /tmp/zx81tapes/${f}_$e.p --txt /tmp/zx81tapes/${f}_$e.txt > /dev/null
    cmp -s /tmp/zx81tapes/${f}_$e.p $src && echo "$f.$e: identical" || echo "$f.$e: DIFFERENT"
  done
done
mono build/zx81dc.exe /tmp/zx81tapes/clock.wav --txt /tmp/zx81tapes/clock_wav2.txt > /tmp/zx81tapes/clock.out
grep -q "WAV audio" /tmp/zx81tapes/clock.out && echo "zx81dc notes the .wav: ok" || echo "zx81dc notes the .wav: FAIL"
for f in t1.wav clock.tzx; do
  mono build/zx81c.exe /tmp/zx81tapes/$f /tmp/zx81tapes/re_$f.p > /dev/null
  src=tests/${f%.*}.p; [ -f $src ] || src=examples/${f%.*}.p
  cmp -s /tmp/zx81tapes/re_$f.p $src && echo "zx81c compiles $f back to an identical .p: ok" || echo "zx81c compiles $f: DIFFERENT"
done
code=0; mono build/zx81dc.exe /tmp/zx81tapes/t1_click.wav --txt /dev/null > /dev/null || code=$?
mono build/zx81dc.exe /tmp/zx81tapes/t1_click.wav --wav-threshold 5 --extract /tmp/zx81tapes/t1_click.p --txt /dev/null --basic /dev/null --asm /dev/null > /dev/null
[ $code = 1 ] && cmp -s /tmp/zx81tapes/t1_click.p tests/t1.p && echo "a quiet .wav with a loud click: fails as standard, reads with --wav-threshold 5: ok" || echo "--wav-threshold: FAIL"
mono build/zx81dc.exe /tmp/zx81tapes/zx80demo.o --txt /tmp/zx81tapes/zx80demo.txt > /dev/null
grep -q 'PRINT "HELLO ZX80"' /tmp/zx81tapes/zx80demo.txt && grep -q "LET A=12-3" /tmp/zx81tapes/zx80demo.txt && echo "zx81dc reads a ZX80 .o: ok" || echo "zx81dc reads a ZX80 .o: FAIL"
head -c 300000 /tmp/zx81tapes/t1.wav > /tmp/zx81tapes/cut.wav
code=0; mono build/zx81dc.exe /tmp/zx81tapes/cut.wav > /tmp/zx81tapes/cut.out || code=$?
grep -q "\[00:[0-9][0-9]\.[0-9][0-9][0-9]\]" /tmp/zx81tapes/cut.out && [ $code = 1 ] && echo "cut .wav: reported with a timestamp, exit 1: ok ($(cat /tmp/zx81tapes/cut.out))" || echo "cut .wav: FAIL ($(cat /tmp/zx81tapes/cut.out), exit $code)"
echo "Now compare the screens by eye: python3 tests/run81.py tests/t2.p"
