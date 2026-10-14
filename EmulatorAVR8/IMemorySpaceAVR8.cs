using System;


namespace EmulatorAVR8
{
    /// <summary>
    /// Interface définissant l'accès d'un processeur de la famille AVR8
    /// à l'espace mémoire qui lui est attaché.
    /// <br/>
    /// On rappelle que pour cette famille de processeurs, l'espace-mémoire
    /// est double (architecture dite "Harvard" :
    /// <ul>
    /// <li>L'espace-programme (ROM) composé de mots de 16 bits et
    /// dont les adresses peuvent aller jusqu'à 22 bits de long.</li>
    /// <li>L'espace-données, composé d'octets, qui inclut aussi
    /// (en plus de la mémoire RAM proprement dite)
    /// les périphériques et autres entrées / sorties.</li>
    /// </ul>
    /// </summary>
    public interface IMemorySpaceAVR8
    {
        /// <summary>
        /// Lit la valeur d'un mot de 16 bits en mémoire programme (ROM).
        /// </summary>
        /// <param name="address">Adresse-mémoire du mot à lire.</param>
        /// <returns>
        /// La valeur lue à l'adresse donnée.
        /// <br/>
        /// Renvoie <code>null</code> si l'adresse en question n'est pas
        /// accessible en lecture.
        /// </returns>
        UInt16? ReadProgramMemory(Int32 address);


        /// <summary>
        /// Lit la valeur d'un octet en mémoire RAM (ou en entrée de
        /// périphérique).
        /// </summary>
        /// <param name="address">Adresse-mémoire de l'octet à lire.</param>
        /// <returns>
        /// La valeur lue à l'adresse donnée.
        /// <br/>
        /// Renvoie <code>null</code> si l'adresse en question n'est pas
        /// accessible en lecture.
        /// </returns>
        Byte? ReadDataMemory(UInt16 address);

        /// <summary>
        /// Écrit la valeur d'un octet en mémoire RAM (ou en sortie
        /// de périphérique).
        /// </summary>
        /// <param name="address">Adresse-mémoire de l'octet à écrire.</param>
        /// <param name="value">Valeur de l'octet à écrire.</param>
        /// <returns>
        /// Renvoie <code>true</code> si l'écriture a réussi ;
        /// renvoie <code>false</code> en cas de problème (par exemple :
        /// si l'adresse en question n'est pas accessible en écriture).
        /// </returns>
        Boolean WriteDataMemory(UInt16 address, Byte value);
    }
}


