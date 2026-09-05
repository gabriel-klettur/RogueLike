using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A healing totem: an opaque stone shaft standing in the world, a ring pinned to the circle
    /// it actually heals, and a pulse on every tick.
    ///
    /// <para><b>THE TICK IS THE EVENT AND IT IS SYNCHRONISED TO THE HEAL.</b>
    /// <see cref="SanctuaryPillarFX.Pulse"/> is called from the sweep, not from a decorative
    /// timer, so the beat carries literal mechanical information and the player can count the
    /// ticks. That is worth more than a busier idle: a ten-second field that is busy the whole
    /// time is a ten-second distraction, and it leaves the tick nothing to stand out against.
    /// The pulse fires even when nothing needed mending, because hiding it would make the totem
    /// look broken exactly when the party is at full health.</para>
    ///
    /// <para><b>IT HEALS AN AREA.</b> It used to heal exactly one entity — <c>_owner</c> — so a
    /// "healing totem" was a stationary self-heal with a decorative pole, and the radius it drew
    /// promised an area nothing consulted. "Friendly" is answered by the Player LAYER plus the
    /// caster, which is the cheapest correct answer in a game with one player; growing a faction
    /// system here would be a second allegiance model beside <c>AlliedUnit</c>, and two models
    /// eventually disagree.</para>
    /// </summary>
    public class TotemController : MonoBehaviour, ISpellEffectDissipates
    {
        private float _remaining;
        private float _radius;
        private int _healPerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private Transform _owner;

        private SanctuaryPillarFX _pillar;

        /// <summary>Seconds of fade at the end, so the pillar sinks instead of blinking out.</summary>
        private const float FADE_OUT_SECONDS = 1.2f;

        private bool _dissipating;
        private float _dissipateWindow = FADE_OUT_SECONDS;

        /// <summary>Player(8). One player, so one layer.</summary>
        private static readonly LayerMask FriendlyLayers = 1 << 8;
        // The query buffer is borrowed from PhysicsScratch, which owns the reset that
        // Domain-Reload-OFF demands.

        /// <param name="tint">
        /// The spell's own swatch. Null keeps the historical gold, so a caller that predates the
        /// colour becoming authorable draws exactly as it used to.
        /// </param>
        public void Initialize(float duration, float radius, int healPerTick, float tickPeriod,
            Transform owner, Color? tint = null, SpellElement? element = null)
        {
            _remaining = duration;
            _radius = Mathf.Max(0.25f, radius);
            _healPerTick = healPerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _owner = owner;

            BuildVisual(tint ?? new Color(1f, 0.9f, 0.3f, 1f), element);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_totem_create"))
                audio.PlaySfxById("spell_totem_create");
        }

        private void BuildVisual(Color tint, SpellElement? element)
        {
            // The colour goes through RecolouredTo, which already handles all three meanings of
            // particleColor in the right order — opaque white is the "nobody authored this"
            // sentinel, an achromatic value is a request for the ABSENCE of colour, and
            // near-black adds nothing on an additive material. Reading the raw field instead is
            // what lights a grey spell pink.
            var palette = ElementPalette.For(element ?? SpellElement.Light).RecolouredTo(tint);

            // The root stays at IDENTITY. It used to be scaled by the radius immediately after
            // AreaFXRig.Attach had already sized every child by that same radius, so the rig was
            // sized twice — and the Light2D hanging under it rendered at `authored x lossyScale`,
            // the pair of lines that once made a vortex light reach an effective 367 units.
            _pillar = SanctuaryPillarFX.Attach(transform, _radius, palette);

            // Anything the executor left on the root — the legacy triangle sprite, if a caller
            // still adds one — is not part of this rig and must not draw over the shaft.
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _pillar?.Destroy();
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
            }

            _pillar?.Tick(Time.deltaTime, Mathf.Clamp01(_remaining / _dissipateWindow));
        }

        /// <summary>
        /// Heal everything friendly standing in the circle, then pulse.
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
                // A ripple over the body that was mended, so the player can see WHO the circle
                // reached rather than only that it fired.
                _pillar?.Ripple(health.transform.position);
            }

            // The owner is healed even when it carries no collider on the friendly layers — a
            // test double, or a caster whose body collider is disabled mid-dash. Guarded so it
            // is never healed twice in one tick.
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
                        _pillar?.Ripple(_owner.position);
                    }
                }
            }

            // Before the early return: see the class doc. A tick that finds everybody at full
            // health is still a tick, and a totem that goes quiet then looks broken.
            _pillar?.Pulse(healed);

            if (healed <= 0) return;

            // Gated on HasSfx: AudioCatalog.asset contains no spell_* id at all, so an ungated
            // PlaySfxById pushes one warning per id into a console this project requires to be
            // clean. The catalog stays the better answer the day a recorded set is authored.
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null && audio.HasSfx("spell_totem_heal_tick"))
                audio.PlaySfxById("spell_totem_heal_tick");
        }

        /// <summary>
        /// A persistent effect has FIVE exit paths — its own timer, eviction by
        /// <c>maxInstances</c>, a zone change, its caster dying, and scene unload — and only the
        /// first runs any of this object's code before the GameObject is gone. Compressing
        /// <c>_remaining</c> rather than starting a second timeline means the close runs through
        /// exactly the same code as a natural expiry.
        /// </summary>
        public bool BeginDissipate(float seconds)
        {
            if (!isActiveAndEnabled) return false;
            if (_dissipating) return true;

            _dissipating = true;
            // A totem on its way out must not still be healing: the caller has already dropped
            // the handle, so as far as maxInstances is concerned this one is gone.
            _healPerTick = 0;
            _dissipateWindow = Mathf.Max(0.05f, seconds);
            _remaining = Mathf.Min(_remaining, _dissipateWindow);
            return true;
        }

        private void OnDestroy()
        {
            _pillar?.Destroy();
            _pillar = null;
        }
    }
}
