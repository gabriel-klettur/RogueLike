using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Owns which <see cref="Loadout"/> a character is currently wearing, and is the only
    /// thing that changes it.
    ///
    /// A loadout is a LOOK, not a stat block — the dwarf with his sword drawn is the same
    /// dwarf, with the same health, speed and spells. Swapping one re-runs
    /// <see cref="EntityAnimationBinder.ApplyLoadout"/>, which rebuilds every sprite set
    /// through the ordinary bind path so the states a loadout does not override keep the base
    /// art and keep it via the same fallback chain as on the first bind.
    ///
    /// <b>The two directions of a toggle are NOT symmetric, and that is the point.</b> The
    /// flare exists to hide a cut, so it has to happen WHERE the cut is — and the cut is at a
    /// different end of the animation each way round:
    ///
    /// * <b>Drawing</b> — the weapon has to be in hand for the draw animation to be showing
    ///   it, so the art swaps on the cast frame and the animation plays over the top of a
    ///   character who is already armed.
    /// * <b>Stowing</b> — the sheathe is the draw run backwards, and it is showing the weapon
    ///   for its whole length. Swapping on the cast frame would strip the sword and then play
    ///   1.2 s of animation putting away a sword the character no longer has. So the art swap
    ///   is DEFERRED to the end of the animation, which is the moment the weapon actually
    ///   leaves the player's view, and the flare fires <see cref="FLASH_LEAD"/> ahead of it so
    ///   its brightest frame lands on the cut rather than one frame after it.
    ///
    /// The intent is still committed on the cast frame either way —
    /// <see cref="SwappedThisFrame"/> and <see cref="LastSwapStowed"/> answer immediately, so
    /// <c>PlayerController.ShouldPlayCastReversed</c> can pick the playback direction in the
    /// same frame the executor ran. Only the ART is late.
    ///
    /// Two things the swap deliberately does NOT do:
    ///
    /// * It does not touch <c>AnimState</c>. There is no "armed idle" state — there is the
    ///   idle state, drawn with a sword. Every whitelist in <c>PlayerController.Movement</c>,
    ///   every FSM state class and every revert path keeps working because none of them can
    ///   tell a loadout swap happened. That is the whole reason this is an override list
    ///   rather than eight new enum values.
    /// * It does not interrupt what is playing. The animator is re-seeded to the idle frame
    ///   by the bind, and whatever state the character was in is re-entered on the next tick
    ///   by the systems that own it. A swap mid-swing therefore finishes the swing in the new
    ///   hands rather than cancelling it, which is the lesser of the two wrong answers: the
    ///   alternative cancels a committed attack from an animation toggle.
    ///
    /// Static-free by construction: the active key is instance state on the character, so
    /// Domain Reload being off costs nothing here.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerLoadoutController : MonoBehaviour
    {
        /// <summary>
        /// How long a pending stow waits when nobody refines it through
        /// <see cref="ScheduleStow"/>. Matches <c>PlayerController.CAST_ANIMATION_DURATION</c>,
        /// the floor that same code applies to a cast window, so a character reached from a
        /// path that measures no animation still completes its stow promptly instead of
        /// hanging armed forever. It is a backstop, not the normal case.
        /// </summary>
        private const float STOW_FALLBACK_DELAY = 0.35f;

        /// <summary>
        /// How far ahead of the art swap the flare fires. <c>WeaponSwapFlashFX</c>'s bloom
        /// peaks 12 % into its 0.34 s cycle — 0.041 s — so leading by that much puts its
        /// BRIGHTEST frame on the cut instead of one frame after it. Firing them together
        /// would let the swap land while the flash is still ramping, which is the one frame
        /// the whole effect exists to cover.
        /// </summary>
        private const float FLASH_LEAD = 0.04f;

        private EntityAssetConfig _config;
        private string _activeKey;

        /// <summary><see cref="Time.frameCount"/> of the last swap that actually changed
        /// something, and whether that swap took the loadout OFF.</summary>
        private int _lastSwapFrame = -1;
        private bool _lastSwapStowed;

        // A stow that has been committed but whose art has not swapped yet. Countdowns
        // rather than Time.time deadlines so the tick can be driven with an explicit delta
        // from a test, where Time.time does not advance.
        private bool _stowPending;
        private float _stowCountdown;
        private float _flashCountdown;
        private bool _flashFired;

        /// <summary>The loadout being worn, or null for the character's base art.</summary>
        public string ActiveLoadoutKey => _activeKey;

        /// <summary>
        /// The config this character's loadouts come from. Exposed because the Spells Editor's
        /// preview rig mirrors the live player rather than being bound from a definition, and
        /// a loadout's art cannot be reached without the config that declares it — there is no
        /// other route from a running character back to its <see cref="EntityAssetConfig"/>.
        /// </summary>
        public EntityAssetConfig Config => _config;

        /// <summary>
        /// True on the frame a swap was COMMITTED — which for a stow is the cast frame, not
        /// the frame its art lands. The window is one frame on purpose: the spell system runs
        /// the executor INSIDE <c>TryCastByKey</c> and
        /// <c>PlayerController.TriggerCastAnimation</c> immediately after it, in that same
        /// frame, so this is exactly wide enough for the animation to ask what just happened
        /// and narrow enough that it cannot answer for a swap two casts ago.
        /// </summary>
        public bool SwappedThisFrame => _lastSwapFrame == Time.frameCount;

        /// <summary>Whether the last swap STOWED rather than drew. The sheathe is the draw
        /// run backwards, so this is what decides playback direction.</summary>
        public bool LastSwapStowed => _lastSwapStowed;

        /// <summary>True while a stow is committed and its art swap is still to come. The
        /// character is still wearing the loadout for as long as this is true.</summary>
        public bool StowPending => _stowPending;

        /// <summary>True when a loadout is worn. Reads better than a null check at callsites
        /// that only care whether the weapon is out.</summary>
        public bool HasLoadoutActive => !string.IsNullOrEmpty(_activeKey);

        private void Update() => TickPendingStow(Time.deltaTime);

        /// <summary>
        /// Wires the config this character's loadouts come from. Called once by
        /// <c>EntitySetup.ConfigurePlayerVisuals</c>, immediately after the first bind, so
        /// the component is never live with a null config.
        /// </summary>
        public void Initialize(EntityAssetConfig config)
        {
            _config = config;
            _activeKey = null;
            CancelPendingStow();
        }

        /// <summary>True when this character declares a loadout under <paramref name="key"/>.</summary>
        public bool HasLoadout(string key) => _config != null && _config.FindLoadout(key) != null;

        /// <summary>
        /// Wears <paramref name="key"/>, or the base art when it is null/empty. Returns
        /// whether anything changed — a caller that plays an animation for the swap wants to
        /// skip it when the swap was refused.
        ///
        /// Always IMMEDIATE, in both directions, and it fires no flare. This is the direct
        /// verb: the animation probes use it to park the character in a loadout so an armed
        /// locomotion state can be watched, and a probe that flashed on every cast would be
        /// unusable for exactly the job it exists to do. The deferred, flared stow belongs to
        /// <see cref="ToggleLoadout"/>, which is what the <c>weapon_toggle</c> spell casts.
        ///
        /// An unknown key is REFUSED rather than silently treated as "unequip": a typo in a
        /// spell asset would otherwise read as a working toggle that only ever undresses.
        /// </summary>
        public bool SetLoadout(string key)
        {
            if (_config == null) return false;

            // A direct set is the more specific instruction, so it wins over a stow that has
            // not landed. Leaving one armed would fire it seconds later and undress a
            // character the caller just dressed.
            CancelPendingStow();

            bool clearing = string.IsNullOrEmpty(key);
            if (!clearing && _config.FindLoadout(key) == null)
            {
                Debug.LogWarning($"[PlayerLoadoutController] '{name}' has no loadout '{key}'. " +
                                 "Nothing changed — check the spell's loadoutKey against the " +
                                 "keys on its PlayerDefinition.");
                return false;
            }

            string next = clearing ? null : key;
            if (string.Equals(next, _activeKey, System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (!ApplyKey(next))
                return false;

            _lastSwapStowed = clearing;
            _lastSwapFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// Toggles <paramref name="key"/> on and off, the way the <c>weapon_toggle</c> spell
        /// casts it. Returns whether anything changed.
        ///
        /// Drawing lands now; stowing is committed now and lands when the sheathe finishes —
        /// see the class doc for why the two directions differ. Toggling to a DIFFERENT key
        /// while one is worn puts the new one on rather than taking the old one off, so a
        /// second weapon added later needs no new verb.
        /// </summary>
        public bool ToggleLoadout(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            bool wearingThisOne = string.Equals(key, _activeKey, System.StringComparison.OrdinalIgnoreCase);

            // A second press while the sheathe is still running means the player changed
            // their mind. Cancelling keeps the weapon out and reports a DRAW, so the equip
            // animation plays forward and the press reads as pulling it back out. Arming a
            // second stow instead would queue the swap the player just asked to call off.
            if (_stowPending && wearingThisOne)
            {
                CancelPendingStow();
                _lastSwapStowed = false;
                _lastSwapFrame = Time.frameCount;
                PlayFlare(stowing: false);
                return true;
            }

            if (!wearingThisOne)
            {
                if (!SetLoadout(key)) return false;
                PlayFlare(stowing: false);
                return true;
            }

            return BeginStow();
        }

        /// <summary>
        /// Refines how long the committed stow waits, in seconds from now. Called by
        /// <c>PlayerController.TriggerCastAnimation</c> with the cast window it just
        /// measured, which is the real on-screen length of the sheathe — the only place that
        /// number exists, because it depends on the variant the animator resolved and on that
        /// variant's own speed multiplier.
        ///
        /// A no-op when no stow is pending, so an unrelated spell cast mid-sheathe cannot
        /// push the swap out by its own window.
        /// </summary>
        public void ScheduleStow(float secondsFromNow)
        {
            if (!_stowPending) return;
            _stowCountdown = Mathf.Max(0f, secondsFromNow);
            if (!_flashFired)
                _flashCountdown = Mathf.Max(0f, secondsFromNow - FLASH_LEAD);
        }

        /// <summary>
        /// Advances a pending stow. Driven from <see cref="Update"/> in play; takes an
        /// explicit delta so an Edit Mode test can land a stow without waiting on
        /// <c>Time.time</c>, which does not advance there.
        /// </summary>
        public void TickPendingStow(float deltaTime)
        {
            if (!_stowPending) return;

            if (!_flashFired)
            {
                _flashCountdown -= deltaTime;
                if (_flashCountdown <= 0f)
                {
                    _flashFired = true;
                    PlayFlare(stowing: true);
                }
            }

            _stowCountdown -= deltaTime;
            if (_stowCountdown > 0f) return;

            _stowPending = false;
            if (!ApplyKey(null))
            {
                // The bind refused. Staying armed is the honest outcome: the character keeps
                // a coherent look and the next press tries again, where completing the swap
                // against a failed bind would leave the art and the active key disagreeing.
                Debug.LogWarning($"[PlayerLoadoutController] '{name}' could not re-bind its " +
                                 "base art to finish a stow. The loadout stays worn.");
            }
        }

        /// <summary>Drops a committed stow before its art lands. The character keeps whatever
        /// it is wearing.</summary>
        public void CancelPendingStow()
        {
            _stowPending = false;
            _flashFired = false;
            _stowCountdown = 0f;
            _flashCountdown = 0f;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private bool BeginStow()
        {
            if (_config == null) return false;

            _stowPending = true;
            _flashFired = false;
            _stowCountdown = STOW_FALLBACK_DELAY;
            _flashCountdown = Mathf.Max(0f, STOW_FALLBACK_DELAY - FLASH_LEAD);

            // Committed NOW even though the art is late, so the cast animation this same
            // frame knows to run its frames back to front.
            _lastSwapStowed = true;
            _lastSwapFrame = Time.frameCount;
            return true;
        }

        private bool ApplyKey(string next)
        {
            if (!EntityAnimationBinder.ApplyLoadout(gameObject, _config, next))
                return false;

            _activeKey = next;
            return true;
        }

        private void PlayFlare(bool stowing) => WeaponSwapFlashFX.Play(transform, stowing);
    }
}
