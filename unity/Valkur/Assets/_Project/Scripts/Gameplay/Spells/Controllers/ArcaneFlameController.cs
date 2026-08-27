using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Persistent arcane flame zone: a hole burned in the floor that light pours out of.
    ///
    /// Composition, bottom to top: a scorch stain and a rotating rune ring painted on
    /// <see cref="SortingConfig.LAYER_FLOOR_DECALS"/>, and above them — on
    /// <see cref="SortingConfig.LAYER_VFX"/>, where entities standing in the fire no
    /// longer occlude it — an ADDITIVE volume (haze / halo / glow / core / hot core /
    /// star accent) plus two particle systems. A Point <c>Light2D</c> built to the same
    /// body-plus-additive-core recipe every torch in the world uses makes the ground
    /// genuinely violet.
    ///
    /// Three invariants this file exists to hold:
    ///
    /// 1. THE DRAWN EDGE IS THE DAMAGE EDGE. <see cref="ElementalSprites.Ring"/>'s bright
    ///    band peaks at normalized radius 0.78 of a sprite that is exactly 1 world unit
    ///    across, so a ring child scaled to <c>_radius / 0.39</c> puts its crest on
    ///    <c>_radius</c> at ANY radius. Before this the crest sat at 60 % of the damage
    ///    circle and 46 % of the hurting area carried no readable pixel.
    /// 2. THE ROOT TRANSFORM IS NEVER SCALED. Every child carries an absolute world size
    ///    derived from <c>_radius</c>. A scaled root is what made the old light render
    ///    2.5x its authored radius and would need a counter-scale to undo.
    /// 3. NOTHING POPS. An ignition ramp opens the effect and a dissipation ramp closes
    ///    it — including on eviction, which at a 2 s cooldown against a 5 s duration is
    ///    the COMMON exit, not the edge case (see <see cref="ISpellEffectDissipates"/>).
    /// </summary>
    public partial class ArcaneFlameController : MonoBehaviour, ISpellEffectDissipates
    {
        // ── Envelope ────────────────────────────────────────────────────────────
        internal const float IgnitionSeconds   = 0.14f;
        internal const float SettleSeconds     = 0.20f;
        internal const float DissipateSeconds  = 0.60f;
        /// <summary>Compressed close used when the registry evicts a live flame.</summary>
        internal const float EvictDissipateSeconds = 0.28f;

        // ── Geometry, as fractions of the damage radius ──────────────────────────
        // Every ElementalSprites sprite is exactly 1x1 world unit (Sprite.Create is
        // handed the texture size as pixelsPerUnit), so a child's localScale IS its
        // world DIAMETER and `scale * 0.5` is its world radius.
        internal const float RingCrestNormalized = 0.78f;   // ElementalSprites.RingPx
        internal const float ScorchRadiusMul  = 0.98f;
        internal const float HazeRadiusMul    = 1.16f;
        internal const float HaloRadiusMul    = 0.96f;
        internal const float GlowRadiusMul    = 0.62f;
        internal const float CoreRadiusMul    = 0.30f;
        internal const float HotCoreRadiusMul = 0.13f;
        internal const float AccentRadiusMul  = 0.34f;

        // ── Runtime state ───────────────────────────────────────────────────────
        private float _remaining;
        private float _age;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;
        private GameObject _caster;
        private SpellElement? _element;

        private ElementPalette _palette;

        private bool _dissipating;
        private float _dissipateSeconds = DissipateSeconds;
        private float _tail;
        private bool _emittersStopped;

        /// <summary>Decays to 0 after a tick that CONNECTED. Never armed by an empty tick.</summary>
        private float _pulsePhase;
        /// <summary>Per-layer Perlin offsets, so no two layers ever breathe in lockstep.</summary>
        private float _flickA, _flickB, _flickC;

        // OverlapCircle returns one entry per COLLIDER, and an entity may carry several
        // (body + hitbox). Without this set a two-collider monster took the tick twice.
        private readonly HashSet<Health> _tickVictims = new HashSet<Health>();
        private readonly Collider2D[] _overlapBuffer = new Collider2D[64];

        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod,
            LayerMask targetLayers, GameObject caster = null, SpellElement? element = null)
        {
            _remaining = Mathf.Max(0.1f, duration);
            _radius = Mathf.Max(0.25f, radius);
            _damagePerTick = damagePerTick;
            _tickPeriod = Mathf.Max(0.05f, tickPeriod);
            // Offset the first tick so the ignition beat is seen before the first flare,
            // instead of both landing on frame one.
            _tickTimer = Mathf.Min(_tickPeriod, IgnitionSeconds + SettleSeconds);
            _targetLayers = targetLayers;
            _caster = caster;
            _element = element;

            // The element is Arcane for this spell either from the SO field or from
            // ProjectileExecutor's legacy key table; the ?? keeps a null from silently
            // producing the all-zero `default(ElementPalette)`, whose scales are 0 and
            // whose sprites are null — that renders nothing at all rather than throwing.
            _palette = ElementPalette.For(_element ?? SpellElement.Arcane);

            _flickA = Random.Range(0f, 100f);
            _flickB = Random.Range(0f, 100f);
            _flickC = Random.Range(0f, 100f);

            // Identity root. See invariant 2 in the class doc.
            transform.localScale = Vector3.one;

            BuildVisual();
            AttachLight();
            SubscribeDayNight();

            // Seat the envelope BEFORE the first render. BuildVisual paints every layer at
            // its authored alpha and Update does not run until the next frame — so without
            // this the effect draws one full-strength frame and only THEN starts its
            // ignition ramp. One frame is 16 ms of exactly the pop this envelope exists to
            // remove, and at _age 0 the ramp evaluates to alpha 0.
            AnimateVisuals(0f);
            AnimateLight();

            PlayGatedSfx("spell_arcane_flame_cast", 1f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            if (_tail > 0f)
            {
                // Active life is over; only the last motes are still drifting out.
                _tail -= dt;
                if (_tail <= 0f) Destroy(gameObject);
                return;
            }

            _remaining -= dt;

            if (!_dissipating && _remaining <= _dissipateSeconds)
                _dissipating = true;

            if (_dissipating) StopEmittersOnce();

            if (_remaining <= 0f)
            {
                // Do not delete live particles mid-air — let the stopped emitters run
                // their remaining motes out. Their own colorOverLifetime fades them.
                _tail = Mathf.Max(0f, MoteLifetime - _dissipateSeconds);
                HideSpriteLayers();
                if (_tail <= 0f) Destroy(gameObject);
                return;
            }

            // A dissipating flame is leaving, not burning. Damage stops with the visual.
            if (!_dissipating)
            {
                _tickTimer -= dt;
                if (_tickTimer <= 0f)
                {
                    _tickTimer = _tickPeriod;
                    int hits = DamageTick();
                    if (hits > 0) OnTickConnected(hits);
                }
            }

            _pulsePhase = Mathf.Max(0f, _pulsePhase - dt * 4f);

            SweepMarks();
            AnimateVisuals(dt);
            AnimateLight();
        }

        /// <summary>Damages everything in the circle once. Returns how many entities were hit.</summary>
        private int DamageTick()
        {
            _tickVictims.Clear();
            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position, _radius, _overlapBuffer, _targetLayers);

            for (int i = 0; i < count; i++)
            {
                var col = _overlapBuffer[i];
                if (col == null) continue;
                var health = col.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;
                if (!_tickVictims.Add(health)) continue;   // second collider on the same body
                health.TakeDotDamage(_damagePerTick, _caster, _element);
            }

            MarkVictims();
            return _tickVictims.Count;
        }

        /// <summary>
        /// A connecting tick redraws the danger circle. Deliberately NOT wired to
        /// <c>GameEvents.OnHitDealt</c> — <c>SpellHitReportingTests</c> excludes continuous
        /// ground effects on purpose, because a self-ticking hazard would keep the combo
        /// alive without the player attacking.
        /// </summary>
        private void OnTickConnected(int hits)
        {
            _pulsePhase = 1f;
            SpawnBoundaryRing();
            EmitTickBurst(hits);
            PlayGatedSfx("spell_arcane_flame_tick", 0.6f);
            // No camera cue per tick: ImpactLight's authored min interval is 0.08 s, far
            // below this beat, so the throttle would not save it and the only backstop
            // left would be MaxTraumaPerSecond. The cast punches; the ticks do not.
        }

        // ── ISpellEffectDissipates ──────────────────────────────────────────────

        /// <summary>
        /// Take ownership of our own death so an eviction closes gracefully instead of
        /// vanishing in one frame. The registry has already dropped our handle by the
        /// time this runs, so a dissipating flame no longer counts against
        /// <c>maxInstances</c> and the recast that evicted us is not refused.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (this == null || !isActiveAndEnabled) return false;
            if (_dissipating) return true;

            _dissipateSeconds = Mathf.Max(0.05f, seconds);
            _remaining = Mathf.Min(_remaining, _dissipateSeconds);
            _dissipating = true;
            StopEmittersOnce();
            return true;
        }

        private void StopEmittersOnce()
        {
            if (_emittersStopped) return;
            _emittersStopped = true;
            StopEmitters();
        }

        private void OnDestroy()
        {
            // OnDestroy is the ONLY callback reached on all five exit paths (duration end,
            // registry eviction, zone change, caster death, scene unload). OnDisable is
            // not a substitute — it also fires on a plain SetActive(false).
            UnsubscribeDayNight();
            // Must run here, not on the dissipation timeline: on four of the five paths
            // there IS no dissipation timeline, and a mark left behind is permanent.
            ClearAllMarks();
        }

        private void PlayGatedSfx(string id, float volume)
        {
            var audio = ServiceLocator.Get<IAudioService>();
            // Gate on HasSfx. Neither arcane id exists in AudioCatalog.asset yet, and a
            // blind PlaySfxById warns once per id — which for a 0.6 s tick is a warning
            // per cast on a spell that is silent anyway. When the clips land this starts
            // working with no further code change.
            if (audio == null || !audio.HasSfx(id)) return;
            if (volume >= 1f) audio.PlaySfxById(id);
            else audio.PlaySfxById(id, volume);
        }
    }
}
