using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Gradually darkens a SpriteRenderer over the corpse window so the cadaver
    /// looks like it's decaying into shadow without losing its identifying tint
    /// (a yellow barbol stays yellow, a cyan one stays cyan, etc.). Replaces the
    /// old "lerp to flat gray" behaviour from Python's death_tint_system, which
    /// flattened all variants into the same neutral color.
    /// Auto-subscribes to Health.OnDeath when present so any entity with both
    /// components (Health + SpriteRenderer + GrayscaleDeath) tints automatically.
    /// </summary>
    public class GrayscaleDeath : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("Multiplier applied to RGB at the end " +
                                                "of the fade. Lower = darker corpse. " +
                                                "0.25 keeps hue/saturation but cuts " +
                                                "brightness to a quarter so the cadaver " +
                                                "reads as shadowed without going gray.")]
        private float endDarknessFactor = 0.25f;

        [SerializeField, Tooltip("Default fade duration (seconds) when no FSM brain is " +
                                 "available to provide the corpse window. The FSM brain's " +
                                 "deathDisappearTime overrides this when present.")]
        private float defaultFadeDuration = 0.5f;

        private SpriteTintStack _tint;
        private Health _health;
        private Color _endFactor;
        private bool _dying;
        private float _t;
        private float _fadeDuration;

        private void Awake()
        {
            _tint = SpriteTintStack.Attach(gameObject);
            _fadeDuration = defaultFadeDuration;
        }

        private void OnEnable()
        {
            _health = GetComponent<Health>();
            if (_health != null) _health.OnDeath += TriggerDeath;

            // A pooled monster comes back through OnEnable. Without this it returns still
            // wearing the darkening of the death that put it in the pool — ResetTint had no
            // caller at all, so nothing ever undid it.
            ResetTint();
        }

        private void OnDisable()
        {
            if (_health != null) _health.OnDeath -= TriggerDeath;
        }

        /// <summary>Call when entity dies to begin the corpse darkening.</summary>
        public void TriggerDeath()
        {
            _dying = true;
            _t = 0f;
            _tint ??= SpriteTintStack.Attach(gameObject);

            // Multiplicative darkening preserves hue and saturation — a yellow corpse
            // ends as dark yellow, a cyan corpse ends as dark cyan — so each variant
            // stays visually identifiable until despawn instead of all converging to
            // the same neutral gray.
            //
            // Expressed as a tint LAYER rather than as a captured colour: the darkening
            // then composes with whatever else is tinting the body, so a corpse that dies
            // mid-burn keeps flickering as it darkens instead of freezing the burn's
            // orange into the corpse for good.
            _endFactor = new Color(endDarknessFactor, endDarknessFactor, endDarknessFactor, 1f);

            // Stretch the fade across the corpse's whole lifetime so the user sees
            // a gradual darkening rather than an instant snap. We pull the window
            // from the FSM brain's MonsterDefinition (already authoritative for the
            // despawn timer in UnconsciousState).
            _fadeDuration = ResolveFadeDuration();
        }

        private float ResolveFadeDuration()
        {
            var brain = GetComponent<FSMMonsterBrain>();
            if (brain != null && brain.Definition != null)
            {
                float corpseWindow = brain.Definition.stats.deathDisappearTime;
                if (corpseWindow > 0.1f) return corpseWindow;
            }
            return defaultFadeDuration;
        }

        private void Update()
        {
            if (!_dying || _tint == null) return;
            _t += Time.deltaTime / Mathf.Max(0.0001f, _fadeDuration);
            _tint.Set(TintLayer.Death, Color.Lerp(Color.white, _endFactor, Mathf.Clamp01(_t)));
        }

        /// <summary>Reset to original color (e.g. on respawn).</summary>
        public void ResetTint()
        {
            _dying = false;
            _t = 0f;
            if (_tint != null) _tint.Clear(TintLayer.Death);
        }
    }
}
