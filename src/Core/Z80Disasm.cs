// =====================================================================================
//  Z80Disasm.cs - Z80 disassembler (the complete instruction set, including IX/IY)
// -------------------------------------------------------------------------------------
//  Decodes using the standard x/y/z/p/q split of the opcode byte:
//      x = bits 7-6, y = bits 5-3, z = bits 2-0, p = y >> 1, q = y & 1
//  which lets a few tables cover all 1,300-odd instructions.
//
//  Each decoded instruction also says where it can jump/call to and whether execution
//  can carry on to the next instruction, which the code tracer (CodeFinder.cs) uses to
//  separate code from data.
//
//  Addresses are written through a label function, so known ROM routines and system
//  variables appear by name (e.g. CALL KEYBOARD, LD HL,(D_FILE)).
// =====================================================================================
using System;
using System.Collections.Generic;

namespace ZX81Compiler
{
    /// <summary>One decoded instruction.</summary>
    public class Instr
    {
        public int Addr;          // address of the first byte
        public int Len;           // number of bytes
        public string Text;       // e.g. "LD HL,(D_FILE)"
        public string Shape;      // operands replaced by nn/n/d, e.g. "LD HL,(nn)" (for pattern matching)
        public int Value = -1;    // the 16-bit (nn) or 8-bit (n) operand, -1 if none
        public int Target = -1;   // jump / call / RST destination, -1 if none
        public bool IsCall;       // CALL or RST (execution returns afterwards)
        public bool IsJump;       // JP / JR / DJNZ (conditional or not)
        public bool Continues = true;   // can execution reach the next instruction?
        public bool Invalid;      // not a valid instruction (shown as DB)
    }

    public static class Z80Disasm
    {
        static readonly string[] R = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
        static readonly string[] RP = { "BC", "DE", "HL", "SP" };
        static readonly string[] RP2 = { "BC", "DE", "HL", "AF" };
        static readonly string[] CC = { "NZ", "Z", "NC", "C", "PO", "PE", "P", "M" };
        static readonly string[] ALU = { "ADD A,", "ADC A,", "SUB ", "SBC A,", "AND ", "XOR ", "OR ", "CP " };
        static readonly string[] ROT = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL" };
        static readonly string[] IM = { "0", "0", "1", "2", "0", "0", "1", "2" };
        static readonly string[,] BLI = {
            { "LDI", "CPI", "INI", "OUTI" }, { "LDD", "CPD", "IND", "OUTD" },
            { "LDIR", "CPIR", "INIR", "OTIR" }, { "LDDR", "CPDR", "INDR", "OTDR" } };

        // ------------------------------------------------------------------
        //  Known addresses
        // ------------------------------------------------------------------
        /// <summary>ZX81 ROM entry points that programs commonly call.</summary>
        public static readonly Dictionary<int, string> RomLabels = new Dictionary<int, string>
        {
            {0x0000,"START"},{0x0008,"ERROR_1"},{0x0010,"PRINT_A"},{0x0018,"GET_CHAR"},{0x0020,"NEXT_CHAR"},
            {0x0028,"FP_CALC"},{0x0030,"BC_SPACES"},{0x0038,"INTERRUPT"},{0x0066,"NMI"},
            {0x0207,"SLOW_FAST"},{0x02BB,"KEYBOARD"},{0x02E7,"SET_FAST"},{0x07BD,"DECODE"},
            {0x08F5,"PRINT_AT"},{0x0A2A,"CLS"},{0x0F23,"FAST"},{0x0F2B,"SLOW"},{0x0F32,"PAUSE"},
            {0x0F46,"BREAK_1"},{0x0F4B,"DEBOUNCE"},{0x1E00,"CHAR_SET"},
            {0x02F6,"SAVE"},{0x02FB,"SAVE_NAMED"},{0x0340,"LOAD"},{0x0343,"LOAD_NAMED"},{0x03A8,"NAME"},
            {0x03C3,"NEW"},{0x07F1,"PRINT_CH"},{0x0851,"LPRINT_CH"},{0x0869,"COPY"},{0x0871,"COPY_BUFF"},
            {0x0EC5,"TEST_ROOM"},{0x0F55,"SCANNING"},{0x12C3,"STK_STORE"},{0x13F8,"STK_FETCH"},
            {0x151D,"STACK_A"},{0x1520,"STACK_BC"},{0x158A,"FP_TO_BC"},{0x15CD,"FP_TO_A"},{0x15DB,"PRINT_FP"},
            {0x199D,"CALCULATE"},
        };

