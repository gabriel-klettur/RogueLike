using UnityEngine;
using Valkur.Core;
using Valkur.Data.Feel;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The living half of the magic shield sphere: how long it holds, what it makes the caster
    /// immune to, and what it does when a blow lands on it. <see cref="ShieldSphereFX"/> draws
    /// it.
    ///
    /// <para>IT FOLLOWS, IT IS NOT PARENTED — unlike the version this replaces, which parented
    /// itself to the caster and then ALSO wrote <c>transform.position = caster.position</c>
    /// every frame, so the two fought. Parenting inherits the entity's scale and takes the
    /// <c>Light2D</c> radius with it, which is the same trap recorded across this folder.</para>
    ///
    /// <para>The shield now REACTS. <c>Health.OnDamageBlocked</c> fires wherever a hit is
    /// refused for invincibility, and a ripple crosses the shell from the direction it came
    /// from. Before that event existed the refusal was silent, so a shield could absorb an
    /// entire fight without a single frame of feedback — the player had no way to tell it was
    /// working, or even still up.</para>
    /// </summary>
    public class ShieldController : MonoBehaviour, ISpellEffectDissipates
    {
        /// <summary>How long the shell takes to come apart when the timer runs out.</summary>
        private const float BreakSeconds = 0.7f;

        /// <summary>Steady level of the sustaining hum. Deliberately far under the one-shots.</summary>
        private const float HumVolume = 0.22f;

        /// <summary>Seconds the hum takes to reach that level, and to leave it.</summary>
        private const float HumFadeSeconds = 0.35f;

        internal struct Setup
        {
            public Transform Caster;
            public float Duration;
            public KiPalette Palette;
            /// <summary>Sphere radius in WORLD UNITS.</summary>
            public float Radius;
        }

        private Setup _setup;
        private ShieldSphereFX _sphere;
        private Health _casterHealth;
        private SpriteRenderer _bodyRenderer;
        private AudioSource _hum;

        private float _remainingTime;
        private bool _ending;
        private bool _hadInvincibility;
        private int _lastBodyOrder = int.MinValue;

        internal void Initialize(Setup setup)
        {
            _setup = setup;
            _remainingTime = setup.Duration;

            _bodyRenderer = ResolveBodyRenderer(setup.Caster);
            Vector3 bodyOffset = _bodyRenderer != null && _bodyRenderer.sprite != null
                ? _bodyRenderer.bounds.center - setup.Caster.position
                : new Vector3(0f, 0.8f, 0f);

            transform.position = setup.Caster.position;

            _sphere = ShieldSphereFX.Attach(transform, new ShieldSphereFX.Config
            {
                Palette = setup.Palette,
                Radius = setup.Radius,
                BodyOffset = bodyOffset,
                Seed = Mathf.Abs(Time.frameCount * 92821 ^ (int)(Time.time * 1000f)),
            });

            _casterHealth = setup.Caster != null ? setup.Caster.GetComponent<Health>() : null;
            if (_casterHealth != null)
            {
                // SAVE AND RESTORE, never a blind clear — the same shape SpellsRuntimeEditor
                // already uses on this flag. Invincibility is a single bool with three
                // independent owners (the dev console's god mode, the F4 editor's test
                // invulnerability, and this), so a shield that expired by writing `false`
                // switched off whichever of the other two was holding it.
                _hadInvincibility = _casterHealth.IsInvincible;
                _casterHealth.SetInvincible(true);
                _casterHealth.OnDamageBlocked += HandleDamageBlocked;
            }

            SyncSortingToCaster();
            BuildHum();

            PlayOneShot(ShieldAudio.Create());
            CameraFeel.Cue(CameraFeelCue.CastHeavy, Vector2.zero, 0.55f);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (_setup.Caster != null) transform.position = _setup.Caster.position;
            SyncSortingToCaster();
            _sphere?.Tick(deltaTime);
            UpdateHum(deltaTime);

            if (_ending)
            {
                if (_sphere == null || _sphere.FadeComplete) Destroy(gameObject);
                return;
            }

            _remainingTime -= deltaTime;
            if (_remainingTime <= 0f) BeginEnd(BreakSeconds, ShieldAudio.Break());
        }

        /// <summary>
        /// Keep the sphere's halves on either side of the caster's live sorting order.
        ///
        /// <para>More load-bearing here than for any other effect in this folder: the whole
        /// illusion is that one hemisphere is BEHIND the character. <c>YSortEntity</c> rewrites
        /// their order whenever they walk, so a base captured once at build time flattens the
        /// sphere into a disc the moment they take a step.</para>
        /// </summary>
        private void SyncSortingToCaster()
        {
            if (_sphere == null || _bodyRenderer == null) return;
            int order = _bodyRenderer.sortingOrder;
            if (order == _lastBodyOrder) return;
            _lastBodyOrder = order;
            _sphere.RebaseSortingOrder(order);
        }

        /// <summary>
        /// A blow was turned away. The direction is taken from the attacker's position so the
        /// ripple starts where the hit actually came from; with no attacker the sphere picks a
        /// point on its near hemisphere, which is honest — something hit it, from somewhere.
        /// </summary>
        private void HandleDamageBlocked(int amount, GameObject attacker)
        {
            if (_ending || _sphere == null) return;

            Vector2 direction = Vector2.zero;
            if (attacker != null && _setup.Caster != null)
                direction = (Vector2)(attacker.transform.position - _setup.Caster.position);

            // A big hit should land bigger. Scaled against the caster's own maximum HP rather
            // than a constant, so the same blow reads as heavier on a frailer character.
            float strength = _casterHealth != null && _casterHealth.MaxHp > 0
                ? Mathf.Clamp01(amount / (_casterHealth.MaxHp * 0.25f))
                : 0.5f;

            _sphere.Impact(direction, strength);
            PlayOneShot(ShieldAudio.Impact(), Mathf.Lerp(0.55f, 1f, strength));
            CameraFeel.Cue(CameraFeelCue.ImpactLight, direction.normalized,
                Mathf.Lerp(0.35f, 0.85f, strength));
        }

        // ── audio ───────────────────────────────────────────────────────────────────

        private void BuildHum()
        {
            _hum = gameObject.AddComponent<AudioSource>();
            _hum.clip = ShieldAudio.Hum();
            _hum.loop = true;
            // 2D: the shield is on the player, so panning it would be wrong every time they
            // walk off the centre of the screen.
            _hum.spatialBlend = 0f;
            _hum.volume = 0f;
            _hum.priority = 200;
            _hum.bypassReverbZones = true;
            _hum.playOnAwake = false;
            _hum.Play();
        }

        private void UpdateHum(float deltaTime)
        {
            if (_hum == null) return;
            float target = _ending ? 0f : HumVolume;
            _hum.volume = Mathf.MoveTowards(_hum.volume, target,
                (HumVolume / HumFadeSeconds) * deltaTime);
        }

        private static void PlayOneShot(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null) return;
            ServiceLocator.Get<IAudioService>()?.PlaySFX(clip, volumeScale);
        }

        // ── lifetime ────────────────────────────────────────────────────────────────

        private void BeginEnd(float seconds, AudioClip clip)
        {
            if (_ending) return;
            _ending = true;

            // Dropped the moment the shell starts to open, not when the object dies: the break
            // is visibly the shield failing, and staying immune through it would be a window
            // where the player is protected by something they can watch coming apart.
            ReleaseInvincibility();

            _sphere?.BeginFade(seconds);
            PlayOneShot(clip);
        }

        /// <summary>
        /// The registry evicted this shield for a newer one — with <c>maxInstances: 1</c> that
        /// is what recasting does. Nothing enforced it before this controller was tracked, so
        /// two shields could overlap and the FIRST to expire dropped invincibility for both.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;
            BeginEnd(seconds, null);
            return true;
        }

        private void ReleaseInvincibility()
        {
            if (_casterHealth == null) return;
            _casterHealth.OnDamageBlocked -= HandleDamageBlocked;
            _casterHealth.SetInvincible(_hadInvincibility);
            // Idempotent: OnDestroy runs after BeginEnd already released, and re-restoring a
            // stale saved value would undo whatever claimed the flag in between.
            _casterHealth = null;
        }

        private void OnDestroy()
        {
            ReleaseInvincibility();
            _sphere?.Destroy();
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
