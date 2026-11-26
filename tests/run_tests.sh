#!/bin/sh
# Rebuild everything and test the toolset.
#   1. compiler: compile the test listings, cross-check the assembler, run them on a Z80 emulator
#   2. decompiler: rebuild the BASIC of every compiled test program (must recompile to
#      identical machine code), and turn each listing back into an identical .p
#   3. tape: make .wav and .tzx and decode them again
# Optional: give a folder of other .p files to round-trip them too:  tests/run_tests.sh ~/zx81files
# Needs: mono (mcs), python3, pip install z80
set -e
cd "$(dirname "$0")/.."
./build.sh
echo "== compiler"
for f in tests/t1 tests/t2 tests/t4 tests/t5; do
  mono build/zx81c.exe $f.bas $f.p --asm $f.asm > /dev/null
  python3 tests/asmcheck.py $f.asm $f.p | tail -1
  python3 tests/run81.py $f.p --frames 600 | tail -1
done
# interactive test: typed INPUT, keys for INKEY$/PAUSE, then BREAK
mono build/zx81c.exe tests/t3.bas tests/t3.p --asm tests/t3.asm > /dev/null
python3 tests/asmcheck.py tests/t3.asm tests/t3.p | tail -1
python3 tests/run81.py tests/t3.p --frames 400 --break 300 \
  --keys "20:B,30:O,40:B,50:RUB,60:B,70:NL,90:MINUS,100:4,110:2,120:NL,140:K,180:Q" | tail -1
mono build/zx81c.exe tests/t6.bas tests/t6.p > /dev/null
mono build/zx81c.exe --no-break --maxstr 40 --no-autorun tests/t5.bas tests/t5opt.p > /dev/null
mono build/zx81c.exe examples/fortress.txt examples/fortress.p > /dev/null
echo "== decompiler"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
mono /tmp/tooltest.exe tests/t1.p tests/t2.p tests/t3.p tests/t4.p tests/t5.p tests/t5opt.p tests/t6.p examples/fortress.p
if [ -n "$1" ]; then mono /tmp/tooltest.exe $(find "$1" -iname "*.p") | tail -15; fi
echo "== tape"
mono /tmp/tooltest.exe --tape tests/t1.p
python3 tests/tapecheck.py tests/t1.p /tmp/zx81test.wav /tmp/zx81test.tzx TEST
echo "Now compare the screens by eye: python3 tests/run81.py tests/t2.p"
