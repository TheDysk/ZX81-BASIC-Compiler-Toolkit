# ZX81 BASIC Toolset

Three Windows tools for **Sinclair ZX81** programs and `.p` files:

| Tool | What it does |
|---|---|
| **ZX81 BASIC Compiler** | Compiles a ZX81 BASIC text listing into fast **Z80 machine code** and saves a ready-to-load `.p` file. |
| **ZX81 Decompiler** | Reads a `.p` file, finds the BASIC and any machine code, and writes it back out as a BASIC text listing, a new `.p` file and a Z80 `.asm` listing. For programs made by the compiler it also rebuilds the original BASIC. |
| **.P File Browser** | Looks inside a `.p` file: system variables, memory map, listing, screen, variables, hex editor with Z80 disassembly, graphics ripper, and export to text, `.wav` and `.tzx`. |

A compiled program looks like this on the ZX81:

```
0 REM <compiled machine code>
10 RAND USR 16514
```

Load it in an emulator such as EightyOne, or on a real ZX81 with a 16K, 32K or 64K RAM pack. By default it starts by itself after loading.

All three tools are at version **V1.00**. Each shows its version in its window title and **Help → About** box. The command-line versions print it with `--version`, and it is stored in each `.exe` file (on Windows: right-click → Properties → Details).

## Download and run

Build the tools yourself (below), or take the executables from a release. They need .NET Framework 4, which is already part of Windows 10/11.

| File | What it is |
|---|---|
| `ZX81BasicCompiler.exe` | Compiler, Windows GUI |
| `zx81c.exe` | Compiler, command line |
| `ZX81Decompiler.exe` | Decompiler, Windows GUI |
| `zx81dc.exe` | Decompiler, command line |
| `ZX81PBrowser.exe` | .P File Browser, Windows GUI |

All three GUIs accept files dragged onto their windows.

## The compiler

### Features

