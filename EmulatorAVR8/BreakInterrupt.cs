using System;


namespace EmulatorAVR8
{
    /// <summary>
    /// Exception représentant une interruption vers le débogueur AVR8
    /// (instruction <code>BREAK</code>).
    /// </summary>
    public class BreakInterrupt : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;

        /* ========================= CONSTRUCTEURS ========================== */

        public BreakInterrupt(Int32 addrBreak) : base()
        {
            this.addr = addrBreak;
        }

        public BreakInterrupt(Int32 addrBreak, String message) : base(message)
        {
            this.addr = addrBreak;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire où l'instruction <code>BREAK</code>
        /// a été rencontrée. (Propriété en lecture seule.)
        /// </summary>
        public Int32 MemoryAddress
        {
            get { return this.addr; }
        }

    }
}

