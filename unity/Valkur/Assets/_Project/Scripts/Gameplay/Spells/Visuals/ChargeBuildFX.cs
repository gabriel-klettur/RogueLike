using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Power gathering in the caster's hand while the key is held.
    ///
    /// <para>THE CHARGE IS THE SPELL. The projectile that eventually leaves matters less than
    /// the 1.6 seconds before it exists, because that window is the only time in this game
    /// where the player is holding power and can watch it grow. A charge drawn as "the same
    /// ball, bigger" wastes the one mechanic that gives a spell a decision in it.</para>
    ///
    /// <para>SO IT GROWS IN DENSITY AND BEHAVIOUR, NOT MERELY IN SIZE. The seven shipped ki
    /// charges record the same rule: what an intensity dial should move is the number of
    /// things happening, not the width of one thing. Across the ramp the core goes from a dim
    /// ember to an overdriven white-hot centre (COLOUR, not alpha — on an additive material
    /// alpha is coverage and colour is brightness), the orbiting motes go from three to
    /// fourteen and their orbit tightens and speeds up, a ground pool of light spreads under
    /// the caster, and past 80 % the ball starts shedding sparks that fall and die on the
    /// floor. Those sparks are the one OPAQUE layer, and they are what says the world is being
    /// affected rather than merely lit.</para>
    ///
    /// <para>THREE BEATS MAKE IT READABLE, and they are worth more than any particle count.
    /// At 100 % the whole rig SNAPS once — a hard flash and a momentary contraction — which is
    /// how the player learns where full is without a bar on screen. The caster's own sprite
    /// brightens on the same curve. And holding past full does nothing and must LOOK like it
    /// does nothing: the rig settles into a steady loop, which is the signal to let go.</para>
    /// </summary>
    internal sealed class ChargeBuildFX : MonoBehaviour
    {
        private const int MOTE_MIN = 3;
        private const int MOTE_MAX = 14;
        private const int SPARK_POOL = 10;

        /// <summary>Charge fraction below which no ground sparks are shed at all. Under this
        /// the charge is a small light; over it, it is doing something to the floor.</summary>
        private const float SPARK_THRESHOLD = 0.80f;

        /// <summary>Seconds the full-charge snap lasts.</summary>
        private const float SNAP_SECONDS = 0.18f;

        private const int ORDER_POOL  = 36;
        private const int ORDER_HALO  = 38;
        private const int ORDER_CORE  = 40;
        private const int ORDER_HOT   = 41;
        private const int ORDER_MOTE  = 42;
        private const int ORDER_SPARK = 34;   // BELOW the pool: they are on the ground

        private SpellDefinition _spell;
        private Transform _owner;
        private Vector3 _handOffset;
        private float _age;
        private bool _released;
        private bool _snapped;
        private float _snapAge = 999f;

        private Color _tint;
        private Color _hot;

        private Transform _groundPlane;
        private SpriteRenderer _pool;
        private SpriteRenderer _halo;
        private SpriteRenderer _core;
        private SpriteRenderer _hotCore;
        private SpriteRenderer[] _motes;
        private SpriteRenderer[] _sparks;
        private float[] _sparkAge;
        private Vector3[] _sparkVel;
        private int _nextSpark;
        private float _sparkTimer;
        private SpriteTintStack _bodyTint;
        private Component _light;
        private AudioSource _tone;

        /// <summary>
        /// Build the rig on <paramref name="owner"/>. Refused outside Play Mode for the reason
        /// every timed rig in this folder is: it advances from Update, which never runs there,
        /// so building one would leave a permanent cluster in the scene.
        /// </summary>
        public static ChargeBuildFX Attach(Transform owner, SpellDefinition spell)
        {
            if (owner == null || spell == null || !spell.IsChargeable) return null;
            if (!Application.isPlaying) return null;

            var body = owner.GetComponent<SpriteRenderer>();
            if (body == null) body = owner.GetComponentInChildren<SpriteRenderer>();
            Vector3 center = body != null && body.sprite != null
                ? body.bounds.center - owner.position
                : new Vector3(0f, 0.8f, 0f);

            var go = new GameObject("ChargeBuildFX");
            go.transform.position = owner.position + center;

            var fx = go.AddComponent<ChargeBuildFX>();
            fx._spell = spell;
            fx._owner = owner;
            fx._handOffset = center;
            fx._bodyTint = SpriteTintStack.Attach(owner.gameObject);
            fx.ResolveColours(spell);
            fx.BuildRig();
            return fx;
        }

        /// <summary>
        /// The key came up. The rig does NOT linger: whatever the charge became is now the
        /// projectile's problem, and light left behind at the hand after the shot has gone
        /// reads as the spell having failed to leave.
        /// </summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Cast);
            Destroy(gameObject);
        }

        private void ResolveColours(SpellDefinition spell)
        {
            // The project's three-way sentinel order, tested in this order everywhere it is
            // read: opaque white means UNAUTHORED, a real achromatic value is a deliberate
            // request for the absence of colour, and anything else is a hue to honour.
            // Checking saturation first catches white in the grey branch.
            Color swatch = spell.particleColor;
            if (KiPalette.IsUnauthored(swatch))
            {
                _tint = new Color(1f, 0.62f, 0.22f);
                _hot  = new Color(1f, 0.92f, 0.72f);
                return;
            }

            Color.RGBToHSV(swatch, out float h, out float sat, out _);
            if (sat < 0.02f)
            {
                _tint = new Color(0.82f, 0.82f, 0.85f);
                _hot  = new Color(0.98f, 0.98f, 1f);
                return;
            }

            // Value held at 1 on both: a dark colour adds almost nothing on an additive
            // surface, so a dim swatch would make the charge vanish rather than darken it.
            _tint = Color.HSVToRGB(h, Mathf.Clamp(sat, 0.45f, 0.95f), 1f);
            _hot  = Color.HSVToRGB(h, Mathf.Clamp(sat * 0.30f, 0.05f, 0.30f), 1f);
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildRig()
        {
            // The ground pool and the fallen sparks lie on the FLOOR, so they go under one
            // squash parent with the per-item rotation on the children (never a squash per
            // item, which foreshortens length without turning direction).
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localPosition = -_handOffset;
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            _pool    = Make("Pool",    ElementalSprites.Glow,    ORDER_POOL, _groundPlane);
            _halo    = Make("Halo",    ElementalSprites.Halo,    ORDER_HALO, transform);
            _core    = Make("Core",    ElementalSprites.Core,    ORDER_CORE, transform);
            _hotCore = Make("HotCore", ElementalSprites.HotCore, ORDER_HOT,  transform);

            _motes = new SpriteRenderer[MOTE_MAX];
            for (int i = 0; i < MOTE_MAX; i++)
            {
                _motes[i] = Make($"Mote{i}", ElementalSprites.Sparkle, ORDER_MOTE, transform);
                _motes[i].transform.localScale = Vector3.one * 0.16f;
            }

            _sparks = new SpriteRenderer[SPARK_POOL];
            _sparkAge = new float[SPARK_POOL];
            _sparkVel = new Vector3[SPARK_POOL];
            for (int i = 0; i < SPARK_POOL; i++)
            {
                // THE OPAQUE LAYER. Deliberately not additive and deliberately dark: a dark
                // chip on an additive surface adds almost nothing, so the layer would vanish
                // with nothing failing. It is the only piece here that says the world is
                // being affected rather than just lit.
                var go = new GameObject($"Spark{i}");
                go.transform.SetParent(_groundPlane, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ElementalSprites.Sparkle;
                sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = ORDER_SPARK;
                sr.color = new Color(0f, 0f, 0f, 0f);
                sr.transform.localScale = Vector3.one * 0.11f;
                _sparks[i] = sr;
                _sparkAge[i] = 1f;
            }

            BuildLight();
            BuildTone();
        }

        private SpriteRenderer Make(string objectName, Sprite sprite, int order, Transform parent)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(_tint.r, _tint.g, _tint.b, 0f);
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private void BuildLight()
        {
            try
            {
                var lightType = ElementalProjectileVisual.GetLight2DType();
                if (lightType == null) return;

                var go = new GameObject("ChargeLight");
                go.transform.SetParent(transform, false);
                _light = go.AddComponent(lightType);

                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _tint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 2.6f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.12f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        /// <summary>
        /// A rising tone whose PITCH tracks the charge exactly. It is not decoration: pitch is
        /// the one channel that can report a continuous quantity without occupying any screen
        /// space, and it lets a player charge while watching the fight instead of watching
        /// their own hand.
        /// </summary>
        private void BuildTone()
        {
            _tone = gameObject.AddComponent<AudioSource>();
            _tone.clip = ChargeAudio.Tone();
            _tone.loop = true;
            _tone.spatialBlend = 0f;   // it is on the player; panning it would be wrong
            _tone.volume = 0f;
            _tone.priority = 200;
            _tone.playOnAwake = false;
            _tone.Play();
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void Update()
        {
            if (_released) return;

            _age += Time.deltaTime;
            if (_owner != null) transform.position = _owner.position + _handOffset;

            float charge = Mathf.Clamp01(_age / Mathf.Max(0.01f, _spell.chargeMaxSeconds));

            if (!_snapped && charge >= 1f)
            {
                _snapped = true;
                _snapAge = 0f;
            }
            if (_snapped) _snapAge += Time.deltaTime;

            // A hard flash and a momentary CONTRACTION at the top. Everything after it settles
            // into a steady loop, which is the rig saying "this is as far as it goes".
            float snap = _snapped
                ? Mathf.Max(0f, 1f - _snapAge / SNAP_SECONDS)
                : 0f;

            UpdateCore(charge, snap);
            UpdateMotes(charge, snap);
            UpdatePool(charge, snap);
            UpdateSparks(charge);
            UpdateBody(charge, snap);
            UpdateLight(charge, snap);
            UpdateTone(charge);
        }

        private void UpdateCore(float charge, float snap)
        {
            // Growth is modest and BRIGHTNESS does the work. On an additive material the
            // intensity dial is the COLOUR and it may exceed 1 -- reaching for alpha instead
            // widens the ball into fog rather than hardening it.
            float size = Mathf.Lerp(0.22f, 0.62f, charge) * (1f - snap * 0.25f);
            float gain = Mathf.Lerp(0.9f, 2.7f, charge) + snap * 1.6f;

            if (_core != null)
            {
                _core.transform.localScale = Vector3.one * size;
                _core.color = Scaled(_tint, gain, Mathf.Lerp(0.55f, 0.95f, charge));
            }
            if (_hotCore != null)
            {
                _hotCore.transform.localScale = Vector3.one * size * 0.45f;
                _hotCore.color = Scaled(_hot, gain * 1.15f, Mathf.Lerp(0.35f, 1f, charge));
            }
            if (_halo != null)
            {
                _halo.transform.localScale = Vector3.one * size * 2.4f;
                _halo.color = Scaled(_tint, 1f, Mathf.Lerp(0.10f, 0.34f, charge) + snap * 0.25f);
            }
        }

        private void UpdateMotes(float charge, float snap)
        {
            // COUNT is the dial, not size. Three motes at rest, fourteen at full.
            int live = Mathf.RoundToInt(Mathf.Lerp(MOTE_MIN, MOTE_MAX, charge));
            float radius = Mathf.Lerp(0.75f, 0.34f, charge) * (1f + snap * 0.4f);
            float rate = Mathf.Lerp(80f, 420f, charge);
            float spin = _age * rate;

            for (int i = 0; i < _motes.Length; i++)
            {
                if (i >= live) { _motes[i].color = Scaled(_hot, 1f, 0f); continue; }

                float a = (spin + i * (360f / Mathf.Max(1, live))) * Mathf.Deg2Rad;
                // Slight vertical squash so the orbit reads as a ring around the hand rather
                // than a flat circle drawn on the screen.
                _motes[i].transform.localPosition =
                    new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.62f, 0f);
                _motes[i].color = Scaled(_hot, 1.2f, 0.55f + 0.35f * charge);
            }
        }

        private void UpdatePool(float charge, float snap)
        {
            if (_pool == null) return;
            // ElementalSprites are 1x1 world units, so this scale IS a world diameter.
            _pool.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.0f, charge);
            _pool.color = Scaled(_tint, 1f, Mathf.Lerp(0f, 0.30f, charge) + snap * 0.2f);
        }

        private void UpdateSparks(float charge)
        {
            if (charge >= SPARK_THRESHOLD)
            {
                _sparkTimer -= Time.deltaTime;
                if (_sparkTimer <= 0f)
                {
                    _sparkTimer = Mathf.Lerp(0.09f, 0.035f, Mathf.InverseLerp(SPARK_THRESHOLD, 1f, charge));
                    _sparkAge[_nextSpark] = 0f;
                    _sparks[_nextSpark].transform.localPosition = _handOffset;
                    _sparkVel[_nextSpark] = new Vector3(Random.Range(-1.1f, 1.1f), Random.Range(-0.2f, 0.5f), 0f);
                    _nextSpark = (_nextSpark + 1) % SPARK_POOL;
                }
            }

            for (int i = 0; i < _sparks.Length; i++)
            {
                if (_sparkAge[i] >= 1f) { _sparks[i].color = new Color(0f, 0f, 0f, 0f); continue; }
                _sparkAge[i] += Time.deltaTime * 1.6f;

                _sparkVel[i] += Vector3.down * 3.2f * Time.deltaTime;   // it falls
                _sparks[i].transform.localPosition += _sparkVel[i] * Time.deltaTime;

                // Dark and opaque: this is matter, not light.
                float a = (1f - _sparkAge[i]) * 0.75f;
                _sparks[i].color = new Color(_tint.r * 0.25f, _tint.g * 0.18f, _tint.b * 0.15f, a);
            }
        }

        private void UpdateBody(float charge, float snap)
        {
            if (_bodyTint == null) return;
            // TintLayer.Cast MULTIPLIES, so it is held near white: driving it hard reads as
            // the character being dimmed rather than as light gathering on them.
            float k = 0.10f + 0.14f * charge + snap * 0.25f;
            _bodyTint.Set(TintLayer.Cast, Color.Lerp(Color.white, _hot, Mathf.Clamp01(k)));
        }

        private void UpdateLight(float charge, float snap)
        {
            if (_light == null) return;
            ElementalProjectileVisual.GetLight2DIntensityProp()
                ?.SetValue(_light, Mathf.Lerp(0.35f, 1.5f, charge) + snap * 1.2f);
        }

        private void UpdateTone(float charge)
        {
            if (_tone == null) return;
            // Pitch tracks the charge exactly across the ramp; volume comes up quickly and
            // then holds, so the PITCH is the only thing carrying information.
            _tone.pitch = Mathf.Lerp(0.75f, 1.85f, charge);
            _tone.volume = Mathf.Lerp(0f, 0.30f, Mathf.Clamp01(_age / 0.15f));
        }

        /// <summary>
        /// Clearing the body tint here as well as in <see cref="Release"/> is what makes the
        /// rig safe on the exit paths a release never reaches: the caster dying mid-charge, a
        /// zone change, scene unload. Only OnDestroy is on all of them.
        /// </summary>
        private void OnDestroy()
        {
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Cast);
        }

        /// <summary>
        /// Colour scaled past 1 for the additive overdrive, with alpha kept as COVERAGE.
        /// The two are different dials and mixing them is what turns a hardening core into
        /// spreading fog.
        /// </summary>
        private static Color Scaled(Color c, float gain, float alpha)
            => new Color(c.r * gain, c.g * gain, c.b * gain, alpha);
    }
}
