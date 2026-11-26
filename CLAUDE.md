# CLAUDE.md

Guidance for working on this repository: a toolset for Sinclair ZX81 `.p` files.

- **Compiler** (`zx81c.exe`, `ZX81BasicCompiler.exe`): ZX81 BASIC text listing → Z80 machine code inside a `.p` file (`0 REM <code>`, `10 RAND USR 16514`).
- **Decompiler** (`zx81dc.exe`, `ZX81Decompiler.exe`): `.p` → exact BASIC text listing, `.asm` of any machine code, a new `.p`; for programs made by our compiler, the original BASIC rebuilt from the machine code.
- **.P File Browser** (`ZX81PBrowser.exe`): inspector/hex editor/exporter for `.p` files.

All five programs share `src/Core`. The three GUIs also share `src/UI`.

## Build and test

```sh
./build.sh                 # Linux/macOS with Mono (mcs) -> all five .exe files in build/
build.bat                  # Windows, uses the csc.exe that ships with .NET Framework 4
tests/run_tests.sh         # rebuild + compiler tests + decompiler round trips + tape check
tests/run_tests.sh DIR     # ... and round-trip every .p file found under DIR
```

Test tooling needs `python3` and `pip install z80` (a Z80 emulator with a disassembler).

- `mono build/zx81c.exe in.bas out.p --asm out.asm` compiles from the command line. The `.asm` file is the full assembly listing with addresses, bytes and the symbol table. Read it first when debugging generated code.
- `python3 tests/run81.py out.p [--frames N] [--keys "frame:KEY,..."] [--break FRAME] [--snap f1,f2]` runs the program and prints the 32x24 screen as text. Keys are ZX81 characters plus `NL`, `RUB`, `MINUS` and `SPACE`; each press lasts 5 frames. The last line reports whether the program finished, whether the ROM area was written to, whether the display file is still intact, and the final SP (it should be back at 7FF0).
- `python3 tests/asmcheck.py out.asm out.p` disassembles every instruction and compares it with the source line. It must report 0 mismatches after any change to `Z80Asm.cs` or to runtime/codegen instruction forms.

- `mono build/zx81dc.exe prog.p --report --txt out.txt --asm out.asm --p back.p` decompiles. `cmp prog.p back.p` should show no difference.
- `tests/cs/ToolTest.cs` (built and run by `run_tests.sh`) checks for each `.p` given that listing → `.p` is identical, and for programs made by our compiler that the rebuilt BASIC is verified. `ToolTest.exe --tape f.p` writes `/tmp/zx81test.wav`/`.tzx`, and `tests/tapecheck.py f.p wav tzx TEST` decodes both and compares them with name + file. `tests/cs/CoreTest.cs` is an older round-trip driver that prints per-file details.
- `.asm` from the decompiler should reassemble to identical bytes; check with `pasmo` (`apt install pasmo`, then `pasmo --bin out.asm out.bin` and compare with the bytes at the ORG address).
- GUIs can be smoke-tested on Linux with `xvfb-run mono build/ZX81PBrowser.exe file.p` plus `xdotool`/ImageMagick `import` for screenshots. Mono's WinForms isn't Windows, so layouts should still be checked on Windows.
- Known: one sample file (`frogging-normalized…p`) doesn't round-trip because its VARS pointer is outside the file. Damaged files like that are reported, not fixed.

**Emulator caveat:** `run81.py` uses a stub ROM, not the real ZX81 ROM. KEYBOARD (02BB), DECODE (07BD), FAST (0F23) and SLOW (0F2B) are faked, FRAMES is decremented every 16000 T-states, and the program is entered directly at 16514. These things are therefore *not* verified: `.p` loading/autorun, real ROM routines, and timing. Confirm real behaviour in EightyOne or on hardware.

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
| `src/Browser/` | `BrowserForm.cs` (`BrowserProgram`, menus, exports), `BrowserTabs.cs` (partial class: the tabs, `MemoryMapPanel`, `Rtf` builder) | ZX81PBrowser |
| `src/UI/` | `UiCommon.cs` (fonts, save dialogs, drag-and-drop, `AboutText`), `ZxRender.cs` (screen/tile bitmaps, ZX81 ROM font, char-set finder) | the three GUIs |

Each exe has its own `Main`, selected with `-main:` in `build.sh`/`build.bat`. If you add a source file to a folder, both build scripts pick it up (they compile `src/Core/*.cs` etc.), but check `build.bat` lists any new folder.

## Compiler pipeline (src/Core)

