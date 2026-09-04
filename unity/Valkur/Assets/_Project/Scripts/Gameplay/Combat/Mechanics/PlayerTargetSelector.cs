using System;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Click-to-target. Clicking an NPC LOCKS it as the player's target and marks its
    /// silhouette yellow; clicking anywhere that is not an NPC clears the lock.
    ///
    /// The hit-test is <see cref="MouseTargetDetector"/>'s — the same component that
    /// already drives the hover panel — so there is exactly ONE answer on screen to
    /// "which NPC is under the cursor". A second sweep here would be a second answer,
    /// and the two would disagree at the edges of a collider.
    ///
    /// The lock is deliberately NOT cleared by moving the cursor away: that is the
    /// whole difference between a hover and a target. It is dropped only by clicking
    /// elsewhere, or when the target dies, is deactivated or is destroyed — polled
    /// rather than subscribed, because a target can leave by any of four routes and
    /// only one of them raises an event.
    ///
    /// Left click also casts, and that is intended: clicking an enemy attacks it and
    /// targets it in the same gesture. The click is ignored over UI and while a
    /// runtime editor is open, exactly as the primary cast is.
    ///
    /// A DOUBLE click on the same NPC additionally works it — talks to Gatita, opens a
    /// vendor's counter — but only through
    /// <see cref="PlayerInteractionController.TryInteractWith"/>, never by calling the
    /// interactable itself. That method owns the suppression, the reachability rule and the
    /// session bookkeeping, and a gesture that reached past them would be a second interact
    /// key with none of its guarantees. Out of range the double click simply does nothing:
    /// the badge is the thing that says a target is workable, and it is already silent there.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerTargetSelector : MonoBehaviour
    {
        [Header("Highlight")]
        [SerializeField, Tooltip("Colour applied to the silhouette of the locked target.")]
        private Color targetColor = new Color(1f, 0.85f, 0.20f, 1f);

        [SerializeField, Tooltip("Outline thickness in world units (~2 px at PPU 32).")]
        private float outlineThickness = 0.06f;

        [Header("Double click")]
        [SerializeField, Tooltip("Longest gap between the two clicks of a double click, in seconds.")]
        private float doubleClickSeconds = 0.35f;

        [SerializeField, Tooltip("How far the cursor may travel between the two clicks, in SCREEN pixels. " +
                                 "Screen units rather than world so the gesture does not get stricter as the camera zooms in.")]
        private float doubleClickSlopPixels = 24f;

        private MouseTargetDetector _detector;
        private PlayerInteractionController _interaction;
        private EntitySilhouetteOutline _outline;

        private GameObject _target;
        private Health _targetHealth;

        private GameObject _lastClicked;
        private float _lastClickTime = float.NegativeInfinity;
        private Vector2 _lastClickScreenPos;

        /// <summary>The locked target, or null. Never a dead or destroyed entity.</summary>
        public GameObject CurrentTarget => _target;

        /// <summary>Fired when the locked target changes. Null means the lock was cleared.</summary>
        public event Action<GameObject> OnTargetChanged;

        private void Awake()
        {
            _detector = GetComponent<MouseTargetDetector>();
            if (_detector == null) _detector = gameObject.AddComponent<MouseTargetDetector>();
            _interaction = GetComponent<PlayerInteractionController>();
            EnsureOutline();
        }

        private void OnDisable() => ClearTarget();

        private void OnDestroy()
        {
            // The rig is unparented, so nothing else would ever take it down.
            if (_outline != null) Destroy(_outline.gameObject);
        }

        private void Update()
        {
            DropTargetIfGone();

            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;
            if (MouseInputManager.IsPointerOverUI()) return;
            if (IsSuppressed()) return;

            GameObject clicked = _detector != null ? _detector.CurrentTarget : null;
            bool isDouble = IsDoubleClickOn(clicked);
            RecordClick(clicked);

            SetTarget(clicked);
            if (isDouble) TryWorkTarget();
        }

        // Double click ---------------------------------------------------------------

        /// <summary>
        /// Whether this press completes a double click on <paramref name="clicked"/>.
        ///
        /// <para>Gated on the SAME entity rather than on time alone: clicking one villager and
        /// then their neighbour within a third of a second is two separate picks, and reading
        /// it as a double click would open a conversation the player never asked for. Null is
        /// never a double click — a double click on empty ground is two clears.</para>
        ///
        /// <para>The cursor-travel slop is what separates a double click from a click, a small
        /// drag and a second click: at 60 fps a moving hand covers real distance between the
        /// two presses, and without it a fast player working two targets in a crowd would
        /// trigger interactions they did not mean.</para>
        /// </summary>
        private bool IsDoubleClickOn(GameObject clicked)
        {
            if (clicked == null || clicked != _lastClicked) return false;
            if (Time.unscaledTime - _lastClickTime > doubleClickSeconds) return false;

            Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
            return (screenPos - _lastClickScreenPos).sqrMagnitude
                   <= doubleClickSlopPixels * doubleClickSlopPixels;
        }

        private void RecordClick(GameObject clicked)
        {
            _lastClicked = clicked;
            _lastClickScreenPos = MouseInputManager.GetScreenMousePosition();
            // Unscaled: a hit-stop or a slowed clock must not change how fast a player has to
            // click. Set AFTER the comparison above, never before it.
            _lastClickTime = Time.unscaledTime;
        }

        /// <summary>
        /// Run the locked target's own action, if it has one and the player can reach it.
        ///
        /// <para>The interactable is taken off the entity the CLICK resolved rather than
        /// looked up again by position. <c>InteractableRegistry.FindAt</c> would be the
        /// generic answer, but it tests the point against
        /// <see cref="IPlayerInteractable.InteractionBounds"/> — which for a character is the
        /// FOOTPRINT, not the sprite — so clicking a villager's head, the part of them the
        /// player actually aims at, would miss. Two searches would also be two answers that
        /// can disagree; the click already produced one.</para>
        /// </summary>
        private void TryWorkTarget()
        {
            if (_target == null) return;
            if (_interaction == null) _interaction = GetComponent<PlayerInteractionController>();
            if (_interaction == null) return;

            var interactable = _target.GetComponent<IPlayerInteractable>();
            if (interactable == null) return;

            _interaction.TryInteractWith(interactable);
        }

        /// <summary>Lock the given entity, or clear the lock when it is null or dead.</summary>
        public void SetTarget(GameObject target)
        {
            Health health = null;
            if (target != null)
            {
                health = target.GetComponent<Health>();
                if (health == null || health.IsDead) target = null;
            }

            if (_target == target) return;

            _target       = target;
            _targetHealth = target != null ? health : null;

            ApplyOutline();
            OnTargetChanged?.Invoke(_target);
        }

        public void ClearTarget() => SetTarget(null);

        private void DropTargetIfGone()
        {
            if (_target == null && _targetHealth == null) return;

            bool gone = _target == null
                     || !_target.activeInHierarchy
                     || _targetHealth == null
                     || _targetHealth.IsDead;

            if (gone) ClearTarget();
        }

        private void ApplyOutline()
        {
            EnsureOutline();
            if (_outline == null) return;

            if (_target == null)
            {
                _outline.Follow(null, null);
                return;
            }

            var sr = _target.GetComponent<SpriteRenderer>();
            if (sr == null) sr = _target.GetComponentInChildren<SpriteRenderer>();
            if (sr == null)
            {
                _outline.Follow(null, null);
                return;
            }

            _outline.Configure(targetColor, outlineThickness);
            _outline.Follow(_target.transform, sr);
        }

        private void EnsureOutline()
        {
            if (_outline != null) return;
            // Scene root on purpose — see EntitySilhouetteOutline's summary.
            var go = new GameObject("Target_SilhouetteOutline");
            _outline = go.AddComponent<EntitySilhouetteOutline>();
            _outline.Configure(targetColor, outlineThickness);
            _outline.SetVisible(false);
        }

        /// <summary>
        /// Same suppression <c>PlayerInteractionController</c> applies, and for the same two
        /// reasons: a runtime editor owns the world and several of them have text fields, and
        /// a blocked gameplay map means a modal — a conversation, the shop — is up, where a
        /// click belongs to that window and not to the world behind it.
        /// </summary>
        private static bool IsSuppressed()
        {
            if (InputBlocker.IsGameplayBlocked) return true;
            return GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive;
        }
    }
}
