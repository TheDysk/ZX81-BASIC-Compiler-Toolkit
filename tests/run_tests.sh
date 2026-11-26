#!/bin/sh
# Rebuild everything and test the toolset.
#   1. compiler: compile the test listings, cross-check the assembler, run them on a
#      Z80 emulator; programs with known results are compared with tests/expected
#   2. decompiler: rebuild the BASIC of every compiled test program (must recompile to
#      identical machine code), and turn each listing back into an identical .p
#   3. tape: make .wav and .tzx and decode them again
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
for f in t1 t2 t3 t4 t5 t6 t7 t8 t9 t10; do
  mono build/zx81c.exe tests/$f.bas tests/$f.p --asm tests/$f.asm > /dev/null
  echo "$f: $(python3 tests/asmcheck.py tests/$f.asm tests/$f.p | tail -1)"
done
mono build/zx81c.exe --no-break --maxstr 40 --no-autorun tests/t5.bas tests/t5opt.p > /dev/null
mono build/zx81c.exe examples/fortress.txt examples/fortress.p > /dev/null
mono build/zx81c.exe examples/clock.txt examples/clock.p > /dev/null
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
else
  echo "-- no ZX81 ROM (tests/zx81.rom): skipping the floating point / printer / tape runs"
fi
echo "== decompiler"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
mono /tmp/tooltest.exe tests/t1.p tests/t2.p tests/t3.p tests/t4.p tests/t5.p tests/t5opt.p tests/t6.p \
  tests/t7.p tests/t8.p tests/t9.p tests/t10.p examples/fortress.p examples/clock.p
if [ -n "$1" ]; then mono /tmp/tooltest.exe $(find "$1" -iname "*.p") | tail -15; fi
echo "== tape"
mono /tmp/tooltest.exe --tape tests/t1.p
python3 tests/tapecheck.py tests/t1.p /tmp/zx81test.wav /tmp/zx81test.tzx TEST
echo "Now compare the screens by eye: python3 tests/run81.py tests/t2.p"
