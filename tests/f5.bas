# Full floating point test: 2D arrays (3 columns), changing the FOR variable, calculated GOSUB/GOTO, POKE/PEEK/USR, string arrays, negative powers (report A)
# run_tests.sh compares the compiled program with the ROM's own BASIC interpreter
10 CLS
20 DIM C(2,3)
30 LET C(2,3)=1.25
40 LET C(1,2)=C(2,3)*2
50 PRINT C(1,2);" ";C(2,3);" ";C(1,1)
60 FOR I=1 TO 3
70 IF I=2 THEN LET I=2.5
80 PRINT I;" ";
90 NEXT I
100 PRINT
110 LET N=3
120 GOSUB N*100+0.4
130 GOTO 150+N/3*10
140 PRINT "WRONG"
160 PRINT "GOTO ";N
170 PLOT 10.6,20.4
180 PRINT AT 5,0;PEEK (PEEK 16396+256*PEEK 16397+33*5+1)
190 POKE 16507,65.7
200 PRINT PEEK 16507;" ";USR 552;" ";CODE "X";" ";LEN "AB"*1.5
210 LET A$=STR$ 5+STR$ (5/4)+STR$ -0.001
220 PRINT A$;" ";VAL A$(1 TO 2)
230 DIM D$(2,3)
240 LET D$(2)="XYZ"
250 PRINT D$(2)(2 TO 2.6);" ";D$(1)="   "
260 LET Z=-1.5
270 PRINT Z*Z;" ";-Z;" ";ABS Z;" ";INT Z;" ";SGN Z;" ";Z**2
280 PRINT 1E-38;" ";1E38;" ";0.000001;" ";1234.5678
290 STOP
300 PRINT "SUB ";N;
310 RETURN
