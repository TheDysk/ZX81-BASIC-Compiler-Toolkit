// =====================================================================================
//  Runtime.cs - the Z80 runtime library for compiled ZX81 BASIC programs
// -------------------------------------------------------------------------------------
//  These routines do the real work for the compiled program: printing, string
//  handling, multiply/divide, random numbers, PLOT, the keyboard, FOR/NEXT and so on.
//  The code generator emits "CALL rt_xxx" and this file supplies rt_xxx.
//
//  The library is split into CHUNKS. Each chunk has a name (its main label), a list
//  of other chunks it needs, and its assembly code. Only the chunks a program actually
//  uses (directly or through dependencies) are put into the output, which keeps small
//  programs small. Note: comments inside the assembly use ';'.
//
//  RULES for all code here (see also CLAUDE.md):
//   * Never use IX, IY or the alternate registers (EXX, EX AF,AF'). The ZX81 ROM's
//     display routine uses them in SLOW mode, while our program runs.
//   * Numbers are 16-bit signed in HL. Strings are HL = address, BC = length (B = 0).
//   * The screen is written directly in the display file, whose address is in the
//     system variable D_FILE (400Ch). It is the fully expanded 16K layout:
//         76h, then 24 lines each of 32 characters + 76h  (33 bytes per line)
//     so character (row, col) is at  (D_FILE) + 1 + row*33 + col.
//     Rows 0-21 are used by PRINT, rows 22-23 by INPUT.
//
//  ZX81 ROM routines used:
//     02BBh KEYBOARD - scan the keyboard: HL = key value, L = FFh if no key
//     07BDh DECODE   - key value in BC -> HL points at the character code, carry set if valid
//     0F23h FAST, 0F2Bh SLOW - the FAST and SLOW commands
//
//  Z80 NOTES for readers new to it:
//     Carry flag (C/NC) - set when a subtraction borrows, used as the result of compares
//     Zero flag (Z/NZ)  - set when a result is zero.  OR A clears the carry flag.
//     SBC HL,DE         - HL = HL - DE - carry, so it is usually preceded by OR A
//     LDIR / LDDR       - copy BC bytes from (HL) to (DE), forwards / backwards
//     DJNZ label        - decrement B, jump if not zero
// =====================================================================================
using System.Collections.Generic;

namespace ZX81Compiler
{
    public static class Runtime
    {
        /// <summary>A named piece of the runtime library.</summary>
        public class Chunk
        {
            public string Name;     // the chunk's main label
            public string[] Deps;   // other chunks it calls
            public string Code;     // its assembly source
        }

        /// <summary>All chunks, by name.</summary>
        public static readonly Dictionary<string, Chunk> Chunks = new Dictionary<string, Chunk>();

        /// <summary>Define a chunk. deps is a space-separated list of chunk names.</summary>
        static void C(string name, string deps, string code)
        {
            Chunks[name] = new Chunk
            {
                Name = name,
                Deps = deps.Length == 0 ? new string[0] : deps.Split(' '),
                Code = code
            };
        }

        // ROM entry points
        public const int ROM_KEYBOARD = 0x02BB;
        public const int ROM_DECODE = 0x07BD;
        public const int ROM_FAST = 0x0F23;
        public const int ROM_SLOW = 0x0F2B;

        // Layout of fixed runtime state at the start of BSS
        // (BSS = the variables area in free memory; its address is chosen by Compiler.cs).
        // User variables start at VARSTART, straight after this fixed part.
        public const int BSS_FIXED = 52;
        public static readonly string BssEquates = @"
PR_ROW  EQU BSS+0       ; print position: row (0-21, 22 = waiting to scroll)
PR_COL  EQU BSS+1       ; print position: column (0-31, 32 = line full)
SEED    EQU BSS+2       ; random number seed (2 bytes)
SL_FROM EQU BSS+4       ; scratch for rt_slice (2 bytes)
NX_BLK  EQU BSS+6       ; scratch for rt_next: address of the FOR block (2 bytes)
PCOUNT  EQU BSS+8       ; PAUSE frames left (2 bytes)
NSFLAG  EQU BSS+10      ; rt_numstr: set once a non-zero digit has been output
VALNEG  EQU BSS+11      ; rt_val: non-zero if the number had a minus sign
INLEN   EQU BSS+12      ; INPUT: number of characters typed
CHARBUF EQU BSS+13      ; 1-byte buffer used by rt_appchr
NUMBUF  EQU BSS+14      ; 6 bytes: digits made by rt_numstr (sign + 5 digits)
INBUF   EQU BSS+20      ; 32 bytes: the text typed at INPUT
VARSTART EQU BSS+52     ; first user variable (CLEAR clears from here)
D_FILE  EQU 400Ch       ; system variable: address of the display file
FRAMES  EQU 4034h       ; system variable: frame counter, counts down 50 times a second
";