        /// <summary>Names of the ROM calculator's operation codes (the bytes after RST 28h),
        /// as in the standard ZX81 ROM disassembly.</summary>
        static readonly string[] CalcNames = {
            "jump-true","exchange","delete","subtract","multiply","division","to-power","or",
            "no-&-no","no-l-eql","no-gr-eql","nos-neql","no-grtr","no-less","nos-eql","addition",
            "str-&-no","str-l-eql","str-gr-eql","strs-neql","str-grtr","str-less","strs-eql","strs-add",
            "negate","code","val","len","sin","cos","tan","asn","acs","atn","ln","exp","int","sqr","sgn","abs",
            "peek","usr-no","str$","chrs","not","duplicate","n-mod-m","jump","stk-data","dec-jr-nz",
            "less-0","greater-0","end-calc","get-argt","truncate","fp-calc-2","e-to-fp" };
        static readonly string[] CalcConsts = { "stk-zero", "stk-one", "stk-half", "stk-pi/2", "stk-ten" };

        /// <summary>Name of one calculator operation code, e.g. 1Ch -> "sin".</summary>
        public static string CalcName(int c)
        {
            if (c < CalcNames.Length) return CalcNames[c];
            if (c >= 0xA0 && c <= 0xA4) return CalcConsts[c - 0xA0];
            if (c >= 0xC0 && c <= 0xC5) return "st-mem-" + (c - 0xC0);
            if (c >= 0xE0 && c <= 0xE5) return "get-mem-" + (c - 0xE0);
            if (c >= 0x80 && c <= 0x9F) return "series-" + (c - 0x80);
            return "?";
        }

        /// <summary>Comment for the bytes after RST 28h (calculator codes) or RST 08h (an
        /// error report), starting at a, for count bytes.</summary>
        public static string CalcComment(byte[] mem, int a, int count)
        {
            // which RST is this after? walk back over the calculator bytes
            int b = a - 1;
            while (b > 0 && b > a - 300 && mem[b] != 0xEF && mem[b] != 0xCF) b--;
            if (b == a - 1 && mem[b] == 0xCF)
            {
                const string reports = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                int r = mem[a] + 1;
                return "error report " + (r < reports.Length ? reports[r].ToString() : "?");
            }
            var names = new List<string>();
            for (int k = a; k < a + count; k++)
            {
                int c = mem[k];
                names.Add(CalcName(c));
                // codes with an operand byte (relative jumps): name it, skip it
                if ((c == 0x00 || c == 0x2F || c == 0x31) && k + 1 < a + count) { names.Add("(offset)"); k++; }
                else if (c == 0x30) { if (k + 1 < a + count) names.Add("(data...)"); break; }
            }
            return "calculator: " + string.Join(", ", names);
        }

        /// <summary>System variables 4000h-407Ch, by address (2-byte ones also match address+1 as name+1).</summary>
        public static readonly Dictionary<int, string> SysvarLabels = new Dictionary<int, string>();

        static Z80Disasm()
        {
            SysvarLabels[0x4000] = "ERR_NR"; SysvarLabels[0x4001] = "FLAGS"; SysvarLabels[0x4002] = "ERR_SP";
            SysvarLabels[0x4004] = "RAMTOP"; SysvarLabels[0x4006] = "MODE"; SysvarLabels[0x4007] = "PPC";
            foreach (var v in PFile.Sysvars) if (!v.Name.StartsWith("(")) SysvarLabels[v.Address] = v.Name;
        }

        /// <summary>Name for an address if it's a known ROM routine or system variable, else null.</summary>
        public static string KnownLabel(int a)
        {
            string s;
            if (RomLabels.TryGetValue(a, out s)) return s;
            if (SysvarLabels.TryGetValue(a, out s)) return s;
            if (a > 0x4000 && a < 0x407D && SysvarLabels.TryGetValue(a - 1, out s) && s != "MEMBOT" && s != "PRBUFF") return s + "+1";
            return null;
        }

        /// <summary>Hex in assembler style: 0ABCDh / 0Ah.</summary>
        public static string Hex(int v, int digits)
        {
            string h = v.ToString(digits == 2 ? "X2" : "X4");
            return (char.IsLetter(h[0]) ? "0" : "") + h + "h";
        }

