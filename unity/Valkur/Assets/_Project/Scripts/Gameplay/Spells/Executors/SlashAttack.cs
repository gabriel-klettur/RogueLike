using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The slash every spell of <c>SpellType.Slash</c> uses, except <c>slash_regular</c>
    /// which keeps its own authored implementation.
    ///
    /// One moving shape owns the drawing and the damage together: a target is hit on the
    /// exact frame the visible leading edge crosses it, inside the exact sector that is
    /// drawn. The family (thrust, crescent, cleave, whirl) comes from the authored arc, so
    /// a 24 degree stab, a 140 degree cleave and a 260 degree boss sweep are different
    /// attacks rather than one sprite at three scales.
    ///
    /// The timeline is wind-up, sweep, linger. Damage exists only during the sweep; the
    /// wind-up outlines the reach for the wide styles so a big swing can be read and
    /// avoided rather than merely suffered.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class SlashAttack : MonoBehaviour
    {
        /// <summary>Alpha the wind-up outline reaches just before the sweep starts.</summary>
        private const float TELEGRAPH_PEAK_ALPHA = 0.30f;

        private readonly HashSet<Health> _damaged = new HashSet<Health>();
        private readonly List<SlashRibbonMesh> _ribbons = new List<SlashRibbonMesh>(5);

        private SpellContext _context;
        private SlashProfile _profile;
        private Vector2 _direction;
        private float _age;
        private float _previousHeadAngle;
        private float _previousReach;
        private int _hitCount;
        private bool _sweepStarted;

        private SpriteRenderer _leadingGlint;
        private SpriteRenderer _originRing;
        private SpriteRenderer _groundWave;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private float[] _moteRadials;
        private Component _light;

        /// <summary>
        /// Spawns the attack at <paramref name="origin"/> facing the cast direction. The
        /// caller has already resolved the cast start, so the swing cannot detach from the
        /// point every other spell is born at.
        /// </summary>
        public static SlashAttack Spawn(SpellContext context, Vector2 origin, float radius,
                                        float arcDegrees, Color tint)
        {
            var go = new GameObject("SlashAttack");
            go.transform.position = origin;

            Vector2 direction = context.Direction.sqrMagnitude > 0.0001f
                ? context.Direction.normalized
                : Vector2.right;
            go.transform.rotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            var attack = go.AddComponent<SlashAttack>();
            attack.Initialize(context, direction, radius, arcDegrees, tint);
            return attack;
        }

        /// <summary>
        /// Draws a slash that damages nothing, for systems that resolve their own contact.
        ///
        /// The NPC basic melee is one: it is not spell-driven, it does its own overlap and
        /// its own damage, and all it ever needed from the VFX layer was the picture. It used
        /// to get a flat untextured disc instead — see <c>MeleeCombat</c> — which is why a
        /// monster's punch painted a coloured ball on the floor while every spell in the game
        /// drew a crescent.
        /// </summary>
        public static SlashAttack SpawnVisual(Transform caster, Vector2 origin, Vector2 direction,
                                              float radius, float arcDegrees, Color tint)
        {
            // A context with no spell and no target layers: every damage path in
            // SlashAttack.Damage guards on exactly those two, so nothing is dealt.
            var context = new SpellContext
            {
                Caster = caster,
                Direction = direction,
                TargetLayers = 0,
            };
            return Spawn(context, origin, radius, arcDegrees, tint);
        }

        /// <summary>Pure sector predicate shared by gameplay and regression tests.</summary>
        public static bool IsInsideSector(Vector2 origin, Vector2 forward, Vector2 point,
                                          float radius, float arcDegrees)
        {
            Vector2 delta = point - origin;
            if (delta.sqrMagnitude > radius * radius) return false;
            if (delta.sqrMagnitude <= 0.0001f) return true;
            if (forward.sqrMagnitude <= 0.0001f) forward = Vector2.right;
            return Mathf.Abs(Vector2.SignedAngle(forward.normalized, delta.normalized))
                   <= arcDegrees * 0.5f + 0.001f;
        }

        /// <summary>Style a given arc resolves to. Exposed so tests can pin the boundaries.</summary>
        public static SlashStyle StyleFor(float arcDegrees)
            => SlashProfile.Build(arcDegrees, 1f, 0f, Color.white).Style;

        private void Initialize(SpellContext context, Vector2 direction, float radius,
                                float arcDegrees, Color tint)
        {
            _context = context;
            _direction = direction;
            float lifetime = context.Spell != null ? context.Spell.lifetime : 0f;
            _profile = SlashProfile.Build(arcDegrees, radius, lifetime, tint);
            _previousHeadAngle = -_profile.HalfArc - 0.01f;
            _previousReach = 0f;

            BuildVisuals();
        }

        private void Update()
        {
            _age += Time.deltaTime;

            if (_age < _profile.SweepStart)
            {
                UpdateWindup(Mathf.Clamp01(_age / Mathf.Max(0.0001f, _profile.Windup)));
            }
            else
            {
                if (!_sweepStarted) BeginSweep();

                float sweep01 = Mathf.Clamp01((_age - _profile.SweepStart) /
                                              Mathf.Max(0.0001f, _profile.Sweep));
                float eased = SmoothSwing(sweep01);
                float linger = _age <= _profile.SweepEnd
                    ? 1f
                    : 1f - Mathf.Clamp01((_age - _profile.SweepEnd) /
                                         Mathf.Max(0.01f, _profile.Total - _profile.SweepEnd));

                UpdateActive(eased, sweep01, linger);
                AdvanceDamage(eased);
            }

            if (_age >= _profile.Total) Destroy(gameObject);
        }

        /// <summary>
        /// The swing announces itself when the blade actually moves, not when the object is
        /// created — otherwise a telegraphed cleave whooshes a third of a second before
        /// anything happens.
        /// </summary>
        private void BeginSweep()
        {
            _sweepStarted = true;
            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_slash_swing");
            SpawnSwingPreset();
        }

        private void SpawnSwingPreset()
        {
            if (_context.Spell == null) return;
            string preset = _context.Spell.vfxPreset;
            if (string.IsNullOrEmpty(preset)) return;

            var manager = VFX.VFXManager.Instance;
            if (manager == null) return;

            // Placed on the mid-arc at three quarters of the reach: far enough out to sit
            // on the blade rather than the caster, still inside the lit stretch.
            Vector3 at = transform.position + transform.rotation *
                         new Vector3(_profile.Radius * 0.75f, 0f, 0f);
            GameObject spawned = manager.SpawnParticlePreset(
                preset, at, _profile.Sweep + _profile.Linger);

            // The slash presets emit through a cone. Left unrotated it fires in whatever
            // direction the preset was authored in, which is how the accent sparks ended up
            // pointing away from the swing that spawned them.
            if (spawned != null) spawned.transform.rotation = transform.rotation;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _ribbons.Count; i++) _ribbons[i]?.Dispose();
            _ribbons.Clear();
            for (int i = 0; i < _lances.Count; i++) _lances[i]?.Dispose();
            _lances.Clear();
            _telegraph?.Dispose();
            _telegraph = null;
        }

        private static float SmoothSwing(float t) => t * t * (3f - 2f * t);
    }
}