        static Runtime()
        {
            // ---------------- start-up, stop, memory ----------------
            C("rt_init", "rt_zero rt_randomize rt_cls", @"
; Start of program (and RUN): clear all variables, seed RND, clear the screen
rt_init:
    LD HL,BSS
    LD BC,BSS_SIZE
    CALL rt_zero            ; all variables = 0, all strings empty
    CALL rt_randomize       ; seed from the frame counter
    JP rt_cls
");
            C("rt_stop", "", @"
; STOP / end of program: return to BASIC from any depth of GOSUB by putting back
; the stack pointer saved at the start. BC is the value RAND USR receives.
rt_stop:
    LD SP,(ENTRYSP)
    LD BC,0
    RET
");
            C("rt_memcheck", "", @"
; make sure the machine stack is safely above our variables, else show a message and return
; (The variables live in free memory below the stack; if the RAM is too small they
; would collide.) The message is OUT OF MEMORY in ZX81 character codes.
rt_memcheck:
    LD HL,0
    ADD HL,SP               ; HL = stack pointer
    LD DE,BSS_END+160       ; end of variables + room for the stack to grow
    OR A
    SBC HL,DE
    RET NC                  ; SP is above that: all is well
    LD HL,(D_FILE)
    INC HL                  ; top left of the screen
    LD DE,mc_msg
    EX DE,HL                ; HL = message, DE = screen
    LD BC,13
    LDIR
    LD SP,(ENTRYSP)         ; back to BASIC
    LD BC,4
    RET
mc_msg:
    DB 34h,3Ah,39h,00h,34h,2Bh,00h,32h,2Ah,32h,34h,37h,3Eh
");
            C("rt_zero", "", @"
rt_zero:                ; zero BC (>=1) bytes at HL
    LD (HL),0               ; clear the first byte...
    DEC BC
    LD A,B
    OR C
    RET Z
    LD D,H
    LD E,L
    INC DE
    LDIR                    ; ...then copy it along the rest of the area
    RET
");
            C("rt_clear", "rt_zero", @"
; CLEAR: zero the user variables (not the print position or the RND seed)
rt_clear:
    LD HL,VARSTART
    LD BC,BSS_SIZE-52
    JP rt_zero
");
            C("rt_brk", "rt_stop", @"
rt_brk:                 ; BREAK (SPACE) pressed? then stop
    LD A,7Fh                ; keyboard row: SPACE . M N B
    IN A,(0FEh)
    RRA                     ; bit 0 (SPACE) into carry: 0 = pressed
    RET C
    JP rt_stop
");
            // ---------------- maths ----------------
            C("neg_hl", "", @"
; HL = -HL  (0 - L, then 0 - H - borrow)
neg_hl:
    XOR A
    SUB L
    LD L,A
    SBC A,A                 ; A = FFh if there was a borrow, else 0
    SUB H
    LD H,A
    RET
");
            C("rt_scmp", "", @"
rt_scmp:                ; carry set if HL < DE (signed). Preserves HL, DE
    LD A,H
    XOR D
    JP M,scmp_d             ; signs differ: handle separately
    PUSH HL                 ; same sign: an unsigned subtract gives the answer
    SBC HL,DE               ; (XOR cleared the carry)
    POP HL
    RET
scmp_d:                     ; signs differ: HL < DE exactly when HL is negative
    LD A,H
    RLA                     ; carry = sign bit of HL
    RET
");
            C("rt_lt", "rt_scmp", @"
; Numeric comparisons: HL = 1 if (HL op DE) is true, else 0
rt_lt:
    CALL rt_scmp
bool_c:                     ; HL = 1 if carry, else 0
    LD HL,0
    RET NC
    INC L
    RET
rt_gt:                      ; HL > DE is the same as DE < HL
    EX DE,HL
    CALL rt_scmp
    JR bool_c
rt_ge:
    CALL rt_scmp
bool_nc:                    ; HL = 1 if no carry, else 0
    LD HL,0
    RET C
    INC L
    RET
rt_le:
    EX DE,HL
    CALL rt_scmp
    JR bool_nc
rt_eq:
    OR A
    SBC HL,DE
    LD HL,0                 ; LD does not change the flags
    RET NZ
    INC L
    RET
rt_ne:
    OR A
    SBC HL,DE
    LD HL,0
    RET Z
    INC L
    RET
");
            C("rt_and", "", @"
rt_and:                 ; ZX81: a AND b = a if b<>0 else 0
    LD A,D
    OR E
    RET NZ
    LD H,A
    LD L,A
    RET
rt_or:                  ; ZX81: a OR b = 1 if b<>0 else a
    LD A,D
    OR E
    RET Z
    LD HL,1
    RET
rt_not:                 ; NOT a = 1 if a=0 else 0
    LD A,H
    OR L
    LD HL,0
    RET NZ
    INC L
    RET
");
            C("rt_mul", "", @"
rt_mul:                 ; HL = HL * DE (low 16 bits)
; Long multiplication in binary: for each bit of DE, from the top, double the
; result and add the multiplicand if the bit is 1. The low 16 bits are the same
; for signed and unsigned numbers, so this works for negative numbers too.
    LD B,H
    LD C,L                  ; BC = multiplicand
    LD HL,0                 ; result
    LD A,16                 ; 16 bits to do
mul_lp:
    ADD HL,HL               ; result * 2
    EX DE,HL
    ADD HL,HL               ; next bit of DE into carry
    EX DE,HL
    JR NC,mul_nc
    ADD HL,BC               ; bit was 1: add the multiplicand
mul_nc:
    DEC A
    JR NZ,mul_lp
    RET
");
            C("rt_mulhi", "", @"
rt_mulhi:               ; HL = (HL * DE) >> 16, unsigned
; The top 16 bits of the 32-bit product. Used by RND*n: random(0..65535) * n / 65536
; gives 0..n-1. Works from the lowest bit of DE, shifting the result right each time.
    LD B,H
    LD C,L
    LD HL,0
    LD A,16
mh_lp:
    SRL D
    RR E                    ; lowest bit of DE into carry
    JR NC,mh_sh
    ADD HL,BC               ; carry = bit 16 of the sum
mh_sh:
    RR H
    RR L                    ; shift the 17-bit result right
    DEC A
    JR NZ,mh_lp
    RET
");
            C("rt_udiv", "", @"
rt_udiv:                ; HL = HL / DE, DE = remainder (unsigned, DE <= 8000h)
; Long division in binary. Dividing by 0 gives 0 (the ZX81 would stop with error 6).
    LD A,D
    OR E
    JR NZ,ud_go
    LD H,A
    LD L,A
    RET
ud_go:
    LD B,H
    LD C,L                  ; BC = dividend; quotient bits are shifted into it
    LD HL,0                 ; remainder
    LD A,16
ud_lp:
    SLA C
    RL B                    ; top bit of the dividend into carry
    ADC HL,HL               ; ...and into the remainder
    OR A
    SBC HL,DE               ; try subtracting the divisor
    JR NC,ud_ok
    ADD HL,DE               ; too big: put it back (quotient bit stays 0)
    JR ud_nx
ud_ok:
    INC C                   ; quotient bit = 1
ud_nx:
    DEC A
    JR NZ,ud_lp
    EX DE,HL                ; DE = remainder
    LD H,B
    LD L,C                  ; HL = quotient
    RET
");
            C("rt_div", "rt_udiv neg_hl", @"
rt_div:                 ; HL = HL / DE signed, truncating towards zero
    LD A,H
    XOR D                   ; bit 7 of A = sign of the answer
    PUSH AF
    BIT 7,H
    CALL NZ,neg_hl          ; HL = ABS HL
    EX DE,HL
    BIT 7,H
    CALL NZ,neg_hl          ; DE = ABS DE
    EX DE,HL
    CALL rt_udiv
    POP AF
    RLA
    RET NC                  ; answer positive
    JP neg_hl
");
            C("rt_pow", "rt_mul", @"
rt_pow:                 ; HL = HL ** DE (DE < 0 gives 0)
; Multiplies HL by itself DE times.
    BIT 7,D
    JR NZ,pw_zero
    LD B,H
    LD C,L                  ; BC = the number
    LD HL,1                 ; result
pw_lp:
    LD A,D
    OR E
    RET Z
    DEC DE
    PUSH DE
    PUSH BC
    LD D,B
    LD E,C
    CALL rt_mul             ; result = result * number
    POP BC
    POP DE
    JR pw_lp
pw_zero:
    LD HL,0
    RET
");
            C("rt_abs", "neg_hl", @"
rt_abs:                 ; HL = ABS HL
    BIT 7,H
    RET Z
    JP neg_hl
");
            C("rt_sgn", "", @"
rt_sgn:                 ; HL = SGN HL: -1, 0 or 1
    LD A,H
    OR L
    RET Z
    BIT 7,H
    LD HL,1
    RET Z
    LD HL,0FFFFh
    RET
");
            C("rt_sqr", "", @"
rt_sqr:                 ; integer square root
; Counts how many odd numbers (1, 3, 5, ...) can be subtracted, because
; 1+3+5+...+(2n-1) = n*n.  Negative numbers give 0.
    BIT 7,H
    JR NZ,sq_zero
    LD DE,1                 ; next odd number
    LD BC,0                 ; count
sq_lp:
    OR A
    SBC HL,DE
    JR C,sq_done
    INC BC
    INC DE
    INC DE
    JR sq_lp
sq_done:
    LD H,B
    LD L,C
    RET
sq_zero:
    LD HL,0
    RET
");
            C("rt_usr", "", @"
; USR n: CALL rt_usr with HL = n jumps to the user's machine code, whose RET
; comes back to the compiled program.
rt_usr:
    JP (HL)
");
            // ---------------- random numbers ----------------
            C("rt_rnd16", "", @"
rt_rnd16:               ; xorshift16, HL = next random
; A 16-bit xorshift generator (shifts 7, 9, 8): goes through all 65535 non-zero
; values before repeating. The seed must not be 0.
    LD HL,(SEED)
    LD A,H
    OR L
    JR NZ,r16_go
    INC L                   ; seed 0 would stay 0: use 1
r16_go:
    LD A,H
    RRA
    LD A,L
    RRA
    XOR H
    LD H,A
    LD A,L
    RRA
    LD A,H
    RRA
    XOR L
    LD L,A
    XOR H
    LD H,A
    LD (SEED),HL
    RET
");
            C("rt_rndn", "rt_rnd16 rt_mulhi neg_hl", @"
rt_rndn:                ; HL = random 0..n-1 (RND*n)
    BIT 7,H
    JR Z,rn_pos
    CALL neg_hl             ; negative n: do it for -n, then negate the answer
    CALL rn_pos
    JP neg_hl
rn_pos:
    PUSH HL
    CALL rt_rnd16           ; HL = random 0..65535
    POP DE
    JP rt_mulhi             ; HL = random * n / 65536
");
            C("rt_randomize", "", @"
; RAND with no number: seed from the frame counter
rt_randomize:
    LD HL,(FRAMES)
rt_rand:                ; RAND n: n=0 uses the frame counter
    LD A,H
    OR L
    JR NZ,rz_set
    LD HL,(FRAMES)
    LD A,H
    OR L
    JR NZ,rz_set
    INC L                   ; never 0
rz_set:
    LD (SEED),HL
    RET
");
            // ---------------- screen ----------------
            C("rt_cls", "", @"
; CLS: blank all 24 screen lines (keeping the 76h line ends) and print at 0,0
rt_cls:
    LD HL,(D_FILE)
    INC HL                  ; skip the 76h at the start of the display file
    LD C,24                 ; lines
cls_row:
    LD B,32                 ; characters per line
cls_ch:
    LD (HL),0
    INC HL
    DJNZ cls_ch
    INC HL                  ; skip the 76h at the end of the line
    DEC C
    JR NZ,cls_row
    XOR A
    LD (PR_ROW),A
    LD (PR_COL),A
    RET
");
            C("scr_up", "", @"
scr_up:                 ; scroll rows 0-21 up one line
    LD HL,(D_FILE)
    INC HL
    LD D,H
    LD E,L                  ; DE = row 0
    LD BC,33
    ADD HL,BC               ; HL = row 1
    LD BC,693               ; 21 rows * 33 bytes
    LDIR                    ; rows 1-21 move to rows 0-20; DE ends at row 21
    LD H,D
    LD L,E
    LD B,32
scu_clr:                    ; blank row 21
    LD (HL),0
    INC HL
    DJNZ scu_clr
    RET
");
            C("rt_scroll", "scr_up", @"
; SCROLL: scroll up and print at the start of the bottom line (as the ZX81 does)
rt_scroll:
    CALL scr_up
    LD A,21
    LD (PR_ROW),A
    XOR A
    LD (PR_COL),A
    RET
");
            C("rc_addr", "", @"
pr_addr:                ; HL = display address of print position
    LD A,(PR_ROW)
    LD H,A
    LD A,(PR_COL)
    LD L,A
rc_addr:                ; HL = display address of row H, column L. Uses A, DE
; address = (D_FILE) + 1 + row*33 + column
    LD E,L
    LD D,0                  ; DE = column
    LD L,H
    LD H,D                  ; HL = row
    PUSH DE
    LD D,H
    LD E,L                  ; DE = row
    ADD HL,HL               ; row*2
    ADD HL,HL               ; row*4
    ADD HL,HL               ; row*8
    ADD HL,HL               ; row*16
    ADD HL,HL               ; row*32
    ADD HL,DE               ; row*33
    POP DE
    ADD HL,DE               ; + column
    LD DE,(D_FILE)
    ADD HL,DE
    INC HL
    RET
");
            C("rt_prchr", "rc_addr scr_up", @"
rt_newline:
    LD A,76h                ; NEWLINE
rt_prchr:               ; print ZX81 character A. Preserves BC, DE, HL
    PUSH HL
    PUSH DE
    PUSH BC
    CP 76h
    JR Z,pc_nl
    BIT 6,A                 ; codes 40h-7Fh and C0h-FFh can't go in the display file
    JR Z,pc_ok
    LD A,0Fh                ; ...so print ? instead
pc_ok:
    LD B,A                  ; B = the character
    LD A,(PR_COL)
    CP 32
    CALL NC,pr_nl           ; line full: wrap to the next line
    LD A,(PR_ROW)
    CP 22
    JR C,pc_row
    PUSH BC                 ; below line 21: scroll up first
    CALL scr_up
    POP BC
    LD A,21
    LD (PR_ROW),A
pc_row:
    CALL pr_addr
    LD (HL),B               ; put the character on the screen
    LD HL,PR_COL
    INC (HL)
    JR pc_end
pc_nl:
    CALL pr_nl
pc_end:
    POP BC
    POP DE
    POP HL
    RET
pr_nl:                  ; new line; the scroll happens when something is printed below line 21
; Moving to row 22 is allowed (it means waiting to scroll); a newline when
; already there scrolls straight away.
    XOR A
    LD (PR_COL),A
    LD A,(PR_ROW)
    CP 22
    JR C,nl_inc
    PUSH BC
    CALL scr_up
    POP BC
    RET
nl_inc:
    INC A
    LD (PR_ROW),A
    RET
");
            C("rt_prstr", "rt_prchr", @"
rt_prstr:               ; print BC characters from HL
    LD A,B
    OR C
    RET Z
ps_lp:
    LD A,(HL)
    CALL rt_prchr
    INC HL
    DEC BC
    LD A,B
    OR C
    JR NZ,ps_lp
    RET
");
            C("rt_numstr", "neg_hl", @"
rt_numstr:              ; HL = number -> HL = NUMBUF, BC = length
; Writes the number as ZX81 digits (1Ch = 0) in NUMBUF, with a minus sign
; if negative and no leading zeros. Used by PRINT and STR$.
    XOR A
    LD (NSFLAG),A           ; no digits output yet
    LD DE,NUMBUF            ; DE = where the next character goes
    BIT 7,H
    JR Z,ns_pos
    LD A,16h                ; minus sign
    LD (DE),A
    INC DE
    CALL neg_hl
ns_pos:
    LD BC,-10000            ; each call outputs one digit
    CALL ns_dig
    LD BC,-1000
    CALL ns_dig
    LD BC,-100
    CALL ns_dig
    LD BC,-10
    CALL ns_dig
    LD A,L                  ; units digit (always output, so 0 prints as 0)
    ADD A,1Ch
    LD (DE),A
    INC DE
    LD HL,NUMBUF
    EX DE,HL                ; HL = end, DE = start
    OR A
    SBC HL,DE               ; length
    LD B,H
    LD C,L
    EX DE,HL                ; HL = start
    RET
ns_dig:
; How many times does the power of ten go into HL? BC = minus the power, so
; ADD HL,BC subtracts it; carry means it went in.
    LD A,0FFh
nd_lp:
    INC A
    ADD HL,BC
    JR C,nd_lp
    SBC HL,BC               ; one too many: add it back (carry is 0 here)
    OR A
    JR NZ,nd_put
    LD A,(NSFLAG)           ; a 0 digit: skip it if nothing has been output yet
    OR A
    RET Z
    XOR A
nd_put:
    ADD A,1Ch               ; digit -> ZX81 code
    LD (DE),A
    INC DE
    LD A,1
    LD (NSFLAG),A
    RET
");
            C("rt_prnum", "rt_numstr rt_prstr", @"
rt_prnum:               ; PRINT a number in HL
    CALL rt_numstr
    JP rt_prstr
");
            C("rt_at", "", @"
rt_at:                  ; PRINT AT HL,DE
; Ignored if out of range (the ZX81 would stop with error B).
    LD A,H
    OR D
    RET NZ
    LD A,L
    CP 22
    RET NC
    LD A,E
    CP 32
    RET NC
    LD A,L
    LD (PR_ROW),A
    LD A,E
    LD (PR_COL),A
    RET
");
            C("rt_tab", "rt_prchr", @"
; PRINT comma: move to column 16, or to the next line if already past it
rt_comma:
    LD A,(PR_COL)
    CP 16
    JP NC,rt_newline
    LD L,16
    JR tab_to
rt_tab:                 ; TAB L
    LD A,L
    AND 31                  ; column 0-31
    LD L,A
    LD A,(PR_COL)
    CP L
    JR Z,tab_to
    CALL NC,rt_newline      ; already past it: go to the next line
tab_to:                     ; print spaces until the column is L
    LD A,(PR_COL)
    CP L
    RET NC
    XOR A
    CALL rt_prchr
    JR tab_to
");
            C("rt_plot", "rc_addr", @"
rt_plot:                ; DE = x, HL = y, C = 0 plot / 1 unplot
; The screen is 64 x 44 pixels: each character cell holds 2 x 2 pixels.
; y = 0 is the bottom. The pixel's quarter of the cell is a bit mask:
; 1 top-left, 2 top-right, 4 bottom-left, 8 bottom-right (see Charset.cs).
    LD A,D
    OR H
    RET NZ                  ; x or y >= 256: ignore
    LD A,E
    CP 64
    RET NC
    LD A,L
    CP 44
    RET NC
    LD A,43
    SUB L                   ; A = 43-y (counting from the top)
    LD B,1                  ; mask: top-left
    SRL A                   ; A = row, carry = odd = bottom half
    JR NC,pl_1
    LD B,4                  ; bottom-left
pl_1:
    LD H,A                  ; H = row
    LD A,E
    SRL A                   ; A = column, carry = odd = right half
    JR NC,pl_2
    SLA B                   ; right: 1 -> 2 or 4 -> 8
pl_2:
    LD L,A                  ; L = column
    PUSH BC
    CALL rc_addr
    POP BC
    LD A,(HL)               ; what's there now
    LD D,A
    AND 7Fh
    CP 8
    LD A,0
    JR NC,pl_have           ; not a block graphic: treat as empty
    LD A,D
    BIT 7,A
    JR Z,pl_have            ; codes 0-7 are the mask itself
    XOR 8Fh                 ; inverse codes: undo the XOR to get the mask
pl_have:                    ; A = current mask 0-15
    DEC C
    JR Z,pl_un
    OR B                    ; PLOT: set the bit
    JR pl_enc
pl_un:
    LD D,A                  ; UNPLOT: clear the bit
    LD A,B
    CPL
    AND D
pl_enc:
    CP 8                    ; mask -> character code
    JR C,pl_st
    XOR 8Fh
pl_st:
    LD (HL),A
    RET
");
            // ---------------- keyboard / timing ----------------
            C("rt_inkey", "", @"
rt_inkey:               ; carry set and A = key code if a key is pressed
; Uses the ROM, in the same way as the ROM's own INKEY$.
    CALL 02BBh              ; KEYBOARD -> HL
    LD B,H
    LD C,L
    LD D,C
    INC D                   ; L = FFh means no key
    JR Z,ik_none
    CALL 07BDh              ; DECODE -> HL points at the character, carry if valid
    RET NC
    LD A,(HL)
    SCF
    RET
ik_none:
    OR A                    ; clear carry
    RET
");
            C("rt_keyany", "", @"
rt_keyany:              ; NZ if any key pressed
    CALL 02BBh
    LD A,L
    INC A                   ; L = FFh (no key) gives zero
    RET
");
            C("rt_frame", "", @"
rt_frame:               ; wait for the next TV frame (or about 20ms in FAST mode)
; In SLOW mode the ROM counts FRAMES down every 1/50 second. In FAST mode it
; doesn't, so a counter of about one frame's time is used as well.
    LD A,(FRAMES)
    LD C,A
    LD DE,1350
fr_lp:
    LD A,(FRAMES)
    CP C
    RET NZ                  ; FRAMES changed: a frame has passed
    DEC DE
    LD A,D
    OR E
    JR NZ,fr_lp
    RET
");
            C("rt_pause", "rt_keyany rt_frame", @"
rt_pause:               ; PAUSE HL frames (>=32768 = forever), ends on a key press
    LD (PCOUNT),HL
    LD A,H
    OR L
    RET Z                   ; PAUSE 0: no wait
pa_lp:
    CALL rt_keyany
    RET NZ                  ; a key stops the pause
    CALL rt_frame
    LD HL,(PCOUNT)
    BIT 7,H
    JR NZ,pa_lp             ; 32768 or more: wait for a key
    DEC HL
    LD (PCOUNT),HL
    LD A,H
    OR L
    JR NZ,pa_lp
    RET
");
            C("rt_input", "rt_inkey rt_keyany rt_frame", @"
rt_input:               ; read a line on screen row 22 -> HL = INBUF, BC = length
; Shows an inverse L cursor. NEWLINE (76h) finishes, RUBOUT (77h) deletes,
; other printable characters are added (up to 31). Each key is acted on once:
; it waits for the key to be released before reading the next one.
    CALL in_clr
    XOR A
    LD (INLEN),A
in_rel:                     ; wait until no key is held down
    CALL rt_keyany
    JR NZ,in_rel
in_draw:
    CALL in_addr
    LD (HL),0B1h            ; cursor (inverse L)
in_wait:
    CALL rt_inkey
    JR NC,in_wait           ; wait for a key
    PUSH AF
in_rel2:
    CALL rt_keyany
    JR NZ,in_rel2           ; wait for it to be released
    CALL rt_frame           ; and a moment for the contacts to settle
    POP AF
    CP 76h
    JR Z,in_done
    CP 77h
    JR Z,in_rub
    BIT 6,A
    JR NZ,in_draw           ; not a printable character: ignore
    LD B,A
    LD A,(INLEN)
    CP 31
    JR NC,in_draw           ; line full
    CALL in_addr
    LD (HL),B               ; show the character (over the cursor)
    LD HL,INLEN
    INC (HL)
    JR in_draw
in_rub:
    LD A,(INLEN)
    OR A
    JR Z,in_draw
    CALL in_addr
    LD (HL),0               ; remove the cursor; it is redrawn one place back
    LD HL,INLEN
    DEC (HL)
    JR in_draw
in_done:                    ; copy the typed text from the screen to INBUF
    CALL in_row
    LD DE,INBUF
    LD A,(INLEN)
    LD C,A
    LD B,0
    OR A
    JR Z,in_nocp
    LDIR
in_nocp:
    CALL in_clr
    LD HL,INBUF
    LD A,(INLEN)
    LD C,A
    LD B,0
    RET
in_row:                     ; HL = start of screen row 22
    LD HL,(D_FILE)
    LD DE,727               ; 1 + 22*33
    ADD HL,DE
    RET
in_addr:                    ; HL = row 22, column INLEN
    CALL in_row
    LD A,(INLEN)
    LD E,A
    LD D,0
    ADD HL,DE
    RET
in_clr:                     ; blank rows 22 and 23
    CALL in_row
    LD B,32
inc_1:
    LD (HL),0
    INC HL
    DJNZ inc_1
    INC HL                  ; skip the 76h line end
    LD B,32
inc_2:
    LD (HL),0
    INC HL
    DJNZ inc_2
    RET
");
            C("rt_val", "neg_hl", @"
rt_val:                 ; HL = integer value of the string at HL, length C
; Skips leading spaces, allows a + or - sign, then reads digits until
; something else. Used by VAL and numeric INPUT.
    LD B,C                  ; B = characters left
    LD DE,0                 ; DE = value so far
    XOR A
    LD (VALNEG),A
vl_sp:                      ; skip spaces
    LD A,B
    OR A
    JR Z,vl_end
    LD A,(HL)
    OR A
    JR NZ,vl_sign
    INC HL
    DEC B
    JR vl_sp
vl_sign:
    CP 16h                  ; -
    JR NZ,vl_plus
    LD (VALNEG),A
    JR vl_skip
vl_plus:
    CP 15h                  ; +
    JR NZ,vl_dig
vl_skip:
    INC HL
    DEC B
vl_dig:
    LD A,B
    OR A
    JR Z,vl_end
    LD A,(HL)
    SUB 1Ch                 ; ZX81 code -> digit value
    JR C,vl_end
    CP 10
    JR NC,vl_end            ; not a digit: stop
    PUSH HL
    LD H,D
    LD L,E
    ADD HL,HL               ; value*2
    ADD HL,HL               ; value*4
    ADD HL,DE               ; value*5
    ADD HL,HL               ; value*10
    LD E,A
    LD D,0
    ADD HL,DE               ; + the digit
    EX DE,HL
    POP HL
    INC HL
    DEC B
    JR vl_dig
vl_end:
    EX DE,HL
    LD A,(VALNEG)
    OR A
    RET Z
    JP neg_hl
");
            C("rt_code", "", @"
rt_code:                ; HL = CODE of string HL,C
; CODE of an empty string is 0.
    LD A,C
    OR A
    JR Z,cd_z
    LD L,(HL)
    LD H,0
    RET
cd_z:
    LD H,A
    LD L,A
    RET
");
            // ---------------- strings ----------------
            C("rt_append", "", @"
rt_appchr:              ; append character A to temp string at DE
    LD HL,CHARBUF
    LD (HL),A
    LD BC,1
rt_append:              ; append BC bytes from HL to temp string (len byte + 255) at DE
; The result is cut short at 255 characters.
    LD A,B
    OR C
    RET Z
    LD A,(DE)               ; current length
    PUSH HL
    LD L,A
    LD H,0
    ADD HL,BC               ; HL = new length
    LD A,H
    OR A
    JR Z,ap_ok
    LD A,(DE)               ; over 255: copy only 255 - current length
    CPL
    LD C,A
    LD B,0
    LD HL,255
ap_ok:
    LD A,(DE)               ; A = old length
    EX DE,HL                ; HL = buffer, DE = new length
    LD (HL),E               ; store the new length
    INC HL
    LD E,A
    LD D,0
    ADD HL,DE               ; HL = just after the old text
    EX DE,HL                ; DE = destination
    POP HL                  ; HL = source
    LD A,B
    OR C
    RET Z
    LDIR
    RET
");
            C("rt_strset", "", @"
rt_strset:              ; string var at DE (len byte + data) = BC bytes at HL, max length A
; Assignment to an ordinary string variable; text longer than its capacity is cut short.
    CP C
    JR NC,ss_ok
    LD C,A
ss_ok:
    LD A,C
    LD (DE),A               ; new length
    INC DE
    OR A
    RET Z
    LDIR                    ; copy the text (safe even for A$=A$(2 TO ), as it copies forwards)
    RET
");
            C("rt_memmove", "", @"
rt_memmove:             ; copy BC bytes HL -> DE, overlap safe
; If the destination is above the source and they overlap, copying forwards
; would overwrite bytes before they are read, so copy backwards instead.
    LD A,B
    OR C
    RET Z
    PUSH HL
    OR A
    SBC HL,DE
    POP HL
    JR NC,mm_fwd            ; source >= destination: forwards is safe
    ADD HL,BC
    DEC HL                  ; HL = last source byte
    EX DE,HL
    ADD HL,BC
    DEC HL                  ; last destination byte
    EX DE,HL
    LDDR
    RET
mm_fwd:
    LDIR
    RET
");
            C("rt_strput", "rt_memmove", @"
rt_strput:              ; fixed-length assign: dest DE len C  <-  src HL len A (pad with spaces)
; Used for slices, DIM'd strings and string array elements: the destination never
; changes length, so the source is cut short or padded with spaces (as on the ZX81).
    LD B,A                  ; B = source length
    LD A,C
    SUB B                   ; A = spaces needed
    JR NC,sp_1
    LD B,C                  ; source too long: copy only the destination length
    XOR A                   ; and no spaces
sp_1:
    PUSH AF
    PUSH DE
    LD C,B
    LD B,0
    PUSH BC
    CALL rt_memmove
    POP BC
    POP HL
    ADD HL,BC               ; HL = end of the copied part
    POP AF
    OR A
    RET Z
    LD B,A
sp_pad:
    LD (HL),0
    INC HL
    DJNZ sp_pad
    RET
");
            C("rt_slice", "", @"
rt_slice:               ; HL = from, DE = to, C = length, string address on stack -> HL,BC
; Works out the part of a string from character FROM to character TO.
; Out-of-range values are made to fit (the ZX81 would stop with error 3), and
; FROM > TO gives an empty string.
    LD (SL_FROM),HL
    POP HL                  ; return address
    EX (SP),HL              ; swap with the string address under it
    PUSH HL                 ; stack: string address, return address
    BIT 7,D
    JR NZ,sl_empty          ; TO negative
    LD A,D
    OR A
    JR NZ,sl_tcl            ; TO >= 256: use the length
    LD A,E
    CP C
    JR C,sl_tok
sl_tcl:
    LD E,C                  ; TO = length
sl_tok:
    LD HL,(SL_FROM)
    BIT 7,H
    JR NZ,sl_f1
    LD A,H
    OR L
    JR NZ,sl_fok
sl_f1:
    LD HL,1                 ; FROM < 1: use 1
sl_fok:
    LD A,H
    OR A
    JR NZ,sl_empty          ; FROM >= 256: past the end
    LD A,E
    SUB L                   ; TO - FROM
    JR C,sl_empty
    INC A
    LD C,A
    LD B,0                  ; BC = TO - FROM + 1
    DEC L
    LD E,L
    LD D,0
    POP HL
    ADD HL,DE               ; HL = address + FROM - 1
    RET
sl_empty:
    POP HL
    LD BC,0
    RET
");
            C("rt_strcmp", "", @"
rt_strcmp:              ; compare DE,len C with HL,len A -> HL = -1, 0, 1
; Character by character by ZX81 code; if one string runs out first, it is the smaller.
    LD B,A                  ; B = second length
sc_lp:
    LD A,C
    OR A
    JR Z,sc_e1              ; first string finished
    LD A,B
    OR A
    JR Z,sc_gt              ; second finished first: first is bigger
    LD A,(DE)
    CP (HL)
    JR C,sc_lt
    JR NZ,sc_gt
    INC DE
    INC HL
    DEC C
    DEC B
    JR sc_lp
sc_e1:
    LD A,B
    OR A
    JR NZ,sc_lt             ; first is shorter
    LD H,A
    LD L,A                  ; equal
    RET
sc_lt:
    LD HL,0FFFFh
    RET
sc_gt:
    LD HL,1
    RET
");
            // ---------------- control flow ----------------
            // A FOR block is 8 bytes: value, limit, step, loop body address (see CodeGen.GenFor)
            C("rt_forchk", "rt_scmp", @"
rt_forchk:              ; HL = FOR block. carry set if the loop should be skipped
; Skip if value > limit (positive step) or value < limit (negative step).
    LD E,(HL)
    INC HL
    LD D,(HL)               ; DE = value
    INC HL
    LD C,(HL)
    INC HL
    LD B,(HL)               ; BC = limit
    INC HL
    INC HL
    LD A,(HL)               ; A = high byte of the step
    LD H,B
    LD L,C                  ; HL = limit
    RLA                     ; carry = step negative
    JP NC,rt_scmp           ; carry if limit < value
    EX DE,HL
    JP rt_scmp              ; carry if value < limit
");
            C("rt_next", "rt_scmp rt_stop", @"
rt_next:                ; HL = FOR block (var, limit, step, loop address)
; value = value + step. If the loop goes on, jump to the body (throwing away
; this routine's return address); otherwise return to the code after NEXT.
    LD (NX_BLK),HL
    LD E,(HL)
    INC HL
    LD D,(HL)               ; DE = value
    INC HL
    LD C,(HL)
    INC HL
    LD B,(HL)               ; BC = limit
    INC HL
    LD A,(HL)
    INC HL
    LD H,(HL)
    LD L,A                  ; HL = step
    BIT 7,H
    PUSH AF                 ; remember: Z = step positive
    ADD HL,DE
    EX DE,HL                ; DE = new value
    LD HL,(NX_BLK)
    LD (HL),E
    INC HL
    LD (HL),D               ; store it
    POP AF
    LD H,B
    LD L,C                  ; HL = limit
    JR NZ,nx_neg
    CALL rt_scmp            ; positive step: finished if limit < value
    RET C
    JR nx_jmp
nx_neg:
    EX DE,HL
    CALL rt_scmp            ; negative step: finished if value < limit
    RET C
nx_jmp:
    POP HL                  ; discard the return address
    LD HL,(NX_BLK)
    LD DE,6
    ADD HL,DE
    LD A,(HL)
    INC HL
    LD H,(HL)
    LD L,A                  ; HL = loop body address
    OR H
    JP Z,rt_stop            ; 0: NEXT without FOR
    JP (HL)
");
            C("rt_goto", "rt_stop", @"
rt_goto:                ; jump to the first line >= HL
; Searches LINETAB (pairs of line number, address; ends with FFFFh). As on the
; ZX81, a missing line number goes to the next line after it.
    EX DE,HL                ; DE = line wanted
    LD HL,LINETAB
gt_lp:
    LD C,(HL)
    INC HL
    LD B,(HL)               ; BC = line number in the table
    INC HL
    LD A,B
    CP 0FFh
    JP Z,rt_stop            ; end of table: past the last line
    PUSH HL
    LD H,B
    LD L,C
    OR A
    SBC HL,DE
    POP HL
    JR NC,gt_fnd            ; table line >= wanted
    INC HL
    INC HL                  ; skip the address
    JR gt_lp
gt_fnd:
    LD A,(HL)
    INC HL
    LD H,(HL)
    LD L,A
    JP (HL)
");
        }

        /// <summary>Given the chunk names the program uses, return those chunks plus
        /// everything they depend on, each once. They're sorted by name so the output is
        /// the same every time.</summary>
        public static List<Chunk> Resolve(IEnumerable<string> needed)
        {
            var done = new HashSet<string>();
            var order = new List<Chunk>();
            var stack = new Stack<string>(needed);
            while (stack.Count > 0)
            {
                string n = stack.Pop();
                if (done.Contains(n)) continue;
                Chunk c;
                if (!Chunks.TryGetValue(n, out c)) throw new System.Exception("internal: unknown runtime routine " + n);
                done.Add(n);
                order.Add(c);
                foreach (var d in c.Deps) stack.Push(d);
            }
            order.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return order;
        }

        /// <summary>Map an entry label to the chunk that defines it.
        /// Some chunks contain several routines (e.g. rt_lt also holds rt_gt, rt_eq ...);
        /// this table lets CodeGen ask for any of them by its own label.</summary>
        public static readonly Dictionary<string, string> EntryChunk = new Dictionary<string, string>
        {
            {"rt_gt","rt_lt"},{"rt_ge","rt_lt"},{"rt_le","rt_lt"},{"rt_eq","rt_lt"},{"rt_ne","rt_lt"},
            {"rt_or","rt_and"},{"rt_not","rt_and"},{"rt_rand","rt_randomize"},{"rt_newline","rt_prchr"},
            {"rt_comma","rt_tab"},{"rt_appchr","rt_append"},{"pr_addr","rc_addr"}
        };
    }
}
