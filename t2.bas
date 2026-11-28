# BASIC rebuilt from the machine code made by the ZX81 BASIC compiler - from t2.p
# AUTO-RUNS from line 10 after loading
# Variable names are not stored in machine code, so they have been renamed.
# Compile with: Max string length 255, BREAK check on, auto-run on
# Verified: the rebuilt BASIC compiles to exactly the same machine code.
#
10 REM
20 LET C=-7
30 PRINT C/2;" ";C*3;" ";ABS C;" ";SGN C;" ";2**10;" ";SQR 50
40 PRINT 1;0;1;" ";5 AND 0;" ";5 AND 2;" ";0 OR 3;" ";1
50 DIM A(10)
60 FOR A=1 TO 10
70 LET A(A)=A*A
80 NEXT A
90 PRINT A(1);",";A(5);",";A(10)
100 DIM B(3,4)
110 LET B(2,3)=99
120 PRINT B(2,3);B(1,1);" ";B(3,4)
130 DIM D$(3,5)
140 LET D$(2)="HELLOWORLD"
150 LET D$(1,2)="X"
160 PRINT D$(1);"/";D$(2);"/";D$(2,2 TO 4);"/"
170 LET A$="ZX81 BASIC"
180 PRINT A$(6 TO);"/";A$(1 TO 4);"/";A$(3)
190 LET A$(1 TO 4)="SINC"
200 PRINT A$
210 LET B$=STR$ 1234+CHR$ 38+STR$ (-5)
220 PRINT B$;" ";VAL "123"+1;" ";VAL "-45"
230 IF A$="SINC BASIC" THEN PRINT "STR EQ OK"
240 IF "ABC"<"ABD" AND "B">"A" THEN PRINT "STR LT OK"
250 GOSUB 500
260 LET D=600
270 GOSUB D
280 FOR B=10 TO 1 STEP -3
290 PRINT B;
300 NEXT B
310 PRINT
320 LET E=0
330 FOR A=1 TO 1000
340 LET F=INT (RND*6)+1
350 IF F<1 OR F>6 THEN LET E=E+1
360 NEXT A
370 PRINT "RND BAD=";E
380 FOR A=5 TO 1
390 PRINT "NEVER"
400 NEXT A
410 PRINT "LEN=";LEN (A$+"XYZ");" CODE=";CODE "A"
420 GOTO 700
500 PRINT "SUB 500"
510 RETURN
600 PRINT "SUB 600"
610 RETURN
700 PRINT -32768;" ";-32768;" ";0
710 LET C$=""
720 FOR A=1 TO 5
730 LET C$=C$+CHR$ (CODE "A"+A)
740 NEXT A
750 PRINT C$;" ";C$(2 TO 3)+C$(5)
760 PRINT "A";TAB 10;"B","C"
770 PRINT 14;" ";-14;" ";-14
780 STOP
790 PRINT "NOT HERE"