- Windows GUI with an editor, compile button (F5), and clickable error and warning messages. Listings can be dragged onto the window.
- Command-line compiler (`zx81c.exe`) for scripting and batch use.
- ZX81 graphics and inverse characters through simple escapes in the text listing.
- Every ZX81 BASIC keyword is accepted, including `SIN COS TAN ASN ACS ATN LN EXP PI`, `LPRINT`, `COPY`, `SAVE`, `LOAD` and `NEW`.
- Compiled programs write straight to the display file, so they are much faster than interpreted BASIC.
- Floating-point maths is done by the ZX81 ROM's own calculator, only where a program needs it (see below). Everything else stays fast 16-bit whole-number code.
- The machine code (line 0) needs nothing but the ZX81 ROM: keyboard, FAST/SLOW, the calculator, the printer and the tape routines.
- Programs can be compiled for a 16K, 32K or 64K RAM pack. With 32K or 64K, the variables, arrays and strings use the RAM above 32K (see [RAM packs](#ram-packs-16k-32k-and-64k)).

### Using the compiler

GUI: open a listing (`.txt`/`.bas`) or type one in, press **F5**, and choose where to save the `.p` file. **Help → Supported BASIC** lists everything in detail.

Command line:

```
zx81c listing.txt [output.p] [--no-autorun] [--no-break] [--lower-inverse] [--maxstr N] [--ram 16|32|64] [--asm listing.asm]
zx81c --version
```

| Option | Meaning |
|---|---|
| `--no-autorun` | Don't start automatically after LOAD (type `RUN`) |
| `--no-break` | Don't stop the program when SPACE (BREAK) is pressed. Use this for games that use SPACE. |
| `--lower-inverse` | Lower-case letters inside strings become inverse video |
| `--maxstr N` | Capacity of ordinary string variables, 1–255 (default 255). Lower it if memory runs out. |
| `--ram 16\|32\|64` | The RAM pack the program is for (default 16). See [RAM packs](#ram-packs-16k-32k-and-64k). In the GUI this is the **RAM** box. |
| `--asm file` | Also write the generated assembly listing |

### Listing format

The listing is plain text, one numbered line per text line. Keywords are written out in full (`GO TO`/`GOTO` and `GO SUB`/`GOSUB` both work):

```basic
10 PRINT AT 0,0;"\00\00\2d\85\17\17\17\2b\2e\33\29\00\39\2d\2a"
20 LET X=X+(INKEY$="8")-(INKEY$="5")
30 GOTO 20
```

Special characters inside strings:

| Write | Meaning |
|---|---|
| `\hh` | Any ZX81 character code in hex, e.g. `\80` black square, `\85`, `\17` (`*`), `\00` (space) |
| `\'.`, `\::` … | Block graphics (zmakebas/ZXText2P style): two characters for the left and right halves, `'` top, `.` bottom, `:` both |
| `%A` | Inverse character |
| `""` | A quote mark |
| `▘▝▀▖▌▞▛▒█▟▙▄▜▐▚▗` | Unicode block characters are also accepted |

### Supported BASIC

**Statements:** `LET` (optional), `PRINT` (with `;` `,` `AT` `TAB`), `IF … THEN`, `GOTO`, `GOSUB`, `RETURN`, `FOR … TO … STEP`, `NEXT`, `DIM`, `INPUT`, `POKE`, `PLOT`, `UNPLOT`, `SCROLL`, `CLS`, `PAUSE`, `RAND`, `STOP`, `CLEAR`, `RUN`, `FAST`, `SLOW`, `REM`, `LPRINT`, `COPY`, `SAVE`, `LOAD`, `NEW`.
GOTO and GOSUB can use a calculated line number, e.g. `GOTO 100*N`.

- `LPRINT` (with `;` `,` `AT` `TAB`) and `COPY` print on the ZX printer through the ROM. With no printer attached, the ROM simply carries on.
- `SAVE "name"` saves the program to tape, as the ZX81 does, and then the program carries on.
- `LOAD "name"` (or `LOAD ""`) loads another program. That program then runs if it auto-runs, as on the ZX81.
- `NEW` wipes the program, as on the ZX81.
- `LIST`, `LLIST` and `CONT` are accepted but do nothing, with a warning. A compiled program has no BASIC to list (the only BASIC in memory is the machine code line), and nothing stopped to continue.

**Functions and operators:** `ABS SGN INT SQR PEEK USR CODE LEN VAL CHR$ STR$ INKEY$ RND PI SIN COS TAN ASN ACS ATN LN EXP`, `NOT AND OR`, `+ - * / **`, and comparisons on numbers and strings. String slicing works: `A$(2)`, `A$(2 TO 5)`, `A$( TO 3)`, `A$(4 TO )`. Numeric arrays can have 1 or 2 dimensions; string arrays work like on the ZX81 (`DIM A$(n,m)`).

### Whole numbers and floating point ("float islands")

Numbers are normally 16-bit whole numbers (−32768 to 32767), which is what makes compiled programs fast.

An expression is worked out in **floating point** instead when it uses any of these:

- `SIN COS TAN ASN ACS ATN LN EXP SQR PI VAL`;
- `RND` on its own;
- a number with a decimal point, such as `0.5` or `1.0`, or one too big for 16 bits.

The ZX81 ROM's own calculator does the work, so the results are exactly what a ZX81 gives:

```basic
10 FOR A=0 TO 359 STEP 10
20 PLOT 32+20*COS (A*PI/180),22+20*SIN (A*PI/180)
30 NEXT A
40 PRINT SIN 1;" ";SQR 2;" ";7/2*1.0
```

Line 40 prints `0.84147099 1.4142136 3.5`, as on a ZX81.

- Where a floating-point result has to become a number, it is **rounded** to the nearest whole number, as the ZX81 does when it needs one. That covers storing it in a variable, `PLOT`, `AT`, a subscript, `POKE` and so on.
- `PRINT`, `LPRINT` and `STR$` show the full value.
- Comparisons and `IF` conditions use the full value, so `IF SIN X>0.5` works, and `IF 0.3 THEN` counts as true.
- **Variables hold whole numbers**, so `LET A=H*PI/6` stores 1, not 0.5236. The compiler warns when this happens. Keep the angle inside the expression instead, e.g. `COS (H*PI/6)`. [examples/clock.txt](examples/clock.txt) does this.
- Floating point runs at the speed of the ZX81's own maths, which is slow, so keep it out of the busiest loops.
- A maths error such as `SQR -1` or `LN 0` stops the program with the ZX81 report code (`A/10`: the program runs from line 10).
- `VAL` works out any expression in its string, e.g. `VAL "2*3.5"` is 7. It can't see the program's variables, because those are kept outside BASIC.

### RAM packs: 16K, 32K and 64K

There were RAM packs for the ZX81 giving 32K (RAM from 4000h to BFFFh) and 64K (usable RAM from 4000h to FFFFh, plus 2000h–3FFFh on some packs). The ZX81 hardware limits what the RAM above 32K (8000h) can be used for. There, the video circuit turns most instruction fetches into NOPs, so the ZX81 can't run machine code there, and the display file has to be below 32K as well. That RAM can only hold data. And when it is switched on, the ROM only uses RAM up to 8000h (RAMTOP); BASIC only uses more after `POKE 16389,192` (32K) and `NEW`.

So with `--ram 32` or `--ram 64` (the **RAM** box in the GUI):

| | 16K (default) | 32K pack | 64K pack |
|---|---|---|---|
| Machine code, screen, machine stack | below 8000h | below 8000h | below 8000h |
| Variables, arrays and strings | after the program, below 8000h | 8000h–BFFFh (16K) | 8000h–FFFFh (32K) |

- The `.p` file itself is the same size whichever you choose, because the variables are not saved in it.
- The code still has to fit below 32K, so the biggest program is about the same. What grows is the room for arrays and strings, e.g. `DIM A(5000)` (10,000 bytes) with 32K, or `DIM A(12000)` with 64K.
- RAMTOP can be left at 8000h, in which case the stack stays below 32K. It can also be raised, as many 32K/64K owners did, in which case the stack sits above the variables. Both work. The program refuses to run if RAMTOP is set somewhere inside its variables.
- When it starts, the program checks that the RAM above 32K is really there. It is not ROM echo (as on a 16K machine) and not a copy of the 16K below (as on some packs). If the check fails, it shows `NEEDS 32K RAM` or `NEEDS 64K RAM` at the top of the screen and goes back to BASIC.
- If a program is too big for 16K, the compiler suggests 32K or 64K. If the code itself is too big for a ZX81 at all (it must fit below 32K), it says so.
- The 8K at 2000h–3FFFh that some 64K packs provide isn't used.

#### Differences from ZX81 BASIC

- **Whole-number maths:** outside floating point, division truncates (`7/2` is 3, `-7/2` is −3) and `2**-1` is 0. Write `7/2.0` to get 3.5.
- **Variables** hold whole numbers (see above). They start at 0 instead of giving "variable not found". Ordinary string variables hold up to 255 characters.
- **RND:** `RND*n` gives a fast whole number from 0 to n−1, so `INT (RND*6)+1` works as expected. `RND` on its own gives a fraction (0 to 0.99998).
- **DIM:** sizes must be constant numbers. Arrays are not bounds checked.
- **Screen:** printing below line 21 scrolls instead of stopping with error 5.
- **Statements per line:** one, as on the ZX81.

Compiled programs run much faster, so games often need extra `PAUSE` lines to stay playable.

## The decompiler

The decompiler takes any `.p` file and turns it back into text you can read, edit and reload.

- **BASIC listing:** every line is written out with the same escapes the compiler uses (`\hh`, `\'.`, `%A`, see above), and checked byte by byte. Lines that can't be written as normal text, such as machine code hidden in a `REM`, are written entirely as `\hh` escapes, so the listing turns back into an identical program. In this listing `\hh` may appear anywhere in a line, not only inside strings. Lines starting with `#` are comments (the decompiler puts its notes and the saved variables there).
- The listing is ZX81 BASIC, so it runs as normal interpreted BASIC once turned back into a `.p`. It can also be fed to the compiler, which accepts every ZX81 keyword, but programs that rely on variables holding fractions will behave differently when compiled (see above).
- **Machine code:** it finds machine code wherever it is: in line 0, in any `REM` line, split across several lines, or elsewhere in RAM (for example in the variables area or past the end of the program). It starts from every `USR` call it can work out, including `USR 16514`, `USR VAL "16514"`, `USR (PEEK 16396+256*PEEK 16397)` and variables saved with the program, then follows jumps and calls. The result is an `.asm` listing with labels, ROM and system-variable names, and `DB` lines for data. The bytes after `RST 28h` (the ROM calculator) are named, e.g. `DB 1Ch,34h ; calculator: sin, end-calc`, and so are the report codes after `RST 08h`. The listing reassembles to the same bytes.
- **RAM packs:** the report says how much RAM a file needs: 1K, 2K, 4K, 16K, 32K or 64K. For programs made by the compiler, it says which RAM they were compiled for and where their variables go. It warns about things a real ZX81 can't do: a display file above 32K, machine code above 32K (it needs an M1NOT modification), or a file that goes past 32K (RAMTOP must be raised before loading it). Large `.p` files, such as BASIC programs with big saved arrays, work like any other, up to the full 64K.
- **Programs made by the compiler:** these are recognised, and the original BASIC is rebuilt from the machine code (with the original line numbers when the program used computed GOTO/GOSUB). Everything the compiler produces is understood, including floating point, `LPRINT`, `COPY`, `SAVE`, `LOAD` and `NEW`. The rebuilt BASIC is recompiled and compared with the file, and the report says whether the result is identical.
- **New `.p` file:** made from the listing, or from the rebuilt BASIC. The original screen and saved variables can be kept.

### Using the decompiler

GUI: open a `.p` file (Ctrl+O, or drag it onto the window). The tabs show:

| Tab | Contents |
|---|---|
| Report | What was found: lines, machine code blocks and where they start, autorun, problems |
| Listing | The BASIC listing (editable) |
| BASIC (rebuilt) | Only for programs made by the compiler: the recovered BASIC source (editable) |
| Machine code | The `.asm` listing |

The **File** menu saves the listing (`.txt`), the rebuilt BASIC (`.bas`) and the machine code (`.asm`), and **Make .p file from this tab** turns the Listing or BASIC tab into a new `.p` file. The rebuilt BASIC can also be opened in the compiler and compiled again.

Command line:

```
zx81dc program.p [--txt file] [--basic file] [--asm file] [--p file] [--p-from listing|basic] [--report]
zx81dc --version
```

With no options it writes `program.txt`, plus `program.bas` if the BASIC could be rebuilt and `program.asm` if there is machine code.

| Option | Meaning |
|---|---|
| `--txt file` | Write the BASIC listing here |
| `--basic file` | Write the rebuilt BASIC here (compiled programs only) |
| `--asm file` | Write the machine code listing here |
| `--p file` | Also write a new `.p` file |
| `--p-from listing\|basic` | Make that `.p` from the listing (default) or from the rebuilt BASIC |
| `--report` | Print the report |

Limits:

- Machine code is only found if something reaches it: a `USR` call the decompiler can evaluate, or a jump or call from code it has already found. Code started in other ways, such as by a `POKE` into the stack, is shown as data. The Browser's **Decode everything as Z80** switch shows it anyway.
- Rebuilding BASIC only works for programs made by this compiler. Other machine code is given as assembly.

## The .P File Browser

Open a `.p` file (Ctrl+O, or drag it onto the window). The tabs:

- **Overview**
  - *Auto-run detector:* whether the program starts by itself after LOAD and at which line (from `NXTLIN`).
  - *System variables* `4009h`–`407Ch`, each with its value and a plain-English explanation, for example where the display file is, or whether the display file is collapsed (1K) or expanded (16K).
  - *Memory map:* a coloured bar and a table of the regions: system variables, each BASIC line, the display file, the variables and the edit line. The free memory and stack are worked out for a 1K, 2K, 4K or 16K machine, or a 32K or 64K RAM pack. RAMTOP isn't saved in a `.p` file, so you choose the machine; the Browser picks the smallest that fits. For programs compiled for 32K/64K, the map shows their variables above 32K.
  - *Warnings* for anything a real ZX81 can't do with the file, whatever RAM is fitted: a display file above 32K, machine code above 32K, or a file that needs RAMTOP raised before loading.
- **Listing:** the BASIC with ZX81 characters translated: keywords, inverse video (shown highlighted) and block graphics as Unicode. The hidden 5-byte numbers that follow every number in a line are hidden by default; a tick box shows them as `{=value}` and marks with `!` any that differ from the digits shown, a common trick. Machine code in `REM` lines is shown as Z80 instructions.
- **Screen:** the display file drawn as the ZX81 would show it (32×24), at a choice of sizes. Collapsed 1K display files are handled. Save it as a picture or as text.
- **Variables:** every variable saved with the program, decoded: numbers, strings, arrays and FOR loops (with their limit, step and loop line).
- **Hex / Z80:** a hex editor and a Z80 disassembly side by side, kept in step. Traced machine code is shown in green. ROM routines and system variables are labelled, and the bytes after `RST 28h` and `RST 08h` are explained (calculator operations, error reports). Go to any address, edit bytes (double-click a row), then **Save .p as**.
- **Graphics:** shows any part of memory as 8×8 tiles, to find and rip character sets and user-defined graphics as used by hi-res and custom-font programs. Likely character sets are found automatically. Save as `.png`, `.bin` or `.asm` (`DB` lines).

The **Export** menu has:

- the BASIC listing as UTF-8 text with zmakebas-style `\..` block-graphics escapes (the same format as the decompiler's listing, so it can be turned back into an identical `.p`);
- a hex dump;
- a Z80 disassembly (`.asm`);
- the screen as `.png` or text;
- cassette audio (`.wav`) to play into a real ZX81's EAR socket after typing `LOAD ""`;
- a `.tzx` tape image for emulators.


## Building from source

There are no dependencies and no NuGet packages. The code is C# 5, targeting .NET Framework 4.

- **Windows:** run `build.bat`. It uses the `csc.exe` that ships with .NET Framework, so Visual Studio isn't needed.
- **Linux/macOS:** install Mono and run `./build.sh`. The resulting `.exe` files run on Windows.

Output goes to `build/`: all five executables.

## How it works

### Compiler

`listing → Lexer/Parser → AST → Z80 assembly text → built-in two-pass assembler → .p file`

- Numeric expressions are evaluated in HL as 16-bit integers. Strings are address/length pairs, with temporary buffers for joined strings.
- A runtime library written in Z80 assembly handles printing, strings, maths, PLOT, INPUT and so on. Only the routines a program uses are included.
- Variables are not stored in the `.p` file. They live in free RAM above the BASIC edit line and are cleared when the program starts. This keeps the file small, so it loads quickly from tape. Programs that use the ROM calculator leave 768 bytes more room before the variables for the calculator stack and the ROM's workspace.
- Floating-point parts of expressions ("float islands") put their values on the ROM calculator's stack and call the calculator (`RST 28h`) for each operation. The result is rounded back into HL, or turned into text by the ROM's `STR$`. The rest of the program never touches the calculator.
- The compiler's own code never uses IX, IY or the alternate registers, so it runs safely in SLOW mode with the display on. The ROM's calculator, printer and tape routines are the ROM's own and are safe too.

### Decompiler and browser

- `.p` files are loaded into a 64K memory image at 4009h, so the tools use real ZX81 addresses throughout.
- The listing is rebuilt from the tokens. Each line is turned back into bytes with the same tokenizer and compared with the original, which is how lines that need `\hh` escapes are spotted.
- Machine code is found by a Z80 disassembler that follows jumps and calls from each entry point. It knows about ROM calculator calls (`RST 28h`) and error calls (`RST 08h`), which are followed by data bytes.
- Compiled programs are recognised by the compiler's start-up code. Each runtime routine is matched against the compiler's own templates, and the program code between them is read back into BASIC statements.

[CLAUDE.md](CLAUDE.md) has the detailed internals: memory layout, register conventions, and how to add statements or runtime routines.

## Testing

`tests/` has test listings, a Python harness and C# test drivers. `tests/run_tests.sh` rebuilds everything and then:

- runs compiled programs on a Z80 emulator and prints the ZX81 screen as text. Programs with known results are checked against `tests/expected`: screen, printer output, and tape/NEW/error events;
- cross-checks the assembler against an independent disassembler;
- decompiles every compiled test program, checks that the rebuilt BASIC recompiles to identical code, and checks that each listing turns back into an identical `.p` file;
- runs the 32K and 64K test programs on emulated 64K, 32K and 16K machines, and with RAMTOP raised. They must run, or show `NEEDS 32K RAM` / `NEEDS 64K RAM` on machines without enough RAM;
- builds large BASIC `.p` files for 32K and 64K packs (`tests/mkbig.py`: saved arrays past 32K and 48K, machine code at 40000, a display file above 32K) and checks they round-trip and get the right RAM estimate and warnings;
- makes a `.wav` and a `.tzx` and decodes them again.

```sh
pip install z80
tests/run_tests.sh                     # all tests
tests/run_tests.sh ~/zx81files         # also round-trip a folder of your own .p files
python3 tests/run81.py tests/t11.p --hw16   # a 32K program on a plain 16K ZX81: NEEDS 32K RAM
python3 tests/run81.py tests/t2.p      # show the screen of a test program
```

The floating-point, `VAL`, printer and tape tests need the real **ZX81 ROM**, which isn't included in this repository because it is copyright. Copy the 8K `zx81.rom` (for example from the EightyOne emulator) to `tests/zx81.rom`, or set `ZX81_ROM=path`. Without it, `run_tests.sh` still compiles and decompiles those programs but doesn't run them. Whole-number programs also run on a small built-in stub ROM (`--stub`).

The harness doesn't emulate the display hardware or real tape and printer signals. It catches the ROM's printer, SAVE, LOAD and NEW entry points and reports them instead. Loading a `.p` file, and the printer and tape on real hardware, should also be checked in a real emulator such as EightyOne.

## Project layout

```
src/Core/        shared code: compiler, .p file reader, listing, Z80 disassembler,
                 machine code finder, BASIC recovery, tape output
src/Compiler/    compiler GUI and command line
src/Decompiler/  decompiler GUI and command line
src/Browser/     .P File Browser
src/UI/          shared Windows Forms helpers and ZX81 screen/graphics drawing
tests/           test listings, emulator harness, test drivers, expected results
examples/        fortress.txt (small game), clock.txt (floating point), feature_test.txt
build.sh         build with Mono
build.bat        build on Windows
LICENSE          MIT licence
```

## Author

**Simon Round**

Bug reports and suggestions are welcome via [GitHub Issues](../../issues); pull requests are welcome too.

## Licence

Copyright (c) 2026 Simon Round.

Released under the [MIT License](LICENSE). You are free to use, copy, modify, merge, publish, distribute, sublicense and sell copies of this software, provided the copyright notice and licence text are included. The software is provided "as is", without warranty of any kind.
