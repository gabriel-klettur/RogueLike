using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Persistent ground field: ticks damage on everything standing inside <c>radius</c>,
    /// applies the spell's authored status effects, and drives whatever visual it was
    /// given.
    ///
    /// <para>THE VISUAL IS THE CALLER'S CHOICE. Passing null keeps the historical
    /// behaviour — <see cref="AreaFXRig"/> with the lava palette, which is what a puddle
    /// wants. Passing an <see cref="IGroundFieldVisual"/> means the owner brought its own
    /// rig, and this class then neither builds one nor scales the root. It used to build
    /// the disc rig unconditionally, so the root field drew four ORANGE sprites and an
    /// orange light underneath its own green stems.</para>
    ///
    /// <para>STATUS RUNS ON ITS OWN CLOCK. <c>StatusEffectManager.Apply</c> REPLACES an
    /// effect of the same type, so re-applying it on the damage clock is a full
    /// remove-and-reapply: at a <c>tickPeriod</c> of 0.3 that tore down and rebuilt the
    /// victim's tint layer and fired both lifecycle events three times a second, for no
    /// extra damage and no extra duration. Refresh a status on its own timer, never on
    /// the damage timer.</para>
    /// </summary>
    public class PuddleController : MonoBehaviour
    {
        private float _remaining;
        private float _radius;
        private int _damagePerTick;
        private float _tickPeriod;
        private float _tickTimer;
        private LayerMask _targetLayers;
        private string _element;
        private GameObject _caster;
        private SpellElement? _damageElement;
        private StatusApplication[] _statusApplications;

        private AreaFXRig _rig;
        private IGroundFieldVisual _ownVisual;
        private float _pulse;
        private float _statusTimer;

        /// <summary>
        /// Shortest gap between two refreshes of the same status on the same victim. Long
        /// enough that a 1.1 s root is refreshed about once per hold rather than three
        /// times a second, short enough that standing in the field keeps you held.
        /// </summary>
        private const float STATUS_PERIOD = 0.85f;

        /// <summary>
        /// Seconds of fade the owned visual is given at the end, so the field sinks
        /// instead of being cut off on one frame.
        /// </summary>
        private const float FADE_OUT_SECONDS = 1f;

        /// <param name="ownVisual">
        /// The rig the caller already built on this GameObject, or null to take the shared
        /// <see cref="AreaFXRig"/>. A non-null value also suppresses the root scale below.
        /// </param>
        public void Initialize(float duration, float radius, int damagePerTick, float tickPeriod,
            LayerMask targetLayers, string element, GameObject caster = null,
            SpellElement? damageElement = null, StatusApplication[] statusApplications = null,
            IGroundFieldVisual ownVisual = null)
        {
            _remaining = duration;
            _radius = radius;
            _damagePerTick = damagePerTick;
            _tickPeriod = tickPeriod;
            _tickTimer = 0f;
            _targetLayers = targetLayers;
            _element = element;
            _caster = caster;
            _damageElement = damageElement;
            _statusApplications = statusApplications;
            _ownVisual = ownVisual;
            // Zero, not STATUS_PERIOD: a control effect that arrives one period into the
            // field is a control effect the victim has already walked out of.
            _statusTimer = 0f;

            BuildVisual();

            // PlaySfxById warns once per unresolved id BY DESIGN — an explicit id that
            // fails to resolve is a data bug. AudioCatalog.asset holds no `spell_*` id at
            // all, so calling it blind pushed a warning into a console this project
            // requires to be clean, on the first field of every session. HasSfx is the
            // documented way to ask for "a sound named after this, if one was ever
            // authored"; what actually makes noise today is the caller's own synthesised
            // one-shot, the same arrangement IceWallAudio and ShieldAudio use.
            var audio = ServiceLocator.Get<IAudioService>();
            string sfxId = element == "lava" ? "spell_puddle_lava_cast" : "spell_puddle_cast";
            if (audio != null && audio.HasSfx(sfxId)) audio.PlaySfxById(sfxId);
        }

        private void BuildVisual()
        {
            // The owner brought its own rig: build nothing, and above all do NOT scale the
            // root. Every child of an owned rig carries an absolute world size, and a root
            // scale multiplies a Light2D radius and a ParticleSystem shape along with the
            // sprites — which is how the root field came to emit over 1.91 units inside a
            // 1.5-unit damage circle.
            if (_ownVisual != null) return;

            // One palette today. The branch that used to stand here tested the element and
            // returned LavaPuddle() from BOTH sides, so it read as a choice and was not
            // one; a real PoisonPuddle/WaterPuddle belongs in AreaPalette when it exists.
            var palette = AreaPalette.LavaPuddle();
            _rig = AreaFXRig.Attach(transform, palette, _radius);
            transform.localScale = Vector3.one * Mathf.Max(0.5f, _radius);

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                _rig?.Destroy();
                _ownVisual?.Destroy();
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            _statusTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                bool refreshStatus = _statusTimer <= 0f;
                DamageTick(refreshStatus);
                if (refreshStatus) _statusTimer = STATUS_PERIOD;
                _tickTimer = _tickPeriod;
                _pulse = 1f;
            }

            if (_ownVisual != null)
                _ownVisual.Tick(Time.deltaTime, Mathf.Clamp01(_remaining / FADE_OUT_SECONDS));

            Animate();
        }

        private void Animate()
        {
            // Nothing to animate when the owner brought its own rig; it was ticked above.
            if (_rig == null) return;
            float t = Time.time;
            _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 2.5f);
            float pulseScale = 1f + 0.20f * _pulse;
            float baseFlick = 0.85f + 0.15f * Mathf.PerlinNoise(t * 4f, 0.31f);
            float fade = (_remaining < 1f) ? Mathf.Clamp01(_remaining) : 1f;

            if (_rig != null)
            {
                if (_rig.Rune != null)
                    _rig.Rune.transform.localRotation = Quaternion.Euler(0f, 0f, t * _rig.Palette.runeSpinSpeed);
                if (_rig.Core != null)
                    _rig.Core.transform.localScale = Vector3.one * _rig.Palette.coreScale * baseFlick * pulseScale;
                if (_rig.Glow != null)
                    _rig.Glow.transform.localScale = Vector3.one * _rig.Palette.glowScale * pulseScale;
                _rig.SetGlobalAlpha(fade * baseFlick);
                _rig.SetIntensity(_rig.Palette.lightIntensity * (0.85f + 0.15f * baseFlick) + 1.0f * _pulse);
            }
        }

        private void DamageTick(bool refreshStatus)
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, _radius, _targetLayers);
            foreach (var hit in hits)
            {
                var health = hit.GetComponentInParent<Health>();
                if (health == null || health.IsDead) continue;

                if (_damagePerTick > 0)
                {
                    health.TakeDotDamage(_damagePerTick, _caster, _damageElement);
                    if (refreshStatus)
                        StatusApplicationFactory.ApplyAll(_statusApplications, health.gameObject, _caster);
                }

                // A damage tick is the only EVENT a persistent field has, and an effect
                // made only of continuous motion stops being read after about a second.
                // The disc rig has nothing to say about one victim, so this reaches an
                // owned visual only.
                if (_ownVisual != null) _ownVisual.Lash(health.transform.position);

                if (_element == "lava" || _element == "fire")
                {
                    // Off `health`, not off `hit`: Health lives on the entity root (hence the
                    // GetComponentInParent above) and EntitySetup attaches StatusEffectManager
                    // to that same root. Querying the collider meant an entity whose collider
                    // sits on a child took the damage but never caught fire.
                    var statusMgr = health.GetComponent<StatusEffectManager>();
                    if (statusMgr != null)
                        statusMgr.Apply(new BurnEffect(3f, 5, applier: _caster));
                }
            }
        }
    }
}
