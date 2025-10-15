using System;
using System.IO;


namespace EmulatorAVR8
{
    /// <summary>
    /// Classe émulant un processeur de la famille AVR8.
    /// </summary>
    public class CPUAVR8
    {
        /* =========================== CONSTANTES =========================== */

        // messages affichés
        private const String ERR_INVALID_GEN_REG_NUM =
                "Mauvais numéro de registre général ({0}) !";
        private const String ERR_UNREADABLE_ROM_ADDRESS =
                "Impossible de lire le contenu de l'adresse-programme ${0:X5} !";
        private const String ERR_UNREADABLE_RAM_ADDRESS =
                "Impossible de lire le contenu de l'adresse-donnée ${0:X4} !";
        private const String ERR_UNWRITABLE_RAM_ADDRESS =
                "Impossible d'écrire la valeur $1:X2 à l'adresse-donnée ${0:X4} !";
        private const String ERR_UNKNOWN_OPCODE =
                "Opcode invalide (${1:X4}) rencontré à l'adresse-programme ${0:X5} !";
        private const String ERR_EXT_PTR_INSTR_WITH_16BIT_PC =
                "Instruction à pointeur étendu avec un processeur à PC 16 bits !";
        private const String ERR_SPM_NOT_SUPPORTED =
                "L'instruction SPM n'est pas émulée !" +
                " Veuillez modifier l'espace-programme par ailleurs.";
        private const String ERR_DES_NOT_SUPPORTED =
                "L'instruction DES n'est pas prise en charge !";

        // valeur binaire des "flags" dans le registre d'état S
        const byte FLAG_C = 0x01;
        const byte FLAG_Z = 0x02;
        const byte FLAG_N = 0x04;
        const byte FLAG_V = 0x08;
        const byte FLAG_S = 0x10;
        const byte FLAG_H = 0x20;
        const byte FLAG_T = 0x40;
        const byte FLAG_I = 0x80;

        // adresses particulières
        const ushort REG_BASE_ADDRESS = 0x0000;
        const ushort STD_IO_BASE_ADDRESS = 0x0020;
        const ushort EXT_IO_BASE_ADDRESS = 0x0060;

        // masques de sélection de bit
        const byte BYTE_MSB_MASK = 0x80;
        const byte BYTE_BCD_MASK = 0x08;
        const byte BYTE_LSB_MASK = 0x01;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire attaché au processeur
        // (défini une fois pour toutes à la construction)
        private readonly IMemorySpaceAVR8 memSpace;

        // compteur ordinal de plus de 16 bits (sur 3 octets)
        private readonly bool largePC;

        // registres généraux du processeur
        private volatile byte reg0;
        private volatile byte reg1;
        private volatile byte reg2;
        private volatile byte reg3;
        private volatile byte reg4;
        private volatile byte reg5;
        private volatile byte reg6;
        private volatile byte reg7;
        private volatile byte reg8;
        private volatile byte reg9;
        private volatile byte reg10;
        private volatile byte reg11;
        private volatile byte reg12;
        private volatile byte reg13;
        private volatile byte reg14;
        private volatile byte reg15;
        private volatile byte reg16;
        private volatile byte reg17;
        private volatile byte reg18;
        private volatile byte reg19;
        private volatile byte reg20;
        private volatile byte reg21;
        private volatile byte reg22;
        private volatile byte reg23;
        private volatile byte reg24;
        private volatile byte reg25;
        private volatile byte reg26;
        private volatile byte reg27;
        private volatile byte reg28;
        private volatile byte reg29;
        private volatile byte reg30;
        private volatile byte reg31;
        // compteur ordinal (pointe sur la mémoire ROM)
        private volatile int regPC;
        // pointeur de pile (pointe sur la mémoire RAM)
        private volatile ushort regSP;
        // "flags" composant le "registre P" (état du processeur)
        private volatile bool flagC;
        private volatile bool flagZ;
        private volatile bool flagN;
        private volatile bool flagV;
        private volatile bool flagS;
        private volatile bool flagH;
        private volatile bool flagT;
        private volatile bool flagI;
        // registre "d'extension" du compteur ordinal (pour les sauts indirects)
        private volatile byte regEIND;
        // registres "d'extension" des pointeurs X, Y et Z
        private volatile byte regRAMPX;
        private volatile byte regRAMPY;
        private volatile byte regRAMPZ;

        // statut "en sommeil" du processeur
        private volatile bool asleep;

        // comptage des cycles écoulés
        private ulong cycles;

        // politique vis-à-vis des opcodes invalides
        private UnknownOpcodePolicy uoPolicy;

        // objet d'écriture dans le fichier de traçage
        private StreamWriter traceFile;
        // désassembleur pour le traçage
        private DisasmAVR8 traceDisasm;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Contructeur de référence (et unique) de la classe CPUAVR8.
        /// </summary>
        /// <param name="memorySpace">
        /// Espace-mémoire à attacher à ce nouveau processeur.
        /// </param>
        /// <param name="largePCreg">
        /// Passer <code>true</code> pour un registre PC de plus de 16 bits
        /// (espace-mémoire programme de plus de 128 Ko) ;
        /// passer <code>false</code> pour un registre PC de 16 bits ou moins
        /// (espace-mémoire de 128 Ko maximum).
        /// </param>
        public CPUAVR8(IMemorySpaceAVR8 memorySpace,
                       bool largePCreg)
        {
            // champs immuables
            this.memSpace = memorySpace;
            this.largePC = largePCreg;
            this.GeneralRegister = new IndexedProperty<int, byte>(
                    GetRegisterValue,
                    SetRegisterValue);
            // valeurs mutables
            this.cycles = 0L;
            this.uoPolicy = UnknownOpcodePolicy.ThrowException;
            this.traceFile = null;
            this.traceDisasm = null;
            Reset();
        }


        /* ======================== MÉTHODES PRIVÉES ======================== */

        /* ~~~~ méthodes utilitaires ~~~~ */

        private static bool IsLongOpcode(ushort opcode)
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
            this.cycles++;
            if (!(memval.HasValue)) {
                throw new AddressUnreadableException(
                        addr,
                        String.Format(ERR_UNREADABLE_ROM_ADDRESS,
                                      addr));
            }
            return memval.Value;
        }

        private byte ReadDataMem(ushort addr)
        {
            byte? memVal = this.memSpace.ReadDataMemory(addr);
            this.cycles++;
            if (!(memVal.HasValue)) {
                throw new AddressUnreadableException(
                        addr,
                        String.Format(ERR_UNREADABLE_RAM_ADDRESS,
                                      addr));
            }
            return memVal.Value;
        }

        private void WriteDataMem(ushort addr, byte val)
        {
            bool ok = this.memSpace.WriteDataMemory(addr, val);
            this.cycles++;
            if (!ok) {
                throw new AddressUnwritableException(
                        addr,
                        String.Format(ERR_UNWRITABLE_RAM_ADDRESS,
                                      addr));
            }
        }

        /* ~~~~ gestion des "flags" ~~~~ */

        private void SetS()
        {
            this.flagS = this.flagN ^ this.flagV;
        }

        private void SetNZS(byte res)
        {
            this.flagZ = (res == 0);
            this.flagN = ((res & BYTE_MSB_MASK) != 0);
            SetS();
        }

        private void SetVSadd(byte res, byte rd, byte rr)
        {
            this.flagV = ( ((rd & BYTE_MSB_MASK) != 0) &&
                           ((rr & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) == 0) ) 
                      || ( ((rd & BYTE_MSB_MASK) == 0) &&
                           ((rr & BYTE_MSB_MASK) == 0) &&
                           ((res & BYTE_MSB_MASK) != 0) );
            SetS();
        }

        private void SetVSsub(byte res, byte rd, byte rr)
        {
            this.flagV = ( ((rd & BYTE_MSB_MASK) != 0) &&
                           ((rr & BYTE_MSB_MASK) == 0) &&
                           ((res & BYTE_MSB_MASK) == 0) )
                      || ( ((rd & BYTE_MSB_MASK) == 0) &&
                           ((rr & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) != 0) );
            SetS();
        }

        private void SetHadd(byte res, byte rd, byte rr)
        {
            this.flagH = ( ((rd & BYTE_BCD_MASK) != 0) &&
                           ((rr & BYTE_BCD_MASK) != 0) )
                      || ( ((rr & BYTE_BCD_MASK) != 0) &&
                           ((res & BYTE_BCD_MASK) == 0) )
                      || ( ((res & BYTE_BCD_MASK) == 0) &&
                           ((rd & BYTE_BCD_MASK) != 0) );
        }

