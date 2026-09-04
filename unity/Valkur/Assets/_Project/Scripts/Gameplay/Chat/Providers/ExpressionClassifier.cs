using Valkur.Data;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// The face that goes with a line the character just said, worked out from the line
    /// itself.
    ///
    /// <para>THE MIRROR OF <see cref="DialogueIntentClassifier"/>, AND THE FLOOR UNDER THE
    /// MODEL. That one reads what the PLAYER typed to choose a reply; this one reads what
    /// the CHARACTER said to choose a face. It is what answers when there is no language
    /// model at all — the default provider is offline — and what answers when an online
    /// model skips its tag, so every reply in the game has a face whether or not anything
    /// remote was reachable.</para>
    ///
    /// <para>Coarse on purpose, exactly as its sibling is. It is not sentiment analysis and
    /// must not pretend to be: every branch leads to a drawing a human made for this
    /// character, and <see cref="FacialExpression.Neutral"/> is the honest majority answer.
    /// </para>
    ///
    /// <para>Ordered first-match rather than scored, and the ORDER carries the design. The
    /// warm set is tested before the pensive one so a greeting that ends in a question mark
    /// reads as a greeting rather than as deliberation — a question mark is the commonest
    /// character in friendly dialogue, and scoring it would make almost every line
    /// pensive.</para>
    /// </summary>
    [Valkur.Core.SelfHealingStatic(
        "Eight immutable keyword tables built once from string literals, plus eight const " +
        "emoji strings. Nothing writes to them after the static initialiser, they hold no " +
        "Unity object and no decision made during a session, so they cannot go stale across " +
        "a Play-mode boundary. Declared on the class rather than eight times on the fields, " +
        "since every static here is of that one kind — the same shape and the same reason as " +
        "DialogueIntentClassifier, whose tables this one sits beside.")]
    public static class ExpressionClassifier
    {
        // Emoji are checked first and separately, BEFORE normalisation — which turns every
        // non-alphanumeric run into a space and would erase them. They are the strongest
        // signal available and the only one that works whatever language the reply is in.
        // Written as code-point escapes rather than literal glyphs so the meaning survives
        // any re-encoding of this file.
        private const string LaughEmoji = "\U0001F602\U0001F923\U0001F606\U0001F605";
        private const string HappyEmoji = "\U0001F60A\U0001F642\U0001F60D❤\U0001F495\U0001F338";
        private const string AngryEmoji = "\U0001F620\U0001F621\U0001F624\U0001F92C";
        private const string SadEmoji = "\U0001F622\U0001F62D\U0001F614\U0001F61E";
        private const string TiredEmoji = "\U0001F634\U0001F62A\U0001F971";
        private const string WinkEmoji = "\U0001F609";
        private const string PlayEmoji = "\U0001F61C\U0001F61B\U0001F61D\U0001F92A";
        private const string ThinkEmoji = "\U0001F914\U0001F928";

        private static readonly string[] LaughWords =
        {
            "jaja", "jajaja", "jeje", "jejeje", "jiji", "ja ja",
            "haha", "hahaha", "hehe", "lol", "que risa", "me parto", "muerta de risa",
        };

        private static readonly string[] AngryWords =
        {
            "basta", "largo de aqui", "ni hablar", "no me toques", "ladron", "sinverguenza",
            "descarado", "fuera de mi puesto", "insolente", "no me hagas perder el tiempo",
            "enfadada", "enfadado", "harta", "harto",
            "enough", "get out", "how dare", "thief", "no way",
        };

        private static readonly string[] SadWords =
        {
            "lo siento", "que pena", "una lastima", "me da pena", "triste", "duele",
            "ojala pudiera", "se me murio", "lo perdi",
            "sorry", "sadly", "a shame", "i lost",
        };

        private static readonly string[] TiredWords =
        {
            "cansada", "cansado", "agotada", "agotado", "no puedo mas", "muerta de sueno",
            "rendida", "rendido", "vaya dia",
            "tired", "exhausted", "long day", "worn out",
        };

        private static readonly string[] PlayfulWords =
        {
            "picarona", "picaron", "travieso", "traviesa", "anda ya", "que morro",
            "menudo eres", "no seas asi",
            "cheeky", "you rascal", "naughty",
        };

        private static readonly string[] WinkWords =
        {
            "entre tu y yo", "que no se entere", "es un secreto", "tu y yo sabemos",
            "no se lo digas", "guino", "solo para ti", "precio de vecina", "por ser tu",
            "between you and me", "our little secret", "just for you",
        };

        private static readonly string[] HappyWords =
        {
            "bienvenida", "bienvenido", "gracias", "me alegro", "que alegria", "encantada",
            "encantado", "hola", "buenas", "que gusto", "un placer", "estupendo", "genial",
            "perfecto", "claro que si", "por supuesto", "buen provecho",
            "welcome", "thank you", "thanks", "glad", "lovely", "wonderful", "of course",
            "delighted", "my pleasure",
        };

        private static readonly string[] ThinkingWords =
        {
            "dejame ver", "a ver", "veamos", "mmm", "hmm", "no se", "quiza", "quizas",
            "tal vez", "puede que", "depende", "habria que", "dejame pensar",
            "let me see", "let me think", "maybe", "perhaps", "depends", "not sure",
        };

        /// <summary>
        /// The face for <paramref name="npcText"/>.
        ///
        /// <para><paramref name="playerIntent"/> is a weak PRIOR, consulted only when the
        /// words themselves said nothing — a character answering a request for a joke is
        /// probably amused even when the punchline holds no keyword, and one answering a
        /// haggle is probably weighing it. It never overrides a signal found in the text,
        /// because what the character actually said is better evidence than what it was
        /// asked.</para>
        /// </summary>
        public static FacialExpression Classify(
            string npcText, DialogueIntent playerIntent = DialogueIntent.Unknown)
        {
            if (string.IsNullOrWhiteSpace(npcText)) return FromIntent(playerIntent);

            if (ContainsAnyChar(npcText, LaughEmoji)) return FacialExpression.Laugh;
            if (ContainsAnyChar(npcText, AngryEmoji)) return FacialExpression.Angry;
            if (ContainsAnyChar(npcText, SadEmoji)) return FacialExpression.Sad;
            if (ContainsAnyChar(npcText, TiredEmoji)) return FacialExpression.Tired;
            if (ContainsAnyChar(npcText, WinkEmoji)) return FacialExpression.Wink;
            if (ContainsAnyChar(npcText, PlayEmoji)) return FacialExpression.Playful;
            if (ContainsAnyChar(npcText, ThinkEmoji)) return FacialExpression.Thinking;
            if (ContainsAnyChar(npcText, HappyEmoji)) return FacialExpression.Happy;

            string normalized = DialogueIntentClassifier.Normalize(npcText);

            if (DialogueIntentClassifier.ContainsAny(normalized, LaughWords)) return FacialExpression.Laugh;
            if (DialogueIntentClassifier.ContainsAny(normalized, AngryWords)) return FacialExpression.Angry;
            if (DialogueIntentClassifier.ContainsAny(normalized, SadWords)) return FacialExpression.Sad;
            if (DialogueIntentClassifier.ContainsAny(normalized, TiredWords)) return FacialExpression.Tired;
            if (DialogueIntentClassifier.ContainsAny(normalized, WinkWords)) return FacialExpression.Wink;
            if (DialogueIntentClassifier.ContainsAny(normalized, PlayfulWords)) return FacialExpression.Playful;
            if (DialogueIntentClassifier.ContainsAny(normalized, HappyWords)) return FacialExpression.Happy;
            if (DialogueIntentClassifier.ContainsAny(normalized, ThinkingWords)) return FacialExpression.Thinking;

            return FromIntent(playerIntent);
        }

        /// <summary>
        /// The face implied by what the player was doing, for a reply whose own words gave
        /// nothing away. <see cref="FacialExpression.Neutral"/> for the majority.
        /// </summary>
        private static FacialExpression FromIntent(DialogueIntent intent)
        {
            switch (intent)
            {
                case DialogueIntent.Joke: return FacialExpression.Laugh;
                case DialogueIntent.Greeting: return FacialExpression.Happy;
                case DialogueIntent.Farewell: return FacialExpression.Happy;
                case DialogueIntent.Trade: return FacialExpression.Thinking;
                default: return FacialExpression.Neutral;
            }
        }

        /// <summary>
        /// True when any code point of <paramref name="set"/> appears in
        /// <paramref name="text"/>.
        ///
        /// Surrogate pairs are matched on their LOW half alone. That is exact for the sets
        /// here and avoids walking the string once per emoji, but it is a property of THESE
        /// sets rather than of the technique: two emoji sharing a low surrogate would
        /// cross-match. <c>ExpressionClassifierTests</c> pins it by asserting every set
        /// matches only itself, so a set that grows fails loudly instead of quietly
        /// answering with the wrong face.
        /// </summary>
        private static bool ContainsAnyChar(string text, string set)
        {
            foreach (char c in set)
            {
                if (char.IsHighSurrogate(c)) continue;
                if (text.IndexOf(c) >= 0) return true;
            }
            return false;
        }
    }
}
