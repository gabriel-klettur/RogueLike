using System;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The face the active character is currently making, and the one seam every source of
    /// it passes through.
    ///
    /// <para>ONE OWNER, like <c>SpriteTintStack</c> owns an entity's colour. The greeting,
    /// each generated reply, the wait while a remote model is thinking and the probe commands
    /// all write here and nothing writes the portrait directly, so there is exactly one
    /// answer to "what face is she making" and exactly one place a new source has to be
    /// added.</para>
    /// </summary>
    public partial class ChatSystem
    {
        /// <summary>
        /// What the active character's face is doing right now.
        /// <see cref="FacialExpression.Neutral"/> when no chat is open.
        /// </summary>
        public FacialExpression CurrentExpression { get; private set; } = FacialExpression.Neutral;

        /// <summary>
        /// Raised whenever <see cref="CurrentExpression"/> actually changes. Not raised for a
        /// write that sets the same face again — the panel crossfades on this, and fading a
        /// portrait into itself is a flicker with no cause the player can see.
        /// </summary>
        public event Action<FacialExpression> OnExpressionChanged;

        /// <summary>
        /// True while a probe is holding the face, which suppresses the conversation's own
        /// writes. Without it, an author running <c>face angry</c> to look at a drawing has
        /// it taken away by the next line of dialogue arriving behind them.
        /// </summary>
        public bool ExpressionOverridden { get; private set; }

        /// <summary>
        /// Moves the face. Ignored while a probe holds it.
        ///
        /// <para>Public because <c>ChatUI</c> is in the same assembly but the probe commands
        /// live in <c>DevConsole</c>, which reaches this the same way anything else does.
        /// </para>
        /// </summary>
        public void SetExpression(FacialExpression expression)
        {
            if (ExpressionOverridden) return;
            ApplyExpression(expression);
        }

        /// <summary>
        /// Holds <paramref name="expression"/> until <see cref="ReleaseExpressionOverride"/>,
        /// regardless of what the conversation does. The probe path, and only that.
        /// </summary>
        public void OverrideExpression(FacialExpression expression)
        {
            ExpressionOverridden = false;      // so the write below is not refused by the flag
            ApplyExpression(expression);
            ExpressionOverridden = true;
        }

        /// <summary>Hands the face back to the conversation, settling on Neutral.</summary>
        public void ReleaseExpressionOverride()
        {
            if (!ExpressionOverridden) return;
            ExpressionOverridden = false;
            ApplyExpression(FacialExpression.Neutral);
        }

        /// <summary>
        /// The face for a line that arrived without one — the persisted greeting, a replayed
        /// history entry, anything not produced by a provider this session.
        /// </summary>
        internal FacialExpression ClassifySpoken(string text, string playerText = null) =>
            ExpressionClassifier.Classify(text, DialogueIntentClassifier.Classify(playerText));

        private void ApplyExpression(FacialExpression expression)
        {
            if (CurrentExpression == expression) return;

            CurrentExpression = expression;
            VerboseLog.Log(VerboseLog.Category.Bootstrap,
                () => $"[ChatSystem] face -> {expression}");
            OnExpressionChanged?.Invoke(expression);
        }

        /// <summary>
        /// Drops the face back to Neutral and lets go of any probe hold. Called when a
        /// conversation ends: a face left on Angry would be the first thing the NEXT
        /// character shows, before their first line has been generated.
        /// </summary>
        private void ResetExpression()
        {
            ExpressionOverridden = false;
            ApplyExpression(FacialExpression.Neutral);
        }
    }
}