        private void SetHsub(byte res, byte rd, byte rr)
        {
            this.flagH = ( ((rd & BYTE_BCD_MASK) == 0) &&
                           ((rr & BYTE_BCD_MASK) != 0) )
                      || ( ((rr & BYTE_BCD_MASK) != 0) &&
                           ((res & BYTE_BCD_MASK) != 0) )
                      || ( ((res & BYTE_BCD_MASK) != 0) &&
                           ((rd & BYTE_BCD_MASK) == 0) );
        }

        private void SetCadd(byte res, byte rd, byte rr)
        {
            this.flagC = ( ((rd & BYTE_MSB_MASK) != 0) &&
                           ((rr & BYTE_MSB_MASK) != 0) )
                      || ( ((rr & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) == 0) )
                      || ( ((res & BYTE_MSB_MASK) == 0) &&
                           ((rd & BYTE_MSB_MASK) != 0) );
        }

        private void SetCsub(byte res, byte rd, byte rr)
        {
            this.flagC = ( ((rd & BYTE_MSB_MASK) == 0) &&
                           ((rr & BYTE_MSB_MASK) != 0) )
                      || ( ((rr & BYTE_MSB_MASK) != 0) &&
                           ((res & BYTE_MSB_MASK) != 0) )
                      || ( ((res & BYTE_MSB_MASK) != 0) &&
                           ((rd & BYTE_MSB_MASK) == 0) );
        }

        /* ~~~~ gestion des "registres"-pointeurs */

        private void DecX()
        {
            ushort val = this.RegisterX;
            --val;
            this.RegisterX = val;
        }

        private void IncX()
        {
            ushort val = this.RegisterX;
            val++;
            this.RegisterX = val;
        }

        private void DecY()
        {
            ushort val = this.RegisterY;
            --val;
            this.RegisterY = val;
        }

        private void IncY()
        {
            ushort val = this.RegisterY;
            val++;
            this.RegisterY = val;
        }

        private void DecZ()
        {
            ushort val = this.RegisterZ;
            --val;
            this.RegisterZ = val;
        }

        private void IncZ()
        {
            ushort val = this.RegisterZ;
            val++;
            this.RegisterZ = val;
        }
       

        /* ~~~~ implantation des instructions ~~~~ */

        private void InstrADC(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            int sum = rdVal + rrVal;
            if (this.flagC) sum++;
            byte res = (byte)sum;
            SetNZS(res);
            SetVSadd(res, rdVal, rrVal);
            SetHadd(res, rdVal, rrVal);
            SetCadd(res, rdVal, rrVal);
            SetRegisterValue(rd, res);
        }

        private void InstrADD(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            int sum = rdVal + rrVal;
            byte res = (byte)sum;
            SetNZS(res);
            SetVSadd(res, rdVal, rrVal);
            SetHadd(res, rdVal, rrVal);
            SetCadd(res, rdVal, rrVal);
            SetRegisterValue(rd, res);
        }

        private void InstrADIW(int rd, byte K)
        {
            byte rdLo = GetRegisterValue(rd);
            byte rdHi = GetRegisterValue(rd + 1);
            int sum = MakeWord(rdHi, rdLo) + K;
            ushort res = (ushort)sum;
            this.flagN = ((res & 0x8000) != 0);
            this.flagV = this.flagN && ((rdHi & BYTE_MSB_MASK) == 0);
            SetS();
            this.flagZ = (res == 0x0000);
            this.flagC = !(this.flagN) && ((rdHi & BYTE_MSB_MASK) != 0);
            this.cycles++;   // instruction prenant 2 cycles
            SetRegisterValue(rd, LoByte(res));
            SetRegisterValue(rd + 1, HiByte(res));
        }

