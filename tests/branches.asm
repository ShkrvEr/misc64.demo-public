LDI R1, 5
LDI R2, 5
JE R1, R2, checkJne
LDI R3, 99
checkJne:
LDI R4, 1
JNE R1, R4, checkJlt
LDI R3, 88
checkJlt:
LDI R5, -1
JLT R5, R4, checkJgt
LDI R3, 77
checkJgt:
LDI R6, 8
JGT R6, R1, checkJmp
LDI R3, 66
checkJmp:
JMP done
LDI R3, 55
done:
HALT