# BASIC rebuilt from the machine code made by the ZX81 BASIC compiler - from t8.p
# AUTO-RUNS from line 10 after loading
# Variable names are not stored in machine code, so they have been renamed.
# Compile with: Max string length 255, BREAK check off, auto-run on
# Verified: the rebuilt BASIC compiles to exactly the same machine code.
#
10 PRINT "BEFORE"
20 SAVE "TEST"+STR$ 81
30 PRINT "AFTER SAVE"
40 PRINT SQR (-1)
50 PRINT "NOT REACHED"
