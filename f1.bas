# Full floating point test: arithmetic, functions, AND/OR/NOT, comparisons, STR$/VAL, rounding for AT/TAB/CHR$
# run_tests.sh compares the compiled program with the ROM's own BASIC interpreter
10 CLS
20 PRINT 7/2;" ";1/3;" ";2/3*3
30 PRINT 10/4*4;" ";-7/2;" ";2**10;" ";2**0.5
40 LET A=2.5
50 LET B=A*A-1/8
60 PRINT A;" ";B;" ";A+B;" ";A-B
70 PRINT INT 3.7;" ";INT -3.7;" ";ABS -2.25;" ";SGN -0.5
80 PRINT SQR 2;" ";SIN 1;" ";COS PI;" ";EXP 1
90 PRINT LN 10;" ";TAN 0.5;" ";ATN 1;" ";ASN 0.5;" ";ACS 0
100 PRINT 3 AND 5;" ";3 AND 0;" ";0 OR 7;" ";2 OR 0;" ";NOT 0.5;" ";NOT 0
110 PRINT 1E10;" ";1.5E-5;" ";123456789;" ";0.1+0.2
120 LET C=1/3
130 IF C*3=1 THEN PRINT "EQUAL"
140 IF C<0.34 AND C>0.33 THEN PRINT "BETWEEN"
150 IF C>0.5 OR C<0.1 THEN PRINT "WRONG"
160 PRINT STR$ (A*3);"/";LEN STR$ C;"/";VAL "2*3.5+1"
170 PRINT CHR$ 38.4;CHR$ 38.6;" ";CODE "A"*1.5;" ";PEEK 16389/2
180 PRINT AT 15.4,3.5;"AT";TAB 10.6;"TAB"
190 PRINT 1-0.9;" ";100000*100000;" ";-C
200 LET D=C*(A-1)
210 PRINT D;" ";(D=0.5);" ";(D<>0.5);" ";"A"<"B"
