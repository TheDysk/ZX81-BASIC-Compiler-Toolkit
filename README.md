# ZX81 BASIC Toolset

Three Windows tools for **Sinclair ZX81** programs and `.p` files:

| Tool | What it does |
|---|---|
| **ZX81 BASIC Compiler** | Compiles a ZX81 BASIC text listing into fast **Z80 machine code** and saves a ready-to-load `.p` file. |
| **ZX81 Decompiler** | Reads a `.p` file (or a tape: `.wav`, `.tzx`, `.p81`, or a ZX80 `.o`), finds the BASIC and any machine code, and writes it back out as a BASIC text listing, a new `.p` file and a Z80 `.asm` listing. For programs made by the compiler it also rebuilds the original BASIC. |
| **.P Toolkit** | Looks inside a `.p` file or tape: system variables, memory map, listing, screen, variables, hex editor with Z80 disassembly, graphics ripper; edits hidden numbers and REM machine code; exports to text, `.wav`, `.tzx`, `.p81` and ZX80 `.o`; plays the tape sound to a real ZX81; runs the program in an emulator. |

A compiled program looks like this on the ZX81:

```
0 REM <compiled machine code>
10 RAND USR 16514
```

Load it in an emulator such as EightyOne, or on a real ZX81 with a 16K, 32K or 64K RAM pack. By default it starts by itself after loading.

## Download and run

Build the tools yourself (below), or take the executables from a release. They need .NET Framework 4, which is already part of Windows 10/11.

| File | What it is |
|---|---|
| `ZX81BasicCompiler.exe` | Compiler, Windows GUI |
| `zx81c.exe` | Compiler, command line |
| `ZX81Decompiler.exe` | Decompiler, Windows GUI |
| `zx81dc.exe` | Decompiler, command line |
| `ZX81PToolkit.exe` | .P Toolkit, Windows GUI |

All three GUIs accept files dragged onto their windows.

### Emulator set-up

The compiler (**Compile & Run**, Ctrl+F5, or `zx81c --run`) and the .P Toolkit (**Run in Emulator**, Ctrl+F5) can start an emulator such as EightyOne with the program. Choose it once with **Options → Configure Emulator...** in either program: the emulator's `.exe` and its arguments, where `{file}` stands for the `.p` file (the default is `"{file}"`). The settings are saved in `ZX81Toolset.ini` next to the programs, or in `%APPDATA%\ZX81Toolset\ZX81Toolset.ini` if that folder can't be written to:

```ini
[Emulator]
Path=C:\Emulators\EightyOne\EightyOne.exe
Arguments="{file}"
```

The environment variable `ZX81TOOLSET_INI` can point at another settings file.

## The compiler

### Features

