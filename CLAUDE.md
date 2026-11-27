# CLAUDE.md

Guidance for working on this repository: a toolset for Sinclair ZX81 `.p` files.

- **Compiler** (`zx81c.exe`, `ZX81BasicCompiler.exe`): ZX81 BASIC text listing → Z80 machine code inside a `.p` file (`0 REM <code>`, `10 RAND USR 16514`).
- **Decompiler** (`zx81dc.exe`, `ZX81Decompiler.exe`): `.p` → exact BASIC text listing, `.asm` of any machine code, a new `.p`; for programs made by our compiler, the original BASIC rebuilt from the machine code.
- **.P Toolkit** (`ZX81PToolkit.exe`): inspector/hex editor/exporter for `.p` files.

All five programs share `src/Core`. The three GUIs also share `src/UI`.

**Versions:** every tool is V1.00. The numbers live in `src/Core/Versions.cs` (shown in titles, About boxes and `--version`) and in the `[assembly: AssemblyVersion]` lines at the top of `src/Compiler/Cli.cs`, `src/Decompiler/DecompilerCli.cs`, `src/Decompiler/DecompilerForm.cs` and `src/Browser/BrowserForm.cs` (stored in the .exe). Change both together when releasing a tool.

## Build and test

```sh
./build.sh                 # Linux/macOS with Mono (mcs) -> all five .exe files in build/
build.bat                  # Windows, uses the csc.exe that ships with .NET Framework 4
tests/run_tests.sh         # rebuild + compiler tests + decompiler round trips + tape check
tests/run_tests.sh DIR     # ... and round-trip every .p file found under DIR
```

Test tooling needs `python3` and `pip install z80` (a Z80 emulator with a disassembler). Floating point, VAL, LPRINT and tape need the real ZX81 ROM in the tests: put the 8K `zx81.rom` in `tests/zx81.rom` (git-ignored, it is copyright: don't commit it) or set `ZX81_ROM`.

- `mono build/zx81c.exe in.bas out.p --asm out.asm` compiles from the command line. The `.asm` file is the full assembly listing with addresses, bytes and the symbol table. Read it first when debugging generated code.
- `python3 tests/run81.py out.p [--frames N] [--keys "frame:KEY,..."] [--break FRAME] [--snap f1,f2] [--rom file | --stub] [--expect file]` runs the program and prints the 32x24 screen as text, then any printer output (`printer:`) and events (`event: report A`, `SAVE "NAME"`, `LOAD ""`, `NEW`). Keys are ZX81 characters plus `NL`, `RUB`, `MINUS` and `SPACE`; each press lasts 5 frames. The last line reports whether the program finished, whether the ROM area was written to, whether the display file is still intact, and the final SP (7FF0 after a normal end). `--expect tests/expected/tN.txt` compares everything with a saved run; make a new one with `run81.py tN.p | sed '$d' > tests/expected/tN.txt` *after* checking the output by eye.
- `python3 tests/asmcheck.py out.asm out.p` disassembles every instruction and compares it with the source line. It must report 0 mismatches after any change to `Z80Asm.cs` or to runtime/codegen instruction forms.

- `mono build/zx81dc.exe prog.p --report --txt out.txt --asm out.asm --p back.p` decompiles. `cmp prog.p back.p` should show no difference.
- `tests/cs/ToolTest.cs` (built and run by `run_tests.sh`) checks for each `.p` given that listing → `.p` is identical, and for programs made by our compiler that the rebuilt BASIC is verified. `ToolTest.exe --tape f.p` writes `/tmp/zx81test.wav`/`.tzx`, and `tests/tapecheck.py f.p wav tzx TEST` decodes both and compares them with name + file. `tests/cs/CoreTest.cs` is an older round-trip driver that prints per-file details.
- `tests/cs/TapeTest.cs` (built and run by `run_tests.sh` as `/tmp/tapetest.exe f.p …`) round-trips each file through `.wav` (several sample formats, quiet/noisy/fast/inverted), `.tzx`, `.p81`, damages recordings and checks the `[mm:ss.ms]` of each error, tests the ZX80 conversion both ways, `HiddenNumberOffsets` and `InjectRem`. `tapetest.exe --write-tapes DIR f.p …` just writes the tape files (and a small ZX80 `.o`) for the `zx81dc --extract` checks. Emulator launching is tested with a stand-in shell script set up through `ZX81TOOLSET_INI`.
- `.asm` from the decompiler should reassemble to identical bytes; check with `pasmo` (`apt install pasmo`, then `pasmo --bin out.asm out.bin` and compare with the bytes at the ORG address).
- GUIs can be smoke-tested on Linux with `xvfb-run mono build/ZX81PToolkit.exe file.p` plus `xdotool`/ImageMagick `import` for screenshots. Mono's WinForms isn't Windows, so layouts should still be checked on Windows.
- Known: one sample file (`frogging-normalized…p`) doesn't round-trip because its VARS pointer is outside the file. Damaged files like that are reported, not fixed.

- 32K/64K: the emulator's memory is a flat 64K of RAM, so programs compiled for 32K/64K just run. `--ramtop C000` sets RAMTOP and the stack as after raising RAMTOP. `--hw16` / `--hw32` single-step the first 20000 instructions and fix memory up after each one to look like a plain 16K ZX81 (8000h-BFFFh = ROM, C000h-FFFFh = 4000h-7FFFh) or a 32K pack (C000h-FFFFh = 4000h-7FFFh): enough for the start-up RAM check. (The emulator's write marks fire too late to emulate this per access.) `tests/mkbig.py` builds large BASIC `.p` files from a small one (big saved arrays, optional code inside them, a bigger first line to push the display file above 8000h); `ToolTest.exe --mkp listing.txt out.p` makes the small one.

