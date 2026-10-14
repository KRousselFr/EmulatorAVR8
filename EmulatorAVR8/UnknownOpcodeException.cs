using System;


namespace EmulatorAVR8
{
    /// <summary>
    /// Exception lancée lorsqu'un opcode invalide est rencontré à l'exécution.
    /// </summary>
    public class UnknownOpcodeException : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;
        private readonly ushort code;

        /* ========================= CONSTRUCTEURS ========================== */

        public UnknownOpcodeException(Int32 address, UInt16 opcode) : base()
        {
            this.addr = address;
            this.code = opcode;
        }

        public UnknownOpcodeException(Int32 address, UInt16 opcode, String message) : base(message)
        {
            this.addr = address;
            this.code = opcode;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire où l'opcode invalide a été lu.
        /// (Propriété en lecture seule.)
        /// </summary>
        public Int32 MemoryAddress
        {
            get { return this.addr; }
        }

        /// <summary>
        /// Opcode invalide lu en mémoire.
        /// (Propriété en lecture seule.)
        /// </summary>
        public UInt16 Opcode
        {
            get { return this.code; }
        }

    }
}


