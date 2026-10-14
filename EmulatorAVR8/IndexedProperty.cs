using System;


namespace EmulatorAVR8
{
    /// <summary>
    /// Classe générique implantant une propriété indexée,
    /// c.-à-d. apparaissant comme un tableau à son utilisateur.
    /// </summary>
    /// <typeparam name="TIndex">
    /// Type d'indexation (ce qui va entre les crochets du "tableau").
    /// </typeparam>
    /// <typeparam name="TValue">
    /// Type de valeur (ce qui est stocké dans le "tableau").
    /// </typeparam>
    public class IndexedProperty<TIndex, TValue>
    {
        /* ========================== CHAMPS PRIVÉS ========================= */

        private readonly Action<TIndex, TValue> SetAction;
        private readonly Func<TIndex, TValue> GetFunction;


        /* ========================== CONSTRUCTEUR ========================== */

        /// <summary>
        /// Constructeur de référence (et unique) de la classe
        /// <code>IndexedProperty</code>.
        /// </summary>
        /// <param name="getFunc">
        /// Référence à la méthode permettant d'accéder au contenu du "tableau".
        /// </param>
        /// <param name="setAct">
        /// Référence à la méthode permettant de modifier le contenu du
        /// "tableau".
        /// </param>
        public IndexedProperty(Func<TIndex, TValue> getFunc,
                               Action<TIndex, TValue> setAct)
        {
            this.GetFunction = getFunc;
            this.SetAction = setAct;
        }


        /* ====================== PROPRIÉTÉS PUBLIQUES ====================== */

        /// <summary>
        /// Indexeur permettant d'accéder au contenu du "tableau".
        /// </summary>
        /// <param name="i">
        /// Index de la valeur voulue.
        /// </param>
        /// <returns>
        /// Valeur désignée par l'index <code>i</code>.
        /// </returns>
        public TValue this[TIndex i]
        {
            get { return GetFunction(i); }
            set { SetAction(i, value); }
        }
    }

}