**Emulator caveats:** `run81.py` runs the real ROM when it can find one (otherwise a small stub, `--stub`), but always fakes KEYBOARD (02BB), DECODE (07BD), FAST (0F23) and SLOW (0F2B), removes the HALT in SET-FAST (no display interrupts are emulated), decrements FRAMES every 16000 T-states and enters the program directly at 16514. With the real ROM it also stops at breakpoints instead of running: RST 08h errors (reported), COPY-BUFF and COPY (the printer line / screen is recorded, then it returns), SAVE at 02FBh (name recorded, returns), LOAD at 0343h and NEW (reported, run ends). The ROM's skip-constants routine writes 5 bytes at address 0 on purpose (DE = 0); real hardware ignores ROM writes, so the harness puts them back. *Not* verified here: `.p` loading/autorun, the display, real printer and tape signals, and timing. Confirm those in EightyOne or on hardware. (A real SAVE was checked once by letting the ROM run and decoding the bits it chose to send: name and program bytes were correct and it came back to the program.)

## Code language constraints

- The code must stay **C# 5 compatible**, with .NET Framework 4 APIs only. `build.bat` uses the old built-in `csc.exe`. Don't use string interpolation (`$"..."`), `?.`, `nameof`, expression-bodied members, `out var`, tuples or pattern matching. You can check with `mcs -langversion:5`.
- No NuGet packages and no project SDK features. The plain source-file builds are intentional.
- `build.sh` compiles with `-nostdlib` against Mono's **.NET Framework 4.0 reference assemblies** (`/usr/lib/mono/4.0-api`). Keep it that way.
  - Mono's normal libraries contain newer overloads that Windows' .NET Framework lacks, such as `string.TrimStart(char)`, `Split(char)` and `Trim(char)`.
  - Code built against them runs fine under Mono but fails on Windows with "Method not found".
  - Against the reference assemblies, the compiler binds to the `params char[]` versions that exist on Windows.
  - Passing tests under Mono does not prove the code works on Windows. Only the reference-assembly build does.

## Comments

- **Header comments:** every source file starts with one explaining its job.
- **Member comments:** classes, methods and fields have `///` summaries.
- **Assembly comments:** the runtime assembly is commented line by line with `;`. Don't put double quotes in those comments, because they sit inside C# `@"..."` strings.
- **Keeping them current:** update the comments whenever the code changes. They are the main documentation for the project's owner.

## Source layout

| Folder | Contents | Built into |
|---|---|---|
| `src/Core/` | Everything that isn't UI | all five exes |
| `src/Compiler/` | `Cli.cs` (`CliMain`, `ReadListing`), `MainForm.cs` (`Program`, compiler GUI) | zx81c, ZX81BasicCompiler |
| `src/Decompiler/` | `DecompilerCli.cs`, `DecompilerForm.cs` (`DecompilerProgram`) | zx81dc, ZX81Decompiler |
| `src/Browser/` | `BrowserForm.cs` (`BrowserProgram`, menus, opening, exports), `BrowserTabs.cs` (partial class: the tabs, `MemoryMapPanel`, `Rtf` builder), `BrowserExtras.cs` (partial class: Run in Emulator, Play Audio, `.p81`/`.o` export, hidden-number editor, Inject Binary into REM) | ZX81PToolkit |
| `src/UI/` | `UiCommon.cs` (fonts, `ShowHelp` = the Help > How it works window: each line of the help text is a paragraph that wraps to the window, with `# heading`, `- bullet`, `term :: meaning` and 4-space indented examples, rendered as RTF; save dialogs, drag-and-drop, `OpenProgram` = open any format and show tape errors with their `[mm:ss.ms]`, `AboutText`), `ZxRender.cs` (screen/tile bitmaps, ZX81 ROM font, char-set finder), `EmulatorDialog.cs` (`EmulatorUi.Configure`, `EmulatorUi.Run`) | the three GUIs |

