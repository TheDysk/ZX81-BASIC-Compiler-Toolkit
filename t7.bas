# BASIC rebuilt from the machine code made by the ZX81 BASIC compiler - from t7.p
# AUTO-RUNS from line 10 after loading
# Variable names are not stored in machine code, so they have been renamed.
# Compile with: Max string length 255, BREAK check on, auto-run on
# Verified: the rebuilt BASIC compiles to exactly the same machine code.
#
10 PRINT SIN 1;" ";COS 1;" ";TAN 1
20 PRINT ASN 0.5;" ";ACS 0.5;" ";ATN 1
30 PRINT LN 10;" ";EXP 1;" ";PI
40 PRINT 3;" ";3.5;" ";0
50 LET B=10*SIN (PI/6)
60 PRINT B;" ";INT (-2.5);" ";SQR 2;" ";2**0.5
70 IF SIN 1>0.8 THEN PRINT "GT OK";
80 IF NOT SIN 1<0.8 THEN PRINT " NOT OK";
90 IF COS 1>0.5 AND COS 1<0.6 THEN PRINT " AND OK"
100 LET A$=STR$ (PI*100)
110 PRINT A$;" ";LEN A$;" ";VAL "2*3.5";" ";VAL A$/100
120 LET C=0
130 FOR A=0 TO 7
140 LET C=C+PEEK (16384+INT (SIN A*3+4))
150 NEXT A
160 PRINT C;" ";ABS (-0.25);" ";SGN (-0.1);" ";25000
170 LET D=RND
180 PRINT D>=0 AND D<1;" ";40000.5;" ";-1E-5;" ";1E10
190 FOR A=0 TO 31
200 PLOT 48+INT (8*COS (A*PI/16)),10+INT (8*SIN (A*PI/16))
210 NEXT A
220 LPRINT "PRINTER ";PI;TAB 20;"T20"
230 LPRINT AT 3,5;"AT5",7.5
240 LPRINT 42
250 COPY
