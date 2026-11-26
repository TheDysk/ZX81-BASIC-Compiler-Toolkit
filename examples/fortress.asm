4082                ; Compiled ZX81 BASIC program
4082                start:
4082  ED 73 A7 42           LD (ENTRYSP),SP
4086  CD AF 43              CALL rt_memcheck
4089  CD 58 43              CALL rt_init
408C                ; line 10
408C                L_10:
408C                E_10:
408C                ; line 20
408C                L_20:
408C  CD 2D 43              CALL rt_cls
408F                E_20:
408F                ; line 30
408F                L_30:
408F  21 00 00              LD HL,0
4092  11 00 00              LD DE,0
4095  CD 10 43              CALL rt_at
4098  21 74 45              LD HL,LIT_0
409B  01 1E 00              LD BC,30
409E  CD BA 44              CALL rt_prstr
40A1  CD 64 44              CALL rt_newline
40A4                E_30:
40A4                ; line 40
40A4                L_40:
40A4  21 15 00              LD HL,21
40A7  11 00 00              LD DE,0
40AA  CD 10 43              CALL rt_at
40AD  21 92 45              LD HL,LIT_1
40B0  01 20 00              LD BC,32
40B3  CD BA 44              CALL rt_prstr
40B6  CD 64 44              CALL rt_newline
40B9                E_40:
40B9                ; line 50
40B9                L_50:
40B9  21 0F 00              LD HL,15
40BC  22 3C 4A              LD (V_X),HL
40BF                E_50:
40BF                ; line 60
40BF                L_60:
40BF  21 00 00              LD HL,0
40C2  22 3E 4A              LD (V_SC),HL
40C5                E_60:
40C5                ; line 70
40C5                L_70:
40C5  21 03 00              LD HL,3
40C8  22 40 4A              LD (V_LIVES),HL
40CB                E_70:
40CB                ; line 80
40CB                L_80:
40CB  21 46 4A              LD HL,A_R
40CE  01 40 00              LD BC,64
40D1  CD 4F 45              CALL rt_zero
40D4                E_80:
40D4                ; line 90
40D4                L_90:
40D4  21 1E 00              LD HL,30
40D7  CD F5 44              CALL rt_rndn
40DA  23                    INC HL
40DB  22 42 4A              LD (V_FX),HL
40DE                E_90:
40DE                ; line 100
40DE                L_100:
40DE  21 02 00              LD HL,2
40E1  22 44 4A              LD (V_FY),HL
40E4                E_100:
40E4                ; line 110
40E4                L_110:
40E4                E_110:
40E4                ; line 120
40E4                L_120:
40E4  21 86 4B              LD HL,T_0
40E7  36 00                 LD (HL),0
40E9  CD 67 43              CALL rt_inkey
40EC  11 86 4B              LD DE,T_0
40EF  DC E4 42              CALL C,rt_appchr
40F2  21 86 4B              LD HL,T_0
40F5  4E                    LD C,(HL)
40F6  06 00                 LD B,0
40F8  23                    INC HL
40F9  11 86 4A              LD DE,S_K
40FC  3E FF                 LD A,255
40FE  CD 43 45              CALL rt_strset
4101                E_120:
4101                ; line 130
4101                L_130:
4101  21 14 00              LD HL,20
4104  ED 5B 3C 4A           LD DE,(V_X)
4108  CD 10 43              CALL rt_at
410B  21 B2 45              LD HL,LIT_2
410E  01 01 00              LD BC,1
4111  CD BA 44              CALL rt_prstr
4114  CD 64 44              CALL rt_newline
4117                E_130:
4117                ; line 140
4117                L_140:
4117  21 86 4A              LD HL,S_K
411A  4E                    LD C,(HL)
411B  06 00                 LD B,0
411D  23                    INC HL
411E  E5                    PUSH HL
411F  C5                    PUSH BC
4120  21 B3 45              LD HL,LIT_3
4123  01 01 00              LD BC,1
4126  79                    LD A,C
4127  C1                    POP BC
4128  D1                    POP DE
4129  CD 1F 45              CALL rt_strcmp
412C  11 00 00              LD DE,0
412F  CD 9D 43              CALL rt_eq
4132  E5                    PUSH HL
4133  2A 3C 4A              LD HL,(V_X)
4136  11 1F 00              LD DE,31
4139  CD 7F 43              CALL rt_lt
413C  EB                    EX DE,HL
413D  E1                    POP HL
413E  CD CF 42              CALL rt_and
4141  EB                    EX DE,HL
4142  2A 3C 4A              LD HL,(V_X)
4145  19                    ADD HL,DE
4146  E5                    PUSH HL
4147  21 86 4A              LD HL,S_K
414A  4E                    LD C,(HL)
414B  06 00                 LD B,0
414D  23                    INC HL
414E  E5                    PUSH HL
414F  C5                    PUSH BC
4150  21 B4 45              LD HL,LIT_4
4153  01 01 00              LD BC,1
4156  79                    LD A,C
4157  C1                    POP BC
4158  D1                    POP DE
4159  CD 1F 45              CALL rt_strcmp
415C  11 00 00              LD DE,0
415F  CD 9D 43              CALL rt_eq
4162  E5                    PUSH HL
4163  2A 3C 4A              LD HL,(V_X)
4166  11 00 00              LD DE,0
4169  CD 88 43              CALL rt_gt
416C  EB                    EX DE,HL
416D  E1                    POP HL
416E  CD CF 42              CALL rt_and
4171  EB                    EX DE,HL
4172  E1                    POP HL
4173  B7                    OR A
4174  ED 52                 SBC HL,DE
4176  22 3C 4A              LD (V_X),HL
4179                E_140:
4179                ; line 150
4179                L_150:
4179  21 14 00              LD HL,20
417C  ED 5B 3C 4A           LD DE,(V_X)
4180  CD 10 43              CALL rt_at
4183  21 B5 45              LD HL,LIT_5
4186  01 01 00              LD BC,1
4189  CD BA 44              CALL rt_prstr
418C  CD 64 44              CALL rt_newline
418F                E_150:
418F                ; line 160
418F                L_160:
418F  2A 44 4A              LD HL,(V_FY)
4192  ED 5B 42 4A           LD DE,(V_FX)
4196  CD 10 43              CALL rt_at
4199  21 B2 45              LD HL,LIT_2
419C  01 01 00              LD BC,1
419F  CD BA 44              CALL rt_prstr
41A2  CD 64 44              CALL rt_newline
41A5                E_160:
41A5                ; line 170
41A5                L_170:
41A5  2A 44 4A              LD HL,(V_FY)
41A8  23                    INC HL
41A9  22 44 4A              LD (V_FY),HL
41AC                E_170:
41AC                ; line 180
41AC                L_180:
41AC  2A 44 4A              LD HL,(V_FY)
41AF  11 14 00              LD DE,20
41B2  B7                    OR A
41B3  ED 52                 SBC HL,DE
41B5  C2 BB 41              JP NZ,E_180
41B8  CD 1E 42              CALL L_300
41BB                E_180:
41BB                ; line 190
41BB                L_190:
41BB  2A 44 4A              LD HL,(V_FY)
41BE  ED 5B 42 4A           LD DE,(V_FX)
41C2  CD 10 43              CALL rt_at
41C5  21 B6 45              LD HL,LIT_6
41C8  01 01 00              LD BC,1
41CB  CD BA 44              CALL rt_prstr
41CE  CD 64 44              CALL rt_newline
41D1                E_190:
41D1                ; line 200
41D1                L_200:
41D1  21 01 00              LD HL,1
41D4  11 00 00              LD DE,0
41D7  CD 10 43              CALL rt_at
41DA  21 B7 45              LD HL,LIT_7
41DD  01 06 00              LD BC,6
41E0  CD BA 44              CALL rt_prstr
41E3  2A 3E 4A              LD HL,(V_SC)
41E6  CD B4 44              CALL rt_prnum
41E9  21 BD 45              LD HL,LIT_8
41EC  01 07 00              LD BC,7
41EF  CD BA 44              CALL rt_prstr
41F2  2A 40 4A              LD HL,(V_LIVES)
41F5  CD B4 44              CALL rt_prnum
41F8  21 C4 45              LD HL,LIT_9
41FB  01 02 00              LD BC,2
41FE  CD BA 44              CALL rt_prstr
4201  CD 64 44              CALL rt_newline
4204                E_200:
4204                ; line 210
4204                L_210:
4204  2A 40 4A              LD HL,(V_LIVES)
4207  7C                    LD A,H
4208  B5                    OR L
4209  C2 12 42              JP NZ,E_210
420C  CD 24 43              CALL rt_brk
420F  C3 71 42              JP L_400
4212                E_210:
4212                ; line 215
4212                L_215:
4212  21 03 00              LD HL,3
4215  CD 47 44              CALL rt_pause
4218                E_215:
4218                ; line 220
4218                L_220:
4218  CD 24 43              CALL rt_brk
421B  C3 E4 40              JP L_120
421E                E_220:
421E                ; line 300
421E                L_300:
421E                E_300:
421E                ; line 310
421E                L_310:
421E  2A 42 4A              LD HL,(V_FX)
4221  ED 5B 3C 4A           LD DE,(V_X)
4225  B7                    OR A
4226  ED 52                 SBC HL,DE
4228  C2 35 42              JP NZ,E_310
422B  2A 3E 4A              LD HL,(V_SC)
422E  11 0A 00              LD DE,10
4231  19                    ADD HL,DE
4232  22 3E 4A              LD (V_SC),HL
4235                E_310:
4235                ; line 320
4235                L_320:
4235  2A 42 4A              LD HL,(V_FX)
4238  ED 5B 3C 4A           LD DE,(V_X)
423C  B7                    OR A
423D  ED 52                 SBC HL,DE
423F  CA 49 42              JP Z,E_320
4242  2A 40 4A              LD HL,(V_LIVES)
4245  2B                    DEC HL
4246  22 40 4A              LD (V_LIVES),HL
4249                E_320:
4249                ; line 330
4249                L_330:
4249  2A 44 4A              LD HL,(V_FY)
424C  2B                    DEC HL
424D  ED 5B 42 4A           LD DE,(V_FX)
4251  CD 10 43              CALL rt_at
4254  21 B2 45              LD HL,LIT_2
4257  01 01 00              LD BC,1
425A  CD BA 44              CALL rt_prstr
425D  CD 64 44              CALL rt_newline
4260                E_330:
4260                ; line 340
4260                L_340:
4260  21 1E 00              LD HL,30
4263  CD F5 44              CALL rt_rndn
4266  23                    INC HL
4267  22 42 4A              LD (V_FX),HL
426A                E_340:
426A                ; line 350
426A                L_350:
426A  21 02 00              LD HL,2
426D  22 44 4A              LD (V_FY),HL
4270                E_350:
4270                ; line 360
4270                L_360:
4270  C9                    RET
4271                E_360:
4271                ; line 400
4271                L_400:
4271  21 0A 00              LD HL,10
4274  11 08 00              LD DE,8
4277  CD 10 43              CALL rt_at
427A  21 C6 45              LD HL,LIT_10
427D  01 09 00              LD BC,9
4280  CD BA 44              CALL rt_prstr
4283  CD 64 44              CALL rt_newline
4286                E_400:
4286                ; line 410
4286                L_410:
4286  21 0C 00              LD HL,12
4289  11 06 00              LD DE,6
428C  CD 10 43              CALL rt_at
428F  21 CF 45              LD HL,LIT_11
4292  01 0C 00              LD BC,12
4295  CD BA 44              CALL rt_prstr
4298  2A 3E 4A              LD HL,(V_SC)
429B  CD B4 44              CALL rt_prnum
429E  CD 64 44              CALL rt_newline
42A1                E_410:
42A1                ; line 420
42A1                L_420:
42A1  C3 17 45              JP rt_stop
42A4                E_420:
42A4  C3 17 45              JP rt_stop
42A7                ENTRYSP:
42A7  00 00                 DW 0
42A9                neg_hl:
42A9  AF                XOR A
42AA  95                SUB L
42AB  6F                LD L,A
42AC  9F                SBC A,A
42AD  94                SUB H
42AE  67                LD H,A
42AF  C9                RET
42B0                pr_addr:                ; HL = display address of print position
42B0  3A 08 4A          LD A,(PR_ROW)
42B3  67                LD H,A
42B4  3A 09 4A          LD A,(PR_COL)
42B7  6F                LD L,A
42B8                rc_addr:                ; HL = display address of row H, column L. Uses A, DE
42B8  5D                LD E,L
42B9  16 00             LD D,0
42BB  6C                LD L,H
42BC  62                LD H,D
42BD  D5                PUSH DE
42BE  54                LD D,H
42BF  5D                LD E,L
42C0  29                ADD HL,HL
42C1  29                ADD HL,HL
42C2  29                ADD HL,HL
42C3  29                ADD HL,HL
42C4  29                ADD HL,HL
42C5  19                ADD HL,DE
42C6  D1                POP DE
42C7  19                ADD HL,DE
42C8  ED 5B 0C 40       LD DE,(D_FILE)
42CC  19                ADD HL,DE
42CD  23                INC HL
42CE  C9                RET
42CF                rt_and:                 ; ZX81: a AND b = a if b<>0 else 0
42CF  7A                LD A,D
42D0  B3                OR E
42D1  C0                RET NZ
42D2  67                LD H,A
42D3  6F                LD L,A
42D4  C9                RET
42D5                rt_or:                  ; ZX81: a OR b = 1 if b<>0 else a
42D5  7A                LD A,D
42D6  B3                OR E
42D7  C8                RET Z
42D8  21 01 00          LD HL,1
42DB  C9                RET
42DC                rt_not:
42DC  7C                LD A,H
42DD  B5                OR L
42DE  21 00 00          LD HL,0
42E1  C0                RET NZ
42E2  2C                INC L
42E3  C9                RET
42E4                rt_appchr:              ; append character A to temp string at DE
42E4  21 15 4A          LD HL,CHARBUF
42E7  77                LD (HL),A
42E8  01 01 00          LD BC,1
42EB                rt_append:              ; append BC bytes from HL to temp string (len byte + 255) at DE
42EB  78                LD A,B
42EC  B1                OR C
42ED  C8                RET Z
42EE  1A                LD A,(DE)
42EF  E5                PUSH HL
42F0  6F                LD L,A
42F1  26 00             LD H,0
42F3  09                ADD HL,BC
42F4  7C                LD A,H
42F5  B7                OR A
42F6  28 08             JR Z,ap_ok
42F8  1A                LD A,(DE)
42F9  2F                CPL
42FA  4F                LD C,A
42FB  06 00             LD B,0
42FD  21 FF 00          LD HL,255
4300                ap_ok:
4300  1A                LD A,(DE)
4301  EB                EX DE,HL
4302  73                LD (HL),E
4303  23                INC HL
4304  5F                LD E,A
4305  16 00             LD D,0
4307  19                ADD HL,DE
4308  EB                EX DE,HL
4309  E1                POP HL
430A  78                LD A,B
430B  B1                OR C
430C  C8                RET Z
430D  ED B0             LDIR
430F  C9                RET
4310                rt_at:                  ; PRINT AT HL,DE
4310  7C                LD A,H
4311  B2                OR D
4312  C0                RET NZ
4313  7D                LD A,L
4314  FE 16             CP 22
4316  D0                RET NC
4317  7B                LD A,E
4318  FE 20             CP 32
431A  D0                RET NC
431B  7D                LD A,L
431C  32 08 4A          LD (PR_ROW),A
431F  7B                LD A,E
4320  32 09 4A          LD (PR_COL),A
4323  C9                RET
4324                rt_brk:                 ; BREAK (SPACE) pressed? then stop
4324  3E 7F             LD A,7Fh
4326  DB FE             IN A,(0FEh)
4328  1F                RRA
4329  D8                RET C
432A  C3 17 45          JP rt_stop
432D                rt_cls:
432D  2A 0C 40          LD HL,(D_FILE)
4330  23                INC HL
4331  0E 18             LD C,24
4333                cls_row:
4333  06 20             LD B,32
4335                cls_ch:
4335  36 00             LD (HL),0
4337  23                INC HL
4338  10 FB             DJNZ cls_ch
433A  23                INC HL
433B  0D                DEC C
433C  20 F5             JR NZ,cls_row
433E  AF                XOR A
433F  32 08 4A          LD (PR_ROW),A
4342  32 09 4A          LD (PR_COL),A
4345  C9                RET
4346                rt_frame:               ; wait for the next TV frame (or about 20ms in FAST mode)
4346  3A 34 40          LD A,(FRAMES)
4349  4F                LD C,A
434A  11 46 05          LD DE,1350
434D                fr_lp:
434D  3A 34 40          LD A,(FRAMES)
4350  B9                CP C
4351  C0                RET NZ
4352  1B                DEC DE
4353  7A                LD A,D
4354  B3                OR E
4355  20 F6             JR NZ,fr_lp
4357  C9                RET
4358                rt_init:
4358  21 08 4A          LD HL,BSS
435B  01 7F 02          LD BC,BSS_SIZE
435E  CD 4F 45          CALL rt_zero
4361  CD C8 44          CALL rt_randomize
4364  C3 2D 43          JP rt_cls
4367                rt_inkey:               ; carry set and A = key code if a key is pressed
4367  CD BB 02          CALL 02BBh
436A  44                LD B,H
436B  4D                LD C,L
436C  51                LD D,C
436D  14                INC D
436E  28 07             JR Z,ik_none
4370  CD BD 07          CALL 07BDh
4373  D0                RET NC
4374  7E                LD A,(HL)
4375  37                SCF
4376  C9                RET
4377                ik_none:
4377  B7                OR A
4378  C9                RET
4379                rt_keyany:              ; NZ if any key pressed
4379  CD BB 02          CALL 02BBh
437C  7D                LD A,L
437D  3C                INC A
437E  C9                RET
437F                rt_lt:
437F  CD 0A 45          CALL rt_scmp
4382                bool_c:
4382  21 00 00          LD HL,0
4385  D0                RET NC
4386  2C                INC L
4387  C9                RET
4388                rt_gt:
4388  EB                EX DE,HL
4389  CD 0A 45          CALL rt_scmp
438C  18 F4             JR bool_c
438E                rt_ge:
438E  CD 0A 45          CALL rt_scmp
4391                bool_nc:
4391  21 00 00          LD HL,0
4394  D8                RET C
4395  2C                INC L
4396  C9                RET
4397                rt_le:
4397  EB                EX DE,HL
4398  CD 0A 45          CALL rt_scmp
439B  18 F4             JR bool_nc
439D                rt_eq:
439D  B7                OR A
439E  ED 52             SBC HL,DE
43A0  21 00 00          LD HL,0
43A3  C0                RET NZ
43A4  2C                INC L
43A5  C9                RET
43A6                rt_ne:
43A6  B7                OR A
43A7  ED 52             SBC HL,DE
43A9  21 00 00          LD HL,0
43AC  C8                RET Z
43AD  2C                INC L
43AE  C9                RET
43AF                ; make sure the machine stack is safely above our variables, else show a message and return
43AF                rt_memcheck:
43AF  21 00 00          LD HL,0
43B2  39                ADD HL,SP
43B3  11 27 4D          LD DE,BSS_END+160
43B6  B7                OR A
43B7  ED 52             SBC HL,DE
43B9  D0                RET NC
43BA  2A 0C 40          LD HL,(D_FILE)
43BD  23                INC HL
43BE  11 CF 43          LD DE,mc_msg
43C1  EB                EX DE,HL
43C2  01 0D 00          LD BC,13
43C5  ED B0             LDIR
43C7  ED 7B A7 42       LD SP,(ENTRYSP)
43CB  01 04 00          LD BC,4
43CE  C9                RET
43CF                mc_msg:
43CF  34 3A 39 00 ..     DB 34h,3Ah,39h,00h,34h,2Bh,00h,32h,2Ah,32h,34h,37h,3Eh
43DC                rt_mulhi:               ; HL = (HL * DE) >> 16, unsigned
43DC  44                LD B,H
43DD  4D                LD C,L
43DE  21 00 00          LD HL,0
43E1  3E 10             LD A,16
43E3                mh_lp:
43E3  CB 3A             SRL D
43E5  CB 1B             RR E
43E7  30 01             JR NC,mh_sh
43E9  09                ADD HL,BC
43EA                mh_sh:
43EA  CB 1C             RR H
43EC  CB 1D             RR L
43EE  3D                DEC A
43EF  20 F2             JR NZ,mh_lp
43F1  C9                RET
43F2                rt_numstr:              ; HL = number -> HL = NUMBUF, BC = length
43F2  AF                XOR A
43F3  32 12 4A          LD (NSFLAG),A
43F6  11 16 4A          LD DE,NUMBUF
43F9  CB 7C             BIT 7,H
43FB  28 07             JR Z,ns_pos
43FD  3E 16             LD A,16h
43FF  12                LD (DE),A
4400  13                INC DE
4401  CD A9 42          CALL neg_hl
4404                ns_pos:
4404  01 F0 D8          LD BC,-10000
4407  CD 2C 44          CALL ns_dig
440A  01 18 FC          LD BC,-1000
440D  CD 2C 44          CALL ns_dig
4410  01 9C FF          LD BC,-100
4413  CD 2C 44          CALL ns_dig
4416  01 F6 FF          LD BC,-10
4419  CD 2C 44          CALL ns_dig
441C  7D                LD A,L
441D  C6 1C             ADD A,1Ch
441F  12                LD (DE),A
4420  13                INC DE
4421  21 16 4A          LD HL,NUMBUF
4424  EB                EX DE,HL
4425  B7                OR A
4426  ED 52             SBC HL,DE
4428  44                LD B,H
4429  4D                LD C,L
442A  EB                EX DE,HL
442B  C9                RET
442C                ns_dig:
442C  3E FF             LD A,0FFh
442E                nd_lp:
442E  3C                INC A
442F  09                ADD HL,BC
4430  38 FC             JR C,nd_lp
4432  ED 42             SBC HL,BC
4434  B7                OR A
4435  20 06             JR NZ,nd_put
4437  3A 12 4A          LD A,(NSFLAG)
443A  B7                OR A
443B  C8                RET Z
443C  AF                XOR A
443D                nd_put:
443D  C6 1C             ADD A,1Ch
443F  12                LD (DE),A
4440  13                INC DE
4441  3E 01             LD A,1
4443  32 12 4A          LD (NSFLAG),A
4446  C9                RET
4447                rt_pause:               ; PAUSE HL frames (>=32768 = forever), ends on a key press
4447  22 10 4A          LD (PCOUNT),HL
444A  7C                LD A,H
444B  B5                OR L
444C  C8                RET Z
444D                pa_lp:
444D  CD 79 43          CALL rt_keyany
4450  C0                RET NZ
4451  CD 46 43          CALL rt_frame
4454  2A 10 4A          LD HL,(PCOUNT)
4457  CB 7C             BIT 7,H
4459  20 F2             JR NZ,pa_lp
445B  2B                DEC HL
445C  22 10 4A          LD (PCOUNT),HL
445F  7C                LD A,H
4460  B5                OR L
4461  20 EA             JR NZ,pa_lp
4463  C9                RET
4464                rt_newline:
4464  3E 76             LD A,76h
4466                rt_prchr:               ; print ZX81 character A. Preserves BC, DE, HL
4466  E5                PUSH HL
4467  D5                PUSH DE
4468  C5                PUSH BC
4469  FE 76             CP 76h
446B  28 2A             JR Z,pc_nl
446D  CB 77             BIT 6,A
446F  28 02             JR Z,pc_ok
4471  3E 0F             LD A,0Fh
4473                pc_ok:
4473  47                LD B,A
4474  3A 09 4A          LD A,(PR_COL)
4477  FE 20             CP 32
4479  D4 9E 44          CALL NC,pr_nl
447C  3A 08 4A          LD A,(PR_ROW)
447F  FE 16             CP 22
4481  38 0A             JR C,pc_row
4483  C5                PUSH BC
4484  CD 5B 45          CALL scr_up
4487  C1                POP BC
4488  3E 15             LD A,21
448A  32 08 4A          LD (PR_ROW),A
448D                pc_row:
448D  CD B0 42          CALL pr_addr
4490  70                LD (HL),B
4491  21 09 4A          LD HL,PR_COL
4494  34                INC (HL)
4495  18 03             JR pc_end
4497                pc_nl:
4497  CD 9E 44          CALL pr_nl
449A                pc_end:
449A  C1                POP BC
449B  D1                POP DE
449C  E1                POP HL
449D  C9                RET
449E                pr_nl:                  ; new line; the scroll happens when something is printed below line 21
449E  AF                XOR A
449F  32 09 4A          LD (PR_COL),A
44A2  3A 08 4A          LD A,(PR_ROW)
44A5  FE 16             CP 22
44A7  38 06             JR C,nl_inc
44A9  C5                PUSH BC
44AA  CD 5B 45          CALL scr_up
44AD  C1                POP BC
44AE  C9                RET
44AF                nl_inc:
44AF  3C                INC A
44B0  32 08 4A          LD (PR_ROW),A
44B3  C9                RET
44B4                rt_prnum:
44B4  CD F2 43          CALL rt_numstr
44B7  C3 BA 44          JP rt_prstr
44BA                rt_prstr:               ; print BC characters from HL
44BA  78                LD A,B
44BB  B1                OR C
44BC  C8                RET Z
44BD                ps_lp:
44BD  7E                LD A,(HL)
44BE  CD 66 44          CALL rt_prchr
44C1  23                INC HL
44C2  0B                DEC BC
44C3  78                LD A,B
44C4  B1                OR C
44C5  20 F6             JR NZ,ps_lp
44C7  C9                RET
44C8                rt_randomize:
44C8  2A 34 40          LD HL,(FRAMES)
44CB                rt_rand:                ; RAND n: n=0 uses the frame counter
44CB  7C                LD A,H
44CC  B5                OR L
44CD  20 08             JR NZ,rz_set
44CF  2A 34 40          LD HL,(FRAMES)
44D2  7C                LD A,H
44D3  B5                OR L
44D4  20 01             JR NZ,rz_set
44D6  2C                INC L
44D7                rz_set:
44D7  22 0A 4A          LD (SEED),HL
44DA  C9                RET
44DB                rt_rnd16:               ; xorshift16, HL = next random
44DB  2A 0A 4A          LD HL,(SEED)
44DE  7C                LD A,H
44DF  B5                OR L
44E0  20 01             JR NZ,r16_go
44E2  2C                INC L
44E3                r16_go:
44E3  7C                LD A,H
44E4  1F                RRA
44E5  7D                LD A,L
44E6  1F                RRA
44E7  AC                XOR H
44E8  67                LD H,A
44E9  7D                LD A,L
44EA  1F                RRA
44EB  7C                LD A,H
44EC  1F                RRA
44ED  AD                XOR L
44EE  6F                LD L,A
44EF  AC                XOR H
44F0  67                LD H,A
44F1  22 0A 4A          LD (SEED),HL
44F4  C9                RET
44F5                rt_rndn:                ; HL = random 0..n-1 (RND*n)
44F5  CB 7C             BIT 7,H
44F7  28 09             JR Z,rn_pos
44F9  CD A9 42          CALL neg_hl
44FC  CD 02 45          CALL rn_pos
44FF  C3 A9 42          JP neg_hl
4502                rn_pos:
4502  E5                PUSH HL
4503  CD DB 44          CALL rt_rnd16
4506  D1                POP DE
4507  C3 DC 43          JP rt_mulhi
450A                rt_scmp:                ; carry set if HL < DE (signed). Preserves HL, DE
450A  7C                LD A,H
450B  AA                XOR D
450C  FA 14 45          JP M,scmp_d
450F  E5                PUSH HL
4510  ED 52             SBC HL,DE
4512  E1                POP HL
4513  C9                RET
4514                scmp_d:
4514  7C                LD A,H
4515  17                RLA
4516  C9                RET
4517                rt_stop:
4517  ED 7B A7 42       LD SP,(ENTRYSP)
451B  01 00 00          LD BC,0
451E  C9                RET
451F                rt_strcmp:              ; compare DE,len C with HL,len A -> HL = -1, 0, 1
451F  47                LD B,A
4520                sc_lp:
4520  79                LD A,C
4521  B7                OR A
4522  28 10             JR Z,sc_e1
4524  78                LD A,B
4525  B7                OR A
4526  28 17             JR Z,sc_gt
4528  1A                LD A,(DE)
4529  BE                CP (HL)
452A  38 0F             JR C,sc_lt
452C  20 11             JR NZ,sc_gt
452E  13                INC DE
452F  23                INC HL
4530  0D                DEC C
4531  05                DEC B
4532  18 EC             JR sc_lp
4534                sc_e1:
4534  78                LD A,B
4535  B7                OR A
4536  20 03             JR NZ,sc_lt
4538  67                LD H,A
4539  6F                LD L,A
453A  C9                RET
453B                sc_lt:
453B  21 FF FF          LD HL,0FFFFh
453E  C9                RET
453F                sc_gt:
453F  21 01 00          LD HL,1
4542  C9                RET
4543                rt_strset:              ; string var at DE (len byte + data) = BC bytes at HL, max length A
4543  B9                CP C
4544  30 01             JR NC,ss_ok
4546  4F                LD C,A
4547                ss_ok:
4547  79                LD A,C
4548  12                LD (DE),A
4549  13                INC DE
454A  B7                OR A
454B  C8                RET Z
454C  ED B0             LDIR
454E  C9                RET
454F                rt_zero:                ; zero BC (>=1) bytes at HL
454F  36 00             LD (HL),0
4551  0B                DEC BC
4552  78                LD A,B
4553  B1                OR C
4554  C8                RET Z
4555  54                LD D,H
4556  5D                LD E,L
4557  13                INC DE
4558  ED B0             LDIR
455A  C9                RET
455B                scr_up:                 ; scroll rows 0-21 up one line
455B  2A 0C 40          LD HL,(D_FILE)
455E  23                INC HL
455F  54                LD D,H
4560  5D                LD E,L
4561  01 21 00          LD BC,33
4564  09                ADD HL,BC
4565  01 B5 02          LD BC,693
4568  ED B0             LDIR
456A  62                LD H,D
456B  6B                LD L,E
456C  06 20             LD B,32
456E                scu_clr:
456E  36 00             LD (HL),0
4570  23                INC HL
4571  10 FB             DJNZ scu_clr
4573  C9                RET
4574                LIT_0:
4574  00 00 2D 85 ..         DB 0,0,45,133,23,23,23,43,46,51,41,0,57,45,42,0,43,52,55,57,55,42,56,56,23,23,23,23,5,45
4592                LIT_1:
4592  80 80 80 80 ..         DB 128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128,128
45B2                LIT_2:
45B2  00                    DB 0
45B3                LIT_3:
45B3  24                    DB 36
45B4                LIT_4:
45B4  21                    DB 33
45B5                LIT_5:
45B5  08                    DB 8
45B6                LIT_6:
45B6  97                    DB 151
45B7                LIT_7:
45B7  38 28 34 37 ..         DB 56,40,52,55,42,0
45BD                LIT_8:
45BD  00 31 2E 3B ..         DB 0,49,46,59,42,56,0
45C4                LIT_9:
45C4  00 00                 DB 0,0
45C6                LIT_10:
45C6  AC A6 B2 AA ..         DB 172,166,178,170,128,180,187,170,183
45CF                LIT_11:
45CF  2B 2E 33 26 ..         DB 43,46,51,38,49,0,56,40,52,55,42,0
45DB                ; variables
45DB                PR_ROW  EQU BSS+0
45DB                PR_COL  EQU BSS+1
45DB                SEED    EQU BSS+2
45DB                SL_FROM EQU BSS+4
45DB                NX_BLK  EQU BSS+6
45DB                PCOUNT  EQU BSS+8
45DB                NSFLAG  EQU BSS+10
45DB                VALNEG  EQU BSS+11
45DB                INLEN   EQU BSS+12
45DB                CHARBUF EQU BSS+13
45DB                NUMBUF  EQU BSS+14
45DB                INBUF   EQU BSS+20
45DB                VARSTART EQU BSS+52
45DB                D_FILE  EQU 400Ch
45DB                FRAMES  EQU 4034h
45DB                V_X EQU BSS+52
45DB                V_SC EQU BSS+54
45DB                V_LIVES EQU BSS+56
45DB                V_FX EQU BSS+58
45DB                V_FY EQU BSS+60
45DB                A_R EQU BSS+62
45DB                S_K EQU BSS+126
45DB                T_0 EQU BSS+382
45DB                BSS_SIZE EQU 639
45DB                BSS_END EQU BSS+639

