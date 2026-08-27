using System.Collections;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Orchestrates the player's death-and-revive flow:
    ///
    ///   ALIVE → DYING (≈0.6s drama beat) → SPIRIT (walk to altar)
    ///         → REVIVING (≈1s grayscale fade-out) → ALIVE
    ///
    /// Subscribes to <c>GameEvents.OnPlayerDied</c> to enter DYING. The
    /// resurrection altar (<see cref="World.ResurrectionZone"/>) calls
    /// <see cref="Revive"/> when the spirit walks into its footprint. The
    /// DevConsole calls <see cref="ForceRevive"/> for the cheat path.
    ///
    /// This controller is the only place that mutates layer masks, the
    /// grayscale volume weight, and the corpse lifetime — keeping the
    /// state machine in a single file prevents the half-applied transition
    /// states that plagued the older DeathScreenUI / GrayscaleDeath split.
    /// </summary>
    public class DeathSequenceController : MonoBehaviour
    {
        public enum Phase
        {
            Alive,
            Dying,
            Spirit,
            Reviving,
        }

        [Header("Tuning")]
        [SerializeField] private float dyingFlashDuration = 0.6f;
        [SerializeField] private float grayscaleFadeIn    = 1.5f;
        [SerializeField] private float grayscaleFadeOut   = 1.0f;

        private GrayscaleVolumeController _grayscale;
        private PlayerCorpseMarker _activeCorpse;
        private Coroutine _activeCoroutine;

        // Layers we exclude from the player collider while in spirit form so
        // NPCs / projectiles / pickups can't physically interact with the
        // ghost. Captured in Awake to avoid re-querying every transition.
        private LayerMask _spiritExcludeLayers;
        private LayerMask _savedExcludeLayers;
        private bool _excludeCaptured;

        public Phase CurrentPhase { get; private set; } = Phase.Alive;
        public bool IsDeathFlowActive => CurrentPhase != Phase.Alive;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void Awake()
        {
            _spiritExcludeLayers = BuildSpiritExcludeMask();
            ServiceLocator.Register<DeathSequenceController>(this);
        }

        private void OnEnable()
        {
            GameEvents.OnPlayerDied += OnPlayerDied;
        }

        private void OnDisable()
        {
            GameEvents.OnPlayerDied -= OnPlayerDied;
        }

        /// <summary>
        /// Safety net for the case where <c>GameEvents.OnPlayerDied</c> fires
        /// before this controller has subscribed (mid-Play recompile, scene
        /// transition that called <c>GameEvents.Clear()</c> and re-spawned the
        /// controller after Health). Polls Player health every frame; if the
        /// player is HP=0 but we haven't started the death flow, dispatch it
        /// manually. Cheap — one GetComponent per frame on a known GameObject.
        /// </summary>
        private void Update()
        {
            if (CurrentPhase != Phase.Alive) return;
            if (_activeCoroutine != null) return;

            var player = EntityRegistry.Player;
            if (player == null) return;
            var health = player.GetComponent<Health>();
            if (health == null || !health.IsDead) return;

            // Health says dead but our flow never started → we missed the
            // event. Kick it off manually.
            Debug.Log("[DeathSequence] Detected HP=0 player without an active death flow — recovering by dispatching DeathRoutine manually.");
            _activeCoroutine = StartCoroutine(DeathRoutine());
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<DeathSequenceController>() == this)
                ServiceLocator.Unregister<DeathSequenceController>();
        }

        // ── Public API ──────────────────────────────────────────────────────────

        public void BindGrayscaleController(GrayscaleVolumeController controller)
        {
            _grayscale = controller;
        }

        /// <summary>Trigger the slow revive sequence (used by ResurrectionZone).</summary>
        public void Revive()
        {
            if (CurrentPhase != Phase.Spirit) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(ReviveRoutine(instant: false));
        }

        /// <summary>
        /// Skip the spirit phase entirely (DevConsole `resurrect`). Snaps grayscale
        /// to 0 and restores the player without waiting on fades.
        /// </summary>
        public void ForceRevive()
        {
            if (CurrentPhase == Phase.Alive) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(ReviveRoutine(instant: true));
        }

        // ── Internal flow ───────────────────────────────────────────────────────

        private void OnPlayerDied()
        {
            if (CurrentPhase != Phase.Alive) return;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            CurrentPhase = Phase.Dying;

            var player = EntityRegistry.Player;
            if (player == null)
            {
                Debug.LogWarning("[DeathSequence] OnPlayerDied with no player in EntityRegistry; aborting.");
                CurrentPhase = Phase.Alive;
                yield break;
            }

            Vector3 deathPos = player.transform.position;

            PlayerDeathDropSystem.DropEverything(player);
            _activeCorpse = PlayerCorpseMarker.Spawn(deathPos);

            if (_grayscale != null) _grayscale.FadeIn(grayscaleFadeIn);

            // Brief pause before the spirit becomes controllable.
            float t = 0f;
            while (t < dyingFlashDuration)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            EnterSpirit(player);
            CurrentPhase = Phase.Spirit;
            _activeCoroutine = null;
        }

        private IEnumerator ReviveRoutine(bool instant)
        {
            CurrentPhase = Phase.Reviving;
            var player = EntityRegistry.Player;

            if (instant)
            {
                if (_grayscale != null) _grayscale.SetWeight(0f);
            }
            else
            {
                if (_grayscale != null) _grayscale.FadeOut(grayscaleFadeOut);
                float t = 0f;
                while (t < grayscaleFadeOut)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (player != null) ExitSpirit(player);

            if (_activeCorpse != null)
            {
                _activeCorpse.Despawn();
                _activeCorpse = null;
            }

            // Getting back up, after the body is solid again and the corpse is gone, so the
            // player sees themselves rise rather than rise next to their own corpse. Skipped
            // on the instant path: ForceRevive is the DevConsole cheat, where waiting out an
            // animation is the opposite of what was asked for.
            if (!instant && player != null)
            {
                var controller = player.GetComponent<PlayerController>();
                if (controller != null)
                {
                    // Length of the actual animation, not a constant: the elven rise is eight
                    // frames and a character with no recover art falls back to a one-frame
                    // idle, which must not hold locomotion for a fixed fifth of a second.
                    float duration = ResolveRecoverDuration(player);
                    if (duration > 0f)
                    {
                        controller.PlayRecoverAnimation(duration);
                        float elapsed = 0f;
                        while (elapsed < duration)
                        {
                            elapsed += Time.unscaledDeltaTime;
                            yield return null;
                        }
                    }
                }
            }

            CurrentPhase = Phase.Alive;
            _activeCoroutine = null;

            // Fire BOTH the legacy event (DeathScreenUI / DevConsole listeners)
            // and the new canonical revive event so newer subscribers can
            // distinguish "real revive" from "DevConsole instant resurrect".
            GameEvents.FirePlayerResurrected();
            GameEvents.FirePlayerRevived();
        }

        /// <summary>
        /// How long the rise animation actually runs, or 0 when this character has none.
        ///
        /// Returns 0 rather than a default when the set is missing so the caller skips the
        /// wait entirely: <c>DirectionalAnimator</c> falls Recover back to idle, and holding
        /// a character in an idle pose for a fixed duration after a revive would read as the
        /// game having frozen.
        /// </summary>
        private static float ResolveRecoverDuration(GameObject player)
        {
            var animator = player.GetComponent<DirectionalAnimator>();
            if (animator == null) return 0f;

            var recover = animator.RecoverSprites;
            bool hasRecoverArt =
                (recover.south != null && recover.south.Length > 0) ||
                (recover.southEast != null && recover.southEast.Length > 0) ||
                (recover.east != null && recover.east.Length > 0) ||
                (recover.northEast != null && recover.northEast.Length > 0) ||
                (recover.north != null && recover.north.Length > 0) ||
                (recover.northWest != null && recover.northWest.Length > 0) ||
                (recover.west != null && recover.west.Length > 0) ||
                (recover.southWest != null && recover.southWest.Length > 0);
            if (!hasRecoverArt) return 0f;

            return animator.GetStateLength(DirectionalAnimator.AnimState.Recover);
        }

        private void EnterSpirit(GameObject player)
        {
            // Defensive AddComponent: EntitySetup is supposed to attach these
            // during ConfigurePlayer, but if the live Player instance in the
            // scene predates a recompile (Play started before the spirit-flow
            // script was added) the components will be missing and the spirit
            // would never activate. Adding here guarantees the flow works on
            // any player GameObject that reaches OnPlayerDied.
            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit == null) spirit = player.AddComponent<PlayerSpiritState>();
            spirit.EnterSpirit();

            var visuals = player.GetComponent<PlayerSpiritVisuals>();
            if (visuals == null) visuals = player.AddComponent<PlayerSpiritVisuals>();
            visuals.Activate();

            var col = player.GetComponent<Collider2D>();
            if (col != null && !_excludeCaptured)
            {
                _savedExcludeLayers = col.excludeLayers;
                _excludeCaptured = true;
                col.excludeLayers = _savedExcludeLayers | _spiritExcludeLayers;
            }
        }

        private void ExitSpirit(GameObject player)
        {
            var spirit = player.GetComponent<PlayerSpiritState>();
            if (spirit != null) spirit.ExitSpirit();

            var visuals = player.GetComponent<PlayerSpiritVisuals>();
            if (visuals != null) visuals.Deactivate();

            var col = player.GetComponent<Collider2D>();
            if (col != null && _excludeCaptured)
            {
                col.excludeLayers = _savedExcludeLayers;
                _excludeCaptured = false;
            }

            // Restore HP / Mana to full so the player can keep playing.
            var health = player.GetComponent<Health>();
            if (health != null) health.Initialize(health.MaxHp);

            var mana = player.GetComponent<Mana>();
            if (mana != null) mana.Restore(mana.MaxMana);

            // Make sure the controller is enabled — historical CmdResurrect
            // disabled the PlayerController on death; we no longer do but the
            // re-enable here is cheap and keeps the contract.
            var pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;

            // If anything left timeScale at 0 during the brief flash, restore it.
            if (Time.timeScale < 0.01f) Time.timeScale = 1f;
        }

        private static LayerMask BuildSpiritExcludeMask()
        {
            int mask = 0;
            int npc = LayerMask.NameToLayer("NPC");
            int projectile = LayerMask.NameToLayer("Projectile");
            int pickup = LayerMask.NameToLayer("Pickup");
            if (npc        >= 0) mask |= 1 << npc;
            if (projectile >= 0) mask |= 1 << projectile;
            if (pickup     >= 0) mask |= 1 << pickup;
            return mask;
        }
    }
}
