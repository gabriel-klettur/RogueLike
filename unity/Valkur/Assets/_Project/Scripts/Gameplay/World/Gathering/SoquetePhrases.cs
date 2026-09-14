namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The twenty things a woodcutter is told when a cut misses the target entirely.
    ///
    /// <para><b>A SHUFFLE BAG, NOT A DICE ROLL.</b> A random pick repeats the same line back to back
    /// one time in twenty and, over a bad minute, the player reads the same three insults and stops
    /// reading. A bag deals every line once before any line comes back, and never deals the last line
    /// of one round as the first of the next.</para>
    ///
    /// <para><b>ABOUT THE CUT, NEVER ABOUT THE PERSON'S IDENTITY.</b> The lines mock the swing, the
    /// rhythm and the tree's opinion of both. They are meant to sting and to be funny the tenth time.</para>
    /// </summary>
    public sealed class SoquetePhrases
    {
        [Valkur.Core.SelfHealingStatic("Immutable table of literal lines, never written after type initialisation.")]
        public static readonly string[] All =
        {
            "Hasta el árbol se está riendo de ti.",
            "¿Eso era un hachazo o una caricia?",
            "Mi abuela tala mejor. Con los ojos cerrados.",
            "El ritmo te busca y tú huyes de él.",
            "Enhorabuena: has inventado el antirritmo.",
            "Ese tronco morirá de viejo antes que de tu hacha.",
            "Un castor con resaca lo haría mejor.",
            "Suelta el hacha antes de que se haga daño.",
            "¿Estás talando o espantando moscas?",
            "Los leñadores del pueblo apostaban por ti. Ya no.",
            "Tienes el ritmo de una piedra cuesta arriba.",
            "El bosque entero siente vergüenza ajena.",
            "Eso no fue un corte, fue una rendición.",
            "Si fallar diera madera, serías rico.",
            "Hasta las hormigas llevan mejor el compás.",
            "Tu hacha ha pedido el traslado a otro leñador.",
            "Golpeaste el aire con muchísima convicción.",
            "Ni el viento falla tanto con este árbol.",
            "¿Pulsas al ritmo o al azar? No contestes.",
            "Soquete certificado. Enmárcalo.",
        };

        private readonly System.Random _rng;
        private readonly int[] _order;
        private int _next;
        private int _lastDealt = -1;

        public SoquetePhrases(int seed)
        {
            _rng = new System.Random(seed);
            _order = new int[All.Length];
            for (int i = 0; i < _order.Length; i++) _order[i] = i;
            _next = _order.Length;
        }

        /// <summary>The next line from the bag.</summary>
        public string Next()
        {
            if (_next >= _order.Length) Refill();
            _lastDealt = _order[_next++];
            return All[_lastDealt];
        }

        private void Refill()
        {
            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            // Never deal the last line of a round as the first of the next.
            if (_order.Length > 1 && _order[0] == _lastDealt)
                (_order[0], _order[_order.Length - 1]) = (_order[_order.Length - 1], _order[0]);
            _next = 0;
        }
    }
}