        // ------------------------------------------------------------------
        //  Decoding
        // ------------------------------------------------------------------
        /// <summary>Decode the instruction at addr. label(address) may return a name for an
        /// address, or null to print it in hex.</summary>
        public static Instr Decode(byte[] mem, int addr, Func<int, string> label)
        {
            var d = new Dec { Mem = mem, Start = addr, Pos = addr, Label = label };
            var ins = new Instr { Addr = addr };
            try { d.Run(ins); }
            catch (IndexOutOfRangeException) { ins.Invalid = true; }
            if (ins.Invalid)
            {
                ins.Len = 1; ins.Text = "DB " + Hex(mem[addr & 0xFFFF], 2); ins.Shape = "DB n"; ins.Continues = true;
                ins.Target = -1; ins.IsCall = ins.IsJump = false;
            }
            else ins.Len = (d.Pos - addr) & 0xFFFF;
            return ins;
        }

        /// <summary>Decoding state for one instruction.</summary>
        class Dec
        {
            public byte[] Mem;
            public int Start, Pos;
            public Func<int, string> Label;
            string ix;          // null, "IX" or "IY" (after a DD / FD prefix)
            int disp;           // the (IX+d) displacement
            bool dispRead;

            int Next() { int b = Mem[Pos & 0xFFFF]; Pos++; return b; }
            int Word() { int lo = Next(); return lo | Next() << 8; }

            /// <summary>An address as a label or hex. immediate = a plain number loaded into a
            /// register (LD BC,nn): small values there are numbers, not ROM addresses.</summary>
            string Addr16(int v, bool immediate = false)
            {
                if (immediate && (v & 0xFFFF) < 0x4000) return Hex(v & 0xFFFF, 4);
                string l = Label == null ? null : Label(v & 0xFFFF);
                return l ?? Hex(v & 0xFFFF, 4);
            }

            /// <summary>Register name, taking a DD/FD prefix into account.</summary>
            string Reg(int r, bool allowIxHalves)
            {
                if (ix == null) return R[r];
                if (r == 6)
                {
                    if (!dispRead) { disp = (sbyte)Next(); dispRead = true; }
                    return "(" + ix + (disp < 0 ? "-" + Hex(-disp, 2) : "+" + Hex(disp, 2)) + ")";
                }
                if (allowIxHalves && (r == 4 || r == 5)) return ix + (r == 4 ? "H" : "L");
                return R[r];
            }
            string Rp(int p) { return p == 2 && ix != null ? ix : RP[p]; }
            string Rp2(int p) { return p == 2 && ix != null ? ix : RP2[p]; }

            void Set(Instr i, string text, string shape) { i.Text = text; i.Shape = shape; }

            public void Run(Instr ins)
            {
                int op = Next();
                // prefixes
                if (op == 0xDD || op == 0xFD)
                {
                    int nxt = Mem[Pos & 0xFFFF];
                    if (nxt == 0xDD || nxt == 0xFD || nxt == 0xED) { ins.Invalid = true; return; }   // prefix with no effect
                    ix = op == 0xDD ? "IX" : "IY";
                    op = Next();
                    if (op == 0xCB) { IndexCB(ins); return; }
                }
                else if (op == 0xCB) { CB(ins, Next()); return; }
                else if (op == 0xED) { ED(ins, Next()); return; }
                Main(ins, op);
            }