Each exe has its own `Main`, selected with `-main:` in `build.sh`/`build.bat`. If you add a source file to a folder, both build scripts pick it up (they compile `src/Core/*.cs` etc.), but check `build.bat` lists any new folder.

## Compiler pipeline (src/Core)

| File | Role |
|---|---|
| `Parser.cs` | `Lexer` (keywords, `GO TO`, strings and their escapes) and recursive-descent `Parser` producing the AST, plus constant folding (`Fold(e, inFloat)`: whole-number folding outside float islands, double inside; `INT` of a whole constant folds away because whole-number INT makes no code). Also has the `Diagnostic` and `CompileError` types. |
| `Ast.cs` | Expression and statement node classes. Every expression has a static type `VT.Num` or `VT.Str`, checked in the parser. Also `FloatMath`: the rules for which expressions are floating point. |
| `Versions.cs` | The version number of each tool. |
| `CodeGen.cs` | AST to Z80 assembly *text* through `Z80Asm`. It holds the variable tables, string temp buffers, literal pool and the line table. |
| `Runtime.cs` | The runtime library as named assembly chunks with dependencies. Only the chunks that are used get emitted. |
| `Z80Asm.cs` | Two-pass assembler for the Z80 subset we use: labels, `EQU`, `DB/DW/DS`, expressions with `+ - * /`, `$`, hex as `0FFh`/`$FF`/`0xFF`. |
| `Compiler.cs` | Orchestration, memory layout and `.p` file writer (system variables, line 0 / line 10, expanded display file). |
| `Charset.cs` | ASCII/Unicode to ZX81 codes, block graphics, token table. |
| `Z80Asm.Undefined` | Optional resolver for unknown symbols (used by `Recover` to assemble runtime chunks on their own). Leave null for normal compiles. |
| `src/Compiler/Cli.cs` | Command-line front end, plus `ReadListing` (UTF-8 with a CP-1252 fallback). |
| `Compiler.Size / SizeLines / Estimate` | Sizes for people ("3,456 bytes (3.38K)"), the summary lines shared by CLI and GUI, and the trial compile the GUI runs when a listing is opened (fits the chosen RAM? would 32K/64K?). `CompileResult.MemoryEnd` is non-zero once the layout was worked out, even if the compile then failed on memory, so sizes can still be shown. |
| `src/Compiler/MainForm.cs` | WinForms GUI. Its `HelpText` is the user-facing list of supported BASIC, so update it when features change. |

## Memory layout of a compiled program

```
4009  system variables (written by Compiler.BuildPFile)
407D  line 0: 00 00 len EA <code...> 76      code starts at 4082h = 16514
      line 10: RAND USR 16514
      D_FILE: fully expanded 793-byte display (24 x (32 + 76h) + leading 76h)
      VARS: 80h ; E_LINE = VARS+1   (the file ends here)
E_LINE+256  "BSS": all variables, created at run time, NOT stored in the .p
            (16K, the default; Options.Ram = 32 / 64 puts BSS at 8000h instead, see below)
```

