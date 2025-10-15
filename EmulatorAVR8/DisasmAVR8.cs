using System;
using System.Text;


namespace EmulatorAVR8
{
    /// <summary>
    /// Classe désassemblant le code-machine des processeurs
    /// de la famille AVR8.
    /// </summary>
    public class DisasmAVR8
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String ERR_UNREADABLE_ADDRESS =
                "Impossible de lire le contenu de l'adresse ${0:X5} !";
        private const String ERR_UNKNOWN_OPCODE =
                "Opcode invalide (${1:X4}) rencontré à l'adresse ${0:X5} !";


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private readonly IMemorySpaceAVR8 memSpace;

        // politique vis-à-vis des opcodes invalides
        private UnknownOpcodePolicy uoPolicy;

        // adresse courante de l'instruction en cours de désassemblage
        private int regPC;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique) de la classe DisasmAVR8.
        /// </summary>
        /// <param name="memorySpace">
        /// Espace-mémoire où lire le code binaire à desassembler.
        /// </param>
        public DisasmAVR8(IMemorySpaceAVR8 memorySpace)
        {
            this.memSpace = memorySpace;
            this.uoPolicy = UnknownOpcodePolicy.DoNop;
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        private byte HiByte(ushort word)
        {
            return (byte)((word >> 8) & 0x00ff);
        }

        private byte LoByte(ushort word)
        {
            return (byte)(word & 0x00ff);
        }

        private ushort MakeWord(byte hi, byte lo)
        {
            return (ushort)((hi << 8) | lo);
        }

        /* ~~~~ accès à l'espace mémoire ~~~~ */

        private ushort ReadProgMem(int addr)
        {
            ushort? memval = this.memSpace.ReadProgramMemory(addr);
            if (!(memval.HasValue)) {
                throw new AddressUnreadableException(
                        addr,
                        String.Format(ERR_UNREADABLE_ADDRESS,
                                      addr));
            }
            return memval.Value;
        }

        /* ~~~~ analyse des opcodes ~~~~ */

        /* retrouve un numéro "plein" (0 à 31) de registre de destination */
        private int GetFullDestRegNum(ushort opcode)
        {
            return ((opcode >> 4) & 0x001f);
        }

        /* retrouve un numéro "plein" (0 à 31) de registre source */
        private int GetFullSrcRegNum(ushort opcode)
        {
            int num = (opcode & 0x000f);
            num |= ((opcode >> 5) & 0x0010);
            return num;
        }

        /* retrouve un numéro "court" (16 à 31) de registre de destination */
        private int GetShortDestRegNum(ushort opcode)
        {
            return ((opcode >> 4) & 0x000f) + 16;
        }

        /* retrouve un numéro "court" (16 à 31) de registre source */
        private int GetShortSrcRegNum(ushort opcode)
        {
            return (opcode & 0x000f) + 16;
        }

        /* retrouve un numéro "mini" (16 à 23) de registre de destination */
        private int GetTinyDestRegNum(ushort opcode)
        {
            return ((opcode >> 4) & 0x0007) + 16;
        }

        /* retrouve un numéro "mini" (16 à 23) de registre source */
        private int GetTinySrcRegNum(ushort opcode)
        {
            return (opcode & 0x0007) + 16;
        }

        /* retrouve un numéro de paire de registres (0, 2, ..., 30)
           de destination */
        private int GetDestRegPairNum(ushort opcode)
        {
            return ((opcode >> 4) & 0x000f) * 2;
        }

        /* retrouve un numéro de paire de registres (0, 2, ..., 30)
           source */
        private int GetSrcRegPairNum(ushort opcode)
        {
            return (opcode & 0x000f) * 2;
        }

        /* retrouve un numéro de paire "restreint" (24, 26, 28 ou 30)
           pour les opérations arithmétiques sur 16 bits */
        private int GetTinyRegPairNum(ushort opcode)
        {
            return ((opcode >> 4) & 0x0003) * 2 + 24;
        }

        /* retrouve la constante 8 bits (0 à 255) pour les opérations
           à valeur immédiate */
        private byte Get8bitConst(ushort opcode)
        {
            int num = ((opcode >> 4) & 0x00f0);
            num |= (opcode & 0x000f);
            return (byte)num;
        }

        /* retrouve la constante 6 bits (0 à 63) pour les opérations
           arithmétiques sur 16 bits */
        private byte Get6bitConst(ushort opcode)
        {
            int num = (opcode & 0x000f);
            num |= ((opcode >> 2) & 0x0030);
            return (byte)num;
        }

        /* retrouve un déplacement sur 6 bits pour les opérations indexées */
        private byte Get6bitOffset(ushort opcode)
        {
            int num = opcode & 0x0007;
            num |= ((opcode >> 7) & 0x0018);
            num |= ((opcode >> 8) & 0x0020);
            return (byte)num;
        }

        /* retrouve l'adresse d'une donnée pour les chargements / écritures
           longs dans l'espace-mémoire de données (opcodes doubles !) */
        private ushort Get16bitDataAddress(ushort opcode)
        {
            ushort addr = ReadProgMem(this.regPC);
            this.regPC++;
            return addr;
        }

        /* retrouve une adresse E/S pour les opérations bit-à-bit */
        private byte GetShortIOAddress(ushort opcode)
        {
            return (byte)((opcode >> 3) & 0x1f);
        }

        /* retrouve une adresse pour les opérations d'entrée / sortie */
        private byte GetFullIOAddress(ushort opcode)
        {
            int num = opcode & 0x000f;
            num |= ((opcode >> 5) & 0x0030);
            return (byte)num;
        }

        /* retrouve l'adresse de destination pour les sauts absolus
           (opcodes doubles !) */
        private int Get22bitDestAddr(ushort opcode)
        {
            int dest = ReadProgMem(this.regPC);
            this.regPC++;
            dest |= ((opcode << 16) & 0x00010000);
            dest |= ((opcode << 13) & 0x003e0000);
            return dest;
        }

        /* retrouve un déplacement entre -2048 et +2047 pour les sauts
           relatifs */
        private short Get12bitRelativeDispl(ushort opcode)
        {
            short displ = (short)(opcode & 0x0fff);
            if (displ > 0x7ff) displ = (short)(displ | 0xf800);
            return displ;
        }

        /* retrouve un déplacement entre -128 et +127 pour
           les branchements conditionnels */
        private sbyte Get7bitRelativeDispl(ushort opcode)
        {
            sbyte displ = (sbyte)((opcode >> 3) & 0x7f);
            if (displ > 0x3f) displ = (sbyte)(displ | 0x80);
            return displ;
        }

        /* renvoie un numéro de bit pour les opérations bit-à-bit */
        private int GetBitNumber(int opcode)
        {
            return (opcode & 0x0007);
        }

        /* ~~~~ désassemblage des opcodes ~~~~ */

        private string DisasmOpcodes0(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            if (opcode == 0x0000) {
                /* NOP */
                return "NOP";
            }
            int rd, rr;
            int secDigit = ((opcode >> 8) & 0x0f);
            switch (secDigit) {
                case 0x1:
                    /* MOVW Rd':Rd, Rr':Rr */
                    mnemo = "MOVW";
                    rd = GetDestRegPairNum(opcode);
                    rr = GetSrcRegPairNum(opcode);
                    args = String.Format("R{0}:R{1}, R{2}:R{3}",
                                         rd + 1, rd, rr + 1, rr);
                    break;
                case 0x2:
                    /* MULS Rd, Rr */
                    mnemo = "MULS";
                    rd = GetShortDestRegNum(opcode);
                    rr = GetShortSrcRegNum(opcode);
                    args = String.Format("R{0}, R{1}", rd, rr);
                    break;
                case 0x3:
                    rd = GetTinyDestRegNum(opcode);
                    rr = GetTinySrcRegNum(opcode);
                    bool bit3 = ((opcode & 0x0007) != 0);
                    bool bit7 = ((opcode & 0x0070) != 0);
                    if (bit3 && bit7) {
                        /* FMULSU Rd, Rr */
                        mnemo = "FMULSU";
                    } else if (bit7) {
                        /* FMULS Rd, Rr */
                        mnemo = "FMULS";
                    } else if (bit3) {
                        /* FMUL Rd, Rr */
                        mnemo = "FMUL";
                    } else {
                        /* MULSU Rd, Rr */
                        mnemo = "MULSU";
                    }
                    args = String.Format("R{0}, R{1}", rd, rr);
                    break;
                case 0x4:
                case 0x5:
                case 0x6:
                case 0x7:
                    /* CPC Rd, Rr */
                    mnemo = "CPC";
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    args = String.Format("R{0}, R{1}", rd, rr);
                    break;
                case 0x8:
                case 0x9:
                case 0xa:
                case 0xb:
                    /* SBC Rd, Rr */
                    mnemo = "SBC";
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    args = String.Format("R{0}, R{1}", rd, rr);
                    break;
                case 0xc:
                case 0xd:
                case 0xe:
                case 0xf:
                    /* ADD Rd, Rr / LSL Rd */
                    mnemo = "ADD";
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    args = String.Format("R{0}, R{1}", rd, rr);
                    if (rd == rr) {
                        mnemo = "LSL";
                        args = String.Format("R{0}", rd);
                    }
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }

        private string DisasmOpcodes1(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            int rd = GetFullDestRegNum(opcode);
            int rr = GetFullSrcRegNum(opcode);
            args = String.Format("R{0}, R{1}", rd, rr);
            int bits1011 = ((opcode >> 10) & 0x03);
            switch (bits1011) {
                case 0:
                    /* CPSE Rd, Rr */
                    mnemo = "CPSE";
                    break;
                case 1:
                    /* CP Rd, Rr */
                    mnemo = "CP";
                    break;
                case 2:
                    /* SUB Rd, Rr */
                    mnemo = "SUB";
                    break;
                case 3:
                    /* ADC Rd, Rr / ROL Rd */
                    mnemo = "ADC";
                    if (rd == rr) {
                        mnemo = "ROL";
                        args = String.Format("R{0}", rd);
                    }
                    break;
            }

            return String.Format("{0} {1}", mnemo, args).Trim();
        }

        private string DisasmOpcodes2(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            int rd = GetFullDestRegNum(opcode);
            int rr = GetFullSrcRegNum(opcode);
            args = String.Format("R{0}, R{1}", rd, rr);
            int bits1011 = ((opcode >> 10) & 0x03);
            switch (bits1011) {
                case 0:
                    /* AND Rd, Rr / TST Rd */
                    mnemo = "AND";
                    if (rd == rr) {
                        mnemo = "TST";
                        args = String.Format("R{0}", rd);
                    }
                    break;
                case 1:
                    /* EOR Rd, Rr / CLR Rd */
                    mnemo = "EOR";
                    if (rd == rr) {
                        mnemo = "CLR";
                        args = String.Format("R{0}", rd);
                    }
                    break;
                case 2:
                    /* OR Rd, Rr */
                    mnemo = "OR";
                    break;
                case 3:
                    /* MOV Rd, Rr */
                    mnemo = "MOV";
                    break;
            }

            return String.Format("{0} {1}", mnemo, args).Trim();
        }

        private string DisasmOpcodes3(ushort opcode)
        {
            /* CPI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("CPI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodes4(ushort opcode)
        {
            /* SBCI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("SBCI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodes5(ushort opcode)
        {
            /* SUBI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("SUBI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodes6(ushort opcode)
        {
            /* ORI Rd, K  (alias SBR) */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("ORI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodes7(ushort opcode)
        {
            /* ANDI Rd, K  (alias CBR) */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("ANDI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodes8A(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            int rd = GetFullDestRegNum(opcode);
            byte q = Get6bitOffset(opcode);
            bool bit3 = ((opcode & 0x0008) != 0);
            bool bit9 = ((opcode & 0x0200) != 0);
            if (q == 0) {
                if (bit9) {
                    /* ST idx, Rd */
                    mnemo = "ST";
                    if (bit3) {
                        args = String.Format("Y, R{0}", rd);
                    } else {
                        args = String.Format("Z, R{0}", rd);
                    }
                } else {
                    /* LD Rd, idx */
                    mnemo = "LD";
                    if (bit3) {
                        args = String.Format("R{0}, Y", rd);
                    } else {
                        args = String.Format("R{0}, Z", rd);
                    }
                }
            } else {
                if (bit9) {
                    /* STD idx + q, Rd */
                    mnemo = "STD";
                    if (bit3) {
                        args = String.Format("Y + {0}, R{1}", q, rd);
                    } else {
                        args = String.Format("Z + {0}, R{1}", q, rd);
                    }
                } else {
                    /* LDD Rd, idx + q */
                    mnemo = "LDD";
                    if (bit3) {
                        args = String.Format("R{0}, Y + {1}", rd, q);
                    } else {
                        args = String.Format("R{0}, Z + {1}", rd, q);
                    }
                }
            }

            return String.Format("{0} {1}", mnemo, args).Trim();
        }

        private string DisasmOpcodes9(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            int A, b, rd, rr;
            byte K;
            int secDigit = ((opcode >> 8) & 0x0f);
            int trdDigit = ((opcode >> 4) & 0x0f);
            int lstDigit = (opcode & 0x0f);
            switch (secDigit) {
                case 0x0:
                case 0x1:
                    rd = GetFullDestRegNum(opcode);
                    switch (lstDigit) {
                        case 0x0:
                            /* LDS Rd, k (double opcode !) */
                            mnemo = "LDS";
                            ushort addr = Get16bitDataAddress(opcode);
                            args = String.Format("R{0}, ${1:X4}", rd, addr);
                            break;
                        case 0x1:
                            /* LD Rd, Z+ */
                            mnemo = "LD";
                            args = String.Format("R{0}, Z+", rd);
                            break;
                        case 0x2:
                            /* LD Rd, -Z */
                            mnemo = "LD";
                            args = String.Format("R{0}, -Z", rd);
                            break;
                        case 0x4:
                            /* LPM Rd, Z */
                            mnemo = "LPM";
                            args = String.Format("R{0}, Z", rd);
                            break;
                        case 0x5:
                            /* LPM Rd, Z+ */
                            mnemo = "LPM";
                            args = String.Format("R{0}, Z+", rd);
                            break;
                        case 0x6:
                            /* ELPM Rd, Z */
                            mnemo = "ELPM";
                            args = String.Format("R{0}, Z", rd);
                            break;
                        case 0x7:
                            /* ELPM Rd, Z+ */
                            mnemo = "ELPM";
                            args = String.Format("R{0}, Z+", rd);
                            break;
                        case 0x9:
                            /* LD Rd, Y+ */
                            mnemo = "LD";
                            args = String.Format("R{0}, Y+", rd);
                            break;
                        case 0xa:
                            /* LD Rd, -Y */
                            mnemo = "LD";
                            args = String.Format("R{0}, -Y", rd);
                            break;
                        case 0xc:
                            /* LD Rd, X */
                            mnemo = "LD";
                            args = String.Format("R{0}, X", rd);
                            break;
                        case 0xd:
                            /* LD Rd, X+ */
                            mnemo = "LD";
                            args = String.Format("R{0}, X+", rd);
                            break;
                        case 0xe:
                            /* LD Rd, -X */
                            mnemo = "LD";
                            args = String.Format("R{0}, -X", rd);
                            break;
                        case 0xf:
                            /* POP Rd */
                            mnemo = "POP";
                            args = String.Format("R{0}", rd);
                            break;
                    }
                    break;
                case 0x2:
                case 0x3:
                    rr = GetFullDestRegNum(opcode);
                    switch (lstDigit) {
                        case 0x0:
                            /* STS k, Rr (double opcode !) */
                            mnemo = "STS";
                            ushort addr = Get16bitDataAddress(opcode);
                            args = String.Format("${0:X4}, R{1}", addr, rr);
                            break;
                        case 0x1:
                            /* ST Z+, Rr */
                            mnemo = "ST";
                            args = String.Format("Z+, R{0}", rr);
                            break;
                        case 0x2:
                            /* ST -Z, Rr */
                            mnemo = "ST";
                            args = String.Format("-Z, R{0}", rr);
                            break;
                        case 0x4:
                            /* XCH Z, Rr */
                            mnemo = "XCH";
                            args = String.Format("Z, R{0}", rr);
                            break;
                        case 0x5:
                            /* LAS Z, Rr */
                            mnemo = "LAS";
                            args = String.Format("Z, R{0}", rr);
                            break;
                        case 0x6:
                            /* LAC Z, Rr */
                            mnemo = "LAC";
                            args = String.Format("Z, R{0}", rr);
                            break;
                        case 0x7:
                            /* LAT Z, Rr */
                            mnemo = "LAT";
                            args = String.Format("Z, R{0}", rr);
                            break;
                        case 0x9:
                            /* ST Y+, Rr */
                            mnemo = "ST";
                            args = String.Format("Y+, R{0}", rr);
                            break;
                        case 0xa:
                            /* ST -Y, Rr */
                            mnemo = "ST";
                            args = String.Format("-Y, R{0}", rr);
                            break;
                        case 0xc:
                            /* ST X, Rr */
                            mnemo = "ST";
                            args = String.Format("X, R{0}", rr);
                            break;
                        case 0xd:
                            /* ST X+; Rr */
                            mnemo = "ST";
                            args = String.Format("X+, R{0}", rr);
                            break;
                        case 0xe:
                            /* ST -X, Rr */
                            mnemo = "ST";
                            args = String.Format("-X, R{0}", rr);
                            break;
                        case 0xf:
                            /* PUSH Rr */
                            mnemo = "PUSH";
                            args = String.Format("R{0}", rr);
                            break;
                    }
                    break;
                case 0x4:
                case 0x5:
                    /* spécificités de 0x94nn */
                    if (secDigit == 4) {
                        switch (lstDigit) {
                            case 0x8:
                                /* BSET s / BCLR s */
                                switch (trdDigit) {
                                    case 0x0:
                                        /* SEC */
                                        return "SEC";
                                    case 0x1:
                                        /* SEZ */
                                        return "SEZ";
                                    case 0x2:
                                        /* SEN */
                                        return "SEN";
                                    case 0x3:
                                        /* SEV */
                                        return "SEV";
                                    case 0x4:
                                        /* SES */
                                        return "SES";
                                    case 0x5:
                                        /* SEH */
                                        return "SEH";
                                    case 0x6:
                                        /* SET */
                                        return "SET";
                                    case 0x7:
                                        /* SEI */
                                        return "SEI";
                                    case 0x8:
                                        /* CLC */
                                        return "CLC";
                                    case 0x9:
                                        /* CLZ */
                                        return "CLZ";
                                    case 0xa:
                                        /* CLN */
                                        return "CLN";
                                    case 0xb:
                                        /* CLV */
                                        return "CLV";
                                    case 0xc:
                                        /* CLS */
                                        return "CLS";
                                    case 0xd:
                                        /* CLH */
                                        return "CLH";
                                    case 0xe:
                                        /* CLT */
                                        return "CLT";
                                    case 0xf:
                                        /* CLI */
                                        return "CLI";
                                }
                                break;
                            case 0x9:
                                switch (trdDigit) {
                                    case 0x0:
                                        /* IJMP */
                                        return "IJMP";
                                    case 1:
                                        /* EIJMP */
                                        return "EIJMP";
                                }
                                break;
                            case 0xb:
                                /* DES K */
                                K = (byte)trdDigit;
                                return String.Format("DES #{0}", K);
                        }
                    }
                    /* spécificités de 0x95nn */
                    if (secDigit == 5) {
                        switch (trdDigit) {
                            case 0x0:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* RET */
                                        return "RET";
                                    case 0x9:
                                        /* ICALL */
                                        return "ICALL";
                                }
                                break;
                            case 0x1:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* RETI */
                                        return "RETI";
                                    case 0x9:
                                        /* EICALL */
                                        return "EICALL";
                                }
                                break;
                            case 0x8:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SLEEP */
                                        return "SLEEP";
                                }
                                break;
                            case 0x9:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* BREAK */
                                        return "BREAK";
                                }
                                break;
                            case 0xa:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* WDR */
                                        return "WDR";
                                }
                                break;
                            case 0xc:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* LPM */
                                        return "LPM";
                                }
                                break;
                            case 0xd:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* ELPM */
                                        return "ELPM";
                                }
                                break;
                            case 0xe:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SPM */
                                        return "SPM";
                                }
                                break;
                            case 0xf:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SPM Z+ */
                                        return "SPM Z+";
                                }
                                break;
                        }
                    }
                    /* opcodes "communs" */
                    int dest;
                    rd = GetFullDestRegNum(opcode);
                    switch (lstDigit) {
                        case 0x0:
                            /* COM Rd */
                            mnemo = "COM";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x1:
                            /* NEG Rd */
                            mnemo = "NEG";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x2:
                            /* SWAP Rd */
                            mnemo = "SWAP";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x3:
                            /* INC Rd */
                            mnemo = "INC";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x5:
                            /* ASR Rd */
                            mnemo = "ASR";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x6:
                            /* LSR Rd */
                            mnemo = "LSR";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0x7:
                            /* ROR Rd */
                            mnemo = "ROR";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0xa:
                            /* DEC Rd */
                            mnemo = "DEC";
                            args = String.Format("R{0}", rd);
                            break;
                        case 0xc:
                        case 0xd:
                            /* JMP k (double opcode !) */
                            mnemo = "JMP";
                            dest = Get22bitDestAddr(opcode);
                            args = String.Format("->${0:X4}", dest);
                            break;
                        case 0xe:
                        case 0xf:
                            /* CALL k (double opcode !) */
                            mnemo = "CALL";
                            dest = Get22bitDestAddr(opcode);
                            args = String.Format("->${0:X4}", dest);
                            break;
                    }
                    break;
                case 0x6:
                    /* ADIW Rd':Rd, K */
                    mnemo = "ADIW";
                    rd = GetTinyRegPairNum(opcode);
                    K = Get6bitConst(opcode);
                    args = String.Format("R{0}:R{1}, #{2}",
                                         rd + 1, rd, K);
                    break;
                case 0x7:
                    /* SBIW Rd':Rd, K */
                    mnemo = "SBIW";
                    rd = GetTinyRegPairNum(opcode);
                    K = Get6bitConst(opcode);
                    args = String.Format("R{0}:R{1}, #{2}",
                                         rd + 1, rd, K);
                    break;
                case 0x8:
                    /* CBI A, b */
                    mnemo = "CBI";
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    args = String.Format("${0:X2}, {1}", A, b);
                    break;
                case 0x9:
                    /* SBIC A, b */
                    mnemo = "SBIC";
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    args = String.Format("${0:X2}, {1}", A, b);
                    break;
                case 0xa:
                    /* SBI A, b */
                    mnemo = "SBI";
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    args = String.Format("${0:X2}, {1}", A, b);
                    break;
                case 0xb:
                    /* SBIS A, b */
                    mnemo = "SBIS";
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    args = String.Format("${0:X2}, {1}", A, b);
                    break;
                case 0xc:
                case 0xd:
                case 0xe:
                case 0xf:
                    /* MUL Rd, Rr */
                    mnemo = "MUL";
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    args = String.Format("R{0}, R{1}", rd, rr);
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }

        private string DisasmOpcodesB(ushort opcode)
        {
            int rd = GetFullDestRegNum(opcode);
            byte A = GetFullIOAddress(opcode);
            bool bit11 = ((opcode & 0x0800) != 0);
            if (bit11) {
                /* OUT A, Rd */
                return String.Format("OUT ${0:X2}, R{1}", A, rd);
            } else {
                /* IN Rd, A */
                return String.Format("IN R{0}, ${1:X2}", rd, A);
            }
        }

        private string DisasmOpcodesC(ushort opcode)
        {
            /* RJMP k */
            short k = Get12bitRelativeDispl(opcode);
            return String.Format("RJMP {0:+0000;-0000} \t (->${1:X4})",
                                 k, this.regPC + k);
        }

        private string DisasmOpcodesD(ushort opcode)
        {
            /* RCALL k */
            short k = Get12bitRelativeDispl(opcode);
            return String.Format("RCALL {0:+0000;-0000} \t (->${1:X4})",
                                 k, this.regPC + k);
        }

        private string DisasmOpcodesE(ushort opcode)
        {
            /* LDI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            return String.Format("LDI R{0}, #${1:X2}", rd, K);
        }

        private string DisasmOpcodesF(ushort opcode)
        {
            string mnemo = null, args = String.Empty;

            int rd;
            sbyte k = Get7bitRelativeDispl(opcode);
            int bNum = GetBitNumber(opcode);
            bool bit3 = ((opcode & 0x0008) != 0);
            bool bit9 = ((opcode & 0x0200) != 0);
            int bits1011 = ((opcode >> 10) & 0x03);
            switch (bits1011) {
                case 0:
                    /* BRBS s, k */
                    switch (bNum) {
                        case 0:
                            /* BRCS k / BRLO k */
                            mnemo = "BRCS";
                            break;
                        case 1:
                            /* BREQ k */
                            mnemo = "BREQ";
                            break;
                        case 2:
                            /* BRMI k */
                            mnemo = "BRMI";
                            break;
                        case 3:
                            /* BRVS k */
                            mnemo = "BRVS";
                            break;
                        case 4:
                            /* BRLT k */
                            mnemo = "BRLT";
                            break;
                        case 5:
                            /* BRHS k */
                            mnemo = "BRHS";
                            break;
                        case 6:
                            /* BRTS k */
                            mnemo = "BRTS";
                            break;
                        case 7:
                            /* BRIE k */
                            mnemo = "BRIE";
                            break;
                    }
                    args = String.Format("{0:+0;-0} \t (->${1:X4})",
                                         k, this.regPC + k);
                    break;
                case 1:
                    /* BRBC s, k */
                    switch (bNum) {
                        case 0:
                            /* BRCC k / BRSH k */
                            mnemo = "BRCC";
                            break;
                        case 1:
                            /* BRNE k */
                            mnemo = "BRNE";
                            break;
                        case 2:
                            /* BRPL k */
                            mnemo = "BRPL";
                            break;
                        case 3:
                            /* BRVC k */
                            mnemo = "BRVC";
                            break;
                        case 4:
                            /* BRGE k */
                            mnemo = "BRGE";
                            break;
                        case 5:
                            /* BRHC k */
                            mnemo = "BRHC";
                            break;
                        case 6:
                            /* BRTC k */
                            mnemo = "BRTC";
                            break;
                        case 7:
                            /* BRID k */
                            mnemo = "BRID";
                            break;
                    }
                    args = String.Format("{0:+0;-0} \t (->${1:X4})",
                                         k, this.regPC + k);
                    break;
                case 2:
                    if (!bit3) {
                        rd = GetFullDestRegNum(opcode);
                        if (bit9) {
                            /* BST Rd, b */
                            mnemo = "BST";
                        } else {
                            /* BLD Rd, b */
                            mnemo = "BLD";
                        }
                        args = String.Format("R{0}, {1}", rd, bNum);
                    }
                    break;
                case 3:
                    if (!bit3) {
                        rd = GetFullDestRegNum(opcode);
                        if (bit9) {
                            /* SBRS Rd, b */
                            mnemo = "SBRS";
                        } else {
                            /* SBRC Rd, b */
                            mnemo = "SBRC";
                        }
                        args = String.Format("R{0}, {1}", rd, bNum);
                    }
                    break;
            }

            if (mnemo != null) {
                return String.Format("{0} {1}", mnemo, args).Trim();
            }
            return null;
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /// <summary>
        /// Désassemble une instruction en mémoire.
        /// </summary>
        /// <param name="memoryAddress">
        /// Adresse où débute l'instruction à désassembler.
        /// </param>
        /// <returns></returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleInstructionAt(int memoryAddress)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = memoryAddress;

            /* écrit d'abord l'adresse traitée */
            sbResult.Append(String.Format("{0:X5} : ", this.regPC));

            /* analyse l'opcode trouvé à cette adresse */
            ushort opcode = ReadProgMem(this.regPC);
            this.regPC++;
            string instr = null;
            int hiDigit = ((opcode >> 12) & 0x0f);
            switch (hiDigit) {
                case 0x0:
                    instr = DisasmOpcodes0(opcode);
                    break;
                case 0x1:
                    instr = DisasmOpcodes1(opcode);
                    break;
                case 0x2:
                    instr = DisasmOpcodes2(opcode);
                    break;
                case 0x3:
                    instr = DisasmOpcodes3(opcode);
                    break;
                case 0x4:
                    instr = DisasmOpcodes4(opcode);
                    break;
                case 0x5:
                    instr = DisasmOpcodes5(opcode);
                    break;
                case 0x6:
                    instr = DisasmOpcodes6(opcode);
                    break;
                case 0x7:
                    instr = DisasmOpcodes7(opcode);
                    break;
                case 0x8:
                case 0xa:
                    instr = DisasmOpcodes8A(opcode);
                    break;
                case 0x9:
                    instr = DisasmOpcodes9(opcode);
                    break;
                case 0xb:
                    instr = DisasmOpcodesB(opcode);
                    break;
                case 0xc:
                    instr = DisasmOpcodesC(opcode);
                    break;
                case 0xd:
                    instr = DisasmOpcodesD(opcode);
                    break;
                case 0xe:
                    instr = DisasmOpcodesE(opcode);
                    break;
                case 0xf:
                    instr = DisasmOpcodesF(opcode);
                    break;
            }
            /* opcode invalide ! */
            if (instr == null) {
                switch (this.uoPolicy) {
                    case UnknownOpcodePolicy.ThrowException:
                        throw new UnknownOpcodeException(
                                this.regPC,
                                opcode,
                                String.Format(ERR_UNKNOWN_OPCODE,
                                              this.regPC, opcode));
                    case UnknownOpcodePolicy.DoNop:
                    default:
                        instr = "*** ?!?";
                        break;
                }
            }

            /* liste les mots ainsi traités */
            int nbWords = this.regPC - memoryAddress;
            for (int n = 0; n < nbWords; n++) {
                int ad = memoryAddress + n;
                ushort w = ReadProgMem(ad);
                sbResult.Append(String.Format("{0:X4} ", w));
            }
            /* aligne le résultat sur la colonne voulue */
            while (sbResult.Length < 18) sbResult.Append(" ");
            sbResult.Append(": ");

            /* enfin, liste l'instruction désassemblée */
            sbResult.Append(instr);

            /* terminé */
            sbResult.Append(" \r\n");
            return sbResult.ToString();
        }

        /// <summary>
        /// Désassemble un nombre donné d'instructions en mémoire.
        /// </summary>
        /// <param name="fromAddress">
        /// Adresse mémoire de la première instruction à désassembler.
        /// </param>
        /// <param name="nbInstr">
        /// Nombre d'instructions consécutives à desassembler.
        /// </param>
        /// <returns>
        /// Chaîne de caractère contenant le désassemblage des instructions
        /// rencontrées à partir de <code>fromAddress</code>.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleManyInstructionsAt(int fromAddress,
                                                    int nbInstr)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = fromAddress;
            for (uint n = 0; n < nbInstr; n++) {
                string instr = DisassembleInstructionAt(this.regPC);
                sbResult.Append(instr);
            }
            return sbResult.ToString();
        }

        /// <summary>
        /// Désassemble le contenu d'une plage d'adresses en mémoire.
        /// </summary>
        /// <param name="fromAddress">
        /// Adresse mémoire de la première instruction à désassembler.
        /// </param>
        /// <param name="toAddress">
        /// Dernière adresse mémoire à desassembler.
        /// </param>
        /// <returns>
        /// Chaîne de caractère contenant le désassemblage des adresses
        /// de la plage mémoire indiquée.
        /// <br/>
        /// Notez que le désassemblage peut aller légèrement au-delà de
        /// <code>toAddress</code> si une instruction s'étend sur cette
        /// adresse de fin.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si l'une des adresses-mémoire à traiter est impossible à lire.
        /// </exception>
        public String DisassembleMemory(int fromAddress,
                                        int toAddress)
        {
            StringBuilder sbResult = new StringBuilder();
            this.regPC = fromAddress;
            while (this.regPC <= toAddress) {
                string instr = DisassembleInstructionAt(this.regPC);
                sbResult.Append(instr);
            }
            return sbResult.ToString();
        }


        /// <summary>
        /// Indique si un opcode AVR8 donné est un opcode "long"
        /// (c.-à-d. qui requiert un mot supplémentaire, s'étendant
        /// ainsi sur 32 bits), ou pas.
        /// </summary>
        /// <param name="opcode">
        /// Opcode AVR8 à tester.
        /// </param>
        /// <returns>
        /// <code>true</code> si <code>opcode</code> est long
        /// (requiert un mot supplémentaire, et donc s'étend sur
        /// 32 bits) ;
        /// <code>false</code> si c'est un <code>opcode</code>
        /// standard contenu sur 16 bits.
        /// </returns>
        public static Boolean IsLongOpcode(ushort opcode)
        {
            if ((opcode & 0xfc0f) == 0x9000)
                /* LDS / STS */
                return true;
            if ((opcode & 0xfe0c) == 0x940c)
                /* JMP / CALL */
                return true;
            /* sinon : opcode standard */
            return false;
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Objet espace-mémoire attaché à ce processeur lors de sa création.
        /// (Propriété en lecture seule.)
        /// </summary>
        public IMemorySpaceAVR8 MemorySpace
        {
            get { return this.memSpace; }
        }

        /// <summary>
        /// Politique de prise en charge des opcodes invalides
        /// au désassemblage.
        /// </summary>
        public UnknownOpcodePolicy InvalidOpcodePolicy
        {
            get { return this.uoPolicy; }
            set { this.uoPolicy = value; }
        }

    }
}