            void Main(Instr ins, int op)
            {
                int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
                int n, nn;
                switch (x)
                {
                    case 0:
                        switch (z)
                        {
                            case 0:
                                if (y == 0) { Set(ins, "NOP", "NOP"); return; }
                                if (y == 1) { Set(ins, "EX AF,AF'", "EX AF,AF'"); return; }
                                {
                                    int d = (sbyte)Next();
                                    int t = (Pos + d) & 0xFFFF;
                                    ins.Target = t; ins.IsJump = true;
                                    if (y == 2) { Set(ins, "DJNZ " + Addr16(t), "DJNZ d"); return; }
                                    if (y == 3) { Set(ins, "JR " + Addr16(t), "JR d"); ins.Continues = false; return; }
                                    Set(ins, "JR " + CC[y - 4] + "," + Addr16(t), "JR " + CC[y - 4] + ",d"); return;
                                }
                            case 1:
                                if (q == 0) { nn = Word(); ins.Value = nn; Set(ins, "LD " + Rp(p) + "," + Addr16(nn, true), "LD " + Rp(p) + ",nn"); return; }
                                Set(ins, "ADD " + Rp(2) + "," + Rp(p), "ADD " + Rp(2) + "," + Rp(p)); return;
                            case 2:
                                {
                                    string[] txt = q == 0
                                        ? new[] { "LD (BC),A", "LD (DE),A", "LD ({0})," + Rp(2), "LD ({0}),A" }
                                        : new[] { "LD A,(BC)", "LD A,(DE)", "LD " + Rp(2) + ",({0})", "LD A,({0})" };
                                    if (p >= 2) { nn = Word(); ins.Value = nn; Set(ins, string.Format(txt[p], Addr16(nn)), string.Format(txt[p], "nn")); }
                                    else Set(ins, txt[p], txt[p]);
                                    return;
                                }
                            case 3: Set(ins, (q == 0 ? "INC " : "DEC ") + Rp(p), (q == 0 ? "INC " : "DEC ") + Rp(p)); return;
                            case 4: { string r = Reg(y, true); Set(ins, "INC " + r, "INC " + r); return; }
                            case 5: { string r = Reg(y, true); Set(ins, "DEC " + r, "DEC " + r); return; }
                            case 6:
                                {
                                    string r = Reg(y, true);   // reads the displacement before n
                                    n = Next(); ins.Value = n;
                                    Set(ins, "LD " + r + "," + Hex(n, 2), "LD " + r + ",n"); return;
                                }
                            case 7:
                                {
                                    string[] t = { "RLCA", "RRCA", "RLA", "RRA", "DAA", "CPL", "SCF", "CCF" };
                                    Set(ins, t[y], t[y]); return;
                                }
                        }
                        break;
                    case 1:
                        if (z == 6 && y == 6) { Set(ins, "HALT", "HALT"); ins.Continues = false; return; }
                        {
                            // when one side is (IX+d), the other side uses plain H / L
                            bool mem = y == 6 || z == 6;
                            string a = Reg(y, !mem), b = Reg(z, !mem);
                            Set(ins, "LD " + a + "," + b, "LD " + a + "," + b); return;
                        }
                    case 2:
                        { string r = Reg(z, true); Set(ins, ALU[y] + r, ALU[y] + r); return; }
                    case 3:
                        switch (z)
                        {
                            case 0: Set(ins, "RET " + CC[y], "RET " + CC[y]); return;
                            case 1:
                                if (q == 0) { Set(ins, "POP " + Rp2(p), "POP " + Rp2(p)); return; }
                                switch (p)
                                {
                                    case 0: Set(ins, "RET", "RET"); ins.Continues = false; return;
                                    case 1: Set(ins, "EXX", "EXX"); return;
                                    case 2: Set(ins, "JP (" + Rp(2) + ")", "JP (" + Rp(2) + ")"); ins.Continues = false; ins.IsJump = true; return;
                                    default: Set(ins, "LD SP," + Rp(2), "LD SP," + Rp(2)); return;
                                }
                            case 2:
                                nn = Word(); ins.Value = nn; ins.Target = nn; ins.IsJump = true;
                                Set(ins, "JP " + CC[y] + "," + Addr16(nn), "JP " + CC[y] + ",nn"); return;
                            case 3:
                                switch (y)
                                {
                                    case 0: nn = Word(); ins.Value = nn; ins.Target = nn; ins.IsJump = true; ins.Continues = false; Set(ins, "JP " + Addr16(nn), "JP nn"); return;
                                    case 2: n = Next(); ins.Value = n; Set(ins, "OUT (" + Hex(n, 2) + "),A", "OUT (n),A"); return;
                                    case 3: n = Next(); ins.Value = n; Set(ins, "IN A,(" + Hex(n, 2) + ")", "IN A,(n)"); return;
                                    case 4: Set(ins, "EX (SP)," + Rp(2), "EX (SP)," + Rp(2)); return;
                                    case 5: Set(ins, "EX DE,HL", "EX DE,HL"); return;
                                    case 6: Set(ins, "DI", "DI"); return;
                                    case 7: Set(ins, "EI", "EI"); return;
                                }
                                break;
                            case 4:
                                nn = Word(); ins.Value = nn; ins.Target = nn; ins.IsCall = true;
                                Set(ins, "CALL " + CC[y] + "," + Addr16(nn), "CALL " + CC[y] + ",nn"); return;
                            case 5:
                                if (q == 0) { Set(ins, "PUSH " + Rp2(p), "PUSH " + Rp2(p)); return; }
                                if (p == 0) { nn = Word(); ins.Value = nn; ins.Target = nn; ins.IsCall = true; Set(ins, "CALL " + Addr16(nn), "CALL nn"); return; }
                                ins.Invalid = true; return;
                            case 6:
                                n = Next(); ins.Value = n; Set(ins, ALU[y] + Hex(n, 2), ALU[y] + "n"); return;
                            case 7:
                                ins.Target = y * 8; ins.IsCall = true;
                                Set(ins, "RST " + Hex(y * 8, 2), "RST " + Hex(y * 8, 2));
                                if (y == 1) ins.Continues = false;   // RST 8 = ERROR_1: never returns
                                return;
                        }
                        break;
                }
                ins.Invalid = true;
            }