        private void InstrAND(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal & rrVal);
            this.flagV = false;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrANDI(int rd, byte K)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(rdVal & K);
            this.flagV = false;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrASR(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            this.flagN = ((rdVal & BYTE_MSB_MASK) != 0);
            byte res = (byte)(rdVal >> 1);
            if (this.flagN) res |= BYTE_MSB_MASK;
            this.flagC = ((rdVal & BYTE_LSB_MASK) != 0);
            this.flagV = this.flagN ^ this.flagC;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrBLD(int rd, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            byte rdVal = GetRegisterValue(rd);
            int mask = 1 << bNum;
            byte newVal;
            if (this.flagT) {
                newVal = (byte)(rdVal | mask);
            } else {
                newVal = (byte)(rdVal & (~mask));
            }
            SetRegisterValue(rd, newVal);
        }

        private void InstrBRCC(sbyte k)
        {
            if (!(this.flagC)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRCS(sbyte k)
        {
            if (this.flagC) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBREAK()
        {
            throw new BreakInterrupt(this.regPC - 1);
        }

        private void InstrBREQ(sbyte k)
        {
            if (this.flagZ) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRGE(sbyte k)
        {
            if (!(this.flagS)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRHC(sbyte k)
        {
            if (!(this.flagH)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRHS(sbyte k)
        {
            if (this.flagH) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRID(sbyte k)
        {
            if (!(this.flagI)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRIE(sbyte k)
        {
            if (this.flagI) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRLT(sbyte k)
        {
            if (this.flagS) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRMI(sbyte k)
        {
            if (this.flagN) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRNE(sbyte k)
        {
            if (!(this.flagZ)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRPL(sbyte k)
        {
            if (!(this.flagN)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRTC(sbyte k)
        {
            if (!(this.flagT)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRTS(sbyte k)
        {
            if (this.flagT) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRVC(sbyte k)
        {
            if (!(this.flagV)) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBRVS(sbyte k)
        {
            if (this.flagV) {
                this.cycles++;
                this.regPC += k;
            }
        }

        private void InstrBST(int rd, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            byte rdVal = GetRegisterValue(rd);
            int mask = 1 << bNum;
            this.flagT = ((rdVal & mask) != 0);
        }

        private void InstrCALL(int k)
        {
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x0000ff));
            this.regSP--;
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x00ff00));
            this.regSP--;
            if (this.largePC) {
                WriteDataMem(this.regSP, (byte)(this.regPC & 0x3f0000));
                this.regSP--;
            }
            this.regPC = k;
            if (this.largePC) {
                this.regPC &= 0x003fffff;
            } else {
                this.regPC &= 0x0000ffff;
            }
        }

        private void InstrCBI(int A, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            byte ioVal = ReadDataMem(addr);
            int mask = 1 << bNum;
            byte newVal = (byte)(ioVal & (~mask));
            WriteDataMem(addr, newVal);
            this.cycles--;   /* registres I/O plus rapides que la mémoire */
        }

        private void InstrCLC()
        {
            this.flagC = false;
        }

        private void InstrCLH()
        {
            this.flagH = false;
        }

        private void InstrCLI()
        {
            this.flagI = false;
        }

        private void InstrCLN()
        {
            this.flagN = false;
        }

        private void InstrCLS()
        {
            this.flagS = false;
        }

        private void InstrCLT()
        {
            this.flagT = false;
        }

        private void InstrCLV()
        {
            this.flagV = false;
        }

        private void InstrCLZ()
        {
            this.flagZ = false;
        }

        private void InstrCOM(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(0xff - rdVal);
            SetNZS(res);
            this.flagV = false;
            SetS();
            this.flagC = true;
            SetRegisterValue(rd, res);
        }

        private void InstrCP(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal - rrVal);
            SetNZS(res);
            SetVSsub(res, rdVal, rrVal);
            SetHsub(res, rdVal, rrVal);
            SetCsub(res, rdVal, rrVal);
        }

        private void InstrCPC(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal - rrVal);
            if (this.flagC) res--;
            SetNZS(res);
            SetVSsub(res, rdVal, rrVal);
            SetHsub(res, rdVal, rrVal);
            SetCsub(res, rdVal, rrVal);
        }

        private void InstrCPI(int rd, byte K)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(rdVal - K);
            SetNZS(res);
            SetVSsub(res, rdVal, K);
            SetHsub(res, rdVal, K);
            SetCsub(res, rdVal, K);
        }

        private void InstrCPSE(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            if (rdVal == rrVal) {
                ushort opcode = ReadProgMem(this.regPC);
                this.regPC++;
                if (IsLongOpcode(opcode)) {
                    this.regPC++;
                    this.cycles++;
                }
            }
        }

        private void InstrDEC(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = --rdVal;
            this.flagV = (res == 0x7f);
            SetNZS(res);
        }

        private void InstrDES(byte K)
        {
            /* L'émulateur ne prend pas en charge le chiffrement DES
               (qui est un standard obsolète) ! */
            throw new NotImplementedException(ERR_DES_NOT_SUPPORTED);
        }

        private void InstrEICALL()
        {
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x0000ff));
            this.regSP--;
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x00ff00));
            this.regSP--;
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x3f0000));
            this.regSP--;
            if (!(this.largePC)) {
                throw new InvalidOperationException(
                        ERR_EXT_PTR_INSTR_WITH_16BIT_PC);
            }
            this.regPC = this.RegisterZ | (this.regEIND << 16);
            this.regPC &= 0x003fffff;
        }

        private void InstrEIJMP()
        {
            if (!(this.largePC)) {
                throw new InvalidOperationException(
                        ERR_EXT_PTR_INSTR_WITH_16BIT_PC);
            }
            this.regPC = this.RegisterZ | (this.regEIND << 16);
            this.regPC &= 0x003fffff;
            this.cycles++;
        }

        private void InstrELPM()
        {
            InstrELPM_Z(0);
        }

        private void InstrELPM_Z(int rd)
        {
            if (!(this.largePC)) {
                throw new InvalidOperationException(
                        ERR_EXT_PTR_INSTR_WITH_16BIT_PC);
            }
            int addr = this.RegisterZ | (this.regRAMPZ << 16);
            bool wantsHi = ((addr & 0x0001) != 0);
            /* on ne lit la mémoire programme que par mots de 16 bits */
            addr >>= 1;
            ushort value = ReadProgMem(addr);
            SetRegisterValue(rd, (wantsHi ? HiByte(value) : LoByte(value)));
            this.cycles++;   /* ELPM prend 3 cycles */
        }

        private void InstrELPM_Zinc(int rd)
        {
            InstrELPM_Z(rd);
            IncZ();
        }

        private void InstrEOR(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal ^ rrVal);
            this.flagV = false;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrFMUL(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            ushort res = (ushort)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            res <<= 1;
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte(res));
            SetRegisterValue(1, HiByte(res));
            this.cycles++;   /* 2 cycles */
            // TODO Vérifier si l'instruction fonctionne bien de cette manière ! !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrFMULS(int rd, int rr)
        {
            sbyte rdVal = (sbyte)(GetRegisterValue(rd));
            sbyte rrVal = (sbyte)(GetRegisterValue(rr));
            short res = (short)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            res <<= 1;
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte((ushort)res));
            SetRegisterValue(1, HiByte((ushort)res));
            this.cycles++;   /* 2 cycles */
            // TODO Vérifier si l'instruction fonctionne bien de cette manière ! !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrFMULSU(int rd, int rr)
        {
            sbyte rdVal = (sbyte)(GetRegisterValue(rd));
            byte rrVal = GetRegisterValue(rd);
            short res = (short)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            res <<= 1;
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte((ushort)res));
            SetRegisterValue(1, HiByte((ushort)res));
            this.cycles++;   /* 2 cycles */
            // TODO Vérifier si l'instruction fonctionne bien de cette manière ! !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrICALL()
        {
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x0000ff));
            this.regSP--;
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x00ff00));
            this.regSP--;
            if (this.largePC) {
                WriteDataMem(this.regSP, (byte)(this.regPC & 0x3f0000));
                this.regSP--;
            }
            this.regPC = this.RegisterZ;
            this.regPC &= 0x0000ffff;
        }

        private void InstrIJMP()
        {
            this.regPC = this.RegisterZ;
            this.regPC &= 0x0000ffff;
            this.cycles++;   /* 2 cycles */
        }

        private void InstrIN(int rd, int A)
        {
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            SetRegisterValue(rd, ReadDataMem(addr));
            this.cycles--;   /* registres I/O plus rapides que la mémoire */
        }

        private void InstrINC(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = ++rdVal;
            this.flagV = (res == 0x80);
            SetNZS(res);
        }

        private void InstrJMP(int k)
        {
            this.regPC = k;
            if (this.largePC) {
                this.regPC &= 0x003fffff;
            } else {
                this.regPC &= 0x0000ffff;
            }
            this.cycles += 2;   /* 3 cycles */
        }

        private void InstrLAC(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            memVal &= (byte)(~rdVal);
            WriteDataMem(addr, memVal);
            this.cycles--;   /* échange rapide */
        }

        private void InstrLAS(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            memVal |= rdVal;
            WriteDataMem(addr, memVal);
            this.cycles--;   /* échange rapide */
        }

        private void InstrLAT(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            memVal ^= rdVal;
            WriteDataMem(addr, memVal);
            this.cycles--;   /* échange rapide */
        }

        private void InstrLD_X(int rd)
        {
            ushort addr = this.RegisterX;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLD_Xinc(int rd)
        {
            ushort addr = this.RegisterX;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            IncX();
        }

        private void InstrLD_decX(int rd)
        {
            DecX();
            ushort addr = this.RegisterX;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLD_Y(int rd)
        {
            ushort addr = this.RegisterY;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLDD_Y(int rd, byte q)
        {
            ushort addr = (ushort)(this.RegisterY + q);
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLD_Yinc(int rd)
        {
            ushort addr = this.RegisterY;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            IncY();
        }

        private void InstrLD_decY(int rd)
        {
            DecY();
            ushort addr = this.RegisterY;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLD_Z(int rd)
        {
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLDD_Z(int rd, byte q)
        {
            ushort addr = (ushort)(this.RegisterZ + q);
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLD_Zinc(int rd)
        {
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
            IncZ();
        }

        private void InstrLD_decZ(int rd)
        {
            DecZ();
            ushort addr = this.RegisterZ;
            byte memVal = ReadDataMem(addr);
            SetRegisterValue(rd, memVal);
        }

        private void InstrLDI(int rd, byte K)
        {
            SetRegisterValue(rd, K);
        }

        private void InstrLDS(int rd, ushort k)
        {
            byte val = ReadDataMem(k);
            SetRegisterValue(rd, val);
        }

        private void InstrLPM()
        {
            InstrLPM_Z(0);
        }

        private void InstrLPM_Z(int rd)
        {
            ushort addr = this.RegisterZ;
            bool wantsHi = ((addr & 0x0001) != 0);
            /* on ne lit la mémoire programme que par mots de 16 bits */
            addr >>= 1;
            ushort value = ReadProgMem(addr);
            SetRegisterValue(rd, (wantsHi ? HiByte(value) : LoByte(value)));
            this.cycles++;   /* LPM prend 3 cycles */
        }

        private void InstrLPM_Zinc(int rd)
        {
            InstrLPM_Z(rd);
            IncZ();
        }

        private void InstrLSR(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)((rdVal >> 1) & 0x7f);
            this.flagC = ((rdVal & BYTE_LSB_MASK) != 0);
            this.flagV = this.flagC;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrMOV(int rd, int rr)
        {
            byte val = GetRegisterValue(rr);
            SetRegisterValue(rd, val);
        }

        private void InstrMOVW(int rd, int rr)
        {
            byte val = GetRegisterValue(rr);
            SetRegisterValue(rd, val);
            val = GetRegisterValue(rr + 1);
            SetRegisterValue(rd + 1, val);
        }

        private void InstrMUL(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            ushort res = (ushort)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte(res));
            SetRegisterValue(1, HiByte(res));
            this.cycles++;   /* 2 cycles */
        }

        private void InstrMULS(int rd, int rr)
        {
            sbyte rdVal = (sbyte)(GetRegisterValue(rd));
            sbyte rrVal = (sbyte)(GetRegisterValue(rr));
            short res = (short)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte((ushort)res));
            SetRegisterValue(1, HiByte((ushort)res));
            this.cycles++;   /* 2 cycles */
        }

        private void InstrMULSU(int rd, int rr)
        {
            sbyte rdVal = (sbyte)(GetRegisterValue(rd));
            byte rrVal = GetRegisterValue(rd);
            short res = (short)(rdVal * rrVal);
            this.flagC = ((res & 0x8000) != 0);
            this.flagZ = (res == 0);
            SetRegisterValue(0, LoByte((ushort)res));
            SetRegisterValue(1, HiByte((ushort)res));
            this.cycles++;   /* 2 cycles */
        }

        private void InstrNEG(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(0 - rdVal);
            this.flagV = (res == 0x80);
            SetNZS(res);
            this.flagC = (res != 0x00);
            this.flagH = ((rdVal & BYTE_BCD_MASK) != 0)
                      || ((res & BYTE_BCD_MASK) != 0);
            SetRegisterValue(rd, res);
        }

        private void InstrNOP()
        {
            /* ne rien faire */
        }

        private void InstrOR(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal | rrVal);
            this.flagV = false;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrORI(int rd, byte K)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(rdVal | K);
            this.flagV = false;
            SetNZS(res);
            SetRegisterValue(rd, res);
        }

        private void InstrOUT(int A, int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            WriteDataMem(addr, rdVal);
            this.cycles--;   /* registres I/O plus rapides que la mémoire */
        }

        private void InstrPOP(int rd)
        {
            this.regSP++;
            SetRegisterValue(rd, ReadDataMem(this.regSP));
        }

        private void InstrPUSH(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(this.regSP, rdVal);
            this.regSP--;
        }

        private void InstrRCALL(short k)
        {
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x0000ff));
            this.regSP--;
            WriteDataMem(this.regSP, (byte)(this.regPC & 0x00ff00));
            this.regSP--;
            if (this.largePC) {
                WriteDataMem(this.regSP, (byte)(this.regPC & 0x3f0000));
                this.regSP--;
            }
            this.regPC += k;
            if (this.largePC) {
                this.regPC &= 0x003fffff;
            } else {
                this.regPC &= 0x0000ffff;
            }
        }

        private void InstrRET()
        {
            this.regSP++;
            int retAddr = ReadDataMem(this.regSP);
            this.regSP++;
            retAddr |= (ReadDataMem(this.regSP) << 8);
            if (this.largePC) {
                this.regSP++;
                retAddr |= (ReadDataMem(this.regSP) << 16);
            }
            this.cycles++;
            this.regPC = retAddr;
            if (this.largePC) {
                this.regPC &= 0x003fffff;
            } else {
                this.regPC &= 0x0000ffff;
            }
        }

        private void InstrRETI()
        {
            InstrRET();
            this.flagI = true;
        }

        private void InstrRJMP(short k)
        {
            this.regPC += k;
            if (this.largePC) {
                this.regPC &= 0x003fffff;
            } else {
                this.regPC &= 0x0000ffff;
            }
            this.cycles++;   /* 2 cycles */
        }

        private void InstrROR(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte inMask = (byte)(this.flagC ? BYTE_MSB_MASK : 0);
            byte res = (byte)((rdVal >> 1) & inMask);
            this.flagC = ((rdVal & BYTE_LSB_MASK) != 0);
            SetNZS(res);
            this.flagV = this.FlagN ^ this.flagC;
            SetS();
            SetRegisterValue(rd, res);
        }

        private void InstrSBC(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal - rrVal);
            if (this.flagC) res--;
            SetNZS(res);
            SetVSsub(res, rdVal, rrVal);
            SetHsub(res, rdVal, rrVal);
            SetCsub(res, rdVal, rrVal);
            SetRegisterValue(rd, res);
        }

        private void InstrSBCI(int rd, byte K)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(rdVal - K);
            if (this.flagC) res--;
            SetNZS(res);
            SetVSsub(res, rdVal, K);
            SetHsub(res, rdVal, K);
            SetCsub(res, rdVal, K);
            SetRegisterValue(rd, res);
        }

        private void InstrSBI(int A, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            byte ioVal = ReadDataMem(addr);
            int mask = 1 << bNum;
            byte newVal = (byte)(ioVal | mask);
            WriteDataMem(addr, newVal);
            this.cycles--;   /* registres I/O plus rapides que la mémoire */
        }

        private void InstrSBIC(int A, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            byte ioVal = ReadDataMem(addr);
            int mask = 1 << bNum;
            if ((ioVal & mask) == 0) {
                ushort opcode = ReadProgMem(this.regPC);
                this.regPC++;
                if (IsLongOpcode(opcode)) {
                    this.regPC++;
                    this.cycles++;
                }
            }
        }

        private void InstrSBIS(int A, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            ushort addr = (ushort)(STD_IO_BASE_ADDRESS + A);
            byte ioVal = ReadDataMem(addr);
            int mask = 1 << bNum;
            if ((ioVal & mask) != 0) {
                ushort opcode = ReadProgMem(this.regPC);
                this.regPC++;
                if (IsLongOpcode(opcode)) {
                    this.regPC++;
                    this.cycles++;
                }
            }
        }

        private void InstrSBIW(int rd, byte K)
        {
            byte rdLo = GetRegisterValue(rd);
            byte rdHi = GetRegisterValue(rd + 1);
            int diff = MakeWord(rdHi, rdLo) - K;
            ushort res = (ushort)diff;
            this.flagN = ((res & 0x8000) != 0);
            this.flagV = !(this.flagN) && ((rdHi & BYTE_MSB_MASK) != 0);
            SetS();
            this.flagZ = (res == 0x0000);
            this.flagC = this.flagN && ((rdHi & BYTE_MSB_MASK) == 0);
            this.cycles++;   // instruction prenant 2 cycles
            SetRegisterValue(rd, LoByte(res));
            SetRegisterValue(rd + 1, HiByte(res));
        }

        private void InstrSBRC(int rd, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            byte rdVal = GetRegisterValue(rd);
            int mask = 1 << bNum;
            if ((rdVal & mask) == 0) {
                ushort opcode = ReadProgMem(this.regPC);
                this.regPC++;
                if (IsLongOpcode(opcode)) {
                    this.regPC++;
                    this.cycles++;
                }
            }
        }

        private void InstrSBRS(int rd, int bNum)
        {
            if ((bNum < 0) || (bNum > 7)) {
                throw new ArgumentOutOfRangeException("bNum");
            }
            byte rdVal = GetRegisterValue(rd);
            int mask = 1 << bNum;
            if ((rdVal & mask) != 0) {
                ushort opcode = ReadProgMem(this.regPC);
                this.regPC++;
                if (IsLongOpcode(opcode)) {
                    this.regPC++;
                    this.cycles++;
                }
            }
        }

        private void InstrSEC()
        {
            this.flagC = true;
        }

        private void InstrSEH()
        {
            this.flagH = true;
        }

        private void InstrSEI()
        {
            this.flagI = true;
        }

        private void InstrSEN()
        {
            this.flagN = true;
        }

        private void InstrSES()
        {
            this.flagS = true;
        }

        private void InstrSET()
        {
            this.flagT = true;
        }

        private void InstrSEV()
        {
            this.flagV = true;
        }

        private void InstrSEZ()
        {
            this.flagZ = true;
        }

        private void InstrSLEEP()
        {
            this.asleep = true;
        }

        private void InstrSPM()
        {
            /* L'émulateur ne prend pas en charge l'écriture dans la ROM
             * (l'utilisateur modifiera l'espace-mémoire programme lui-même) ! */
            throw new NotImplementedException(ERR_SPM_NOT_SUPPORTED);
        }
        private void InstrSPM_Zinc()
        {
            InstrSPM();
            IncZ();
        }

        private void InstrST_X(int rd)
        {
            ushort addr = this.RegisterX;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrST_Xinc(int rd)
        {
            ushort addr = this.RegisterX;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
            IncX();
        }

        private void InstrST_decX(int rd)
        {
            DecX();
            ushort addr = this.RegisterX;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrST_Y(int rd)
        {
            ushort addr = this.RegisterY;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrSTD_Y(byte q, int rd)
        {
            ushort addr = (ushort)(this.RegisterY + q);
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrST_Yinc(int rd)
        {
            ushort addr = this.RegisterY;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
            IncY();
        }

        private void InstrST_decY(int rd)
        {
            DecY();
            ushort addr = this.RegisterY;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrST_Z(int rd)
        {
            ushort addr = this.RegisterZ;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrSTD_Z(byte q, int rd)
        {
            ushort addr = (ushort)(this.RegisterZ + q);
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrST_Zinc(int rd)
        {
            ushort addr = this.RegisterZ;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
            IncZ();
        }

        private void InstrST_decZ(int rd)
        {
            DecZ();
            ushort addr = this.RegisterZ;
            byte rdVal = GetRegisterValue(rd);
            WriteDataMem(addr, rdVal);
        }

        private void InstrSTS(ushort k, int rd)
        {
            byte val = GetRegisterValue(rd);
            WriteDataMem(k, val);
        }

        private void InstrSUB(int rd, int rr)
        {
            byte rdVal = GetRegisterValue(rd);
            byte rrVal = GetRegisterValue(rr);
            byte res = (byte)(rdVal - rrVal);
            SetNZS(res);
            SetVSsub(res, rdVal, rrVal);
            SetHsub(res, rdVal, rrVal);
            SetCsub(res, rdVal, rrVal);
            SetRegisterValue(rd, res);
        }

        private void InstrSUBI(int rd, byte K)
        {
            byte rdVal = GetRegisterValue(rd);
            byte res = (byte)(rdVal - K);
            SetNZS(res);
            SetVSsub(res, rdVal, K);
            SetHsub(res, rdVal, K);
            SetCsub(res, rdVal, K);
            SetRegisterValue(rd, res);
        }

        private void InstrSWAP(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            byte hiNib = (byte)(rdVal & 0xf0);
            byte loNib = (byte)(rdVal & 0x0f);
            int res = (hiNib >> 4) | (loNib << 4);
            SetRegisterValue(rd, (byte)res);
        }

        private void InstrWDR()
        {
            // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        }

        private void InstrXCH(int rd)
        {
            byte rdVal = GetRegisterValue(rd);
            ushort addr = this.RegisterZ;
            SetRegisterValue(rd, ReadDataMem(addr));
            WriteDataMem(addr, rdVal);
            this.cycles--;   /* échange rapide */
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
            this.regPC++;   /* opcode double */
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
            this.regPC++;   /* opcode double */
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

        /* ~~~~ décodage et exécution des opcodes ~~~~ */

        private void ExecOpcodes0(ushort opcode)
        {
            if (opcode == 0x0000) {
                /* NOP */
                InstrNOP();
            }
            int rd, rr;
            int secDigit = ((opcode >> 8) & 0x0f);
            switch (secDigit) {
                case 0x1:
                    /* MOVW Rd':Rd, Rr':Rr */
                    rd = GetDestRegPairNum(opcode);
                    rr = GetSrcRegPairNum(opcode);
                    InstrMOVW(rd, rr);
                    return;
                case 0x2:
                    /* MULS Rd, Rr */
                    rd = GetShortDestRegNum(opcode);
                    rr = GetShortSrcRegNum(opcode);
                    InstrMULS(rd, rr);
                    return;
                case 0x3:
                    rd = GetTinyDestRegNum(opcode);
                    rr = GetTinySrcRegNum(opcode);
                    bool bit3 = ((opcode & 0x0007) != 0);
                    bool bit7 = ((opcode & 0x0070) != 0);
                    if (bit3 && bit7) {
                        /* FMULSU Rd, Rr */
                        InstrFMULSU(rd, rr);
                    } else if (bit7) {
                        /* FMULS Rd, Rr */
                        InstrFMULS(rd, rr);
                    } else if (bit3) {
                        /* FMUL Rd, Rr */
                        InstrFMUL(rd, rr);
                    } else {
                        /* MULSU Rd, Rr */
                        InstrMULSU(rd, rr);
                    }
                    return;
                case 0x4:
                case 0x5:
                case 0x6:
                case 0x7:
                    /* CPC Rd, Rr */
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    InstrCPC(rd, rr);
                    return;
                case 0x8:
                case 0x9:
                case 0xa:
                case 0xb:
                    /* SBC Rd, Rr */
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    InstrSBC(rd, rr);
                    return;
                case 0xc:
                case 0xd:
                case 0xe:
                case 0xf:
                    /* ADD Rd, Rr / LSL Rd */
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    InstrADD(rd, rr);
                    return;
            }

            /* OPCODE INVALIDE ! */
            if (this.uoPolicy == UnknownOpcodePolicy.DoNop) return;
            throw new UnknownOpcodeException(
                    this.regPC - 1, opcode,
                    String.Format(ERR_UNKNOWN_OPCODE, this.regPC - 1, opcode));
        }

        private void ExecOpcodes1(ushort opcode)
        {
            int rd = GetFullDestRegNum(opcode);
            int rr = GetFullSrcRegNum(opcode);
            int bits1011 = ((opcode >> 10) & 0x03);
            switch (bits1011) {
                case 0:
                    /* CPSE Rd, Rr */
                    InstrCPSE(rd, rr);
                    return;
                case 1:
                    /* CP Rd, Rr */
                    InstrCP(rd, rr);
                    return;
                case 2:
                    /* SUB Rd, Rr */
                    InstrSUB(rd, rr);
                    return;
                case 3:
                    /* ADC Rd, Rr / ROL Rd */
                    InstrADC(rd, rr);
                    return;
            }
        }

        private void ExecOpcodes2(ushort opcode)
        {
            int rd = GetFullDestRegNum(opcode);
            int rr = GetFullSrcRegNum(opcode);
            int bits1011 = ((opcode >> 10) & 0x03);
            switch (bits1011) {
                case 0:
                    /* AND Rd, Rr / TST Rd */
                    InstrAND(rd, rr);
                    return;
                case 1:
                    /* EOR Rd, Rr / CLR Rd */
                    InstrEOR(rd, rr);
                    return;
                case 2:
                    /* OR Rd, Rr */
                    InstrOR(rd, rr);
                    return;
                case 3:
                    /* MOV Rd, Rr */
                    InstrMOV(rd, rr);
                    return;
            }
        }

        private void ExecOpcodes3(ushort opcode)
        {
            /* CPI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrCPI(rd, K);
        }

        private void ExecOpcodes4(ushort opcode)
        {
            /* SBCI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrSBCI(rd, K);
        }

        private void ExecOpcodes5(ushort opcode)
        {
            /* SUBI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrSUBI(rd, K);
        }

        private void ExecOpcodes6(ushort opcode)
        {
            /* ORI Rd, K  (alias SBR) */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrORI(rd, K);
        }

        private void ExecOpcodes7(ushort opcode)
        {
            /* ANDI Rd, K  (alias CBR) */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrANDI(rd, K);
        }

        private void ExecOpcodes8A(ushort opcode)
        {
            int rd = GetFullDestRegNum(opcode);
            byte q = Get6bitOffset(opcode);
            bool bit3 = ((opcode & 0x0008) != 0);
            bool bit9 = ((opcode & 0x0200) != 0);
            if (q == 0) {
                if (bit9) {
                    /* ST idx, Rd */
                    if (bit3) {
                        InstrST_Y(rd);
                    } else {
                        InstrST_Z(rd);
                    }
                } else {
                    /* LD Rd, idx */
                    if (bit3) {
                        InstrLD_Y(rd);
                    } else {
                        InstrLD_Z(rd);
                    }
                }
            } else {
                if (bit9) {
                    /* STD idx + q, Rd */
                    if (bit3) {
                        InstrSTD_Y(q, rd);
                    } else {
                        InstrSTD_Z(q, rd);
                    }
                } else {
                    /* LDD Rd, idx + q */
                    if (bit3) {
                        InstrLDD_Y(rd, q);
                    } else {
                        InstrLDD_Z(rd, q);
                    }
                }
            }
        }

        private void ExecOpcodes9(ushort opcode)
        {
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
                            ushort addr = Get16bitDataAddress(opcode);
                            InstrLDS(rd, addr);
                            return;
                        case 0x1:
                            /* LD Rd, Z+ */
                            InstrLD_Zinc(rd);
                            return;
                        case 0x2:
                            /* LD Rd, -Z */
                            InstrLD_decZ(rd);
                            return;
                        case 0x4:
                            /* LPM Rd, Z */
                            InstrLPM_Z(rd);
                            return;
                        case 0x5:
                            /* LPM Rd, Z+ */
                            InstrLPM_Zinc(rd);
                            return;
                        case 0x6:
                            /* ELPM Rd, Z */
                            InstrELPM_Z(rd);
                            return;
                        case 0x7:
                            /* ELPM Rd, Z+ */
                            InstrELPM_Zinc(rd);
                            return;
                        case 0x9:
                            /* LD Rd, Y+ */
                            InstrLD_Yinc(rd);
                            return;
                        case 0xa:
                            /* LD Rd, -Y */
                            InstrLD_decY(rd);
                            return;
                        case 0xc:
                            /* LD Rd, X */
                            InstrLD_X(rd);
                            return;
                        case 0xd:
                            /* LD Rd, X+ */
                            InstrLD_Xinc(rd);
                            return;
                        case 0xe:
                            /* LD Rd, -X */
                            InstrLD_decX(rd);
                            return;
                        case 0xf:
                            /* POP Rd */
                            InstrPOP(rd);
                            return;
                    }
                    break;
                case 0x2:
                case 0x3:
                    rr = GetFullDestRegNum(opcode);
                    switch (lstDigit) {
                        case 0x0:
                            /* STS k, Rr (double opcode !) */
                            ushort addr = Get16bitDataAddress(opcode);
                            InstrSTS(addr, rr);
                            return;
                        case 0x1:
                            /* ST Z+, Rr */
                            InstrST_Zinc(rr);
                            return;
                        case 0x2:
                            /* ST -Z, Rr */
                            InstrST_decZ(rr);
                            return;
                        case 0x4:
                            /* XCH Z, Rr */
                            InstrXCH(rr);
                            return;
                        case 0x5:
                            /* LAS Z, Rr */
                            InstrLAS(rr);
                            return;
                        case 0x6:
                            /* LAC Z, Rr */
                            InstrLAC(rr);
                            return;
                        case 0x7:
                            /* LAT Z, Rr */
                            InstrLAT(rr);
                            return;
                        case 0x9:
                            /* ST Y+, Rr */
                            InstrST_Yinc(rr);
                            return;
                        case 0xa:
                            /* ST -Y, Rr */
                            InstrST_decY(rr);
                            return;
                        case 0xc:
                            /* ST X, Rr */
                            InstrST_X(rr);
                            return;
                        case 0xd:
                            /* ST X+; Rr */
                            InstrST_Xinc(rr);
                            return;
                        case 0xe:
                            /* ST -X, Rr */
                            InstrST_decX(rr);
                            return;
                        case 0xf:
                            /* PUSH Rr */
                            InstrPUSH(rr);
                            return;
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
                                        InstrSEC();
                                        return;
                                    case 0x1:
                                        /* SEZ */
                                        InstrSEZ();
                                        return;
                                    case 0x2:
                                        /* SEN */
                                        InstrSEN();
                                        return;
                                    case 0x3:
                                        /* SEV */
                                        InstrSEV();
                                        return;
                                    case 0x4:
                                        /* SES */
                                        InstrSES();
                                        return;
                                    case 0x5:
                                        /* SEH */
                                        InstrSEH();
                                        return;
                                    case 0x6:
                                        /* SET */
                                        InstrSET();
                                        return;
                                    case 0x7:
                                        /* SEI */
                                        InstrSEI();
                                        return;
                                    case 0x8:
                                        /* CLC */
                                        InstrCLC();
                                        return;
                                    case 0x9:
                                        /* CLZ */
                                        InstrCLZ();
                                        return;
                                    case 0xa:
                                        /* CLN */
                                        InstrCLN();
                                        return;
                                    case 0xb:
                                        /* CLV */
                                        InstrCLV();
                                        return;
                                    case 0xc:
                                        /* CLS */
                                        InstrCLS();
                                        return;
                                    case 0xd:
                                        /* CLH */
                                        InstrCLH();
                                        return;
                                    case 0xe:
                                        /* CLT */
                                        InstrCLT();
                                        return;
                                    case 0xf:
                                        /* CLI */
                                        InstrCLI();
                                        return;
                                }
                                break;
                            case 0x9:
                                switch (trdDigit) {
                                    case 0x0:
                                        /* IJMP */
                                        InstrIJMP();
                                        return;
                                    case 1:
                                        /* EIJMP */
                                        InstrEIJMP();
                                        return;
                                }
                                break;
                            case 0xb:
                                /* DES K */
                                K = (byte)trdDigit;
                                InstrDES(K);
                                return;
                        }
                    }
                    /* spécificités de 0x95nn */
                    if (secDigit == 5) {
                        switch (trdDigit) {
                            case 0x0:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* RET */
                                        InstrRET();
                                        return;
                                    case 0x9:
                                        /* ICALL */
                                        InstrICALL();
                                        return;
                                }
                                break;
                            case 0x1:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* RETI */
                                        InstrRETI();
                                        return;
                                    case 0x9:
                                        /* EICALL */
                                        InstrEICALL();
                                        return;
                                }
                                break;
                            case 0x8:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SLEEP */
                                        InstrSLEEP();
                                        return;
                                }
                                break;
                            case 0x9:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* BREAK */
                                        InstrBREAK();
                                        return;
                                }
                                break;
                            case 0xa:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* WDR */
                                        InstrWDR();
                                        return;
                                }
                                break;
                            case 0xc:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* LPM */
                                        InstrLPM();
                                        return;
                                }
                                break;
                            case 0xd:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* ELPM */
                                        InstrELPM();
                                        return;
                                }
                                break;
                            case 0xe:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SPM */
                                        InstrSPM();
                                        return;
                                }
                                break;
                            case 0xf:
                                switch (lstDigit) {
                                    case 0x8:
                                        /* SPM Z+ */
                                        InstrSPM_Zinc();
                                        return;
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
                            InstrCOM(rd);
                            return;
                        case 0x1:
                            /* NEG Rd */
                            InstrNEG(rd);
                            return;
                        case 0x2:
                            /* SWAP Rd */
                            InstrSWAP(rd);
                            return;
                        case 0x3:
                            /* INC Rd */
                            InstrINC(rd);
                            return;
                        case 0x5:
                            /* ASR Rd */
                            InstrASR(rd);
                            return;
                        case 0x6:
                            /* LSR Rd */
                            InstrLSR(rd);
                            return;
                        case 0x7:
                            /* ROR Rd */
                            InstrROR(rd);
                            return;
                        case 0xa:
                            /* DEC Rd */
                            InstrDEC(rd);
                            return;
                        case 0xc:
                        case 0xd:
                            /* JMP k (double opcode !) */
                            dest = Get22bitDestAddr(opcode);
                            InstrJMP(dest);
                            return;
                        case 0xe:
                        case 0xf:
                            /* CALL k (double opcode !) */
                            dest = Get22bitDestAddr(opcode);
                            InstrCALL(dest);
                            return;
                    }
                    break;
                case 0x6:
                    /* ADIW Rd':Rd, K */
                    rd = GetTinyRegPairNum(opcode);
                    K = Get6bitConst(opcode);
                    InstrADIW(rd, K);
                    return;
                case 0x7:
                    /* SBIW Rd':Rd, K */
                    rd = GetTinyRegPairNum(opcode);
                    K = Get6bitConst(opcode);
                    InstrSBIW(rd, K);
                    return;
                case 0x8:
                    /* CBI A, b */
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    InstrCBI(A, b);
                    return;
                case 0x9:
                    /* SBIC A, b */
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    InstrSBIC(A, b);
                    return;
                case 0xa:
                    /* SBI A, b */
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    InstrSBI(A, b);
                    return;
                case 0xb:
                    /* SBIS A, b */
                    A = GetShortIOAddress(opcode);
                    b = GetBitNumber(opcode);
                    InstrSBIS(A, b);
                    return;
                case 0xc:
                case 0xd:
                case 0xe:
                case 0xf:
                    /* MUL Rd, Rr */
                    rd = GetFullDestRegNum(opcode);
                    rr = GetFullSrcRegNum(opcode);
                    InstrMUL(rd, rr);
                    return;
            }

            /* OPCODE INVALIDE ! */
            if (this.uoPolicy == UnknownOpcodePolicy.DoNop) return;
            throw new UnknownOpcodeException(
                    this.regPC - 1, opcode,
                    String.Format(ERR_UNKNOWN_OPCODE, this.regPC - 1, opcode));
        }

        private void ExecOpcodesB(ushort opcode)
        {
            int rd = GetFullDestRegNum(opcode);
            byte A = GetFullIOAddress(opcode);
            bool bit11 = ((opcode & 0x0800) != 0);
            if (bit11) {
                /* OUT A, Rd */
                InstrOUT(A, rd);
            } else {
                /* IN Rd, A */
                InstrIN(rd, A);
            }
        }

        private void ExecOpcodesC(ushort opcode)
        {
            /* RJMP k */
            short k = Get12bitRelativeDispl(opcode);
            InstrRJMP(k);
        }

        private void ExecOpcodesD(ushort opcode)
        {
            /* RCALL k */
            short k = Get12bitRelativeDispl(opcode);
            InstrRCALL(k);
        }

        private void ExecOpcodesE(ushort opcode)
        {
            /* LDI Rd, K */
            int rd = GetShortDestRegNum(opcode);
            byte K = Get8bitConst(opcode);
            InstrLDI(rd, K);
        }

        private void ExecOpcodesF(ushort opcode)
        {
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
                            InstrBRCS(k);
                            break;
                        case 1:
                            /* BREQ k */
                            InstrBREQ(k);
                            break;
                        case 2:
                            /* BRMI k */
                            InstrBRMI(k);
                            break;
                        case 3:
                            /* BRVS k */
                            InstrBRVS(k);
                            break;
                        case 4:
                            /* BRLT k */
                            InstrBRLT(k);
                            break;
                        case 5:
                            /* BRHS k */
                            InstrBRHS(k);
                            break;
                        case 6:
                            /* BRTS k */
                            InstrBRTS(k);
                            break;
                        case 7:
                            /* BRIE k */
                            InstrBRIE(k);
                            break;
                    }
                    return;
                case 1:
                    /* BRBC s, k */
                    switch (bNum) {
                        case 0:
                            /* BRCC k / BRSH k */
                            InstrBRCC(k);
                            break;
                        case 1:
                            /* BRNE k */
                            InstrBRNE(k);
                            break;
                        case 2:
                            /* BRPL k */
                            InstrBRPL(k);
                            break;
                        case 3:
                            /* BRVC k */
                            InstrBRVC(k);
                            break;
                        case 4:
                            /* BRGE k */
                            InstrBRGE(k);
                            break;
                        case 5:
                            /* BRHC k */
                            InstrBRHC(k);
                            break;
                        case 6:
                            /* BRTC k */
                            InstrBRTC(k);
                            break;
                        case 7:
                            /* BRID k */
                            InstrBRID(k);
                            break;
                    }
                    return;
                case 2:
                    if (!bit3) {
                        rd = GetFullDestRegNum(opcode);
                        if (bit9) {
                            /* BST Rd, b */
                            InstrBST(rd, bNum);
                        } else {
                            /* BLD Rd, b */
                            InstrBLD(rd, bNum);
                        }
                    }
                    return;
                case 3:
                    if (!bit3) {
                        rd = GetFullDestRegNum(opcode);
                        if (bit9) {
                            /* SBRS Rd, b */
                            InstrSBRS(rd, bNum);
                        } else {
                            /* SBRC Rd, b */
                            InstrSBRC(rd, bNum);
                        }
                    }
                    return;
            }

            /* OPCODE INVALIDE ! */
            if (this.uoPolicy == UnknownOpcodePolicy.DoNop) return;
            throw new UnknownOpcodeException(
                    this.regPC - 1, opcode,
                    String.Format(ERR_UNKNOWN_OPCODE, this.regPC - 1, opcode));
        }

        // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

        /* ~~~~ traçage ~~~~ */

        private void DoTrace()
        {
            this.traceFile.WriteLine("=> PC=${0:X5}", this.regPC);
            this.traceFile.WriteLine("   SP=${0:X4}", this.regSP);
            this.traceFile.Write("  ");
            for (int n = 0; n < 32; n++) {
                this.traceFile.Write(" R{0}=${1:X2}", n, GetRegisterValue(n));
            }
            this.traceFile.WriteLine();
            this.traceFile.WriteLine(
                    "   SREG=${0:X2}" +
                    " (I={1} T={2} H={3} S={4} V={5} N={6} Z={7} C={8})",
                    this.RegisterS,
                    (this.flagI ? 1 : 0),
                    (this.flagT ? 1 : 0),
                    (this.flagH ? 1 : 0),
                    (this.flagS ? 1 : 0),
                    (this.flagV ? 1 : 0),
                    (this.flagN ? 1 : 0),
                    (this.flagZ ? 1 : 0),
                    (this.flagC ? 1 : 0));
            this.traceFile.Flush();
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /* ~~ Accès aux registres ~~ */

        /// <summary>
        /// Renvoie la valeur du registre général indiqué.
        /// </summary>
        /// <param name="nReg">
        /// Numéro du registre général dont on veut récupérer la valeur.
        /// </param>
        /// <returns>
        /// Valeur du registre général numéro <code>nReg</code>.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Si <code>nReg</code> est en dehors des numéros de registres
        /// généraux disponibles (entre 0 et 31).
        /// </exception>
        public Byte GetRegisterValue(Int32 nReg)
        {
            switch (nReg) {
                case 0:
                    return this.reg0;
                case 1:
                    return this.reg1;
                case 2:
                    return this.reg2;
                case 3:
                    return this.reg3;
                case 4:
                    return this.reg4;
                case 5:
                    return this.reg5;
                case 6:
                    return this.reg6;
                case 7:
                    return this.reg7;
                case 8:
                    return this.reg8;
                case 9:
                    return this.reg9;
                case 10:
                    return this.reg10;
                case 11:
                    return this.reg11;
                case 12:
                    return this.reg12;
                case 13:
                    return this.reg13;
                case 14:
                    return this.reg14;
                case 15:
                    return this.reg15;
                case 16:
                    return this.reg16;
                case 17:
                    return this.reg17;
                case 18:
                    return this.reg18;
                case 19:
                    return this.reg19;
                case 20:
                    return this.reg20;
                case 21:
                    return this.reg21;
                case 22:
                    return this.reg22;
                case 23:
                    return this.reg23;
                case 24:
                    return this.reg24;
                case 25:
                    return this.reg25;
                case 26:
                    return this.reg26;
                case 27:
                    return this.reg27;
                case 28:
                    return this.reg28;
                case 29:
                    return this.reg29;
                case 30:
                    return this.reg30;
                case 31:
                    return this.reg31;
                default:
                    throw new ArgumentOutOfRangeException(
                            "nReg",
                            String.Format(ERR_INVALID_GEN_REG_NUM, nReg));
            }
        }

        /// <summary>
        /// Définit la valeur du registre général indiqué.
        /// </summary>
        /// <param name="nReg">
        /// Numéro du registre général dont on veut changer la valeur.
        /// </param>
        /// <param name="value">
        /// Nouvelle valeur du registre général numéro <code>nReg</code>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Si <code>nReg</code> est en dehors des numéros de registres
        /// généraux disponibles (entre 0 et 31).
        /// </exception>
        public void SetRegisterValue(Int32 nReg, Byte value)
        {
            switch (nReg) {
                case 0:
                    this.reg0 = value;
                    break;
                case 1:
                    this.reg1 = value;
                    break;
                case 2:
                    this.reg2 = value;
                    break;
                case 3:
                    this.reg3 = value;
                    break;
                case 4:
                    this.reg4 = value;
                    break;
                case 5:
                    this.reg5 = value;
                    break;
                case 6:
                    this.reg6 = value;
                    break;
                case 7:
                    this.reg7 = value;
                    break;
                case 8:
                    this.reg8 = value;
                    break;
                case 9:
                    this.reg9 = value;
                    break;
                case 10:
                    this.reg10 = value;
                    break;
                case 11:
                    this.reg11 = value;
                    break;
                case 12:
                    this.reg12 = value;
                    break;
                case 13:
                    this.reg13 = value;
                    break;
                case 14:
                    this.reg14 = value;
                    break;
                case 15:
                    this.reg15 = value;
                    break;
                case 16:
                    this.reg16 = value;
                    break;
                case 17:
                    this.reg17 = value;
                    break;
                case 18:
                    this.reg18 = value;
                    break;
                case 19:
                    this.reg19 = value;
                    break;
                case 20:
                    this.reg20 = value;
                    break;
                case 21:
                    this.reg21 = value;
                    break;
                case 22:
                    this.reg22 = value;
                    break;
                case 23:
                    this.reg23 = value;
                    break;
                case 24:
                    this.reg24 = value;
                    break;
                case 25:
                    this.reg25 = value;
                    break;
                case 26:
                    this.reg26 = value;
                    break;
                case 27:
                    this.reg27 = value;
                    break;
                case 28:
                    this.reg28 = value;
                    break;
                case 29:
                    this.reg29 = value;
                    break;
                case 30:
                    this.reg30 = value;
                    break;
                case 31:
                    this.reg31 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                            "nReg",
                            String.Format(ERR_INVALID_GEN_REG_NUM, nReg));
            }
        }

        /* ~~ Réinitialisation du processeur ~~ */

        /// <summary>
        /// Réinitialise le processeur.
        /// </summary>
        /// <exception cref="AddressUnreadableException">
        /// Si une adresse-mémoire (vecteur RESET ou sa cible)
        /// ne peut pas être lue.
        /// </exception>
        public void Reset()
        {
            // valeurs initiales des registres
            this.regPC = 0x0000;
            this.regSP = 0x0000;
            this.RegisterS = 0x00;
            this.cycles = 0L;
            this.asleep = false;
            // traçage si besoin est
            if (this.traceFile != null) {
                this.traceFile.WriteLine("\n\n*** RESET! ***\n");
                DoTrace();
            }
        }


        /// <summary>
        /// Exécute l'instruction actuellement pointée par le registre PC.
        /// </summary>
        /// <returns>
        /// Nombre de cycles écoulés pour l'exécution de l'instruction.
        /// </returns>
        /// <exception cref="AddressUnreadableException">
        /// Si le contenu d'une adresse-mémoire nécessaire au travail
        /// du processeur ne peut pas être lu.
        /// </exception>
        public ulong Step()
        {
            ulong cycBegin = this.cycles;

            // ne rien faire en "mode sommeil"
            if (this.asleep) {
                return 0L;
            }

            // désassemblage si traçage
            if (this.traceFile != null) {
                this.traceFile.Write(
                        this.traceDisasm.DisassembleInstructionAt(this.regPC));
            }

            // lit, décode et exécute le prochain opcode
            ushort opcode = ReadProgMem(this.regPC);
            this.regPC++;
            int hiDigit = ((opcode >> 12) & 0x0f);
            switch (hiDigit) {
                case 0x0:
                    ExecOpcodes0(opcode);
                    break;
                case 0x1:
                    ExecOpcodes1(opcode);
                    break;
                case 0x2:
                    ExecOpcodes2(opcode);
                    break;
                case 0x3:
                    ExecOpcodes3(opcode);
                    break;
                case 0x4:
                    ExecOpcodes4(opcode);
                    break;
                case 0x5:
                    ExecOpcodes5(opcode);
                    break;
                case 0x6:
                    ExecOpcodes6(opcode);
                    break;
                case 0x7:
                    ExecOpcodes7(opcode);
                    break;
                case 0x8:
                case 0xa:
                    ExecOpcodes8A(opcode);
                    break;
                case 0x9:
                    ExecOpcodes9(opcode);
                    break;
                case 0xb:
                    ExecOpcodesB(opcode);
                    break;
                case 0xc:
                    ExecOpcodesC(opcode);
                    break;
                case 0xd:
                    ExecOpcodesD(opcode);
                    break;
                case 0xe:
                    ExecOpcodesE(opcode);
                    break;
                case 0xf:
                    ExecOpcodesF(opcode);
                    break;
            }

            // traçage de l'exécution si besoin est
            if (this.traceFile != null) {
                DoTrace();
            }

            // comptage des cycles écoulés
            ulong cycEnd = this.cycles;
            return cycEnd - cycBegin;
        }

        /// <summary>
        /// Lance l'exécution du processeur pendant AU MOINS
        /// le nombre de cycles passé en paramètre.
        /// <br/>
        /// En effet : toute instruction entamée est terminée
        /// (y compris les éventuelles réponses aux interruptions).
        /// Ainsi, le nombre de cycles exécutés peut être égal ou
        /// supérieur au nombre voulu.
        /// </summary>
        /// <param name="numCycles">
        /// Nombre de cycles processeur à exécuter.
        /// </param>
        /// <returns>
        /// Le nombre de cycles processeur réellement exécutés.
        /// </returns>
        public ulong Run(ulong numCycles)
        {
            ulong cycCount = 0L;

            while (cycCount < numCycles) {
                cycCount += Step();
                if (this.asleep) { break; }
            }

            return cycCount;
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
        /// Indique si le registre PC tient sur 3 octets (<code>true</code>,
        /// espace-mémoire programme de plus de 128 Ko), ou s'il tient
        /// sur 2 octets (<code>false</code>, espace-mémoire programme
        /// de 128 Ko maximum).
        /// <br/>
        /// (Propriété en lecture seule, définie à la création
        /// du processeur.)
        /// </summary>
        public Boolean HasLargePC
        {
            get { return this.largePC; }
        }


        /// <summary>
        /// Accès aux registres généraux du processeur.
        /// (Propriété "indexée".)
        /// </summary>
        public IndexedProperty<Int32, Byte> GeneralRegister { get; }

        /// <summary>
        /// Accès au "registre" d'adresse X (r27:r26) du processeur.
        /// </summary>
        public UInt16 RegisterX
        {
            get {
                return MakeWord(this.reg27, this.reg26);
            }
            set {
                this.reg26 = LoByte(value);
                this.reg27 = HiByte(value);
            }
        }

        /// <summary>
        /// Accès au "registre" d'adresse Y (r29:r28) du processeur.
        /// </summary>
        public UInt16 RegisterY
        {
            get {
                return MakeWord(this.reg29, this.reg28);
            }
            set {
                this.reg28 = LoByte(value);
                this.reg29 = HiByte(value);
            }
        }

        /// <summary>
        /// Accès au "registre" d'adresse Z (r31:r30) du processeur.
        /// </summary>
        public UInt16 RegisterZ
        {
            get {
                return MakeWord(this.reg31, this.reg30);
            }
            set {
                this.reg30 = LoByte(value);
                this.reg31 = HiByte(value);
            }
        }


        /// <summary>
        /// Accès au registre PC ("Program Counter", compteur programme
        /// alias compteur ordinal) du processeur.
        /// </summary>
        public Int32 RegisterPC
        {
            get { return this.regPC; }
            set { this.regPC = value; }
        }

        /// <summary>
        /// Accès au registre d'index SP ("Stack Ponter", pointeur de pile)
        /// du processeur.
        /// </summary>
        public UInt16 RegisterSP
        {
            get { return this.regSP; }
            set { this.regSP = value; }
        }

        /// <summary>
        /// Accès au registre S (registre d'état / de Statut)
        /// du processeur.
        /// </summary>
        public Byte RegisterS
        {
            // le contenu de ce registre est calculé à la volée
            // en fonction des "flags"
            get {
                byte p = 0x00;
                if (this.flagC) p |= FLAG_C;
                if (this.flagZ) p |= FLAG_Z;
                if (this.flagN) p |= FLAG_N;
                if (this.flagV) p |= FLAG_V;
                if (this.flagS) p |= FLAG_S;
                if (this.flagH) p |= FLAG_H;
                if (this.flagT) p |= FLAG_T;
                if (this.flagI) p |= FLAG_I;
                return p;
            }
            set {
                this.flagC = ((value & FLAG_C) != 0);
                this.flagZ = ((value & FLAG_Z) != 0);
                this.flagN = ((value & FLAG_N) != 0);
                this.flagV = ((value & FLAG_V) != 0);
                this.flagS = ((value & FLAG_S) != 0);
                this.flagH = ((value & FLAG_H) != 0);
                this.flagT = ((value & FLAG_T) != 0);
                this.flagI = ((value & FLAG_I) != 0);
            }
        }

        /// <summary>
        /// Flag I (Interruptions ACTIVES) dans le registre de statut
        /// du processeur.
        /// </summary>
        public Boolean FlagI
        {
            get { return this.flagI; }
            set { this.flagI = value; }
        }

        /// <summary>
        /// Flag T ("flag" uTilisateur) du registre de statut du processeur.
        /// </summary>
        public Boolean FlagT
        {
            get { return this.flagT; }
            set { this.flagT = value; }
        }

        /// <summary>
        /// Flag H ("Half-carry", demi-retenue, pour les calculs BCD)
        /// dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagH
        {
            get { return this.flagH; }
            set { this.flagH = value; }
        }

        /// <summary>
        /// Flag S (bit de Signe = N xor V) du registre de statut du
        /// processeur.
        /// </summary>
        public Boolean FlagS
        {
            get { return this.flagS; }
            set { this.flagS = value; }
        }

        /// <summary>
        /// Flag V ("oVerflow", débordement) dans le registre de statut
        /// du processeur.
        /// </summary>
        public Boolean FlagV
        {
            get { return this.flagV; }
            set { this.flagV = value; }
        }

        /// <summary>
        /// Flag N (Négatif) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagN
        {
            get { return this.flagN; }
            set { this.flagN = value; }
        }

        /// <summary>
        /// Flag Z (Zéro) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagZ
        {
            get { return this.flagZ; }
            set { this.flagZ = value; }
        }

        /// <summary>
        /// Flag C ("Carry", retenue) dans le registre de statut du processeur.
        /// </summary>
        public Boolean FlagC
        {
            get { return this.flagC; }
            set { this.flagC = value; }
        }


        /// <summary>
        /// Accès au registre EIND du processeur.
        /// </summary>
        public Byte RegisterEIND
        {
            get { return this.regEIND; }
            set { this.regEIND = value; }
        }

        /// <summary>
        /// Accès au registre RAMPX du processeur.
        /// </summary>
        public Byte RegisterRAMPX
        {
            get { return this.regRAMPX; }
            set { this.regRAMPX = value; }
        }

        /// <summary>
        /// Accès au registre RAMPY du processeur.
        /// </summary>
        public Byte RegisterRAMPY
        {
            get { return this.regRAMPY; }
            set { this.regRAMPY = value; }
        }

        /// <summary>
        /// Accès au registre RAMPX du processeur.
        /// </summary>
        public Byte RegisterRAMPZ
        {
            get { return this.regRAMPZ; }
            set { this.regRAMPZ = value; }
        }


        /// <summary>
        /// Indique si le processeur a été mis en sommeil (par
        /// l'instruction <code>SLEEP</code>, valeur <code>true</code>),
        /// ou s'il fonctionne activement (valeur <code>false</code>).
        /// </summary>
        public Boolean IsAsleep
        {
            get { return this.asleep; }
            set { this.asleep = value; }
        }


        /// <summary>
        /// Nombre de cycles écoulés lors du fonctionnement du processeur.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt64 ElapsedCycles
        {
            get { return this.cycles; }
        }


        /// <summary>
        /// Politique de prise en charge des opcodes invalides à l'exécution.
        /// </summary>
        public UnknownOpcodePolicy InvalidOpcodePolicy
        {
            get { return this.uoPolicy; }
            set { this.uoPolicy = value; }
        }


        /// <summary>
        /// Objet d'écriture dans le fichier de traçage
        /// à employer pour l'exécution de ce processeur.
        /// <br/>
        /// Mettre à <code>null</code> pour ne pas faire de trace.
        /// </summary>
        public StreamWriter TraceFileWriter
        {
            get { return this.traceFile; }
            set {
                if (this.traceFile != null) {
                    this.traceFile.Flush();
                }
                this.traceFile = value;
                if (this.traceFile != null) {
                    this.traceDisasm = new DisasmAVR8(this.memSpace) {
                        InvalidOpcodePolicy = this.uoPolicy
                    };
                } else {
                    this.traceDisasm = null;
                }
            }
        }

    }
}

