# Full floating point test: division by zero: report 6, as on the ZX81
# run_tests.sh compares the compiled program with the ROM's own BASIC interpreter
10 CLS
20 PRINT "BEFORE"
30 LET A=2
40 PRINT A/(A-2)
