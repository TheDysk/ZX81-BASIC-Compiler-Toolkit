# Full floating point test: numeric INPUT (typed keys) into a variable and an array element
# run_tests.sh runs it with typed keys and compares the screen with tests/expected/f6.txt
10 INPUT A
20 INPUT B
30 PRINT A/B;" ";A*2;" ";B
40 DIM C(2)
50 INPUT C(2)
60 PRINT C(2)+0.5
