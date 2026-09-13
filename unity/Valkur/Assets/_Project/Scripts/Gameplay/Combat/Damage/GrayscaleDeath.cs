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
        private MaterialPropertyBlock _mpb;

        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        /// <summary>
        /// Fraction of the corpse window over which the body crumbles away at the end. The
        /// first part of the window is the darkening, so the corpse is still a corpse for most
        /// of its life and only goes to pieces as it is about to despawn — a body that started
        /// dissolving on the frame it fell would read as a kill with no corpse.
        /// </summary>
        public const float DissolveTail = 0.35f;

        /// <summary>How far the body has crumbled, 0..1. Test seam.</summary>
        public float DissolveAmount { get; private set; }

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

        private void Update() => Advance(Time.deltaTime);

        /// <summary>Advance the corpse. Public so a test can run one out.</summary>
        public void Advance(float dt)
        {
            if (!_dying || _tint == null) return;
            _t += dt / Mathf.Max(0.0001f, _fadeDuration);
            _tint.Set(TintLayer.Death, Color.Lerp(Color.white, _endFactor, Mathf.Clamp01(_t)));

            // The tail: the body is eaten away cell by cell with an ember edge, so the despawn
            // is something that happens to the corpse rather than something that happens TO
            // the frame.
            float tailStart = 1f - DissolveTail;
            float amount = _t <= tailStart ? 0f : Mathf.Clamp01((_t - tailStart) / DissolveTail);
            if (!Mathf.Approximately(amount, DissolveAmount)) WriteDissolve(amount);
        }

        private void WriteDissolve(float amount)
        {
            DissolveAmount = amount;
            var sr = _tint != null ? _tint.BodyRenderer : SpriteTintStack.ResolveBodyRenderer(gameObject);
            if (sr == null) return;
            _mpb ??= new MaterialPropertyBlock();
            // GET before SET: the hit flash and the HDR tint live in the same block.
            sr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(DissolveId, amount);
            sr.SetPropertyBlock(_mpb);
        }

        /// <summary>Reset to original color (e.g. on respawn), and put the body back together.</summary>
        public void ResetTint()
        {
            _dying = false;
            _t = 0f;
            if (_tint != null) _tint.Clear(TintLayer.Death);
            if (DissolveAmount > 0f) WriteDissolve(0f);
        }
    }
}
