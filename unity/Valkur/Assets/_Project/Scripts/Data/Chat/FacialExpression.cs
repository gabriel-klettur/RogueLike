using System.Collections.Generic;

namespace Valkur.Data
{
    /// <summary>
    /// The faces a character can pull while talking.
    ///
    /// <para>CLOSED AND SHARED. The vocabulary is global; the ART is per character. Gatita
    /// ships nine drawings, another vendor may ship four, and defining the enum from
    /// whatever one character happens to have drawn breaks the second character that
    /// arrives. What lets a small set of drawings answer the whole vocabulary is
    /// <see cref="FacialExpressionFallback"/>, not a per-character enum.</para>
    ///
    /// <para>Values are appended, never renumbered: they are persisted in nothing today, but
    /// they ARE the wire format between the language model and the panel, and a reordered
    /// enum would silently change which face an unchanged prompt produces.</para>
    ///
    /// <para><see cref="Neutral"/> is 0 so <c>default</c> is a face every character has. A
    /// reply that carries no expression at all is the overwhelmingly common case — an
    /// offline line, a model that skipped the tag, a failure that fell through — and it must
    /// land on something showable rather than on a hole.</para>
    /// </summary>
    public enum FacialExpression
    {
        /// <summary>Nothing in particular. The resting face, and the last resort.</summary>
        Neutral = 0,

        /// <summary>Pleased. Warm, eyes closed or soft, smiling.</summary>
        Happy = 1,

        /// <summary>Openly amused. Bigger than <see cref="Happy"/> — mouth open, laughing.</summary>
        Laugh = 2,

        /// <summary>Weighing something. Also what is shown while a reply is being fetched.</summary>
        Thinking = 3,

        /// <summary>Cross. Brows down, mouth flat.</summary>
        Angry = 4,

        /// <summary>Teasing. Tongue out, mischief rather than joy.</summary>
        Playful = 5,

        /// <summary>Downcast. Inner brows up, mouth turned down.</summary>
        Sad = 6,

        /// <summary>Worn out. Half-lidded, unbothered.</summary>
        Tired = 7,

        /// <summary>Complicity. One eye shut, closed smile.</summary>
        Wink = 8,
    }

    /// <summary>
    /// What to show when a character has no drawing for the face that was asked for.
    ///
    /// <para>Without this, a character with four drawings shows nothing on five replies out
    /// of nine, and a blank portrait reads as a bug rather than as "this character has less
    /// art". It is the same job <c>EntityAnimationBinder</c> does for an animation state
    /// with no frames, and it answers the same way: show the nearest thing that exists, in
    /// the wrong INTENSITY rather than the wrong EMOTION.</para>
    ///
    /// <para>Which is why <see cref="FacialExpression.Angry"/> falls straight to
    /// <see cref="FacialExpression.Neutral"/> and never through <see cref="FacialExpression.Sad"/>:
    /// a smaller version of cross is blank-faced, but sad is a different claim about the
    /// character and the player would read it as one.</para>
    /// </summary>
    [Valkur.Core.SelfHealingStatic(
        "One immutable table of enum arrays, built once from literals in the static " +
        "initialiser. Nothing writes to it afterwards, it holds no Unity object and no " +
        "decision made during a session, so it cannot go stale across a Play-mode boundary.")]
    public static class FacialExpressionFallback
    {
        // Ordered nearest-first, always ending at Neutral. Each chain STARTS with the
        // expression itself so a caller can walk one list rather than special-casing the
        // exact match.
        private static readonly Dictionary<FacialExpression, FacialExpression[]> Chains =
            new Dictionary<FacialExpression, FacialExpression[]>
            {
                { FacialExpression.Neutral,  new[] { FacialExpression.Neutral } },
                { FacialExpression.Happy,    new[] { FacialExpression.Happy, FacialExpression.Neutral } },
                { FacialExpression.Laugh,    new[] { FacialExpression.Laugh, FacialExpression.Happy, FacialExpression.Neutral } },
                { FacialExpression.Wink,     new[] { FacialExpression.Wink, FacialExpression.Happy, FacialExpression.Neutral } },
                { FacialExpression.Playful,  new[] { FacialExpression.Playful, FacialExpression.Happy, FacialExpression.Neutral } },
                { FacialExpression.Thinking, new[] { FacialExpression.Thinking, FacialExpression.Neutral } },
                { FacialExpression.Sad,      new[] { FacialExpression.Sad, FacialExpression.Thinking, FacialExpression.Neutral } },
                { FacialExpression.Tired,    new[] { FacialExpression.Tired, FacialExpression.Thinking, FacialExpression.Neutral } },
                { FacialExpression.Angry,    new[] { FacialExpression.Angry, FacialExpression.Neutral } },
            };

        /// <summary>
        /// <paramref name="wanted"/> first, then progressively less specific faces, always
        /// ending at <see cref="FacialExpression.Neutral"/>. Never empty, never null — an
        /// enum value with no declared chain still resolves to itself and then Neutral, so
        /// adding a value to the enum degrades rather than throwing.
        /// </summary>
        public static IReadOnlyList<FacialExpression> Chain(FacialExpression wanted)
        {
            if (Chains.TryGetValue(wanted, out var chain)) return chain;
            return wanted == FacialExpression.Neutral
                ? new[] { FacialExpression.Neutral }
                : new[] { wanted, FacialExpression.Neutral };
        }

        /// <summary>Every value of the enum, in declaration order. Used by the probe commands and the tests.</summary>
        public static FacialExpression[] All =>
            (FacialExpression[])System.Enum.GetValues(typeof(FacialExpression));

        /// <summary>
        /// The enum value named by <paramref name="token"/>, case-insensitively, or false.
        ///
        /// Deliberately NOT <c>Enum.TryParse</c> alone: that accepts any INTEGER as a valid
        /// value, so a model answering "[3]" or a console typo of "7" would be taken as a
        /// real expression rather than refused.
        /// </summary>
        public static bool TryParse(string token, out FacialExpression expression)
        {
            expression = FacialExpression.Neutral;
            if (string.IsNullOrWhiteSpace(token)) return false;

            string trimmed = token.Trim();
            foreach (FacialExpression candidate in All)
            {
                if (string.Equals(candidate.ToString(), trimmed, System.StringComparison.OrdinalIgnoreCase))
                {
                    expression = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
