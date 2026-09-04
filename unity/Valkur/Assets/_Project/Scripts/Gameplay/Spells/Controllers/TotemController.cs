using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Healing totem with epic green visuals: rotating ground rune, halo, rising
    /// sparkle particles, dynamic Light2D pulse on heal tick.
    /// Mirrors Python's TotemComponent (kind=heal).
    /// </summary>
    public class TotemController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _healPerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Transform _owner;

        private AreaFXRig _rig;
        private float _pulse;

        /// <summary>Player(8) plus Pickup-free friendly space. One player, so one layer.</summary>
        private static readonly LayerMask FriendlyLayers = 1 << 8;
        // Borrowed from PhysicsScratch, which owns the reset Domain-Reload-OFF demands.

        public void Initialize(float duration, float radius, int healPerTick, float tickPeriod, Transform owner)
        {
            _remaining = duration;
            _radius = radius;
            _healPerTick = healPerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _owner = owner;

            BuildVisual();

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_totem_create"))
                audio.PlaySfxById("spell_totem_create");
        }

        private void BuildVisual()
        {
            // The root stays at IDENTITY. It used to be scaled by the radius immediately
            // after AreaFXRig.Attach had already sized every child by that same radius, so
            // the rig was sized twice -- and, worse, the Light2D hanging under it rendered at
            // `authored x lossyScale`. That is the exact pair of lines that made the vortex's
            // light reach an effective 367 world units.
            _rig = AreaFXRig.Attach(transform, AreaPalette.HealingTotem(), _radius);

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _rig?.Destroy();
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
                _pulse = 1f;
            }

            Animate();
        }

        private void Animate()
        {
            float t = Time.time;
            _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 2f);
            float baseFlick = 0.85f + 0.15f * Mathf.Sin(t * 3f);
            float fade = (_remaining < 2f) ? Mathf.Clamp01(_remaining * 0.5f) : 1f;

            if (_rig != null)
            {
                if (_rig.Rune != null)
                    _rig.Rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * _rig.Palette.runeSpinSpeed);
                if (_rig.Core != null)
                    _rig.Core.transform.localScale = Vector3.one * _rig.Palette.coreScale * (1f + 0.30f * _pulse);
                if (_rig.Glow != null)
                    _rig.Glow.transform.localScale = Vector3.one * _rig.Palette.glowScale * (1f + 0.20f * _pulse);
                _rig.SetGlobalAlpha(fade * baseFlick);
                _rig.SetIntensity(_rig.Palette.lightIntensity * baseFlick + 1.2f * _pulse);
            }
        }

        /// <summary>
        /// Heal everything friendly standing in the circle.
        ///
        /// <para>It used to heal exactly one entity -- <c>_owner</c> -- so a "healing totem"
        /// was a stationary self-heal with a decorative pole, and the radius it drew on the
        /// ground promised an area that nothing consulted. Anyone else inside the ring got
        /// nothing, silently, which is the shape of defect this project has recorded eleven
        /// times: authored, drawn, and read by no gameplay code.</para>
        ///
        /// <para>"Friendly" is answered by the Player LAYER plus the caster, which is the
        /// cheapest correct answer in a game with one player. Growing a faction system for
        /// this would be a second allegiance model beside <c>AlliedUnit</c>, and two models
        /// eventually disagree.</para>
        /// </summary>
        private void HealTick()
        {
            bool prevHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position, _radius, PhysicsScratch.TotemHeal, FriendlyLayers);
            Physics2D.queriesHitTriggers = prevHitTriggers;

            int healed = 0;
            for (int i = 0; i < count; i++)
            {
                var col = PhysicsScratch.TotemHeal[i];
                if (col == null) continue;
                var health = col.GetComponent<Health>() ?? col.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;
                if (health.CurrentHp >= health.MaxHp) continue;   // nothing to give
                health.Heal(_healPerTick);
                healed++;
            }

            // The owner is healed even when it carries no collider on the friendly layers --
            // a test double, or a caster whose body collider is disabled mid-dash. Guarded so
            // it is never healed twice in one tick.
            if (_owner != null && healed == 0)
            {
                float dist = Vector2.Distance(transform.position, _owner.position);
                if (dist <= _radius)
                {
                    var ownerHealth = _owner.GetComponent<Health>();
                    if (ownerHealth != null && !ownerHealth.IsDead)
                    {
                        ownerHealth.Heal(_healPerTick);
                        healed++;
                    }
                }
            }

            if (healed <= 0) return;

            // Gated on HasSfx: AudioCatalog.asset contains no spell_* id at all, so an
            // ungated PlaySfxById pushes one warning per id into a console this project
            // requires to be clean. The catalog stays the better answer the day a recorded
            // set is authored.
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_totem_heal_tick"))
                audio.PlaySfxById("spell_totem_heal_tick");
        }
    }
}
