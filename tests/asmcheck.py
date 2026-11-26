import sys, re, z80
lst = open(sys.argv[1]).read().splitlines()
p = open(sys.argv[2],'rb').read()
sym = {}
for l in lst:
    m = re.match(r'^; (\S+) = ([0-9A-F]{4})$', l)
    if m: sym[m.group(1).upper()] = int(m.group(2),16)
def ev(e):
    e = e.upper()
    e = re.sub(r'\b([0-9][0-9A-F]*)H\b', lambda m: str(int(m.group(1),16)), e)
    e = re.sub(r'\b0X([0-9A-F]+)\b', lambda m: str(int(m.group(1),16)), e)
    e = re.sub(r'[A-Z_][A-Z0-9_]*', lambda m: str(sym[m.group(0)]), e)
    return eval(e) & 0xFFFF
REGS = {'A','B','C','D','E','H','L','HL','DE','BC','SP','AF',"AF'",'NZ','Z','NC','PO','PE','P','M','(HL)','(DE)','(BC)','(SP)','(C)'}
def canon_ours(addr, src):
    src = src.split(';')[0].strip()
    if ' ' not in src: return src.upper(), []
    mn, ops = src.split(None,1)
    out = []
    for o in [x.strip() for x in ops.split(',')]:
        u = o.upper().replace(' ','')
        if u in REGS: out.append(u)
        elif u.startswith('(') and u.endswith(')'): out.append('(%d)' % ev(u[1:-1]))
        else:
            v = ev(u)
            if mn.upper() in ('BIT','SET','RES') and not out: out.append(str(v))
            else: out.append(str(v))
    return mn.upper(), out
def canon_dis(addr, d):
    d = re.sub(r'(?<![A-Za-z])[A-Z](?=[a-z0-9(\$\-])', '', d)
    mn, _, ops = d.partition(' ')
    out = []
    for o in [x.strip() for x in ops.split(',')] if ops else []:
        u = o.upper().replace(' ','')
        if u.startswith('$'):
            out.append(str((addr + int(u[1:] or '0')) & 0xFFFF)); continue
        if u in REGS: out.append(u); continue
        if u.startswith('(') and u.endswith(')'):
            out.append('(%d)' % (int(u[1:-1],0) & 0xFFFF)); continue
        out.append(str(int(u,0) & 0xFFFF))
    return mn.upper(), out
base = 0x4082; off = 0x4082 - 0x4009
n = bad = 0
for l in lst:
    m = re.match(r'^([0-9A-F]{4})  ((?:[0-9A-F]{2} )*(?:\.\. )?)\s*(.*)$', l)
    if not m or not m.group(2).strip(): continue
    addr = int(m.group(1),16); src = m.group(3).split(';')[0].strip()
    if re.match(r'^[A-Za-z_][\w.]*:', src): src = src.split(':',1)[1].strip()
    if not src or src.upper().split()[0] in ('DB','DW','DS'): continue
    b = p[off + addr - base: off + addr - base + 4]
    d, ln = z80.Z80Machine._disasm(bytes(b) + b'\0\0\0\0')
    a1 = canon_ours(addr, src); a2 = canon_dis(addr, d)
    # disassembler shows signed 8-bit immediates etc; normalise byte values
    if a1 != a2:
        a1b = (a1[0], [str(int(x) & 0xFF) if x.lstrip('-').isdigit() and int(x) < 256 else x for x in a1[1]])
        a2b = (a2[0], [str(int(x) & 0xFF) if x.lstrip('-').isdigit() and int(x) < 256 else x for x in a2[1]])
        if a1b != a2b:
            bad += 1; print('%04X ours=%s %s   disasm=%s' % (addr, a1, src, d))
    n += 1
print('checked %d instructions, %d mismatches' % (n, bad))