| File | Role |
|---|---|
| `Parser.cs` | `Lexer` (keywords, `GO TO`, strings and their escapes) and recursive-descent `Parser` producing the AST, plus constant folding. Also has the `Diagnostic` and `CompileError` types. |
| `Ast.cs` | Expression and statement node classes. Every expression has a static type `VT.Num` or `VT.Str`, checked in the parser. |
| `CodeGen.cs` | AST to Z80 assembly *text* through `Z80Asm`. It holds the variable tables, string temp buffers, literal pool and the line table. |
| `Runtime.cs` | The runtime library as named assembly chunks with dependencies. Only the chunks that are used get emitted. |
| `Z80Asm.cs` | Two-pass assembler for the Z80 subset we use: labels, `EQU`, `DB/DW/DS`, expressions with `+ - * /`, `$`, hex as `0FFh`/`$FF`/`0xFF`. |
| `Compiler.cs` | Orchestration, memory layout and `.p` file writer (system variables, line 0 / line 10, expanded display file). |
| `Charset.cs` | ASCII/Unicode to ZX81 codes, block graphics, token table. |
| `Z80Asm.Undefined` | Optional resolver for unknown symbols (used by `Recover` to assemble runtime chunks on their own). Leave null for normal compiles. |
| `src/Compiler/Cli.cs` | Command-line front end, plus `ReadListing` (UTF-8 with a CP-1252 fallback). |
| `src/Compiler/MainForm.cs` | WinForms GUI. Its `HelpText` is the user-facing list of supported BASIC, so update it when features change. |

## Memory layout of a compiled program

```
4009  system variables (written by Compiler.BuildPFile)
407D  line 0: 00 00 len EA <code...> 76      code starts at 4082h = 16514
      line 10: RAND USR 16514
      D_FILE: fully expanded 793-byte display (24 x (32 + 76h) + leading 76h)
      VARS: 80h ; E_LINE = VARS+1   (the file ends here)
E_LINE+256  "BSS": all variables, created at run time, NOT stored in the .p
```

- Code size doesn't depend on BSS addresses. `Compiler.Compile` assembles once with `BSS=6000h` to measure the code, then sets the real `BSS` and assembles again.
- BSS begins with fixed runtime state (`Runtime.BssEquates`, `BSS_FIXED = 52` bytes: print row/col, RNG seed, scratch, the 32-byte INPUT buffer). User variables come after `VARSTART`. CLEAR zeroes from `VARSTART`; `rt_init` zeroes everything.
- `ENTRYSP` (a `DW` inside the code) stores the SP at entry. `rt_stop` restores it and returns to BASIC, so STOP works from inside GOSUBs. RUN reloads SP from it.
- `rt_memcheck` refuses to run, showing "OUT OF MEMORY", if the stack is within 160 bytes of `BSS_END`. The compile-time limit is `0x8000 - 0x200` (16K machine).

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

## Runtime rules (Runtime.cs)

- **Never use IX, IY, or the alternate registers (`EX AF,AF'`, `EXX`).** The ROM's SLOW-mode display interrupt depends on them.
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

- **Arithmetic:** integer only. Division truncates toward zero. Values wrap at 16 bits. Floating-point functions (SIN, COS, TAN, ASN, ACS, ATN, LN, EXP and PI) are compile errors.
- **RND:** only valid as `RND*n` or `n*RND`. It is rewritten to `RndN` in `ParseMul` and gives 0..n-1. A bare RND is an error.
- **AND/OR** follow ZX81 semantics: `a AND b` gives a if b≠0, otherwise 0. `a OR b` gives 1 if b≠0, otherwise a. Comparisons give 1/0. NOT binds more loosely than the comparisons.
- **Function arguments:** functions take a *primary* argument, as in Sinclair BASIC, so `PEEK 16396+256*PEEK 16397` works.
- **Listing escapes** (`Lexer.ReadString`): `\hh` hex code, `\` + 2 of `' .:` for block graphics, `%c` inverse, `""` quote, Unicode block characters. Codes ≥ C0h inside strings are expanded to keyword text.
- **One statement per line.** `:` gives a helpful error.
- **Ignored statements:** LIST, LLIST, LPRINT, COPY, SAVE, LOAD and CONT are dropped with a warning. NEW becomes STOP.

## Decompiler and browser internals (src/Core)

