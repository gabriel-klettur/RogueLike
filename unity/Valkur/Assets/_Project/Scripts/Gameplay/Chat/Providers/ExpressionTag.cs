using System;
using Valkur.Data;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// The channel a language model uses to say which face it is pulling: a bracketed
    /// expression name at the very start of the reply, stripped before anyone sees it.
    ///
    /// <para>WHY A TAG AND NOT A TOOL. The provider already gives the model one tool,
    /// <c>propose_trade</c>, and a tool call means "I want to DO something" — a model given a
    /// second one returns turns that are a tool call and no words at all, which for an emote
    /// is exactly backwards: the face is a property of the sentence, so it has to ride the
    /// channel the sentence is already on. A tag also costs about three tokens and degrades
    /// to nothing: a model that forgets it produces a reply that is simply untagged, and
    /// <see cref="ExpressionClassifier"/> answers instead.</para>
    ///
    /// <para>WHY IT IS STRIPPED IN THE PROVIDER. <c>ChatSystem</c> records the reply to
    /// memory and to the session log BEFORE breaking it into bubbles, so a tag surviving
    /// that far would be remembered as part of what the character said and would then be
    /// what the do-not-repeat check compares against. By the time a reply leaves a provider
    /// its text is what the character says out loud, and nothing else.</para>
    ///
    /// <para>The parse is deliberately narrow. An unknown word in brackets is NOT stripped:
    /// a character is allowed to say "[risas]" or to open with a bracketed aside, and
    /// swallowing that would silently eat words a human authored. Only a token that names a
    /// real <see cref="FacialExpression"/> is treated as a tag.</para>
    /// </summary>
    public static class ExpressionTag
    {
        /// <summary>What the prompt tells the model to write. Kept here so the instruction
        /// and the parser cannot drift apart.</summary>
        public const char OPEN = '[';
        public const char CLOSE = ']';

        /// <summary>
        /// Splits <paramref name="raw"/> into the face it declared and the words that remain.
        ///
        /// <para>Returns false and leaves <paramref name="text"/> as the trimmed input when
        /// there is no recognisable tag, which is the common case for every offline line and
        /// for any model turn that skipped it.</para>
        /// </summary>
        public static bool TryStrip(string raw, out FacialExpression expression, out string text)
        {
            expression = FacialExpression.Neutral;
            text = raw?.Trim() ?? string.Empty;
            if (text.Length < 3 || text[0] != OPEN) return false;

            int close = text.IndexOf(CLOSE);
            if (close <= 1) return false;

            string token = text.Substring(1, close - 1);
            if (!FacialExpressionFallback.TryParse(token, out expression))
            {
                expression = FacialExpression.Neutral;
                return false;
            }

            text = text.Substring(close + 1).TrimStart(' ', '\t', '\r', '\n', ':', '-', '—');
            return true;
        }

        /// <summary>
        /// The sentence handed to the model describing the tag and the vocabulary it may use.
        ///
        /// <para>Only the expressions this character can actually distinguish are listed.
        /// Offering a face the art cannot show does not break anything — the fallback chain
        /// absorbs it — but it spends tokens teaching the model a distinction the player will
        /// never see, and it invites a "laugh" that renders identically to every "happy".</para>
        /// </summary>
        public static string BuildInstruction(NPCPersonaDefinition persona)
        {
            var sb = new System.Text.StringBuilder(256);
            bool first = true;

            foreach (FacialExpression candidate in FacialExpressionFallback.All)
            {
                if (persona != null && !persona.HasOwnFace(candidate)) continue;
                sb.Append(first ? "" : ", ").Append(candidate.ToString().ToLowerInvariant());
                first = false;
            }

            // Nothing distinguishable — the caller should not have asked, but a persona whose
            // only art is the single fallback portrait lands here and must not emit a rule
            // with an empty list, which reads to a model as "choose from nothing".
            if (first) return null;

            return "Empieza SIEMPRE tu respuesta con la cara que pones, entre corchetes y en " +
                   "minusculas, seguida de un espacio. Elige solo entre: " + sb + ". " +
                   "Ejemplo: [" + FacialExpression.Neutral.ToString().ToLowerInvariant() +
                   "] Buenas, viajero. La etiqueta no es parte de lo que dices en voz alta, " +
                   "asi que no la menciones ni la repitas dentro de la frase.";
        }
    }
}