; ---- symbols ----
; BSS_SIZE = 027F
; D_FILE = 400C
; FRAMES = 4034
; start = 4082
; L_10 = 408C
; E_10 = 408C
; L_20 = 408C
; E_20 = 408F
; L_30 = 408F
; E_30 = 40A4
; L_40 = 40A4
; E_40 = 40B9
; L_50 = 40B9
; E_50 = 40BF
; L_60 = 40BF
; E_60 = 40C5
; L_70 = 40C5
; E_70 = 40CB
; L_80 = 40CB
; E_80 = 40D4
; L_90 = 40D4
; E_90 = 40DE
; L_100 = 40DE
; E_100 = 40E4
; L_110 = 40E4
; E_110 = 40E4
; L_120 = 40E4
; E_120 = 4101
; L_130 = 4101
; E_130 = 4117
; L_140 = 4117
; E_140 = 4179
; L_150 = 4179
; E_150 = 418F
; L_160 = 418F
; E_160 = 41A5
; L_170 = 41A5
; E_170 = 41AC
; L_180 = 41AC
; E_180 = 41BB
; L_190 = 41BB
; E_190 = 41D1
; L_200 = 41D1
; E_200 = 4204
; L_210 = 4204
; E_210 = 4212
; L_215 = 4212
; E_215 = 4218
; L_220 = 4218
; E_220 = 421E
; L_300 = 421E
; E_300 = 421E
; L_310 = 421E
; E_310 = 4235
; L_320 = 4235
; E_320 = 4249
; L_330 = 4249
; E_330 = 4260
; L_340 = 4260
; E_340 = 426A
; L_350 = 426A
; E_350 = 4270
; L_360 = 4270
; E_360 = 4271
; L_400 = 4271
; E_400 = 4286
; L_410 = 4286
; E_410 = 42A1
; L_420 = 42A1
; E_420 = 42A4
; ENTRYSP = 42A7
; neg_hl = 42A9
; pr_addr = 42B0
; rc_addr = 42B8
; rt_and = 42CF
; rt_or = 42D5
; rt_not = 42DC
; rt_appchr = 42E4
; rt_append = 42EB
; ap_ok = 4300
; rt_at = 4310
; rt_brk = 4324
; rt_cls = 432D
; cls_row = 4333
; cls_ch = 4335
; rt_frame = 4346
; fr_lp = 434D
; rt_init = 4358
; rt_inkey = 4367
; ik_none = 4377
; rt_keyany = 4379
; rt_lt = 437F
; bool_c = 4382
; rt_gt = 4388
; rt_ge = 438E
; bool_nc = 4391
; rt_le = 4397
; rt_eq = 439D
; rt_ne = 43A6
; rt_memcheck = 43AF
; mc_msg = 43CF
; rt_mulhi = 43DC
; mh_lp = 43E3
; mh_sh = 43EA
; rt_numstr = 43F2
; ns_pos = 4404
; ns_dig = 442C
; nd_lp = 442E
; nd_put = 443D
; rt_pause = 4447
; pa_lp = 444D
; rt_newline = 4464
; rt_prchr = 4466
; pc_ok = 4473
; pc_row = 448D
; pc_nl = 4497
; pc_end = 449A
; pr_nl = 449E
; nl_inc = 44AF
; rt_prnum = 44B4
; rt_prstr = 44BA
; ps_lp = 44BD
; rt_randomize = 44C8
; rt_rand = 44CB
; rz_set = 44D7
; rt_rnd16 = 44DB
; r16_go = 44E3
; rt_rndn = 44F5
; rn_pos = 4502
; rt_scmp = 450A
; scmp_d = 4514
; rt_stop = 4517
; rt_strcmp = 451F
; sc_lp = 4520
; sc_e1 = 4534
; sc_lt = 453B
; sc_gt = 453F
; rt_strset = 4543
; ss_ok = 4547
; rt_zero = 454F
; scr_up = 455B
; scu_clr = 456E
; LIT_0 = 4574
; LIT_1 = 4592
; LIT_2 = 45B2
; LIT_3 = 45B3
; LIT_4 = 45B4
; LIT_5 = 45B5
; LIT_6 = 45B6
; LIT_7 = 45B7
; LIT_8 = 45BD
; LIT_9 = 45C4
; LIT_10 = 45C6
; LIT_11 = 45CF
; BSS = 4A08
; PR_ROW = 4A08
; PR_COL = 4A09
; SEED = 4A0A
; SL_FROM = 4A0C
; NX_BLK = 4A0E
; PCOUNT = 4A10
; NSFLAG = 4A12
; VALNEG = 4A13
; INLEN = 4A14
; CHARBUF = 4A15
; NUMBUF = 4A16
; INBUF = 4A1C
; VARSTART = 4A3C
; V_X = 4A3C
; V_SC = 4A3E
; V_LIVES = 4A40
; V_FX = 4A42
; V_FY = 4A44
; A_R = 4A46
; S_K = 4A86
; T_0 = 4B86
; BSS_END = 4C87
