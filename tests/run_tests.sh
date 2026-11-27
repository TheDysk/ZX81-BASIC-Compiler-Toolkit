#!/bin/sh
# Rebuild everything and test the toolset.
#   1. compiler: compile the test listings, cross-check the assembler, run them on a
#      Z80 emulator; programs with known results are compared with tests/expected
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
  mono build/zx81c.exe --ram $ram tests/$f.bas tests/$f.p --asm tests/$f.asm > /dev/null
  echo "$f: $(python3 tests/asmcheck.py tests/$f.asm tests/$f.p | tail -1)"
done
mono build/zx81c.exe --no-break --maxstr 40 --no-autorun tests/t5.bas tests/t5opt.p > /dev/null
mono build/zx81c.exe examples/fortress.txt examples/fortress.p > /dev/null
mono build/zx81c.exe examples/clock.txt examples/clock.p > /dev/null
echo "-- strict integer mode: whole-number programs compile identically, floating point is refused"
printf '#STRICT_INT\n' | cat - tests/t1.bas > /tmp/zx81strict1.bas
mono build/zx81c.exe /tmp/zx81strict1.bas /tmp/zx81strict1.p > /dev/null
cmp -s /tmp/zx81strict1.p tests/t1.p && echo "t1 with #STRICT_INT: identical" || echo "t1 with #STRICT_INT: DIFFERENT"
n=$(mono build/zx81c.exe --strict-int tests/t7.bas /tmp/zx81strict7.p | grep -c "strict integer mode") || true
echo "t7 with --strict-int: $n floating-point lines refused"
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
echo "== decompiler"
mcs -langversion:5 -nowarn:414,219,168 -out:/tmp/tooltest.exe src/Core/*.cs tests/cs/ToolTest.cs
mono /tmp/tooltest.exe tests/t1.p tests/t2.p tests/t3.p tests/t4.p tests/t5.p tests/t5opt.p tests/t6.p \
  tests/t7.p tests/t8.p tests/t9.p tests/t10.p tests/t11.p tests/t12.p examples/fortress.p examples/clock.p
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
mono build/zx81dc.exe /tmp/zx81tapes/zx80demo.o --txt /tmp/zx81tapes/zx80demo.txt > /dev/null
grep -q 'PRINT "HELLO ZX80"' /tmp/zx81tapes/zx80demo.txt && grep -q "LET A=12-3" /tmp/zx81tapes/zx80demo.txt && echo "zx81dc reads a ZX80 .o: ok" || echo "zx81dc reads a ZX80 .o: FAIL"
head -c 300000 /tmp/zx81tapes/t1.wav > /tmp/zx81tapes/cut.wav
code=0; mono build/zx81dc.exe /tmp/zx81tapes/cut.wav > /tmp/zx81tapes/cut.out || code=$?
grep -q "\[00:[0-9][0-9]\.[0-9][0-9][0-9]\]" /tmp/zx81tapes/cut.out && [ $code = 1 ] && echo "cut .wav: reported with a timestamp, exit 1: ok ($(cat /tmp/zx81tapes/cut.out))" || echo "cut .wav: FAIL ($(cat /tmp/zx81tapes/cut.out), exit $code)"
echo "Now compare the screens by eye: python3 tests/run81.py tests/t2.p"
