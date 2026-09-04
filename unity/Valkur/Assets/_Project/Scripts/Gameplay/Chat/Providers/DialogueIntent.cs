using System;
using System.Globalization;
using System.Text;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// What the player appears to be doing with a line of chat.
    ///
    /// Coarse on purpose. This is not language understanding and must not pretend to be:
    /// it exists so an offline NPC answers a haggle with a haggling line instead of the
    /// next entry in a list, and every branch it can take leads to text a human authored
    /// for that character. <see cref="Unknown"/> is the honest majority case and falls
    /// back to the persona's ordinary repertoire.
    /// </summary>
    public enum DialogueIntent
    {
        Unknown = 0,
        Greeting,
        Farewell,
        Trade,
        SmallTalk,
        Joke,

        // The four below are about how the player TREATED the character rather than about
        // what they wanted from it. They exist because a face needs a cause: without them
        // an NPC has no way to be insulted, flattered, alarmed or moved, and six of the
        // ten drawn expressions can never appear no matter what the player types.
        /// <summary>The player was rude to this character.</summary>
        Insult,
        /// <summary>The player flattered or teased this character.</summary>
        Flirt,
        /// <summary>The player reported something frightening happening out there.</summary>
        Danger,
        /// <summary>The player shared bad news or their own unhappiness.</summary>
        Distress,
    }

    /// <summary>
    /// Classifies a player line into a <see cref="DialogueIntent"/> by keyword.
    ///
    /// <para>Both Spanish and English are matched, because the language toggle is per NPC
    /// and a player may type in either regardless of it. Matching is accent- and
    /// case-insensitive — "cuánto" and "cuanto" are the same word to a person, and an NPC
    /// that answers only one of them reads as broken.</para>
    ///
    /// <para>These keywords are RECOGNITION data, not displayed text: nothing here is ever
    /// shown to the player, so they stay out of the localization tables on purpose. What
    /// the NPC says back always comes from its own authored persona.</para>
    /// </summary>
    [Valkur.Core.SelfHealingStatic(
        "Nine immutable keyword tables built once from string literals. Nothing writes to " +
        "them after the static initialiser, they hold no Unity object and no decision made " +
        "during a session, so they cannot go stale across a Play-mode boundary. Declared on " +
        "the class rather than once per field, since every static here is of that " +
        "one kind.")]
    public static class DialogueIntentClassifier
    {
        // Ordered by specificity: a line can hit several sets, and the first match wins.
        // Trade before Greeting, because "hola, ¿cuánto vale esto?" is a customer, not a
        // passer-by, and answering it with small talk is the more annoying failure.
        // Written WITHOUT accents on purpose: Normalize strips diacritics before matching,
        // so an accented needle here could never fire. Matching is whole-word, so short
        // entries are safe.
        private static readonly string[] InsultWords =
        {
            "ladrona", "ladron", "estafadora", "estafador", "estafa", "timo", "tramposa",
            "tramposo", "asquerosa", "fea", "inutil", "idiota", "estupida", "estupido",
            "bruja", "callate", "basura", "no sirves", "me estas robando",
            "thief", "scam", "cheat", "ugly", "stupid", "idiot", "shut up", "useless",
        };

        private static readonly string[] DangerWords =
        {
            "peligro", "peligroso", "monstruo", "monstruos", "bandidos", "lobos",
            "me atacaron", "nos atacaron", "ataque", "guerra", "dragon", "muertos",
            "no vayas", "ten cuidado", "hay algo ahi fuera",
            "danger", "monsters", "bandits", "attacked", "war", "beware",
        };

        private static readonly string[] DistressWords =
        {
            "estoy triste", "me siento mal", "estoy solo", "estoy sola", "perdi",
            "se murio", "murio", "necesito ayuda", "no tengo nada", "estoy arruinado",
            "estoy herido", "estoy herida", "me duele", "lo he perdido todo",
            "i am sad", "i lost", "died", "i need help", "i am alone", "it hurts",
        };

        private static readonly string[] FlirtWords =
        {
            "guapa", "guapo", "bonita", "hermosa", "linda", "preciosa", "me gustas",
            "te quiero", "eres adorable", "que ojos", "mi amor", "bombon", "encantadora",
            "beautiful", "pretty", "cute", "i like you", "gorgeous", "darling",
        };

        private static readonly string[] TradeWords =
        {
            "cuanto", "precio", "vale", "cuesta", "comprar", "vender", "venta", "descuento",
            "rebaja", "barato", "caro", "oro", "monedas", "trato", "negociar", "regatear",
            "price", "cost", "buy", "sell", "discount", "cheap", "expensive", "gold", "deal",
            "trade", "haggle",
        };

        private static readonly string[] FarewellWords =
        {
            "adios", "hasta luego", "hasta pronto", "me voy", "nos vemos", "chao", "cuidate",
            "bye", "goodbye", "see you", "later", "farewell",
        };

        private static readonly string[] GreetingWords =
        {
            "hola", "buenas", "buenos dias", "buenas tardes", "buenas noches", "saludos",
            "que tal", "como estas", "como te va",
            "hello", "hi", "hey", "good morning", "good evening", "how are you",
        };

        private static readonly string[] JokeWords =
        {
            "chiste", "broma", "gracioso", "risa", "reir", "divertido",
            "joke", "funny", "laugh",
        };

        private static readonly string[] SmallTalkWords =
        {
            "cuentame", "cuenta", "hablame", "que haces", "quien eres", "tu vida", "historia",
            "pueblo", "bosque", "tiempo", "clima", "novedades",
            "tell me", "who are you", "what do you do", "your life", "story", "weather", "news",
        };

        /// <summary>
        /// The intent of <paramref name="playerText"/>. Never throws; blank input is
        /// <see cref="DialogueIntent.Unknown"/>.
        /// </summary>
        public static DialogueIntent Classify(string playerText)
        {
            if (string.IsNullOrWhiteSpace(playerText)) return DialogueIntent.Unknown;

            string normalized = Normalize(playerText);

            // The four emotional intents are tested FIRST, and deliberately ahead of Trade.
            // Their keyword sets are narrow and distinctive while the trade set is broad and
            // ordinary, so a collision is far likelier to be a real insult inside a haggle
            // than a real haggle inside an insult — and of the two, answering "cuanto vale,
            // ladrona" with a price and a smile is the worse failure.
            if (ContainsAny(normalized, InsultWords)) return DialogueIntent.Insult;
            if (ContainsAny(normalized, DangerWords)) return DialogueIntent.Danger;
            if (ContainsAny(normalized, DistressWords)) return DialogueIntent.Distress;
            if (ContainsAny(normalized, FlirtWords)) return DialogueIntent.Flirt;

            if (ContainsAny(normalized, TradeWords)) return DialogueIntent.Trade;
            if (ContainsAny(normalized, FarewellWords)) return DialogueIntent.Farewell;
            if (ContainsAny(normalized, JokeWords)) return DialogueIntent.Joke;
            if (ContainsAny(normalized, GreetingWords)) return DialogueIntent.Greeting;
            if (ContainsAny(normalized, SmallTalkWords)) return DialogueIntent.SmallTalk;

            return DialogueIntent.Unknown;
        }

        /// <summary>
        /// Lowercases, strips diacritics, turns every non-alphanumeric run into a single
        /// space and wraps the result in spaces.
        ///
        /// <para>Internal rather than private because <see cref="ExpressionClassifier"/>
        /// matches keywords the same way and a second copy of this would be two normalisers
        /// drifting apart — one accepting "cuánto" and the other not, with nothing failing.</para>
        ///
        /// The diacritic pass is why "¿Cuánto?" and "cuanto" match the same keyword.
        /// The space-wrapping is what makes the match a WORD match: plain substring
        /// matching on a two-letter needle like "hi" fires inside "this" and "machine", and
        /// "vale" inside a name, so an NPC would answer a greeting to someone asking about
        /// the weather. Costs one allocation per line the player types.
        /// </summary>
        internal static string Normalize(string text)
        {
            string lowered = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(lowered.Length + 2);
            sb.Append(' ');
            bool lastWasSpace = true;

            foreach (char c in lowered)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            if (!lastWasSpace) sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>
        /// True when any needle appears in <paramref name="haystack"/> as a whole word (or
        /// whole phrase). <paramref name="haystack"/> must already be space-wrapped by
        /// <see cref="Normalize"/>.
        /// </summary>
        internal static bool ContainsAny(string haystack, string[] needles)
        {
            foreach (string needle in needles)
            {
                if (haystack.IndexOf(" " + needle + " ", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
