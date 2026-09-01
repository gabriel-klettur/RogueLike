using UnityEngine;
using Valkur.Data;

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
        private EntityAssetConfig _config;
        private string _activeKey;

        /// <summary><see cref="Time.frameCount"/> of the last swap that actually changed
        /// something, and whether that swap took the loadout OFF.</summary>
        private int _lastSwapFrame = -1;
        private bool _lastSwapStowed;

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
        /// True on the frame a swap landed. The window is one frame on purpose: the spell
        /// system runs the executor INSIDE <c>TryCastByKey</c> and
        /// <c>PlayerController.TriggerCastAnimation</c> immediately after it, in that same
        /// frame, so this is exactly wide enough for the animation to ask what just happened
        /// and narrow enough that it cannot answer for a swap two casts ago.
        /// </summary>
        public bool SwappedThisFrame => _lastSwapFrame == Time.frameCount;

        /// <summary>Whether the last swap STOWED rather than drew. The sheathe is the draw
        /// run backwards, so this is what decides playback direction.</summary>
        public bool LastSwapStowed => _lastSwapStowed;

        /// <summary>True when a loadout is worn. Reads better than a null check at callsites
        /// that only care whether the weapon is out.</summary>
        public bool HasLoadoutActive => !string.IsNullOrEmpty(_activeKey);

        /// <summary>
        /// Wires the config this character's loadouts come from. Called once by
        /// <c>EntitySetup.ConfigurePlayerVisuals</c>, immediately after the first bind, so
        /// the component is never live with a null config.
        /// </summary>
        public void Initialize(EntityAssetConfig config)
        {
            _config = config;
            _activeKey = null;
        }

        /// <summary>True when this character declares a loadout under <paramref name="key"/>.</summary>
        public bool HasLoadout(string key) => _config != null && _config.FindLoadout(key) != null;

        /// <summary>
        /// Wears <paramref name="key"/>, or the base art when it is null/empty. Returns
        /// whether anything changed — a caller that plays an animation for the swap wants to
        /// skip it when the swap was refused.
        ///
        /// An unknown key is REFUSED rather than silently treated as "unequip": a typo in a
        /// spell asset would otherwise read as a working toggle that only ever undresses.
        /// </summary>
        public bool SetLoadout(string key)
        {
            if (_config == null) return false;

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

            if (!EntityAnimationBinder.ApplyLoadout(gameObject, _config, next))
                return false;

            _activeKey = next;
            _lastSwapStowed = clearing;
            _lastSwapFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// Toggles <paramref name="key"/> on and off. Returns whether anything changed.
        ///
        /// Toggling to a DIFFERENT key while one is worn puts the new one on rather than
        /// taking the old one off, so a second weapon added later needs no new verb.
        /// </summary>
        public bool ToggleLoadout(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            bool wearingThisOne = string.Equals(key, _activeKey, System.StringComparison.OrdinalIgnoreCase);
            return SetLoadout(wearingThisOne ? null : key);
        }
    }
}
