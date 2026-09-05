using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Burns the ground the caster walks over: drops an independent <see cref="CinderPatchFX"/>
    /// behind them for as long as the spell runs, and damages whatever stands in any of them.
    ///
    /// <para>WHY A SECOND CONTROLLER AND NOT A PUDDLE. <c>cinder_trail</c> authors
    /// <c>followCaster: 1</c> and <c>ttl: 3.5</c> and BOTH reached zero code on this path, so
    /// the spell was one static 1.2-unit disc parked at the cursor for eight seconds — the same
    /// disc <c>blizzard</c> drew, in the same colour, because <c>PuddleController</c> had one
    /// palette. A trail is not a puddle with a different tint; it is a different TOPOLOGY, and
    /// no recolouring of a single circle can express many small independent fires.</para>
    ///
    /// <para>NEVER ON A STATIONARY CASTER. The drop is gated on DISTANCE first and time second.
    /// A pure timer piles eight patches on one tile while the player stands still, which is
    /// both free damage and a picture that says nothing about movement — and this is the one
    /// spell in the game that is supposed to reward moving.</para>
    ///
    /// <para>A VICTIM IS DAMAGED ONCE PER TICK, however many patches they are standing in. The
    /// patches deliberately overlap, so a per-patch sweep would multiply the spell's authored
    /// damage by however many circles happen to cover a tile — a number nobody authored and
    /// nothing states.</para>
    /// </summary>
    public class CinderTrailController : MonoBehaviour, ISpellEffectDissipates
    {
        /// <summary>
        /// Centre-to-centre spacing between consecutive patches, as a multiple of the patch
        /// radius. At 5/3 two neighbours overlap by a third of a radius: close enough that the
        /// trail reads as one continuous burning line, far enough that it is visibly made of
        /// separate fires rather than being one smeared shape.
        /// </summary>
        private const float PATCH_SPACING_FRAC = 5f / 3f;

        /// <summary>Floor on the interval between drops. The distance gate is the real rule;
        /// this stops a dash laying its whole trail inside two frames.</summary>
        private const float DROP_INTERVAL = 0.35f;

        /// <summary>
        /// Shortest gap between two refreshes of the same status on the same victim.
        /// <c>StatusEffectManager.Apply</c> REPLACES an effect of the same type, so refreshing
        /// on the damage clock is a full remove-and-reapply that tears down and rebuilds the
        /// victim's tint layer twice a second for no extra damage. <c>PuddleController</c>
        /// records the same rule.
        /// </summary>
        private const float STATUS_PERIOD = 0.85f;

        /// <summary>Seconds of fade the whole trail is given as the emitter runs out.</summary>
        private const float FADE_OUT_SECONDS = 1f;

        private readonly List<CinderPatchFX> _patches = new List<CinderPatchFX>();

        /// <summary>Instance ids already hit this tick. A field rather than a local, because a
        /// fresh set per tick is an allocation twice a second for eight seconds.</summary>
        private readonly HashSet<int> _struckThisTick = new HashSet<int>();

        private Transform _caster;
        private GameObject _casterGo;
        private float _remaining;
        private float _patchRadius;
        private float _patchTtl;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private float _statusTimer;
        private float _dropTimer;
        private LayerMask _targetLayers;
        private SpellElement? _damageElement;
        private StatusApplication[] _statusApplications;
        private ElementPalette _palette;

        private Vector3 _lastDropPosition;
        private bool _hasDropped;
        private bool _emitting = true;

        /// <summary>How many patches are alive. Read by tests, which cannot count them
        /// otherwise, and by nothing in production.</summary>
        public int LivePatchCount => _patches.Count;

        internal void Initialize(Transform caster, float duration, float patchRadius, float patchTtl,
            int damagePerTick, float tickPeriod, LayerMask targetLayers,
            SpellElement? damageElement, StatusApplication[] statusApplications,
            ElementPalette palette)
        {
            _caster = caster;
            _casterGo = caster != null ? caster.gameObject : null;
            _remaining = Mathf.Max(0.1f, duration);
            _patchRadius = Mathf.Max(0.25f, patchRadius);
            _patchTtl = Mathf.Max(0.4f, patchTtl);
            _damagePerTick = damagePerTick;
            _tickPeriod = Mathf.Max(0.05f, tickPeriod);
            _targetLayers = targetLayers;
            _damageElement = damageElement;
            _statusApplications = statusApplications;
            _palette = palette;

            // Zero, not the period: a control effect that arrives one tick into the field is a
            // control effect the victim has already walked out of.
            _tickTimer = 0f;
            _statusTimer = 0f;
            _dropTimer = 0f;

            // One patch under the caster's feet at once, so the spell has a picture on the
            // frame it is cast even if the player has not taken a step yet.
            if (_caster != null) Drop(_caster.position, 0f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_emitting)
            {
                _remaining -= dt;
                if (_remaining <= 0f) _emitting = false;
                else TryDrop(dt);
            }

            float fade = _emitting ? Mathf.Clamp01(_remaining / FADE_OUT_SECONDS) : 1f;

            _tickTimer -= dt;
            _statusTimer -= dt;
            if (_tickTimer <= 0f)
            {
                bool refreshStatus = _statusTimer <= 0f;
                DamageTick(refreshStatus);
                if (refreshStatus) _statusTimer = STATUS_PERIOD;
                _tickTimer = _tickPeriod;
            }

            for (int i = _patches.Count - 1; i >= 0; i--)
            {
                var patch = _patches[i];
                patch.Tick(dt, fade);
                if (!patch.IsSpent) continue;
                patch.Destroy();
                _patches.RemoveAt(i);
            }

            // The trail outlives its emitter: the last patch still has to burn down and its
            // scorch still has to fade. Destroying on `duration` would cut all of that.
            if (!_emitting && _patches.Count == 0) Destroy(gameObject);
        }

        private void TryDrop(float dt)
        {
            _dropTimer -= dt;
            if (_caster == null || _casterGo == null) return;

            Vector3 here = _caster.position;
            float moved = _hasDropped ? Vector3.Distance(here, _lastDropPosition) : float.MaxValue;
            float spacing = _patchRadius * PATCH_SPACING_FRAC;

            // Distance FIRST. See the class doc: a timer alone piles the whole trail on one tile.
            if (moved < spacing) return;
            if (_dropTimer > 0f) return;

            // The patch lights a beat after the foot has left it, and the beat is proportional
            // to how far the caster has already travelled — so the flame visibly chases the
            // footsteps instead of the whole line appearing at once.
            float delay = CinderPatchFX.IGNITION_SECONDS_PER_UNIT * Mathf.Min(moved, spacing * 2f);
            Drop(here, delay);
            _dropTimer = DROP_INTERVAL;
        }

        private void Drop(Vector3 worldPosition, float ignitionDelay)
        {
            _patches.Add(CinderPatchFX.Spawn(transform, worldPosition, _patchRadius,
                _patchTtl, ignitionDelay, _palette));
            _lastDropPosition = worldPosition;
            _hasDropped = true;
        }

        /// <summary>
        /// Hurt everything standing in any BURNING patch, once each. A patch that has not
        /// ignited yet cannot burn anybody, and neither can a scorch mark left behind after
        /// the flame is out — which is what <see cref="CinderPatchFX.IsBurning"/> states.
        /// </summary>
        private void DamageTick(bool refreshStatus)
        {
            if (_damagePerTick <= 0 || _patches.Count == 0) return;
            _struckThisTick.Clear();

            for (int i = 0; i < _patches.Count; i++)
            {
                var patch = _patches[i];
                if (!patch.IsBurning) continue;

                var hits = Physics2D.OverlapCircleAll(patch.Position, patch.Radius, _targetLayers);
                for (int h = 0; h < hits.Length; h++)
                {
                    var health = hits[h] != null ? hits[h].GetComponentInParent<Health>() : null;
                    if (health == null || health.IsDead) continue;
                    if (!_struckThisTick.Add(health.GetInstanceID())) continue;

                    health.TakeDotDamage(_damagePerTick, _casterGo, _damageElement);
                    if (refreshStatus)
                        StatusApplicationFactory.ApplyAll(_statusApplications, health.gameObject, _casterGo);
                }
            }
        }

        /// <summary>
        /// Eviction, a zone change, the caster dying or a scene unload all reach this object
        /// through <c>SpellEffectRegistry.DestroySafely</c>, and only <c>OnDestroy</c> is on all
        /// five paths — so without this the trail is CUT rather than burnt out. With
        /// <c>maxInstances: 1</c> and a 16 s cooldown against an 8 s duration that is rare, but
        /// a zone change mid-trail is not.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;

            _emitting = false;
            _damagePerTick = 0;

            // Snuff every patch over the window rather than letting each run its own ttl: the
            // caller has already dropped the handle, so this object no longer counts against
            // maxInstances and must not outstay the seconds it was given.
            float window = Mathf.Max(0.05f, seconds);
            StartCoroutine(SnuffRoutine(window));
            return true;
        }

        private System.Collections.IEnumerator SnuffRoutine(float window)
        {
            float t = 0f;
            while (t < window)
            {
                t += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(t / window);
                for (int i = 0; i < _patches.Count; i++) _patches[i].Tick(Time.deltaTime, fade);
                yield return null;
            }

            for (int i = 0; i < _patches.Count; i++) _patches[i].Destroy();
            _patches.Clear();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _patches.Count; i++) _patches[i].Destroy();
            _patches.Clear();
        }
    }
}
