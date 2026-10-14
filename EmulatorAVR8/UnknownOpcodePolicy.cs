namespace EmulatorAVR8
{
    /// <summary>
    /// Énumère les différentes politiques possibles en cas de rencontre
    /// d'un opcode non défini à l'exécution.
    /// </summary>
    public enum UnknownOpcodePolicy
    {
        /// Simuler une instruction NOP.
        DoNop,

        /// Lancer une UnknownOpcodeException.
        ThrowException,

        /// Tenter d'émuler le comportement du processeur physique.
        Emulate

    }

}
