using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using EmulatorAVR8;


namespace TestEmulatorAVR8
{
    /// <summary>
    /// Classe de test du désassembleur de code binaire AVR8.
    /// </summary>
    [TestClass]
    public class TestDisasmAVR8 : IMemorySpaceAVR8
    {
        /* =========================== CONSTANTES =========================== */

        /* ~~ Noms / chemins de fichiers ~~ */
        private const string ALL_OPCODES_BIN_FILE = "AVR8_All_Opcodes.bin";
        private const string DISASSEMBLY_TEXT_FILE = "AVR8_Disasm.txt";

        // nombre total d'opcodes AVR8
        private const int TOTAL_OPCODES_NB = 65536;   // 2 ^ 16
        // taille du fichier contenant l'intégralité des opcodes AVR8
        // (en mots de 16 bits)
        private const int ALL_OPCODES_WORD_SIZE = 65536 + 64 + 128;


        /* ========================== CHAMPS PRIVÉS ========================= */

        // espace-mémoire programme (ROM) émulé
        private ushort[] programSpace;


        /* ================= MÉTHODES PRIVÉES (UTILITAIRES) ================= */

        /* création du fichier contenant tous les opcodes AVR8 possibles
         * (méthode statique) */
        private static void CreateAllOpcodesBinaryFile(string filePath)
        {
            byte[] binBuf = new byte[ALL_OPCODES_WORD_SIZE * 2];
            int offset = 0;
            for (uint op = 0; op < TOTAL_OPCODES_NB; op++) {
                byte[] bv = BitConverter.GetBytes((ushort)op);
                Array.Copy(bv, 0, binBuf, offset, 2);
                offset += 2;
                if (DisasmAVR8.IsLongOpcode((ushort)op)) {
                    bv = BitConverter.GetBytes(0x5A5A);
                    Array.Copy(bv, 0, binBuf, offset, 2);
                    offset += 2;
                }
            }

            using (FileStream fs = File.Create(filePath)) {
                fs.Write(binBuf, 0, binBuf.Length);
                fs.Flush();
            }
        }


        /* charge le fichier des opcodes à désassembler */
        private void LoadAllOpcodesFile(string allOpcodesFilePath)
        {
            int fileSize = (int)(new FileInfo(allOpcodesFilePath).Length);
            byte[] fileContents = new byte[fileSize];
            using (FileStream fs = File.OpenRead(allOpcodesFilePath)) {
                fs.Read(fileContents, 0, fileSize);
            }

            this.programSpace = new ushort[fileSize / 2];
            for (int n = 0; n < this.programSpace.Length; n++) {
                this.programSpace[n] =
                        BitConverter.ToUInt16(fileContents, n * 2);
            }
        }


        /* ======================= MÉTHODES PUBLIQUES ======================= */

        /* ~~ Méthodes héritées (de IMemorySpaceAVR8) ~~ */

        public ushort? ReadProgramMemory(int address)
        {
            /* renvoie le mot voulu de l'espace-mémoire programme */
            return this.programSpace[address];
        }

        public byte? ReadDataMemory(ushort address)
        {
            /* inutile pour tester le désassembleur */
            return null;
        }

        public bool WriteDataMemory(ushort address, byte value)
        {
            /* inutile pour tester le désassembleur */
            return false;
        }

        /* ~~ Méthodes de test (= points d'entrée) ~~ */

        /// <summary>
        /// Teste le désassemblage de tous les opcodes AVR8 possibles.
        /// </summary>
        [TestMethod]
        public void TestAllOpcodes()
        {
            if (!(File.Exists(ALL_OPCODES_BIN_FILE))) {
                CreateAllOpcodesBinaryFile(ALL_OPCODES_BIN_FILE);
            }
            LoadAllOpcodesFile(ALL_OPCODES_BIN_FILE);
            GC.Collect();

            DisasmAVR8 disasm = new DisasmAVR8(this);
            string disassembly = disasm.DisassembleManyInstructionsAt(
                    0,
                    TOTAL_OPCODES_NB);
            using (StreamWriter sw = File.CreateText(DISASSEMBLY_TEXT_FILE))
            {
                sw.WriteLine(disassembly);
                sw.Flush();
            }
        }

    }
}