- Windows GUI with an editor, compile button (F5), and clickable error and warning messages. Listings can be dragged onto the window.
- Command-line compiler (`zx81c.exe`) for scripting and batch use.
- ZX81 graphics and inverse characters through simple escapes in the text listing.
- Every ZX81 BASIC keyword is accepted, including `SIN COS TAN ASN ACS ATN LN EXP PI`, `LPRINT`, `COPY`, `SAVE`, `LOAD` and `NEW`.
- Compiled programs write straight to the display file, so they are much faster than interpreted BASIC.
- **Three ways to work out numbers** (the **Numbers** box, `--float`, or a line in the listing; see [Numbers](#numbers-full-floating-point-float-islands-or-integer-only)):
  - **Full floating point** (the default): exactly as the ZX81, using the ROM's own calculator for all arithmetic. 7/2 is 3.5 and variables keep fractions.
  - **Float islands:** fast 16-bit whole numbers, with floating point only in the expressions that need it.
  - **Integer only:** whole numbers only. Anything that would need floating point is an error.
- The machine code (line 0) needs nothing but the ZX81 ROM: keyboard, FAST/SLOW, the calculator, the printer and the tape routines.
- **Compile & Run** (Ctrl+F5) saves the `.p` file and starts the emulator (see [Emulator set-up](#emulator-set-up)).
- **Labels instead of line numbers:** write `@loop:` and `GOTO @loop`, and leave the numbers out (see [Labels](#labels-instead-of-line-numbers)). **Tools → Renumber Lines** and **Tools → Convert Labels to Line Numbers** tidy a listing up.
- **Character palette** (**Tools → Character Palette**, F8, or the **Characters** button): every ZX81 character, block graphic and inverse character; click one to type its escape at the cursor.
- Programs can be compiled for a 16K, 32K or 64K RAM pack. With 32K or 64K, the variables, arrays and strings use the RAM above 32K (see [RAM packs](#ram-packs-16k-32k-and-64k)).

### Using the compiler

GUI: open a listing (`.txt`/`.bas`/`.b81`) or type one in, press **F5**, and choose where to save the `.p` file. **Help → How it works** (F1) lists everything in detail.

When a listing is opened, the compiler does a trial compile and shows the **expected size** straight away: machine code, variables, and how much of the RAM it uses. It also says whether the program fits a 16K ZX81 (or the RAM chosen in the RAM box), and if not, whether a 32K or 64K RAM pack would do. After compiling, both the window and `zx81c` show the sizes in bytes and K, for example:

```
Compiled 25 BASIC lines for a ZX81 with 16K RAM.
Machine code: 2,778 bytes (2.71K) at 16514-19291.
Variables: 579 bytes (0.57K) at 21129-21707 (made when the program runs; not in the .p file).
Memory: 5,324 bytes (5.20K) of the 16K RAM used (program, screen, workspace and variables); about 10,548 bytes (10.30K) left.
.p file: 3,712 bytes (3.63K).
```

If a program doesn't fit, the sizes are still shown with the error.

The compiler can also open a program or tape: a `.p`, `.81` or `.p81` file, a ZX80 `.o`/`.80` file, a `.wav` recording or a `.tzx` image (see [Tapes, .p81 and ZX80 files](#tapes-p81-and-zx80-files)). Its BASIC is shown as a new, unsaved listing: for a program made by this compiler, the BASIC rebuilt from its machine code, which compiles back to the same code; otherwise the BASIC as stored. `zx81c` accepts the same files as input.

In every program's Open dialog, the file type list starts with all the types that program can open together, then each type on its own, then All files.

Command line:

```
zx81c listing.txt [output.p] [--float full|islands|int] [--labels] [--no-autorun] [--no-break] [--lower-inverse] [--maxstr N] [--ram 16|32|64] [--asm listing.asm] [--run] [--emulator path]
```

| Option | Meaning |
|---|---|
| `--float full` | Full floating point, as the ZX81 (the default). See [Numbers](#numbers-full-floating-point-float-islands-or-integer-only). In the GUI this is the **Numbers** box. |
| `--float islands` | Float islands: fast whole numbers, floating point only where an expression needs it |
| `--float int` | Integer only: floating point is a compile error. `--strict-int` means the same. |
| `--labels` | Lines may leave out their numbers, and `@name:` labels them (see [Labels](#labels-instead-of-line-numbers)). Also switched on by a `#LABELS` line or any `@name:` in the listing. |
| `--no-autorun` | Don't start automatically after LOAD (type `RUN`) |
| `--no-break` | Don't stop the program when SPACE (BREAK) is pressed. Use this for games that use SPACE. |
| `--lower-inverse` | Lower-case letters inside strings become inverse video |
| `--maxstr N` | Capacity of ordinary string variables, 1–255 (default 255). Lower it if memory runs out. |
| `--ram 16\|32\|64` | The RAM pack the program is for (default 16). See [RAM packs](#ram-packs-16k-32k-and-64k). In the GUI this is the **RAM** box. |
| `--asm file` | Also write the generated assembly listing |
| `--run` | After a successful compile, start the emulator set up in `ZX81Toolset.ini` with the `.p` file. Exit code 3 if it can't be started. |
| `--emulator path` | Start this emulator instead (implies `--run`; not saved) |
| `--?` | List the options with a line about each (also `-?`, `/?`, `-h`, `--help`) |

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

The **Tools → Character Palette** (F8) shows all 64 characters and their 64 inverse versions as the ZX81 draws them. Clicking one types what the listing needs for it at the cursor: the character itself, `%A` for an inverse letter, a Unicode block graphic (`▘`, `█` …), `""` for a quote, or `\hh` for the rest.

### Labels instead of line numbers

A listing can use labels for `GOTO` and `GOSUB` and leave out its line numbers:

```basic
@start:
CLS
LET N=0
@loop: LET N=N+1
PRINT N;" ";
IF N<5 THEN GOTO @loop
GOSUB @beep
GOTO @start
9000 @beep: PRINT "BEEP"
RETURN
```

- `@name:` defines a label, on a line of its own (it labels the next line) or in front of a statement. Names are letters, digits and `_`, and are not case sensitive.
- `@name` anywhere else in a statement, outside strings, becomes that line's number. It works after `GOTO`/`GO TO`, `GOSUB`/`GO SUB`, `IF … THEN`, `RUN` and `LIST`, and in expressions (`GOTO @table+N*10`).
- **Mixed numbering:** lines without a number get one, 10 more than the line before (10 for the first). Explicit numbers are kept (`9000 @beep:` above). If unnumbered lines would run into the next explicit number, they are spaced out evenly in between.
- Labels are used when the listing has an `@name:`, a line `#LABELS`, or with `--labels`. Then unnumbered lines are numbered even without any labels.
- Errors name the text line they are on: a label that isn't defined, one defined twice (with the line of the first one), no room to number lines between two explicit numbers, or numbers past 9999.

**Tools → Convert Labels to Line Numbers** replaces the labels with the numbers, giving an ordinary listing. **Tools → Renumber Lines** asks for the first number and the step, and renumbers the whole listing (numbering a labelled one first). `GOTO`, `GO SUB`, `THEN`, `RUN`, `LIST` and `LLIST` followed by a line number are changed to match; a number with no line of its own goes to the next line after it, as on the ZX81. A calculated target such as `GOTO 100+N` can't be changed, so each one gets a warning to check it.

### Supported BASIC

**Statements:** `LET` (optional), `PRINT` (with `;` `,` `AT` `TAB`), `IF … THEN`, `GOTO`, `GOSUB`, `RETURN`, `FOR … TO … STEP`, `NEXT`, `DIM`, `INPUT`, `POKE`, `PLOT`, `UNPLOT`, `SCROLL`, `CLS`, `PAUSE`, `RAND`, `STOP`, `CLEAR`, `RUN`, `FAST`, `SLOW`, `REM`, `LPRINT`, `COPY`, `SAVE`, `LOAD`, `NEW`.
GOTO and GOSUB can use a calculated line number, e.g. `GOTO 100*N`.

- `LPRINT` (with `;` `,` `AT` `TAB`) and `COPY` print on the ZX printer through the ROM. With no printer attached, the ROM simply carries on.
- `SAVE "name"` saves the program to tape, as the ZX81 does, and then the program carries on.
- `LOAD "name"` (or `LOAD ""`) loads another program. That program then runs if it auto-runs, as on the ZX81.
- `NEW` wipes the program, as on the ZX81.
- `LIST`, `LLIST` and `CONT` are accepted but do nothing, with a warning. A compiled program has no BASIC to list (the only BASIC in memory is the machine code line), and nothing stopped to continue.

**Functions and operators:** `ABS SGN INT SQR PEEK USR CODE LEN VAL CHR$ STR$ INKEY$ RND PI SIN COS TAN ASN ACS ATN LN EXP`, `NOT AND OR`, `+ - * / **`, and comparisons on numbers and strings. String slicing works: `A$(2)`, `A$(2 TO 5)`, `A$( TO 3)`, `A$(4 TO )`. Numeric arrays can have 1 or 2 dimensions; string arrays work like on the ZX81 (`DIM A$(n,m)`).

### Numbers: full floating point, float islands or integer only

ZX81 BASIC does all its arithmetic in 5-byte floating point. That is accurate, but slow. The compiler can work out numbers in three ways. Choose one with the **Numbers** box on the options bar, with `--float` on the command line, or with a line of its own anywhere in the listing:

| Mode | How to choose it | What it does |
|---|---|---|
| **Full floating point** (default) | **Numbers: Full floating point**, `--float full`, or `#FULL_FLOAT` | Exactly as the ZX81. Every number is a 5-byte floating-point number and all arithmetic is done by the ROM's calculator. |
| **Float islands** | **Numbers: Float islands**, `--float islands`, or `#FLOAT_ISLANDS` | Fast. Numbers are 16-bit whole numbers worked out by the Z80. Only expressions that need it use the ROM calculator (see below). |
| **Integer only** | **Numbers: Integer only**, `--float int` (or `--strict-int`), or `#STRICT_INT` | Whole numbers only. Anything that would need floating point is a compile error. |

A directive line in the listing overrides the box or the option, and opening a listing that has one sets the box to match. The decompiler writes the right directive at the top of the BASIC it rebuilds, so it compiles the same way again. The examples start with `#FLOAT_ISLANDS`, because they were written to be fast.

#### Full floating point

Numbers behave exactly as on a ZX81: `7/2` is 3.5, `LET X=2.5` keeps 2.5, `FOR I=0 TO 1 STEP 0.1` works, comparisons see the fractions, and `AND`, `OR` and `NOT` follow ZX81 rules (`3 AND 5` is 3, `0 OR 7` is 1). `RND` uses the ZX81's own formula and seed (`SEED`, 4032h), so after `RAND 1` it gives the same numbers as a real ZX81, and `USR` starts the machine code with BC holding its address, as the ROM does. Maths errors stop the program with the ZX81 report code (`1/0` gives `6`, `SQR -1` gives `A`). The test suite runs programs both compiled this way and in the ROM's own BASIC interpreter, and the screens must match.

- Arithmetic runs at about the speed of ZX81 BASIC's, because the same ROM routines do it. Everything else is compiled machine code as usual: `PRINT`, `GOTO`, `IF`, string handling, the work of a `FOR` loop. So programs are still faster than interpreted BASIC, but not by as much as with float islands.
- Values that must be whole numbers (`PLOT`, `AT`, `TAB`, `POKE`, `GOTO`, array subscripts, `CHR$` …) are rounded to the nearest whole number, as the ZX81 does.
- Each numeric variable takes 5 bytes and each array element 5 bytes (2 with float islands), so arrays take more room.

#### Float islands

Numbers are 16-bit whole numbers (−32768 to 32767), which is what makes compiled programs fast. An expression is worked out in **floating point** instead when it uses any of these:

- `SIN COS TAN ASN ACS ATN LN EXP SQR PI VAL`;
- `RND` on its own;
- a number with a decimal point, such as `0.5` or `1.0`, or one too big for 16 bits.

The ZX81 ROM's own calculator does the work, so the result of the island is exactly what a ZX81 gives:

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
- **Variables hold whole numbers**, so `LET A=H*PI/6` stores 1, not 0.5236. The compiler warns when this happens. Keep the angle inside the expression instead, e.g. `COS (H*PI/6)`, or use full floating point. [examples/clock.txt](examples/clock.txt) does this.
- Floating point runs at the speed of the ZX81's own maths, which is slow, so keep it out of the busiest loops.

#### Integer only (strict integer mode)

Everything that would start a float island is a compile error naming the line and the cause: `SIN`, `COS` … `PI`, `VAL`, `RND` on its own, and numbers that aren't whole (`2.5`). A whole number written with a decimal point, like `34.0`, is allowed. Use it for games and other programs that must stay fast, so nothing slow can creep in unnoticed. The code is the same as with float islands.

```
ERROR  Line 10: SIN needs floating point (the ROM calculator), but the numbers are set to integer only (strict integer mode: #STRICT_INT, --float int)
ERROR  Line 40: the number 2.5 is not a whole number, but the numbers are set to integer only (strict integer mode: #STRICT_INT, --float int)
```

#### In all three modes

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

- **Float islands and integer only** (not full floating point): outside floating point, division truncates (`7/2` is 3, `-7/2` is −3) and `2**-1` is 0 (write `7/2.0` to get 3.5). Variables hold whole numbers. `RND*n` gives a fast whole number from 0 to n−1, so `INT (RND*6)+1` works as expected, and `RND` on its own gives a fraction (0 to 0.99998), but not the ZX81's sequence.
- **Variables** start at 0 instead of giving "variable not found". Ordinary string variables hold up to 255 characters.
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
- **Programs made by the compiler:** these are recognised, and the original BASIC is rebuilt from the machine code (with the original line numbers when the program used computed GOTO/GOSUB). Everything the compiler produces is understood, in all three number modes, including floating point, `LPRINT`, `COPY`, `SAVE`, `LOAD` and `NEW`. The decompiler sees from the code whether the program was compiled with full floating point or float islands (integer only makes the same code as float islands), says so in the report, and starts the rebuilt BASIC with `#FULL_FLOAT` or `#FLOAT_ISLANDS` so it compiles the same way. The rebuilt BASIC is recompiled and compared with the file, and the report says whether the result is identical.
- **New `.p` file:** made from the listing, or from the rebuilt BASIC. The original screen and saved variables can be kept.

### Using the decompiler

GUI: open a `.p` file (Ctrl+O, or drag it onto the window). Tapes and the other formats in [Tapes, .p81 and ZX80 files](#tapes-p81-and-zx80-files) open the same way; the Report tab then starts with where the program came from (for a `.wav`, the time it was found in the recording). The tabs show:

| Tab | Contents |
|---|---|
| Report | What was found: lines, machine code blocks and where they start, autorun, problems |
| Listing | The BASIC listing (editable) |
| BASIC (rebuilt) | Only for programs made by the compiler: the recovered BASIC source (editable) |
| Machine code | The `.asm` listing |

The **File** menu saves the listing (`.txt`), the rebuilt BASIC (`.bas`) and the machine code (`.asm`), and **Make .p file from this tab** turns the Listing or BASIC tab into a new `.p` file. The rebuilt BASIC can also be opened in the compiler and compiled again.

### Control files (.ctl)

A control file tells the decompiler what it can't work out for itself, and names things in the `.asm`. It sits next to the program with the same name (`game.p` and `game.ctl`) and is then used automatically; `zx81dc --ctl file` or **File → Load Control File** uses another. One directive per line; lines starting with `;` or `#` are comments. Addresses are decimal (`16514`) or hex (`4082h`, `0x4082`, `$4082`):

```
; game.ctl
LABEL   4082h main          name an address (instead of L4082)
LABEL   4100h draw_ship
COMMENT 4090h wait for a key   a comment line above that address
CODE    4200h               code that no USR reaches (a jump table, a stack trick...)
CODE    4300h 4380h         ... and decode the whole range, instruction by instruction
DATA    4400h 64            these 64 bytes are data (DB), never code
```

In the GUI, right-click a line of the **Machine code** tab for **Rename Label** and **Add Comment**. The `.asm` is redrawn at once, and **File → Save Control File** keeps the changes (you are asked before they would be lost). The report says what the control file contains and lists any lines it didn't understand. The `.asm` still reassembles to the same bytes, with your names.

Command line:

```
zx81dc program.p [--txt file] [--basic file] [--asm file] [--p file] [--p-from listing|basic] [--extract file] [--ctl file] [--wav-threshold N] [--wav-hysteresis N] [--wav-invert] [--report]
```

The input can also be a `.wav` recording, a `.tzx`, a `.p81` or a ZX80 `.o`/`.80` file. If a recording can't be read, `zx81dc` prints where it went wrong, e.g. `[00:03.400] the recording ends in the middle of the program (107 of 1893 program bytes read)`, and exits with code 1.

With no options it writes `program.txt`, plus `program.bas` if the BASIC could be rebuilt and `program.asm` if there is machine code.

| Option | Meaning |
|---|---|
| `--txt file` | Write the BASIC listing here |
| `--basic file` | Write the rebuilt BASIC here (compiled programs only) |
| `--asm file` | Write the machine code listing here |
| `--p file` | Also write a new `.p` file |
| `--p-from listing\|basic` | Make that `.p` from the listing (default) or from the rebuilt BASIC |
| `--extract file` | Write the program exactly as read from the input as a `.p` file (e.g. to turn a `.wav` into a `.p`) |
| `--ctl file` | Use this control file (default: `program.ctl` next to the program, if there is one) |
| `--wav-threshold N` | Reading a `.wav`: the noise threshold, in % of the loudest sound (default 20). Quieter than this counts as silence. Lower it for a recording that fades or has a loud click; raise it if hiss is read as pulses. |
| `--wav-hysteresis N` | Reading a `.wav`: how far, in % of the signal, the level must swing to count (default 15). Raise it for ripples on the pulses, lower it for weak, rounded pulses. |
| `--wav-invert` | Reading a `.wav`: turn the recording upside down first |
| `--report` | Print the report (it includes the number mode the program was compiled with) |
| `--?` | List the options with a line about each (also `-?`, `/?`, `-h`, `--help`) |

The decompiler needs no number-mode option: it reads the mode from the program's code.

Limits:

- Machine code is only found if something reaches it: a `USR` call the decompiler can evaluate, or a jump or call from code it has already found. Code started in other ways, such as by a `POKE` into the stack, is shown as data, unless a [control file](#control-files-ctl) marks it with `CODE`. The .P Toolkit's **Decode everything as Z80** switch shows it anyway.
- Rebuilding BASIC only works for programs made by this compiler. Other machine code is given as assembly.

## The .P Toolkit

Open a `.p` file (Ctrl+O, or drag it onto the window), or a tape or other format (see [below](#tapes-p81-and-zx80-files)). If a `.wav` can't be read, a message says what went wrong and when in the recording, as `[mm:ss.ms]`. The tabs:

- **Overview**
  - *Auto-run detector:* whether the program starts by itself after LOAD and at which line (from `NXTLIN`).
  - *System variables* `4009h`–`407Ch`, each with its value and a plain-English explanation, for example where the display file is, or whether the display file is collapsed (1K) or expanded (16K).
  - *Memory map:* a coloured bar and a table of the regions: system variables, each BASIC line, the display file, the variables and the edit line. The free memory and stack are worked out for a 1K, 2K, 4K or 16K machine, or a 32K or 64K RAM pack. RAMTOP isn't saved in a `.p` file, so you choose the machine; the .P Toolkit picks the smallest that fits. For programs compiled for 32K/64K, the map shows their variables above 32K.
  - *Warnings* for anything a real ZX81 can't do with the file, whatever RAM is fitted: a display file above 32K, machine code above 32K, or a file that needs RAMTOP raised before loading.
- **Listing:** the BASIC with ZX81 characters translated: keywords, inverse video (shown highlighted) and block graphics as Unicode. The hidden 5-byte numbers that follow every number in a line are hidden by default; a tick box shows them as `{=value}` and marks with `!` any that differ from the digits shown, a common trick. Machine code in `REM` lines is shown as Z80 instructions.
  - *Edit a hidden number:* with the hidden numbers shown, double-click a `{=value}` (or right-click it). Type a new value or the 5 bytes in hex; only the hidden number changes, not the digits in the listing, which is exactly the trick some programs use.
  - *Inject Binary into REM:* right-click a `REM` line and choose a `.bin` file. Everything after the `REM` is replaced by the file's bytes, and the lines after it, the display file and the variables move along, with `D_FILE`, `DF_CC`, `VARS`, `E_LINE`, `NXTLIN` and the other pointers worked out again. The status bar says where the code now starts (16514 for the first line). Injecting the same bytes back gives an identical file.
- **Screen:** the display file drawn as the ZX81 would show it (32×24), at a choice of sizes. Collapsed 1K display files are handled. Save it as a picture or as text.
  - *Draw on it:* tick **Draw**, choose a character in the strip of all 128 (or right-click the screen to pick one up from it), then click or drag on the screen. The characters are changed in place in the display file, so no system variable moves; **File → Save as .p** keeps them. This works on an expanded (16K style) display file; a collapsed one has lines of different lengths, so it is refused.
- **Variables:** every variable saved with the program, decoded: numbers, strings, arrays and FOR loops (with their limit, step and loop line).
- **Hex / Z80:** a hex editor and a Z80 disassembly side by side, kept in step. Traced machine code is shown in green. ROM routines and system variables are labelled, and the bytes after `RST 28h` and `RST 08h` are explained (calculator operations, error reports). Go to any address, edit bytes (double-click a row), then **Save .p as**.
- **Graphics:** shows any part of memory as 8×8 tiles, to find and rip character sets and user-defined graphics as used by hi-res and custom-font programs. Likely character sets are found automatically. Save as `.png`, `.bin` or `.asm` (`DB` lines).
  - *Edit a tile:* click a tile to load it into the 8×8 editor on the right, then click or drag over its pixels (the first click decides whether you are setting or clearing). **Clear**, **Invert**, **Mirror** and **Flip** help, **Revert** goes back, and **Apply** writes the 8 bytes back where they came from.
  - *Add as new REM line:* puts the tiles shown (or just the one being edited), with your edit, into a new `REM` line, leaving the original bytes alone. The rest of the program, the display file and the variables move along, with `D_FILE`, `VARS`, `E_LINE` and the other system variables worked out again; the status bar says where the bytes now start.

The **Export** menu has:

- the BASIC listing as UTF-8 text with zmakebas-style `\..` block-graphics escapes (the same format as the decompiler's listing, so it can be turned back into an identical `.p`);
- a hex dump;
- a Z80 disassembly (`.asm`);
- the screen as `.png` or text;
- cassette audio (`.wav`) to play into a real ZX81's EAR socket after typing `LOAD ""`;
- a `.tzx` tape image for emulators;
- a `.p81` file (the program with its tape name);
- a ZX80 program (`.o`), when the program only uses what a ZX80 has (see below);
- **Play Audio to ZX81** (also on the toolbar): plays the tape sound through the computer's sound output, with a progress bar and a Stop button. Connect it to the ZX81's EAR socket, type `LOAD ""`, then press Play.

**Options → Audio Import Options** changes how `.wav` recordings are read, for ones that don't read with the standard settings: the noise threshold (default 20% of the loudest sound), the hysteresis (default 15%) and inverting the recording. They apply to every `.wav` opened until the Toolkit is closed, and you can read the last `.wav` again straight away. `zx81dc` has the same settings as `--wav-threshold`, `--wav-hysteresis` and `--wav-invert`.

**Run in Emulator** (Ctrl+F5, File menu and toolbar) starts the emulator set up in [Options → Configure Emulator](#emulator-set-up). An unchanged `.p` file is run from where it is; an edited program, or one opened from a tape, is run from a temporary copy.

Edited programs are saved with **File → Save as .p**.

## Tapes, .p81 and ZX80 files

Both the decompiler and the .P Toolkit open these as well as `.p` and `.81` files:

| Format | Notes |
|---|---|
| `.wav` | A recording of a ZX81 tape (or one made by the .P Toolkit). 8, 16, 24 or 32-bit PCM or 32-bit float, mono or stereo (the channels are mixed), any sample rate from about 11 kHz. The decoder follows the signal's level, so quiet recordings, a DC offset, inverted polarity, noise and tape running a little fast or slow are fine. If a recording can't be read, the error gives the time where it went wrong as `[mm:ss.ms]`: where the signal was lost, a bit that can't be read, or where the recording ends in the middle of the program. |
| `.tzx` | Tape images: standard (10h), turbo (11h), pure data (14h), generalized data (19h, the ZX81 format) and direct recording (15h) blocks. Other blocks are skipped. |
| `.p81` | The tape name followed by the `.p` data. |
| `.o`, `.80` | ZX80 (4K ROM) programs. They are converted to ZX81 BASIC: tokens and characters are translated, `RND(n)` becomes `(1+INT (RND*(n)))` and the variables are kept. ZX80 BASIC is whole-number only, so compile the result with the compiler (rather than running it as ZX81 BASIC) to keep the ZX80's arithmetic. |

If a tape holds several programs, the first is opened and the others are listed.

A `.wav` that won't read with the standard settings can often be read with others: see **Options → Audio Import Options** in the .P Toolkit, or `--wav-threshold`, `--wav-hysteresis` and `--wav-invert` in `zx81dc`. A loud click at the start of a quiet recording, for example, makes the real signal look like silence; a lower noise threshold (say 5%) reads it.

Exporting to a ZX80 `.o` file works the other way, for programs that only use what a ZX80 has: no `PLOT`, `SIN` and other floating-point functions, no numbers with a decimal point, line numbers up to 9999. Every line that can't be converted is listed. Variables that a ZX80 can't hold are left out, with a note.


## Building from source

There are no dependencies and no NuGet packages. The code is C# 5, targeting .NET Framework 4.

- **Windows:** run `build.bat`. It uses the `csc.exe` that ships with .NET Framework, so Visual Studio isn't needed.
- **Linux/macOS:** install Mono and run `./build.sh`. The resulting `.exe` files run on Windows.

Output goes to `build/`: all five executables.

### Releases

`.github/workflows/release.yml` builds a release on GitHub when a version tag is pushed (`git tag v1.01`, then `git push origin v1.01`). It runs `tests/run_tests.sh` on Linux (the tests that need the ZX81 ROM are skipped, because the ROM can't be put in the repository), builds the five programs on Windows with `build.bat`, and publishes `ZX81-Toolkit-win.zip` (the programs, `README.md`, `LICENSE` and `examples/`) as a GitHub Release.

## How it works

### Compiler

`listing → Lexer/Parser → AST → Z80 assembly text → built-in two-pass assembler → .p file`

- Numeric expressions are evaluated in HL as 16-bit integers. Strings are address/length pairs, with temporary buffers for joined strings.
- A runtime library written in Z80 assembly handles printing, strings, maths, PLOT, INPUT and so on. Only the routines a program uses are included.
- Variables are not stored in the `.p` file. They live in free RAM above the BASIC edit line and are cleared when the program starts. This keeps the file small, so it loads quickly from tape. Programs that use the ROM calculator leave 768 bytes more room before the variables for the calculator stack and the ROM's workspace.
- Floating-point parts of expressions ("float islands") put their values on the ROM calculator's stack and call the calculator (`RST 28h`) for each operation. The result is rounded back into HL, or turned into text by the ROM's `STR$`. The rest of the program never touches the calculator.
- In full floating point every numeric expression is worked out that way. Variables, array elements and `FOR` values are 5-byte numbers, put on the calculator stack and stored back with small runtime routines, and `FOR`/`NEXT` compare with the calculator too. Only `PEEK`, `CODE`, `LEN`, `USR`, string comparisons and constants are worked out in HL first.
- The compiler's own code never uses IX, IY or the alternate registers, so it runs safely in SLOW mode with the display on. The ROM's calculator, printer and tape routines are the ROM's own and are safe too.

### Decompiler and .P Toolkit

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
- checks strict integer mode (a whole-number program compiles identically with `#STRICT_INT`; a floating-point one is refused), `--float` and the directives, and `--run` with a stand-in emulator;
- compiles the full floating point tests `f1`–`f6` and runs each, and the same BASIC in the ZX81 ROM's own interpreter (`tests/cmpbasic.py`, using `run81.py --basic`): the screens and any error report must be identical. That covers arithmetic and functions, fractions in variables, arrays and `FOR` loops, `RND` after `RAND`, `USR`, rounding for `AT`/`PLOT`/`CHR$`, and errors 6 and A. Every test program is also compiled in full floating point and decompiled;
- turns `.p` files into `.wav`, `.tzx` and `.p81` and back, byte for byte, in the C# test `tests/cs/TapeTest.cs` and again through `zx81dc --extract`. The `.wav` tests include 8-bit stereo, 24-bit and float recordings, inverted, quiet, noisy and fast ones;
- damages recordings (a dropout, a burst of noise, a cut-off end, silence) and checks each is reported at the right `[mm:ss.ms]` time;
- converts ZX81 programs to ZX80 `.o` files and back, and a hand-made ZX80 file to ZX81 BASIC;
- injects binaries of other sizes into `REM` lines and checks the system variables, and that putting the original back gives an identical file; checks the hidden-number positions match the listing;
- reads quiet `.wav` recordings with a loud click, which fail with the standard settings, with a lower noise threshold (`AudioOptions`, `zx81dc --wav-threshold`);
- labels (`tests/cs/LabelTest.cs`): a labelled listing compiles to exactly the same code as the numbered one; mixed numbering; every label error with its text line; renumbering (the renumbered program compiles to identical code; calculated GOTOs are warned about);
- control files (`tests/cs/CtlTest.cs`): parsing every address form, `LABEL` and `COMMENT` in the `.asm`, `CODE` tracing and range decoding, `DATA` stopping tracing, and the `.asm` reassembling to the same bytes; then `zx81dc` with `program.ctl` and with `--ctl`;
- the .P Toolkit's editors (`tests/cs/EditTest.cs`): drawn screen characters land at `D_FILE+1+row*33+col` with no system variable moving; a written-back tile changes only its 8 bytes; a new `REM` line of tiles moves `D_FILE`, `VARS`, `E_LINE`, `STKBOT` and `STKEND` by exactly its size, keeps `DF_CC`, the display file, the variables and the auto-run line, and errors are refused (collapsed display, keyword codes, an existing line number).

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
src/Core/        shared code: compiler, labels and renumbering, .p file reader, listing,
                 Z80 disassembler, machine code finder, control files, BASIC recovery,
                 tape writing and reading (.wav/.tzx/.p81), ZX80 conversion, emulator
                 settings, .p edits made by the Toolkit
src/Compiler/    compiler GUI and command line
src/Decompiler/  decompiler GUI and command line
src/Browser/     .P Toolkit
src/UI/          shared Windows Forms helpers and ZX81 screen/graphics drawing
tests/           test listings, emulator harness, test drivers, expected results
examples/        fortress.txt (small game), clock.txt (floating point), feature_test.txt
build.sh         build with Mono
build.bat        build on Windows
.github/workflows/release.yml   builds, tests and publishes a release for a v* tag
LICENSE          MIT licence
```

## Author

**Simon Round**

Bug reports and suggestions are welcome via [GitHub Issues](../../issues); pull requests are welcome too.

## Licence

Copyright (c) 2026 Simon Round.

Released under the [MIT License](LICENSE). You are free to use, copy, modify, merge, publish, distribute, sublicense and sell copies of this software, provided the copyright notice and licence text are included. The software is provided "as is", without warranty of any kind.
