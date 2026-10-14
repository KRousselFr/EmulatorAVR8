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
        private const String ERR_UNREADABLE_ADDRESS =
                "Impossible de lire le contenu de l'adresse ${0:X5} !";
        private const String ERR_UNWRITABLE_ADDRESS =
                "Impossible d'écrire la valeur $1:X2 à l'adresse ${0:X5} !";
        private const String ERR_UNKNOWN_OPCODE =
                "Opcode invalide (${1:X4}) rencontré à l'adresse ${0:X5} !";

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





        // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!









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
            this.regPC = 0x0000;
            this.regSP = 0x0000;
            this.RegisterS = 0x00;
            this.cycles = 0L;
        }


        // TODO !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!




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

