using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The living half of an ice wall: its clock, its health, and which of its three exits
    /// it takes. <see cref="IceWallVisual"/> draws it; this decides what it is doing.
    ///
    /// <para>THREE EXITS, TWO OF THEM NEW. The wall can be broken (shatter), it can run out
    /// of time (melt), or it can be evicted by the next cast — <c>wall_ice</c> ships
    /// <c>maxInstances: 1</c>, so recasting is the COMMON way an existing wall ends, and
    /// without <see cref="ISpellEffectDissipates"/> the registry destroys it in a single
    /// frame with no animation at all. That is the same hard cut <c>arcane_flame</c> was
    /// fixed for, and for the same reason it was not an edge case.</para>
    ///
    /// <para>Being breakable at all is new too: nothing in the project could reduce the
    /// <c>Health</c> this component has always carried. See
    /// <see cref="IDestructibleObstacle"/> for why a layer mask could not have done it.</para>
    ///
    /// <para>TWO RIGS, CHOSEN BY ELEMENT. <see cref="Setup.Element"/> was captured here and
    /// read by no line in the project, so <c>arcane_barrier</c> — authored Arcane, with a
    /// violet swatch — drew a blue ice wall down to its cracking-ice sound. It now picks the
    /// drawing and the four one-shots: Ice keeps <see cref="IceWallVisual"/>, everything else
    /// gets <see cref="ArcaneBarrierVisual"/>. A Fire wall would want a third rig rather than
    /// the woven one recoloured orange — see <see cref="IWallVisual"/> for why — but the woven
    /// one at least takes the spell's own colour, and the ice one structurally cannot.</para>
    /// </summary>
    public class WallController : MonoBehaviour, IDestructibleObstacle, ISpellEffectDissipates
    {
        /// <summary>How long the wall takes to sublimate when its timer runs out.</summary>
        private const float MeltSeconds = 0.8f;

        /// <summary>How long the wreckage lingers after the killing blow.</summary>
        private const float ShatterFadeSeconds = 0.3f;

        public struct Setup
        {
            public float Duration;
            public Health Health;
            public BoxCollider2D Collider;
            public float Length;
            public float Height;
            public Vector2 Axis;
            public SpellElement? Element;
            /// <summary>The spell's <c>particleColor</c>. Drives the whole woven palette.</summary>
            public Color Swatch;
        }

        private enum Phase { Alive, Ending }

        private Setup _setup;
        private IWallVisual _visual;
        private bool _woven;
        private Phase _phase = Phase.Alive;
        private float _remainingTime;
        private int _lastHp = -1;
        private float _colliderFullLength;
        private bool _registered;

        public void Initialize(Setup setup)
        {
            _setup = setup;
            _remainingTime = setup.Duration;
            _colliderFullLength = setup.Collider != null ? setup.Collider.size.x : setup.Length;

            // Time-derived so no two walls are the same formation, but still one fixed seed
            // per wall, so the layout never changes under the player mid-life.
            int seed = Mathf.Abs(Time.frameCount * 73856093 ^ (int)(Time.time * 1000f));

            _woven = setup.Element != SpellElement.Ice;
            _visual = _woven
                ? ArcaneBarrierVisual.Build(transform, new ArcaneBarrierVisual.Config
                {
                    Length = setup.Length,
                    Height = setup.Height,
                    Axis = setup.Axis,
                    Seed = seed,
                    Swatch = setup.Swatch,
                })
                : (IWallVisual)IceWallVisual.Build(transform, new IceWallVisual.Config
                {
                    Length = setup.Length,
                    Height = setup.Height,
                    Axis = setup.Axis,
                    Seed = seed,
                });

            if (setup.Health != null) _lastHp = setup.Health.CurrentHp;

            DestructibleObstacleRegistry.Register(this);
            _registered = true;

            PlayOneShot(_woven ? ArcaneBarrierAudio.Create() : IceWallAudio.Create());
            CameraFeel.Cue(CameraFeelCue.CastHeavy, _setup.Axis);
        }

        /// <summary>
        /// Legacy entry point kept so any caller written against the old two-argument
        /// signature still compiles into a working — if unshaped — wall.
        /// </summary>
        public void Initialize(float duration, Health health)
            => Initialize(new Setup
            {
                Duration = duration,
                Health = health,
                Collider = GetComponentInChildren<BoxCollider2D>(),
                Length = 6f,
                Height = 1.8f,
                Axis = Vector2.right,
                // NOT default(Color): that is transparent black, and on the additive material
                // the woven rig is built from, black adds nothing at all — the barrier would
                // be invisible rather than merely uncoloured. White is the project's
                // "unauthored" sentinel and resolves to the arcane violet fallback.
                Swatch = Color.white,
            });

        private void OnDestroy()
        {
            if (!_registered) return;
            DestructibleObstacleRegistry.Unregister(this);
            _registered = false;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            _visual?.Tick(deltaTime);

            if (_phase == Phase.Ending)
            {
                if (_visual == null || _visual.MeltComplete) Destroy(gameObject);
                return;
            }

            if (_setup.Health != null && _setup.Health.IsDead) { BeginShatter(); return; }

            _remainingTime -= deltaTime;
            if (_remainingTime <= 0f)
                BeginMelt(MeltSeconds, _woven ? ArcaneBarrierAudio.Melt() : IceWallAudio.Melt());
        }

        // ── IDestructibleObstacle ───────────────────────────────────────────────────

        public Vector2 ObstaclePosition => transform.position;

        public Bounds ObstacleBounds => _setup.Collider != null
            ? _setup.Collider.bounds
            : new Bounds(transform.position, new Vector3(_setup.Length, _setup.Height, 1f));

        public bool AcceptsDamage => _phase == Phase.Alive && _setup.Health != null && !_setup.Health.IsDead;

        public void ApplyObstacleDamage(int amount, GameObject attacker, Vector2 contactPoint, SpellElement? element)
        {
            if (!AcceptsDamage || amount <= 0) return;

            _setup.Health.TakeDamage(amount, attacker, element);
            if (_setup.Health.CurrentHp == _lastHp) return;
            _lastHp = _setup.Health.CurrentHp;

            _visual?.Hit(contactPoint);
            PlayOneShot(_woven ? ArcaneBarrierAudio.Hit() : IceWallAudio.Hit(), 0.7f);
            CameraFeel.Cue(CameraFeelCue.ImpactLight, Vector2.zero);

            RefreshDamageState();
        }

        /// <summary>
        /// Push the wall's health onto the visual and shrink the collider to whatever is
        /// still standing, so what blocks is exactly what is drawn.
        /// </summary>
        private void RefreshDamageState()
        {
            if (_visual == null || _setup.Health == null) return;

            float damage01 = _setup.Health.MaxHp > 0
                ? 1f - Mathf.Clamp01(_setup.Health.CurrentHp / (float)_setup.Health.MaxHp)
                : 0f;
            _visual.SetDamage01(damage01);

            if (_setup.Collider == null) return;
            float span = Mathf.Max(0.2f, _visual.SurvivingHalfSpan() * 2f);
            var size = _setup.Collider.size;
            size.x = Mathf.Min(_colliderFullLength, span);
            _setup.Collider.size = size;
        }

        // ── exits ───────────────────────────────────────────────────────────────────

        private void BeginShatter()
        {
            if (_phase == Phase.Ending) return;
            _visual?.Shatter();
            PlayOneShot(_woven ? ArcaneBarrierAudio.Shatter() : IceWallAudio.Shatter());
            CameraFeel.Cue(CameraFeelCue.ImpactHeavy, Vector2.zero);
            BeginMelt(ShatterFadeSeconds, null);
        }

        private void BeginMelt(float seconds, AudioClip clip)
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            // Stop blocking the instant it starts to go: a barrier that is visibly melting
            // but still solid is worse than one that vanishes.
            if (_setup.Collider != null) _setup.Collider.enabled = false;
            if (_registered)
            {
                DestructibleObstacleRegistry.Unregister(this);
                _registered = false;
            }

            if (clip != null) PlayOneShot(clip, 0.8f);
            _visual?.BeginMelt(seconds);
        }

        /// <summary>
        /// The registry evicted this wall for a newer one. It has already dropped the handle,
        /// so taking ownership here does not keep the recast waiting on <c>maxInstances</c>.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;
            BeginMelt(seconds, null);
            return true;
        }

        private void PlayOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            ServiceLocator.Get<IAudioService>()?.PlaySFXAtPosition(clip, transform.position, volume);
        }
    }
}
