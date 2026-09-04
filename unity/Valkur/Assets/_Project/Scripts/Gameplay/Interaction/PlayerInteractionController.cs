using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// The single reader of the interact key, and the one place that decides what a press of
    /// it means where the player is standing: work the seam, chop the tree, or talk to the
    /// person.
    ///
    /// <para><c>InputService.Gameplay.Interact</c> had ZERO readers in the whole project
    /// until this component. <c>BuildingDoor</c> says so in its own comment, and it is why
    /// <c>NPCInteractable.Interact()</c> was never called by anything either. The action was
    /// also bound to the same physical key as <c>SpellSlash</c>; that double binding is
    /// resolved in the input asset (slash moved to Z) rather than here.</para>
    ///
    /// <para>ONE reader, not one per interactable. Only one thing can be acted on at a time,
    /// so "which of the four trees I am standing between did I mean" has to be answered in a
    /// single place; giving each tree its own key reader would have all four answer yes.</para>
    ///
    /// <para>ORDER. A registered <see cref="IPlayerInteractable"/> in range wins over chat,
    /// and chat is the fallback rather than an entry in the registry because
    /// <see cref="ChatSystem.TryOpenChat"/> already owns its own proximity search over
    /// personas. Wrapping NPCs as interactables would run that search twice and let the two
    /// disagree. A harvest node radius is small and deliberate, so a villager standing inside
    /// one is a placement problem the player solves by stepping half a metre.</para>
    ///
    /// <para>Closing a conversation is deliberately NOT handled here. Chat open means the
    /// Gameplay action map is disabled and <see cref="InputBlocker"/> is engaged, so this
    /// component reads nothing at all while a conversation is up; Escape and Enter close it,
    /// and both are on <see cref="InputBlocker.IsAlwaysAllowedKey"/> for that reason.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInteractionController : MonoBehaviour
    {
        /// <summary>
        /// How far the player may drift from where a session started before it is cancelled.
        /// A session is "stand here and work", so the leash is what turns walking away into
        /// stopping. It is measured on POSITION rather than on the move input, which makes it
        /// immune to the recurring 2022.3 Editor InputSystem event-drop bug the same way every
        /// other poll in this project is. Generous enough that being nudged by a wandering NPC
        /// does not end a shift.
        /// </summary>
        private const float CANCEL_LEASH_WORLD = 0.35f;

        private InteractionPromptView _prompt;

        /// <summary>
        /// A target the player POINTED AT, which overrides the proximity pick for as long as
        /// it stays valid.
        ///
        /// <para>Proximity alone cannot express "that one", and standing among four things in
        /// range is the normal case rather than the edge — a shore with several shoals on the
        /// water, a forest. So a pointing gesture sets this, and everything else keeps working
        /// off whatever the prompt is currently showing.</para>
        ///
        /// <para>A sticky target that is never cleared is the same failure shape as a state
        /// locomotion refuses to override and nothing reverts, so it has FOUR exits and each
        /// is a different way of going stale: it leaves range, it stops offering itself (a
        /// spent seam), the session on it ends, or it is UNREGISTERED under the player — which
        /// is what happens the moment a Destroy node is felled and the stump leaves the
        /// registry. The fourth is the one that is easy to miss, because the object is still
        /// alive and still answers every question except whether it is still in the game.</para>
        /// </summary>
        private IPlayerInteractable _pointedTarget;

        private IPlayerInteractable _target;
        private IPlayerInteractable _session;
        private Vector2 _sessionAnchor;

        /// <summary>What the prompt is currently offering. Exposed for tests and diagnosis.</summary>
        public IPlayerInteractable CurrentTarget => _target;

        /// <summary>What is being worked right now, or null.</summary>
        public IPlayerInteractable ActiveSession => _session;

        private void Awake()
        {
            _prompt = InteractionPromptView.Create(transform);
        }

        private void OnDisable()
        {
            // Leaving the session running would leave a node mid-shift with nothing able to
            // stop it: the node cannot see the player, and this is the only thing that can.
            if (_session != null) EndSession();
            _pointedTarget = null;
            if (_prompt != null) _prompt.Hide();
        }

        private void Update()
        {
            if (IsSuppressed())
            {
                if (_session != null) EndSession();
                SetTarget(null);
                return;
            }

            Vector2 position = transform.position;

            if (_session != null && !SessionStillValid(position))
                EndSession();

            if (_pointedTarget != null && !PointedTargetStillValid(position))
                _pointedTarget = null;

            // Order: an ongoing session wins, then what the player pointed at, then whatever
            // is nearest. While a session runs the prompt names the way OUT of it, so the
            // player is never offered a key that would start something they are already doing.
            SetTarget(_session
                      ?? _pointedTarget
                      ?? InteractableRegistry.FindBest(gameObject, position));

            if (!WasInteractPressed()) return;

            if (_session != null) { EndSession(); return; }

            // A target may be VISIBLE and still refuse the key — a spent seam is showing its
            // countdown, not offering itself. Pressing there falls through to the conversation
            // branch rather than doing nothing, so the key is never simply swallowed.
            if (_target != null && _target.CanInteract(gameObject))
            {
                _session = _target;
                _sessionAnchor = position;
                _session.BeginInteraction(gameObject);
                return;
            }

            TryOpenConversation();
        }

        // Pointing -------------------------------------------------------------------

        /// <summary>
        /// Point at something, or clear the pointed target by passing null.
        ///
        /// <para>Public and gesture-free on purpose: WHICH input selects a target is a
        /// keybinding decision with real cost — every mouse button in this project already
        /// casts a spell — so the mechanism is separated from the gesture and the gesture is
        /// one call.</para>
        /// </summary>
        public void PointAt(IPlayerInteractable target)
        {
            _pointedTarget = target != null && target.CanInteract(gameObject) ? target : null;
        }

        /// <summary>What the player pointed at, if anything. A test and diagnosis seam.</summary>
        public IPlayerInteractable PointedTarget => _pointedTarget;

        private bool PointedTargetStillValid(Vector2 position)
            => IsReachable(_pointedTarget, position);

        /// <summary>
        /// Whether <paramref name="target"/> is still something the player could act on from
        /// <paramref name="position"/>. ONE predicate, shared by the pointed target's
        /// per-frame validity check and by <see cref="TryInteractWith"/>, so a gesture can
        /// never reach something the badge would have dropped a frame later.
        /// </summary>
        private bool IsReachable(IPlayerInteractable target, Vector2 position)
        {
            if (target == null) return false;

            // Unregistered is checked FIRST because it is the exit the object cannot report:
            // a felled tree is still a live C# object answering every other question.
            if (!InteractableRegistry.Contains(target)) return false;
            if (!target.DescribePrompt(gameObject).IsVisible) return false;

            Vector2 surface = target.InteractionBounds.ClosestPoint(position);
            float radius = target.InteractionRadius;
            return (surface - position).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Act on a target the player singled out with a GESTURE instead of with the key —
        /// today, a double click on an NPC. Returns whether the interaction actually started.
        ///
        /// <para>It goes through this component rather than calling
        /// <see cref="IPlayerInteractable.BeginInteraction"/> directly because everything that
        /// makes a press safe lives here and nowhere else: the editor/input-block suppression,
        /// the reachability rule, and the session bookkeeping that is the only thing able to
        /// stop a shift once it starts. A gesture that reached past all three would be a
        /// second, quieter interact key with none of its guarantees.</para>
        ///
        /// <para>The target is also POINTED AT before it is acted on, so the badge names the
        /// thing that is about to happen. Without that the player would read one name over the
        /// nearest villager and open a conversation with a different one — the exact
        /// disagreement <see cref="NPCConversationInteractable"/> exists to prevent.</para>
        /// </summary>
        public bool TryInteractWith(IPlayerInteractable target)
        {
            if (target == null || IsSuppressed()) return false;

            Vector2 position = transform.position;
            if (!IsReachable(target, position)) return false;
            if (!target.CanInteract(gameObject)) return false;

            // Ended BEFORE the pointed target is set: EndSession clears it, so the other order
            // would point at the new target and immediately forget it.
            if (_session != null) EndSession();

            _pointedTarget = target;
            SetTarget(target);

            _session = target;
            _sessionAnchor = position;
            _session.BeginInteraction(gameObject);
            return true;
        }

        // Session --------------------------------------------------------------------

        private bool SessionStillValid(Vector2 position)
        {
            if (_session == null) return false;
            if (!_session.IsInteracting) return false;
            if (!_session.CanInteract(gameObject)) return false;

            if ((position - _sessionAnchor).sqrMagnitude > CANCEL_LEASH_WORLD * CANCEL_LEASH_WORLD)
                return false;

            Vector2 surface = _session.InteractionBounds.ClosestPoint(position);
            float radius = _session.InteractionRadius;
            return (surface - position).sqrMagnitude <= radius * radius;
        }

        /// <summary>
        /// Drop the field BEFORE telling the interactable, so a cancel handler that ends up
        /// back here (a node that unregisters itself, then this component re-evaluating on the
        /// same frame) cannot see a session that is already over.
        /// </summary>
        private void EndSession()
        {
            var session = _session;
            _session = null;
            _pointedTarget = null;
            if (session != null) session.CancelInteraction();
        }

        // Conversation fallback --------------------------------------------------------

        private void TryOpenConversation()
        {
            // The shop is opened FROM a conversation, so a press while it is up is the player
            // trying to get back, not to start a second one.
            var shop = NPC.VendorShopUI.Instance;
            if (shop != null && shop.IsVisible) return;

            var chat = ChatSystem.Instance;
            if (chat == null || chat.IsChatOpen) return;

            chat.TryOpenChat(transform.position);
        }

        // Prompt -----------------------------------------------------------------------

        private void SetTarget(IPlayerInteractable next)
        {
            _target = next;

            if (_prompt == null) return;
            if (next == null) { _prompt.Hide(); return; }

            // The interactable owns every word of this, including whether it is offering
            // itself at all. Deciding here what a spent mine or a wrong tool should read would
            // put half the answer in the badge and half in the node, and the two would drift.
            _prompt.Show(next.InteractionBounds, next.DescribePrompt(gameObject));
        }

        // Input ------------------------------------------------------------------------

        /// <summary>
        /// True on the frame the interact key goes down, through both backends.
        ///
        /// The bound action is asked first because the binding is the source of truth and a
        /// player may have rebound it; the literal E is the legacy OR-gate every input read in
        /// this project carries, for the Unity 2022.3 event-drop bug.
        /// <see cref="KeyboardInputManager"/> already refuses while input is blocked, so the
        /// fallback cannot re-open a conversation that is already open.
        /// </summary>
        private static bool WasInteractPressed()
        {
            var action = InputService.Instance?.Gameplay?.Interact;
            if (action != null && action.WasPerformedThisFrame()) return true;
            return KeyboardInputManager.WasEPressedThisFrame();
        }

        /// <summary>
        /// A runtime editor owns the world while it is open, and several of them have text
        /// fields. Starting a shift or a conversation underneath one is never what the author
        /// meant. <c>GameEditorManager.AnyEditorActive</c> already answers this and nothing was
        /// asking it.
        /// </summary>
        private static bool IsSuppressed()
        {
            if (InputBlocker.IsGameplayBlocked) return true;
            return GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive;
        }
    }
}