            void CB(Instr ins, int op)
            {
                int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
                string r = R[z];
                if (x == 0) { Set(ins, ROT[y] + " " + r, ROT[y] + " " + r); return; }
                string[] b = { "", "BIT ", "RES ", "SET " };
                Set(ins, b[x] + y + "," + r, b[x] + y + "," + r);
            }

            void IndexCB(Instr ins)
            {
                disp = (sbyte)Next(); dispRead = true;
                int op = Next();
                int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
                string m = Reg(6, false);
                string extra = (z != 6 && x != 1) ? "," + R[z] : "";   // undocumented: result also copied to a register
                if (x == 0) { Set(ins, ROT[y] + " " + m + extra, ROT[y] + " " + m + extra); return; }
                string[] b = { "", "BIT ", "RES ", "SET " };
                Set(ins, b[x] + y + "," + m + extra, b[x] + y + "," + m + extra);
            }

            void ED(Instr ins, int op)
            {
                int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
                int nn;
                if (x == 1)
                {
                    switch (z)
                    {
                        case 0: Set(ins, y == 6 ? "IN (C)" : "IN " + R[y] + ",(C)", y == 6 ? "IN (C)" : "IN " + R[y] + ",(C)"); return;
                        case 1: Set(ins, y == 6 ? "OUT (C),0" : "OUT (C)," + R[y], y == 6 ? "OUT (C),0" : "OUT (C)," + R[y]); return;
                        case 2: Set(ins, (q == 0 ? "SBC HL," : "ADC HL,") + RP[p], (q == 0 ? "SBC HL," : "ADC HL,") + RP[p]); return;
                        case 3:
                            nn = Word(); ins.Value = nn;
                            if (q == 0) Set(ins, "LD (" + Addr16(nn) + ")," + RP[p], "LD (nn)," + RP[p]);
                            else Set(ins, "LD " + RP[p] + ",(" + Addr16(nn) + ")", "LD " + RP[p] + ",(nn)");
                            return;
                        case 4: Set(ins, "NEG", "NEG"); return;
                        case 5: Set(ins, y == 1 ? "RETI" : "RETN", y == 1 ? "RETI" : "RETN"); ins.Continues = false; return;
                        case 6: Set(ins, "IM " + IM[y], "IM " + IM[y]); return;
                        case 7:
                            {
                                string[] t = { "LD I,A", "LD R,A", "LD A,I", "LD A,R", "RRD", "RLD", "NOP", "NOP" };
                                if (y >= 6) { ins.Invalid = true; return; }
                                Set(ins, t[y], t[y]); return;
                            }
                    }
                }
                if (x == 2 && z <= 3 && y >= 4) { Set(ins, BLI[y - 4, z], BLI[y - 4, z]); return; }
                ins.Invalid = true;
            }
        }

        // ------------------------------------------------------------------
        //  Simple linear listing (used by the browser's disassembly pane)
        // ------------------------------------------------------------------
        /// <summary>Disassemble from start to end, one instruction after another.</summary>
        public static List<Instr> Linear(byte[] mem, int start, int end, Func<int, string> label)
        {
            var list = new List<Instr>();
            int a = start;
            while (a < end)
            {
                var i = Decode(mem, a, label);
                if (i.Len <= 0) i.Len = 1;
                if (a + i.Len > end) { i = new Instr { Addr = a, Len = 1, Text = "DB " + Hex(mem[a], 2), Shape = "DB n", Invalid = true }; }
                list.Add(i);
                a += i.Len;
            }
            return list;
        }
    }
}
