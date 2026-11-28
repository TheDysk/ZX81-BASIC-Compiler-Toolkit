# BASIC only test: labels, missing line numbers, LET and THEN GOTO added, DIM A(N), 3-D arrays
# run_tests.sh builds it with --basic-only and runs it in the ROM's own BASIC
#BASIC_ONLY
#STRICT_INT
10 CLS
N=4
DIM A(N)
DIM C(2,2,3)
FOR I=1 TO N
A(I)=I*I/2
NEXT I
LET C(2,1,3)=7.25
PRINT A(1);" ";A(3);" ";C(2,1,3)
IF A(4)>5 THEN @big
PRINT "SMALL"
@big: PRINT "BIG ";7/2
LET S$="hello"
PRINT S$;" ";LEN S$
GOSUB @sub
PRINT "BACK"
STOP
@sub: PRINT "SUB"
RETURN
