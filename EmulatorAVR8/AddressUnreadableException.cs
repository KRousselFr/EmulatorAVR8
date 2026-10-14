using System;


namespace EmulatorAVR8
{
    /// <summary>
    /// Exception lancée quand une opération de lecture dans l'espace-mémoire
    /// échoue, bloquant ainsi une opération critique.
    /// </summary>
    class AddressUnreadableException : Exception
    {
        /* ========================= CHAMPS PRIVÉS ========================== */

        private readonly int addr;

        /* ========================= CONSTRUCTEURS ========================== */

        public AddressUnreadableException(Int32 address) : base()
        {
            this.addr = address;
        }

        public AddressUnreadableException(Int32 address, String message) : base(message)
        {
            this.addr = address;
        }

        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Adresse-mémoire n'ayant pu être lue.
        /// (Propriété en lecture seule.)
        /// </summary>
        public Int32 MemoryAddress
        {
            get { return this.addr; }
        }

    }
}

