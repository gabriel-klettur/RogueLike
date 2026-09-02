using UnityEngine;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The living half of an energy charge: how long it burns, where it follows, and how the
    /// character it belongs to is lit by it. <see cref="KiAuraFX"/> draws it.
    ///
    /// <para>IT FOLLOWS, IT IS NOT PARENTED. The same reason as every other effect in this
    /// folder: parenting inherits the entity's scale, and a scaled transform takes the
    /// <c>Light2D</c> radius with it. Following also means the charge survives the caster
    /// walking — which is not how a charge is usually played, but a rig that breaks when the
    /// player moves is a rig that breaks.</para>
    ///
    /// <para>The character's own colour goes through <see cref="SpriteTintStack"/> on
    /// <see cref="TintLayer.Charge"/>. That is what makes them part of the aura rather than
    /// something standing inside it, and going through the stack is what stops it fighting a
    /// burn, a hit flash or a weapon swap that happens to overlap.</para>
    /// </summary>
    public class EnergyChargeController : MonoBehaviour, ISpellEffectDissipates
    {
        /// <summary>How long the aura takes to die down when its timer runs out.</summary>
        private const float FadeSeconds = 0.65f;

        internal struct Setup
        {
            public Transform Caster;
            public float Duration;
            public KiPalette Palette;
            public float GroundRadius;
        }

        private Setup _setup;
        private KiAuraFX _aura;
        private SpriteTintStack _bodyTint;
        private SpriteRenderer _bodyRenderer;
        private float _remainingTime;
        private bool _ending;
        private int _lastBodyOrder = int.MinValue;

        internal void Initialize(Setup setup)
        {
            _setup = setup;
            _remainingTime = setup.Duration;

            _bodyRenderer = ResolveBodyRenderer(setup.Caster);
            Vector2 size = _bodyRenderer != null && _bodyRenderer.sprite != null
                ? (Vector2)_bodyRenderer.bounds.size
                : new Vector2(0.9f, 1.6f);
            Vector3 bodyOffset = _bodyRenderer != null && _bodyRenderer.sprite != null
                ? _bodyRenderer.bounds.center - setup.Caster.position
                : new Vector3(0f, 0.8f, 0f);

            transform.position = setup.Caster.position;

            _aura = KiAuraFX.Attach(transform, new KiAuraFX.Config
            {
                Palette = setup.Palette,
                BodySize = new Vector2(Mathf.Max(0.3f, size.x), Mathf.Max(0.5f, size.y)),
                BodyOffset = bodyOffset,
                GroundRadius = setup.GroundRadius,
                Seed = Mathf.Abs(Time.frameCount * 83492791 ^ (int)(Time.time * 1000f)),
            });
            _aura.OnGroundPulse = HandleGroundPulse;

            _bodyTint = SpriteTintStack.Attach(setup.Caster.gameObject);
            SyncSortingToCaster();

            CameraFeel.Cue(CameraFeelCue.CastHeavy, Vector2.zero,
                Mathf.Lerp(0.4f, 1f, setup.Palette.Intensity));
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            FollowCaster();
            SyncSortingToCaster();
            _aura?.Tick(deltaTime);
            UpdateBodyTint();

            if (_ending)
            {
                if (_aura == null || _aura.FadeComplete) Destroy(gameObject);
                return;
            }

            _remainingTime -= deltaTime;
            if (_remainingTime <= 0f) BeginEnd(FadeSeconds);
        }

        private void FollowCaster()
        {
            if (_setup.Caster != null) transform.position = _setup.Caster.position;
        }

        /// <summary>
        /// Keep the aura's layers on either side of the CASTER's own sorting order, so the
        /// column burns behind them and the light of it falls in front.
        ///
        /// <para>Their order moves with their Y (<c>YSortEntity</c> rewrites it whenever they
        /// walk), so a value captured once at build time is correct only while they stand
        /// still — and the failure is the aura popping in front of the character the first
        /// time they take a step. Re-read, and write only on a change.</para>
        /// </summary>
        private void SyncSortingToCaster()
        {
            if (_aura == null || _bodyRenderer == null) return;
            int order = _bodyRenderer.sortingOrder;
            if (order == _lastBodyOrder) return;
            _lastBodyOrder = order;
            _aura.RebaseSortingOrder(order);
        }

        private void UpdateBodyTint()
        {
            if (_bodyTint == null) return;
            // Deliberately gentle even at full intensity. The aura is additive and can blow
            // out to white on its own; the tint MULTIPLIES, so pushing it hard would darken
            // the character towards the aura's colour rather than lighting them with it.
            float drive = Mathf.Lerp(0.18f, 0.42f, _setup.Palette.Intensity) * (_ending ? 0.35f : 1f);
            _bodyTint.Set(TintLayer.Charge,
                Color.Lerp(Color.white, _setup.Palette.Light, drive));
        }

        /// <summary>
        /// One camera beat per ground pulse, and only once the charge is violent enough to be
        /// breaking the floor. A sustained shake under a spell that can run for eight seconds
        /// is nauseating rather than impressive, so this rides the pulses instead.
        /// </summary>
        private void HandleGroundPulse()
        {
            if (_setup.Palette.Intensity < 0.45f) return;
            CameraFeel.Cue(CameraFeelCue.ImpactLight, Vector2.zero,
                Mathf.InverseLerp(0.45f, 1f, _setup.Palette.Intensity));
        }

        private void BeginEnd(float seconds)
        {
            if (_ending) return;
            _ending = true;
            _aura?.BeginFade(seconds);
        }

        /// <summary>
        /// The registry evicted this charge for a newer one — with <c>maxInstances: 1</c> that
        /// is what recasting does, and it is the common exit, not the edge case.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;
            BeginEnd(seconds);
            return true;
        }

        private void OnDestroy()
        {
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Charge);
        }

        private static SpriteRenderer ResolveBodyRenderer(Transform owner)
        {
            if (owner == null) return null;

            var sr = owner.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in owner.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
        }
    }
}
