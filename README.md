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

Load it in an emulator such as EightyOne, or on a real ZX81 with a 16K RAM pack. By default it starts by itself after loading.

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
- Compiled programs write straight to the display file, so they are much faster than interpreted BASIC.
- The machine code (line 0) has no dependencies beyond the ZX81 ROM, which it uses only for keyboard reading and FAST/SLOW.

### Using the compiler

GUI: open a listing (`.txt`/`.bas`) or type one in, press **F5**, and choose where to save the `.p` file. **Help → Supported BASIC** lists everything in detail.

Command line:

```
zx81c listing.txt [output.p] [--no-autorun] [--no-break] [--lower-inverse] [--maxstr N] [--asm listing.asm]
```

| Option | Meaning |
|---|---|
| `--no-autorun` | Don't start automatically after LOAD (type `RUN`) |
| `--no-break` | Don't stop the program when SPACE (BREAK) is pressed. Use this for games that use SPACE. |
| `--lower-inverse` | Lower-case letters inside strings become inverse video |
| `--maxstr N` | Capacity of ordinary string variables, 1–255 (default 255). Lower it if memory runs out. |
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

**Statements:** `LET` (optional), `PRINT` (with `;` `,` `AT` `TAB`), `IF … THEN`, `GOTO`, `GOSUB`, `RETURN`, `FOR … TO … STEP`, `NEXT`, `DIM`, `INPUT`, `POKE`, `PLOT`, `UNPLOT`, `SCROLL`, `CLS`, `PAUSE`, `RAND`, `STOP`, `CLEAR`, `RUN`, `FAST`, `SLOW`, `REM`.
GOTO and GOSUB can use a calculated line number, e.g. `GOTO 100*N`.

**Functions and operators:** `ABS SGN INT SQR PEEK USR CODE LEN VAL CHR$ STR$ INKEY$ RND`, `NOT AND OR`, `+ - * / **`, and comparisons on numbers and strings. String slicing works: `A$(2)`, `A$(2 TO 5)`, `A$( TO 3)`, `A$(4 TO )`. Numeric arrays can have 1 or 2 dimensions; string arrays work like on the ZX81 (`DIM A$(n,m)`).

#### Differences from ZX81 BASIC

This is an integer compiler:

- **Numbers:** whole numbers from −32768 to 32767, so `7/2` is `3`. `SIN COS TAN ASN ACS ATN LN EXP PI` are not available.
- **RND:** must be written as `RND*n`, which gives a whole number from 0 to n−1. `INT (RND*6)+1` works as expected.
- **DIM:** sizes must be constant numbers. Arrays are not bounds checked.
- **Variables:** start at 0 instead of giving "variable not found". Ordinary string variables hold up to 255 characters.
- **Screen:** printing below line 21 scrolls instead of stopping with error 5.
- **Statements per line:** one, as on the ZX81. `LPRINT LLIST COPY LIST SAVE LOAD CONT` are ignored with a warning.
- **VAL:** only converts plain whole numbers.

Compiled programs run much faster, so games often need extra `PAUSE` lines to stay playable.

## The decompiler

The decompiler takes any `.p` file and turns it back into text you can read, edit and reload.

- **BASIC listing:** every line is written out with the same escapes the compiler uses (`\hh`, `\'.`, `%A`, see above), and checked byte by byte. Lines that can't be written as normal text, such as machine code hidden in a `REM`, are written entirely as `\hh` escapes, so the listing turns back into an identical program. In this listing `\hh` may appear anywhere in a line, not only inside strings. Lines starting with `#` are comments (the decompiler puts its notes and the saved variables there).
- The listing is ZX81 BASIC, so it runs as normal interpreted BASIC once turned back into a `.p`. Feeding it to the compiler only works for programs that keep to the compiler's integer BASIC.
- **Machine code:** it finds machine code wherever it is: in line 0, in any `REM` line, split across several lines, or elsewhere in RAM (for example in the variables area or past the end of the program). It starts from every `USR` call it can work out, including `USR 16514`, `USR VAL "16514"`, `USR (PEEK 16396+256*PEEK 16397)` and variables saved with the program, then follows jumps and calls. The result is an `.asm` listing with labels, ROM and system-variable names, and `DB` lines for data. It reassembles to the same bytes.
- **Programs made by the compiler:** these are recognised, and the original BASIC is rebuilt from the machine code (with the original line numbers when the program used computed GOTO/GOSUB). The rebuilt BASIC is recompiled and compared with the file, and the report says whether the result is identical.
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
  - *Memory map:* a coloured bar and a table of the regions: system variables, each BASIC line, the display file, the variables and the edit line. The free memory and stack are worked out for a 1K, 2K, 4K, 16K or 32K machine. RAMTOP isn't saved in a `.p` file, so you choose the machine.