- Code size doesn't depend on BSS addresses. `Compiler.Compile` assembles once with `BSS=6000h` to measure the code, then sets the real `BSS` and assembles again.
- BSS begins with fixed runtime state (`Runtime.BssEquates`, `BSS_FIXED = 52` bytes: print row/col, RNG seed, scratch, the 32-byte INPUT buffer). User variables come after `VARSTART`. CLEAR zeroes from `VARSTART`; `rt_init` zeroes everything.
- `ENTRYSP` (a `DW` inside the code) stores the SP at entry. `rt_stop` restores it and returns to BASIC, so STOP works from inside GOSUBs. RUN reloads SP from it.
- `rt_memcheck` refuses to run, showing "OUT OF MEMORY", if the stack is within 160 bytes of `BSS_END`. The compile-time limit is `0x8000 - 0x200` (16K machine).
- **32K / 64K RAM packs** (`Options.Ram`, `--ram`, the GUI's RAM box). Hardware rule: a ZX81 can only *run code* and *show the display* below 8000h (above it, the ULA forces NOP onto most M1 fetches); RAM above 8000h is data only, and the ROM sets RAMTOP to at most 8000h at power-up. So for Ram 32/64:
  - code, display, edit line, calculator room and the ROM's machine stack stay below 8000h; `BSS = 8000h` (Compiler.HighVars), up to BFFFh (32) or FFFFh (64), minus StackReserve. `Compiler.CheckMemory` gives the errors (code too big for any ZX81; variables too big for the pack; for 16K, it suggests 32K/64K).
  - the prologue calls `rt_memcheckx` instead of `rt_memcheck`. It tests BSS and BSS_END-1 are RAM (write the complement, read back, restore: on a 16K machine 8000h-BFFFh is ROM echo) and, at C000h+, that the byte isn't the same memory as HL-8000h (16K/32K packs often mirror there). Then the stack must be either below BSS (RAMTOP not raised) or above BSS_END+160 (RAMTOP raised). Otherwise it shows `NEEDS 32K RAM` / `NEEDS 64K RAM`: the digits are the EQUs RAMD1/RAMD2, which Compiler.cs sets from where BSS_END falls (not from the option), so --ram 64 with small variables makes the same code as --ram 32.
  - Recover: `Options.Ram` = 16 if BSS < 8000h, else 32 or 64 by BSS end; its templates give RAMD1/RAMD2 small differing values so they become wildcards.
  - Ram 16 output must stay byte-identical to before (the default path is unchanged).
- **Calculator room:** if `CodeGen.UsesCalc` (any float, VAL, SAVE or LOAD), BSS starts `Compiler.CalcReserve` (768) bytes later, because the ROM's calculator stack and workspace grow up from E_LINE. Those programs also `CALL rt_finit` straight after rt_init; it records STKBOT/STKEND/depth in DW slots inside its own chunk (not in BSS, so BSS_FIXED is unchanged).

## Code generation conventions

- **Numbers:** 16-bit signed, result in **HL**. `GenOperands(l, r)` leaves left in HL and right in DE. It avoids PUSH/POP when one side is a literal or a plain variable.
- **Strings:** a "region" is HL = address, BC = length (always ≤ 255, so **B = 0**). `GenStrRegion(e, f)` produces one.
  - Addressable strings (literals, variables, slices of them) point straight at their data.
  - Anything else (concatenation, `CHR$`, `STR$`, `INKEY$`, `A$ AND n`) is built in temp buffer `T_f` (length byte + 255 bytes). `GenAppend` does the building.
  - **Depth rule:** while a region at depth `f` is still needed, nested evaluation must use `f+1`. String compare and appends already do this. Break this rule and nested expressions silently corrupt each other.
- **Slices:** push `from`, then `to`, then evaluate the base region. After that comes `POP DE / EX (SP),HL / CALL rt_slice`, and `rt_slice` pops the address from under its return address. Out-of-range slices are clamped, not reported as errors.
- **Assignments:**
  - Ordinary string variable `A$` (no DIM): `S_x` = length byte + `MaxString` bytes, set via `rt_strset`, which truncates.
  - DIM'd strings, slices and string-array elements are fixed length: assigned via `rt_strput`, which truncates or pads with spaces and is overlap-safe.
- **Variables:** numeric `V_NAME` (any length name). FOR variables reuse `V_x` as an 8-byte block: value, limit, step, loop-body address. Numeric arrays are `A_x`, ordinary strings `S_x`, DIM'd strings `SA_x`.
- **Control flow:**
  - Line labels are `L_<n>` (start) and `E_<n>` (end; IF jumps there when false).
  - GOSUB/RETURN are CALL/RET.
  - A constant GOTO to a missing line goes to the next higher line, as on the ZX81.
  - Computed GOTO/GOSUB/RUN emit `LINETAB` and use `rt_goto`.
  - FOR finds its NEXT *lexically*, like the ROM: the first `NEXT v` after it, including one inside an IF.
- Fresh labels come from `NewLabel()` (`_kN`). Runtime labels must be globally unique because all chunks share one namespace.
- **Floating point** (`GenFloat`, `Calc`): values go on the ROM calculator stack: `LD HL,n / CALL rt_fpushi` (signed whole number), `LD BC,n / CALL 1520h` (STACK-BC, for Unsigned constants), `LD HL,LIT_n / CALL rt_fplit` (5-byte constant from the literal pool, also PI). Each operation is its own `RST 28h / DB op,34h` (codes in `Runtime.CalcOps`); comparisons also need `LD B,op` first, because the ROM's comparison routine reads B. The result goes to HL with `CALL rt_fpop`, or to text with `RST 28h / DB 2Ah,34h / CALL rt_fstr / LD DE,T_t / CALL rt_append / CALL rt_freset`. Don't merge several operations into one RST 28h block: Recover expects one per block, and comparisons need B at entry.

## Runtime rules (Runtime.cs)

- **Never use IX, IY, or the alternate registers (`EX AF,AF'`, `EXX`)** in our own code. The ROM's SLOW-mode display interrupt depends on them. (The ROM's calculator uses EXX itself; that's fine, the ROM does that in SLOW mode all the time.)
- **ROM calculator invariants:** `rt_freset` must run after every island's result is used (rt_fpop and the STR$ path do it). It only tidies up when the calculator stack is back to its depth at start-up, so nested islands (`SIN X+PEEK (SIN Y*100)`) are safe; the ROM's workspace (STR$ text, VAL's copy of its string, SAVE/LOAD names) is reclaimed then. `rt_fplit` doesn't check for room: the CalcReserve gap is the room.
- The ROM's own errors (`SQR -1`, `LN 0`, overflow) and our `rt_fpop` overflow (report B) go through RST 08h, which unwinds to BASIC and shows the report. That's intended: it is what the ZX81 does.
- The screen is written directly into the display file at `(D_FILE)`. It assumes the fully expanded 16K layout, so a collapsed (1K) display file is not supported. Only codes with bit 6 clear may be stored there. `rt_prchr` turns anything else into `?`, and `76h` means newline. Writing a raw `76h` into a screen line would break the display.
- Rows 0–21 are for PRINT and rows 22–23 for INPUT. Scrolling is deferred: a newline on row 21 moves to "row 22 pending", and the next printed character scrolls. That keeps the ZX81 `SCROLL` then `PRINT` idiom working.
- Keyboard input goes through the ROM: `CALL 02BBh` (KEYBOARD), and if `L <> FFh`, `CALL 07BDh` (DECODE), which returns carry set and HL pointing at the key code. NEWLINE is `76h`; RUBOUT is `77h`.
- `rt_frame` waits for the low byte of FRAMES (4034h) to change, or about 20 ms. That covers FAST mode, where FRAMES doesn't count.
- BREAK is `IN A,(0FEh)` with A=7Fh, bit 0. It is inserted before GOTO and NEXT when `Options.BreakCheck` is on.
- **Adding a routine:**
  1. Call `C("name", "dep1 dep2", @"asm")`. The chunk name must be the main entry label.
  2. If the chunk has more entry labels, add them to `Runtime.EntryChunk`.
  3. In CodeGen, use `Call("label")`. It records the dependency; a bare `Op("CALL …")` doesn't.

## Language decisions (keep these consistent; document changes in `MainForm.HelpText` and README.md)

- **Arithmetic:** whole numbers (16-bit, wrap around) except in float islands. Division truncates toward zero; integer `**` with a negative power gives 0.
- **Float islands** (`FloatMath` in Ast.cs is the single source of truth):
  - An expression is floating point if it contains a FloatLit, PI, bare RND, or SIN COS TAN ASN ACS ATN LN EXP SQR VAL (`FloatFuncs`). ABS SGN INT (`PassFuncs`), the arithmetic and comparison operators, AND/OR (`PassOps`), NEG/NOT and `RND*n` are floating point when a child is.
  - Inside an island *every* arithmetic part is floating point, including all-whole-number parts (`SIN (7/2)` is SIN 3.5): GenFloat recurses top-down. Only nodes that aren't arithmetic (variables, PEEK, CODE, LEN, USR, string compares, array elements) are worked out as whole numbers and stacked with `rt_fpushi`.
  - Constants: whole numbers -32768..65535 are NumLit (`Unsigned` for 32768+); anything else, and any number *written with a decimal point*, is FloatLit. `FloatMath.Const` normalises folded results the same way.
  - Where a whole number is needed, the value is rounded like the ROM's FP-TO-BC (nearest, halves away from zero; over 65535 gives report B). `CodeGen.NormStmt/Norm` turns FloatLit constants in whole-number places into NumLit first, with a warning if rounded, so `34.0` and `34` give identical code. `WarnIfRounded` warns when a non-constant float is stored in a variable or a FOR loop (`LET A=H*PI/6`).
  - PRINT/LPRINT of a float = PRINT STR$ (the ROM's own number format). IF and `A$ AND n` test floats for "not zero" with calculator NOT (0.3 is true).
  - VAL always uses the ROM (any expression; it can't see compiled variables). INPUT still uses `rt_val` (whole numbers).
- **RND:** `RND*n`/`n*RND` becomes `RndN` in `ParseMul` (fast whole number 0..n-1). A bare RND is a float (rt_rnd16 / 65536); inside an island, RndN is RND*n in floating point.
- **SQR** is always floating point (so `SQR 2` is 1.4142136). `rt_sqr` (integer square root) is kept in Runtime.cs only so older compiled programs still decompile.
- **AND/OR** follow ZX81 semantics: `a AND b` gives a if b≠0, otherwise 0. `a OR b` gives 1 if b≠0, otherwise a. Comparisons give 1/0. NOT binds more loosely than the comparisons.
- **Function arguments:** functions take a *primary* argument, as in Sinclair BASIC, so `PEEK 16396+256*PEEK 16397` works.
- **Listing escapes** (`Lexer.ReadString`): `\hh` hex code, `\` + 2 of `' .:` for block graphics, `%c` inverse, `""` quote, Unicode block characters. Codes ≥ C0h inside strings are expanded to keyword text.
- **One statement per line.** `:` gives a helpful error.
- **LPRINT** (`PrintStmt.Printer`) uses the rt_l… printer routines (ROM LPRINT-CH 0851h: printer column = (PR_CC & 7Fh) - 3Ch; LPRINT AT only sets the column, as on the ZX81). **COPY** = `CALL 0869h`. **NEW** = `JP 03C3h`.
- **SAVE/LOAD** (`TapeStmt`): `rt_tname` copies the name into the ROM workspace (BC-SPACES) with the last character inverse and calls SET-FAST; SAVE then calls 02FBh (just after the ROM's NAME, which would parse BASIC text) and returns; LOAD resets SP to ENTRYSP and jumps to 0343h with carry set for `LOAD ""`, so when the ROM's LOAD returns it lands in the ROM's USR handler and BASIC carries on with the loaded program.
- **LIST, LLIST, CONT:** accepted, do nothing, warning (no BASIC to list / nothing to continue).

## Decompiler and .P Toolkit internals (src/Core)

| File | Role |
|---|---|
| `PFile.cs` | Loads a `.p` into a 64K image `Mem` with byte 0 at 4009h, so all code uses real addresses (files up to the full 48K above 4009h work). `RamEstimate` rounds to 1K/2K/4K/16K/32K/64K; `HardwareNotes` lists what a real ZX81 can't do (display file or machine code above 8000h; a file past 8000h needs RAMTOP raised before LOAD). Decodes lines (`PLine`), the `Sysvars` table with plain-English meanings (`SysvarNote`), autorun (`NXTLIN`), the display file (`DisplayInfo`: expanded/collapsed, `Chars[24,32]`), saved variables (`ReadVariables` → `PVar`), the memory map (`Region`, `RamEstimate`). Damaged files load with `Problems` filled in rather than throwing. |
| `ZxFloat.cs` | ZX81 5-byte floats: `Decode`, `Encode`, `Matches` (does the hidden number equal the digits shown?), `ShortText` (shortest text giving the same 5 bytes, ZX81-style exponent `1E-5`). |
| `Basic81.cs` | Detokeniser (`Detokenize`, `ProgramText`) in two styles, `Exact` (reloadable) and `Display` (Unicode graphics, inverse ranges, optional `{=value}`); tokeniser (`TokenizeBody`, `TokenizeProgram`); `BuildP` (new `.p` from lines, copying a template's sysvars/display/vars and shifting pointers); `HiddenNumberOffsets` (the 7Eh markers of a line in the same order as the `{=value}` annotations, skipping strings and REM; keep it in step with `DetokenizeInner`); `InjectRem` (replace a REM's payload and rebuild with `BuildP`, so D_FILE, DF_CC, VARS, E_LINE, NXTLIN … move; putting the same bytes back is byte-identical). |
| `Z80Disasm.cs` | Full Z80 decoder (CB/ED/DD/FD/DDCB) using the x/y/z/p/q decomposition. `RomLabels` (incl. the calculator, printer and tape entry points), `SysvarLabels`, `CalcName`/`CalcComment` (names of calculator codes after RST 28h, report after RST 08h). Immediates below 4000h are shown as hex, not labels (`Addr16(v, immediate)`), because `LD BC,0` isn't `LD BC,START`. |
| `CodeFinder.cs` | Finds machine code: evaluates every `USR` argument (`Eval`: hidden floats, `PEEK` of saved memory, saved variables, `VAL "..."`, `+ - * /`), traces from each entry following jumps/calls, handles `RST 28h` calculator literals and the byte after `RST 08h`. `ToAsm` writes ORG blocks, EQUs for used ROM/sysvar labels, `Lxxxx:` labels and `DB` for data; output must reassemble identically. |
| `Recover.cs` | Rebuilds BASIC from code made by our compiler; see below. |
| `BasicWriter.cs` | Prints recovered AST expressions/statements as BASIC text with correct precedence (OR 2, AND 3, NOT 4, comparisons 5, + - 6, * / 8, unary minus 9, ** 10). `RndN` prints as `INT (RND*n)`. A FloatLit prints as its `ShortText`, but a whole value in -32768..65535 *must* print as `720.0` (not `720` or `7.2E2`), or it would be read back as a whole-number constant and compile differently. |
| `Decompiler.cs` | `Decompiler.Run(p, name)` → `DecompileResult` (Listing, Basic, Asm, Report, `MakeP(text, …)`). Shared by the CLI and GUI. |
| `TapeDecode.cs` | Reading tapes. `ReadWav`: any PCM/float `.wav`, channels mixed, then `DecodeSamples`: a sliding 1 ms min/max window (ring-buffer monotonic queues, so memory doesn't grow with the recording) gives the local level; a pulse is a rise past the middle with 15% hysteresis, the quiet threshold is 20% of the peak-to-peak; pulses closer than 0.75 ms belong to one bit, 4 = 0 and 9 = 1 (counts in between are an error); a 30 ms gap inside a program is a dropout. Every error is a `TapeDecodeException` whose message starts with `Stamp(seconds)` = `[mm:ss.fff]` and whose `Seconds` is the time. `ReadTzx` (blocks 10/11/14/19/15, others skipped by length per TZX 1.20; in 19h the symbol with more pulses is a 1; 15h is decoded like audio), `ReadP81`/`MakeP81`. `ParseTapeData` takes the name then uses E_LINE (offset 0Bh of the data) for the length. |
| `TapeFormats.cs` | `TapeFormats.Load(path)` → `LoadedProgram` (P, Name, Format, Notes, Converted) for `.p .81 .p81 .o .80 .wav .tzx`, with `Sniff` for wrong extensions; the first program of a tape is used, the others listed in Notes. `OpenFilter` is the shared Open dialog filter. Every tool opens files through this. |
| `Zx80.cs` | ZX80 `.o` ⇄ ZX81. ZX80 memory 4000h..E_LINE (sysvars to 4028h, VARS at 4008h, E_LINE 400Ah); lines are a big-endian number, text, 76h (no length, no hidden numbers); its own token (D4h-FEh) and character codes (graphics 02h-0Bh map to ZX81 codes found by matching the ROM bitmaps). `ToP` writes ZX81 Exact-style text and runs it through `TokenizeProgram`/`BuildP`, then converts variables (`SetVariables`). `FromP` refuses what a ZX80 can't do and throws `Zx80Exception` with every problem; variables it can't convert are dropped with a note. |
| `ToolConfig.cs` | Emulator settings in `ZX81Toolset.ini` (`[Emulator] Path=`, `Arguments=` with `{file}`), next to the exes or in %APPDATA%\ZX81Toolset; `ZX81TOOLSET_INI` overrides (the tests use it). `RunInEmulator` throws `InvalidOperationException` with a message for the user; `TempP` writes a temporary `.p` for unsaved programs. |
| `Tape.cs` | `.wav` (44.1 kHz 16-bit; bit 0 = 4 pulses, bit 1 = 9 pulses, 150 µs high/150 µs low, 1300 µs gap) and `.tzx` (block 30h text, 20h pause, 19h generalised data with the same pulse symbols). Tape data is the program name in ZX81 codes, last char ORed with 80h, then the `.p` bytes. |

### Exact listing format (decompiler output, .P Toolkit export)

- Same escapes as the compiler (`\hh`, `\'.`-style block graphics, `%X` inverse, `""` inside a string = the `""` token C0h), but `\hh` is allowed anywhere in a line.
- Lines starting with `#` (or `;`/`//`) are comments. Line numbers 0–16383, kept in file order (programs may have duplicate or out-of-order numbers, and these must survive).
- The tokeniser drops spaces next to keywords, as the ZX81 lister adds them. A literal space beside a keyword therefore must be written `\00`.
- **Invariant:** every line written by `Detokenize` in Exact style is re-tokenised and compared. If it differs (odd hidden numbers, tokens inside REM, etc.) the whole line falls back to `\hh` escapes. Never remove this check; it's what makes listing → `.p` byte-identical.

### Recovering BASIC (Recover.cs)

1. **Recognise** by the start-up code `ED 73 nn nn` (LD (ENTRYSP),SP) `CD …` (rt_memcheck) `CD …` (rt_init).
2. **Match runtime chunks.** Each `Runtime` chunk is assembled twice at different ORG/BSS with `Z80Asm.Undefined` resolving externals; differing bytes become wildcards. Chunks are emitted in ordinal name order, so they are matched sequentially after ENTRYSP with backtracking, validated by the call targets in the program code.
3. **Pre-scan** BSS addresses into numbers, FOR blocks, numeric arrays (1D/2D), strings, DIM'd strings and temp buffers. Order matters: check 2D string arrays (`LD BC,…`) before numeric arrays (`ADD HL,HL`). MaxString comes from `S_x` spacing or the `LD A,n` before `rt_strset`.
4. **Interpret** symbolically: registers hold expression trees (`SN`, `SK`, `SR`, `SArr`, `SCmp`, `SInput` …); pattern handlers turn stores and runtime calls into statements.
5. **Name** variables (A, B, … ; strings A$ …) and **number** lines: real numbers from `LINETAB` if present (REM-only lines restored as `REM`), else 10, 20, 30 ….
6. **Verify** by recompiling the rebuilt listing with the detected options and comparing the code. Only report "verified" when identical.

Floating point in Recover: when decoding the compiled lines, each `RST 28h … 34h` becomes one pseudo-instruction (Shape `"CALC"`, codes in `calcOps`), because the code bytes would otherwise be misread as instructions (21h = LD HL,nn). `fstack` is a symbolic calculator stack: rt_fpushi/rt_fplit/STACK-BC/rt_frnd/rt_fval push, CALC applies operations, rt_fpop/rt_fstr pop. `LD B,n` before a CALC is skipped; the float truth test (`NOT x` then `JP NZ`) becomes `IF x` (or `A$ AND x` inside a string build); `PRINT STR$ float` is shown as `PRINT float`.

Fuzzing is the best check after changing CodeGen or Recover: generate random programs full of float expressions, compile, decompile, and require "Verified" for all of them (a generator like this found several real mismatches, e.g. `34.0` vs `34`).

**Coupling rule:** `Recover.cs` reads the exact instruction patterns that `CodeGen.cs` writes. If you change code generation, run `tests/run_tests.sh`: the decompiler section must still say "BASIC rebuilt+verified" for every test program. If it doesn't, update the matching pattern handler in `Recover.cs`.

### .P Toolkit notes

The .P Toolkit was called the .P File Browser; the code still uses that name (`src/Browser/`, `BrowserForm`, `BrowserProgram`, `Versions.Browser`). Only what the user sees, and the exe name `ZX81PToolkit.exe`, changed.


- `BrowserForm` is a partial class split over the two files; `LoadInto(PFile)` refreshes every tab. Byte edits change `p.Mem`, then the file is reloaded from the edited bytes (`edited` flag; title shows "(edited)").
- The hex and disassembly views are virtual `ListView`s (large files must stay fast): use the cached per-line "traced" lookup and `PFile.LineAt` (binary search). The listing tab caps code rows (`MaxCodeRows`) and line length (`MaxLineChars`), and the RTF builder groups runs of the same style. Don't append to a RichTextBox character by character.
- RAMTOP isn't in a `.p`, so the memory map asks which machine (1K…32K) to draw.
- `converted` is set when the file came from a tape, `.p81` or ZX80 file; Run in Emulator then uses `ToolConfig.TempP` (also when `edited`), and `tapeName` keeps the name from the tape for exports.
- The listing view records the BASIC line of every row in `listingRows` (Z80 rows under a REM belong to that REM). Double-click / right-click map a point → row → line, and the n-th `{=` in the row → the n-th offset from `HiddenNumberOffsets`. Edits go through `ReloadEdited`.
- Play Audio uses `System.Media.SoundPlayer.Play()` on a `MemoryStream` of `Tape.MakeWav`; progress is a timer against the known length (SoundPlayer has no position).

### Compiler: strict integer mode and Compile & Run

- `Options.StrictInt` (set by `--strict-int`, the GUI tick box, or a `#STRICT_INT` line, which `Compiler.Compile` strips into a cloned Options). `CodeGen` calls `CheckStrict` on each statement after `NormStmt`; `FloatCause` names the first thing that would make a float island, and `RoundConst` refuses fractional constants. Every refused line is reported, not just the first.
- `zx81c --run` / `--emulator path` and GUI Compile & Run go through `ToolConfig`. `--run` failing to start the emulator is exit code 3; the compile itself has still succeeded.

## Workflow for a change

1. Add or extend a test listing in `tests/` that exercises the feature, with output you can check on the text screen.
2. Make the change, run `tests/run_tests.sh`, then look at the screen with `python3 tests/run81.py tests/tN.p`.
3. `asmcheck.py` must show 0 mismatches, and `run81.py` must show `rom ok`, `display ok` and SP 7FF0 (or the expected ZX81 report), and `matches expected` for the programs in `tests/expected`.
4. Check it still compiles as C# 5 (`build.bat` / `mcs -langversion:5`).
5. Update `MainForm.HelpText` (compiler), `DecompilerForm.HelpText` (decompiler), `BrowserForm.HelpText` (.P Toolkit) and README.md if user-visible behaviour changed, and bump the version (see Versions above) for a release.
6. If the change touches `CodeGen.cs` or `Runtime.cs`, the decompiler's recovery must still verify (see the coupling rule above).

## Privacy

The author's email address appears only in the About boxes of the three GUIs (`UiCommon.AboutText`), built at run time in `UiCommon.ContactEmail()` from pieces (with the `@` written as `\u0040`). Don't put the address in plain text in any file (README, comments, help text). Some editing tools turn `\u0040` into a literal `@`; check it after editing that file.
