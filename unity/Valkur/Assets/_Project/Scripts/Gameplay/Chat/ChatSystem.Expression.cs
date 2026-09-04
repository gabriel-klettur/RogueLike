using System;
using UnityEngine;
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
        /// True while the character is the one being TALKED TO rather than the one talking.
        ///
        /// <para>THE FLOOR, NOT THE INPUT FIELD. It covers the player composing a line AND
        /// the wait for the reply, because from the player's side those are one continuous
        /// beat: you are still saying your thing until she starts saying hers. Driving it
        /// off the text box alone left a hole exactly at the handover — <c>ChatUI</c>
        /// submits before it clears the field, so the wait fell through to the TALKING axis
        /// while she had not said a word yet.</para>
        ///
        /// <para>A SECOND AXIS OVER THE SAME VOCABULARY, not nine more expressions. What a
        /// character is feeling and whether it is currently listening are independent: she
        /// can be amused while you type and amused while she answers, and those are two
        /// drawings of one mood. Folding listening into <see cref="FacialExpression"/>
        /// instead would double the enum, and — because that enum is the wire format the
        /// language model writes — would let a model emit "[listening_happy]" for a line it
        /// is in the middle of SAYING, which is not a state that exists.</para>
        /// </summary>
        public bool Listening => _playerTyping || _awaitingReply;

        /// <summary>Raised when <see cref="Listening"/> flips. Never for a repeat, for the
        /// same reason <see cref="OnExpressionChanged"/> is not.</summary>
        public event Action<bool> OnListeningChanged;

        /// <summary>
        /// How long she attends before a wait becomes visible DELIBERATION.
        ///
        /// <para>A wait is not a state of mind, and for most of its length it should not be
        /// drawn as one — she is taking in what you said, which is listening. But past a
        /// couple of seconds the silence stops reading as attention and starts reading as
        /// nothing happening, and at THAT point "she is thinking about your question" is
        /// both the honest description and the thing worth showing.</para>
        ///
        /// <para>Sized against the two providers rather than picked. The offline one — this
        /// project's default — answers synchronously and its first bubble is held back 0.5 s,
        /// so its whole wait is under a third of this and it never escalates: no thinking
        /// flash on "hola, ¿qué tal?", which is the case this was reported on. A remote model
        /// takes seconds, so it does.</para>
        /// </summary>
        private const float WAIT_THINKING_SECONDS = 1.5f;

        /// <summary>
        /// How long a visible thought is held before the answer comes out, even when the
        /// answer has been sitting ready the whole time.
        ///
        /// <para>Once she has VISIBLY started thinking, answering the instant the provider
        /// returns undoes the thought: the face says she is working on it and the reply
        /// arrives a frame later, which reads as the thinking pose having been a loading
        /// spinner — which is exactly what it used to be. Holding it makes the deliberation
        /// something that took time, because it did.</para>
        ///
        /// <para>It is a FLOOR under the escalation, not a fixed pause: a model that takes
        /// six seconds has already been thinking for four and a half by the time it returns,
        /// so nothing is added. It only ever delays a reply that arrived just after the
        /// escalation fired.</para>
        /// </summary>
        private const float THINKING_DWELL_SECONDS = 1.5f;

        private bool _playerTyping;
        private bool _awaitingReply;
        private float _awaitingSince;
        private bool _waitEscalated;
        private float _waitEscalatedAt;

        /// <summary>
        /// Reports whether the player is mid-sentence. Driven by the panel's input field; a
        /// chat with no panel never calls it and the flag stays false.
        /// </summary>
        public void SetPlayerTyping(bool typing)
        {
            if (_playerTyping == typing) return;

            bool was = Listening;
            _playerTyping = typing;

            // Her deliberation ends when you start talking to her again. Without this a
            // haggle leaves CurrentExpression on Thinking, so the very next keystroke shows
            // the eyes-aside pose with no wait behind it — which is the opposite of
            // attentive, and would make that drawing mean two different things depending on
            // how it was reached. Only Thinking is cleared: staying amused or cross while
            // you type is exactly the continuity the listening axis is for.
            if (typing && CurrentExpression == FacialExpression.Thinking)
                SetExpression(FacialExpression.Neutral);

            RaiseListeningIfChanged(was);
        }

        /// <summary>
        /// Marks the start of the wait for a reply. The face does NOT move here — that was
        /// the defect: it set Thinking outright, so every message in the game flashed the
        /// deliberating face for the half second before the first bubble, greetings
        /// included. The wait now shows her listening, and only becomes Thinking if it lasts
        /// (see <see cref="WAIT_THINKING_SECONDS"/>).
        /// </summary>
        internal void BeginAwaitingReply()
        {
            bool was = Listening;
            _awaitingReply = true;
            _awaitingSince = Time.time;
            _waitEscalated = false;
            RaiseListeningIfChanged(was);
        }

        /// <summary>
        /// Ends the wait. Called when the first bubble lands, and on every failure path —
        /// a cancelled provider, a thrown one, and the conversation closing — because a wait
        /// nothing ends leaves her listening to a player who is no longer typing.
        /// </summary>
        internal void EndAwaitingReply()
        {
            if (!_awaitingReply) return;

            bool was = Listening;
            _awaitingReply = false;
            _waitEscalated = false;
            RaiseListeningIfChanged(was);
        }

        /// <summary>
        /// Turns a long wait into visible thinking, once. Ticked from <c>Update</c>.
        ///
        /// <para>It writes the ordinary expression, so what appears is the LISTENING
        /// thinking pose — she is still attending, and now visibly working on it. The
        /// talking thinking face is reached only by a line that deliberates, which is the
        /// haggle reaction, and that separation is the whole point: one drawing means "she
        /// is taking her time over your question", the other means "she is weighing this
        /// deal out loud".</para>
        /// </summary>
        private void TickWaitEscalation()
        {
            if (!_awaitingReply || _waitEscalated) return;
            if (Time.time - _awaitingSince < WAIT_THINKING_SECONDS) return;

            _waitEscalated = true;
            _waitEscalatedAt = Time.time;
            SetExpression(FacialExpression.Thinking);
        }

        /// <summary>
        /// The earliest the first bubble may land, given how long she has been visibly
        /// thinking. <c>0</c> when she never started, which is every offline exchange.
        /// </summary>
        private float EarliestReplyTime =>
            _waitEscalated ? _waitEscalatedAt + THINKING_DWELL_SECONDS : 0f;

        private void RaiseListeningIfChanged(bool was)
        {
            if (Listening != was) OnListeningChanged?.Invoke(Listening);
        }

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
            SetPlayerTyping(false);
            EndAwaitingReply();
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
            SetPlayerTyping(false);
            EndAwaitingReply();
            ApplyExpression(FacialExpression.Neutral);
        }
    }
}