- **Listing:** the BASIC with ZX81 characters translated: keywords, inverse video (shown highlighted) and block graphics as Unicode. The hidden 5-byte numbers that follow every number in a line are hidden by default; a tick box shows them as `{=value}` and marks with `!` any that differ from the digits shown, a common trick. Machine code in `REM` lines is shown as Z80 instructions.
- **Screen:** the display file drawn as the ZX81 would show it (32×24), at a choice of sizes. Collapsed 1K display files are handled. Save it as a picture or as text.
- **Variables:** every variable saved with the program, decoded: numbers, strings, arrays and FOR loops (with their limit, step and loop line).
- **Hex / Z80:** a hex editor and a Z80 disassembly side by side, kept in step. Traced machine code is shown in green. ROM routines and system variables are labelled. Go to any address, edit bytes (double-click a row), then **Save .p as**.
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
- Variables are not stored in the `.p` file. They live in free RAM above the BASIC edit line and are cleared when the program starts. This keeps the file small, so it loads quickly from tape.
- The code never uses IX, IY or the alternate registers, so it runs safely in SLOW mode with the display on.

### Decompiler and browser

- `.p` files are loaded into a 64K memory image at 4009h, so the tools use real ZX81 addresses throughout.
- The listing is rebuilt from the tokens. Each line is turned back into bytes with the same tokenizer and compared with the original, which is how lines that need `\hh` escapes are spotted.
- Machine code is found by a Z80 disassembler that follows jumps and calls from each entry point. It knows about ROM calculator calls (`RST 28h`) and error calls (`RST 08h`), which are followed by data bytes.
- Compiled programs are recognised by the compiler's start-up code. Each runtime routine is matched against the compiler's own templates, and the program code between them is read back into BASIC statements.

[CLAUDE.md](CLAUDE.md) has the detailed internals: memory layout, register conventions, and how to add statements or runtime routines.

## Testing

`tests/` has test listings, a Python harness and C# test drivers. `tests/run_tests.sh` rebuilds everything and then:

- runs compiled programs on a Z80 emulator and prints the ZX81 screen as text;
- cross-checks the assembler against an independent disassembler;
- decompiles every compiled test program, checks that the rebuilt BASIC recompiles to identical code, and checks that each listing turns back into an identical `.p` file;
- makes a `.wav` and a `.tzx` and decodes them again.

```sh
pip install z80
tests/run_tests.sh                     # all tests
tests/run_tests.sh ~/zx81files         # also round-trip a folder of your own .p files
python3 tests/run81.py tests/t2.p      # show the screen of a test program
```

The harness uses a stub ROM. Loading a `.p` file and real ROM behaviour should also be checked in a real emulator such as EightyOne.

## Project layout

```
src/Core/        shared code: compiler, .p file reader, listing, Z80 disassembler,
                 machine code finder, BASIC recovery, tape output
src/Compiler/    compiler GUI and command line
src/Decompiler/  decompiler GUI and command line
src/Browser/     .P File Browser
src/UI/          shared Windows Forms helpers and ZX81 screen/graphics drawing
tests/           test listings, emulator harness, test drivers
examples/        fortress.txt (small game), feature_test.txt
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