| File | Role |
|---|---|
| `PFile.cs` | Loads a `.p` into a 64K image `Mem` with byte 0 at 4009h, so all code uses real addresses. Decodes lines (`PLine`), the `Sysvars` table with plain-English meanings (`SysvarNote`), autorun (`NXTLIN`), the display file (`DisplayInfo`: expanded/collapsed, `Chars[24,32]`), saved variables (`ReadVariables` → `PVar`), the memory map (`Region`, `RamEstimate`). Damaged files load with `Problems` filled in rather than throwing. |
| `ZxFloat.cs` | ZX81 5-byte floats: `Decode`, `Encode`, `Matches` (does the hidden number equal the digits shown?). |
| `Basic81.cs` | Detokeniser (`Detokenize`, `ProgramText`) in two styles, `Exact` (reloadable) and `Display` (Unicode graphics, inverse ranges, optional `{=value}`); tokeniser (`TokenizeBody`, `TokenizeProgram`); `BuildP` (new `.p` from lines, copying a template's sysvars/display/vars and shifting pointers). |
| `Z80Disasm.cs` | Full Z80 decoder (CB/ED/DD/FD/DDCB) using the x/y/z/p/q decomposition. `RomLabels`, `SysvarLabels`. Immediates below 4000h are shown as hex, not labels (`Addr16(v, immediate)`), because `LD BC,0` isn't `LD BC,START`. |
| `CodeFinder.cs` | Finds machine code: evaluates every `USR` argument (`Eval`: hidden floats, `PEEK` of saved memory, saved variables, `VAL "..."`, `+ - * /`), traces from each entry following jumps/calls, handles `RST 28h` calculator literals and the byte after `RST 08h`. `ToAsm` writes ORG blocks, EQUs for used ROM/sysvar labels, `Lxxxx:` labels and `DB` for data; output must reassemble identically. |
| `Recover.cs` | Rebuilds BASIC from code made by our compiler; see below. |
| `BasicWriter.cs` | Prints recovered AST expressions/statements as BASIC text with correct precedence (OR 2, AND 3, NOT 4, comparisons 5, + - 6, * / 8, unary minus 9, ** 10). `RndN` prints as `INT (RND*n)`. |
| `Decompiler.cs` | `Decompiler.Run(p, name)` → `DecompileResult` (Listing, Basic, Asm, Report, `MakeP(text, …)`). Shared by the CLI and GUI. |
| `Tape.cs` | `.wav` (44.1 kHz 16-bit; bit 0 = 4 pulses, bit 1 = 9 pulses, 150 µs high/150 µs low, 1300 µs gap) and `.tzx` (block 30h text, 20h pause, 19h generalised data with the same pulse symbols). Tape data is the program name in ZX81 codes, last char ORed with 80h, then the `.p` bytes. |

### Exact listing format (decompiler output, browser export)

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

**Coupling rule:** `Recover.cs` reads the exact instruction patterns that `CodeGen.cs` writes. If you change code generation, run `tests/run_tests.sh`: the decompiler section must still say "BASIC rebuilt+verified" for every test program. If it doesn't, update the matching pattern handler in `Recover.cs`.

### Browser notes

- `BrowserForm` is a partial class split over the two files; `LoadInto(PFile)` refreshes every tab. Byte edits change `p.Mem`, then the file is reloaded from the edited bytes (`edited` flag; title shows "(edited)").
- The hex and disassembly views are virtual `ListView`s (large files must stay fast): use the cached per-line "traced" lookup and `PFile.LineAt` (binary search). The listing tab caps code rows (`MaxCodeRows`) and line length (`MaxLineChars`), and the RTF builder groups runs of the same style. Don't append to a RichTextBox character by character.
- RAMTOP isn't in a `.p`, so the memory map asks which machine (1K…32K) to draw.

## Workflow for a change

1. Add or extend a test listing in `tests/` that exercises the feature, with output you can check on the text screen.
2. Make the change, run `tests/run_tests.sh`, then look at the screen with `python3 tests/run81.py tests/tN.p`.
3. `asmcheck.py` must show 0 mismatches, and `run81.py` must show `rom ok`, `display ok` and SP 7FF0.
4. Check it still compiles as C# 5 (`build.bat` / `mcs -langversion:5`).
5. Update `MainForm.HelpText` (compiler), `DecompilerForm.HelpText` (decompiler) and README.md if user-visible behaviour changed.
6. If the change touches `CodeGen.cs` or `Runtime.cs`, the decompiler's recovery must still verify (see the coupling rule above).

## Privacy

The author's email address appears only in the About box, built at run time in `MainForm.ContactEmail()` from pieces (with the `@` written as `\u0040`). Don't put the address in plain text in any file (README, comments, help text). Some editing tools turn `\u0040` into a literal `@`; check it after editing that file.
