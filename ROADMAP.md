# Enhancements being considered

This is a list of ideas for future versions of the ZX81 BASIC Toolset. Nothing here is promised, and the order may change. Suggestions and comments are welcome via [GitHub Issues](../../issues).

Each item says which program it is for and roughly how much work it is (**small**, **medium** or **large**). The ones marked ⭐ are the current favourites for the next version.

## Compiler

| # | Enhancement | What it would do | Size |
|---|---|---|---|
| 1 ⭐ | **Inline assembly** | `ASM … END ASM` blocks inside a listing, and `#INCLUDE "file.asm"`, assembled by the compiler's own Z80 assembler. Lets a program use hand-written machine code for the parts that need it, with access to its labels and variables. | medium |
| 2 ⭐ | **Run and debug inside the compiler** | A built-in ZX81 emulator (the one the tests already use) to run the compiled program, step through it one BASIC line at a time, set breakpoints on lines and watch the variables. | large |
| 3 | **A better editor** | Syntax highlighting of keywords, numbers, strings and labels; keyword completion; errors underlined as you type. | medium |
| 4 | **Speed report** | After compiling, show which lines use fast whole-number code and which use the slower ROM calculator (float islands or full floating point), so you can see where a program spends its time and what to change. | small |
| 5 | **Strict ZX81 behaviour (debug builds)** | An option to check array bounds, stop with error 5 at the bottom of the screen instead of scrolling, and report "variable not found", so a program behaves exactly as it would in ZX81 BASIC while it is being tested. | medium |
| 6 | **Save as a tape directly** | Write the compiled program as a `.wav` or `.tzx` from the compiler and `zx81c`, not only from the .P Toolkit. | small |
| 7 | **More graphics support** | Help for user-defined character sets and pseudo hi-res displays, and for add-ons such as CHR$128 boards. | large |
| 8 | **ZX80 target** | Compile for the ZX80 (4K ROM) as well as the ZX81. | large |

## BASIC only output

| # | Enhancement | What it would do | Size |
|---|---|---|---|
| 9 ⭐ | **Read other tools' listing formats** | Accept listings written for ZXText2P and similar tools, with their own ways of writing quotes, graphics and inverse characters, as well as this toolset's escapes. Many existing listings use those formats. | small |
| 10 | **Machine code in a REM line** | Assemble an `.asm` file into a `REM` line of a BASIC only program, the classic way ZX81 programs combined BASIC and machine code. | medium |
| 11 | **Listing protection options** | Optionally hide a line (such as the machine code line) from `LIST`, or stop lines being edited, as some ZX81 programs did. | small |
| 12 | **A more exact memory check** | Work out the room taken by strings and arrays more fully when checking whether a program fits a 1K or 2K ZX81. | small |

## Decompiler

| # | Enhancement | What it would do | Size |
|---|---|---|---|
| 13 | **Browsable HTML disassembly** | Web pages for a disassembled program, with links between routines, the comments and labels from its `.ctl` file, and pictures of graphics data. | medium |
| 14 | **Create a `.ctl` file automatically** | Run the program in the built-in emulator, record which bytes are run as code, and write that out as `CODE` and `DATA` lines, as a starting point for a disassembly. | medium |
| 15 | **Output for other assemblers** | Write `.asm` listings that pasmo, sjasmplus and z88dk's assembler can assemble back to the same bytes. | small |
| 16 | **Recognise other compilers' output** | Spot programs made by other ZX81 BASIC compilers and describe their runtime, rather than treating them as unknown machine code. | medium |

## .P Toolkit

| # | Enhancement | What it would do | Size |
|---|---|---|---|
| 17 | **Record from a microphone or line-in** | Load a program straight from a real cassette player into the Toolkit, without saving a `.wav` first. | medium |
| 18 | **Compare two programs** | Show the differences between two `.p` files byte by byte and BASIC line by line: handy for finding changes between versions, POKEs and patches. | small |
| 19 | **Batch conversion** | Convert a whole folder at once: tapes to `.p` files, `.p` files to listings, or listings to `.p` files. | small |
| 20 | **Built-in emulator** | Run the program inside the Toolkit (shared with item 2), with the memory map and variables updating as it runs. | large |

## Distribution

| # | Enhancement | What it would do | Size |
|---|---|---|---|
| 21 | **Linux and macOS** | Release the command-line tools (`zx81c`, `zx81dc`) for Linux and macOS as well; they already build and run with Mono. | small |
| 22 | **Code signing** | Sign the Windows programs so that Windows SmartScreen no longer warns that they come from an unknown publisher. | small (needs a certificate) |
| 23 | **Installer** | An optional installer with Start menu shortcuts and file associations for `.p`, `.p81` and `.tzx` files. | small |

## Already done in v0.9

For comparison, these are in the first release: full floating point, float islands and integer only number modes; labels instead of line numbers; renumbering; 16K, 32K and 64K RAM packs; BASIC only output for any ZX81 from 1K; the decompiler with control files; the .P Toolkit's editors; and reading and writing `.wav`, `.tzx`, `.p81` and ZX80 files. See the [README](README.md) for details.
