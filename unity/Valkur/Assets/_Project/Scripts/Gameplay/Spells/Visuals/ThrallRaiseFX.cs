using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The rising: the biggest single beat in the spell expansion, and it earns the budget
    /// because it is rare.
    ///
    /// <para>Six beats over about 1.1 seconds, and the FIRST one is silence. The mark flares
    /// and collapses to a point, and then nothing happens for 0.15 s — no particle, no sound.
    /// That pause is the beat the whole effect hangs on: it is what makes the eye stop on the
    /// spot where the kill landed, so the rest of the sequence arrives on an audience instead
    /// of in the middle of a fight. Cutting it to "tighten" the effect is the one change that
    /// would ruin this.</para>
    ///
    /// <para>The motes stream UP OUT of the floor and INTO the body, which is the exact
    /// reverse of the death effect the player has just watched. Reversal is doing the work
    /// here; a generic purple burst would say "a spell happened" and this has to say
    /// "that came back".</para>
    /// </summary>
    internal sealed class ThrallRaiseFX : MonoBehaviour
    {
        // ── The six beats, in seconds from the kill ──────────────────────────
        private const float T_SILENCE   = 0.15f;   // 1. flare collapses; nothing else moves
        private const float T_CRACK     = 0.15f;   // 2. ground opens
        private const float T_GATHER    = 0.30f;   // 3. motes rise and converge
        private const float T_RISE      = 0.55f;   // 4. the body comes up
        private const float T_RIM       = 0.85f;   // 5. the ally rim ignites
        private const float T_CONTROL   = 1.10f;   // 6. the FSM takes over

        private const int MOTE_COUNT = 40;
        private const float MOTE_SPREAD = 1.5f;

        private const int ORDER_RING = 46;
        private const int ORDER_MOTE = 48;
        private const int ORDER_FLARE = 49;

        private static readonly Color Violet = new Color(0.60f, 0.35f, 0.75f, 1f);
        private static readonly Color Pale = new Color(0.92f, 0.78f, 1f, 1f);

        private MonsterDefinition _definition;
        private Vector3 _position;
        private float _thrallDuration;
        private float _healthScale;
        private float _age;
        private bool _spawned;

        private Transform _groundPlane;
        private SpriteRenderer _ring;
        private SpriteRenderer _flare;
        private SpriteRenderer[] _motes;
        private Vector3[] _moteStart;
        private Component _light;

        /// <summary>
        /// Run the raising at <paramref name="position"/> and spawn the ally at its end.
        /// Refused outside Play Mode for the reason every timed rig in this project is: the
        /// sequence advances from Update, which never runs there.
        /// </summary>
        public static void Play(MonsterDefinition definition, Vector3 position,
                                float thrallDuration, float healthScale)
        {
            if (definition == null) return;
            if (!Application.isPlaying) return;

            var go = new GameObject("ThrallRaiseFX");
            go.transform.position = position;

            var fx = go.AddComponent<ThrallRaiseFX>();
            fx._definition = definition;
            fx._position = position;
            fx._thrallDuration = thrallDuration;
            fx._healthScale = healthScale;
            fx.BuildRig();

            ThrallAudio.PlayRaise(position);
        }

        private void BuildRig()
        {
            // The ground ring lies on the floor, so it goes under ONE squash parent with its
            // rotation on the child.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            _ring = CreateSprite("Ring", ElementalSprites.Ring, ORDER_RING, _groundPlane);
            _flare = CreateSprite("Flare", ElementalSprites.Glow, ORDER_FLARE, transform);

            _motes = new SpriteRenderer[MOTE_COUNT];
            _moteStart = new Vector3[MOTE_COUNT];
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                _motes[i] = CreateSprite($"Mote{i}", ElementalSprites.Sparkle, ORDER_MOTE, transform);
                _motes[i].transform.localScale = Vector3.one * Random.Range(0.12f, 0.24f);

                // Born on the FLOOR, spread around the spot. They rise into the body, which
                // is the reversal that makes this read as the opposite of dying.
                float a = Random.value * Mathf.PI * 2f;
                float r = Random.Range(0.2f, MOTE_SPREAD);
                _moteStart[i] = new Vector3(Mathf.Cos(a) * r, -0.35f + Mathf.Sin(a) * r * 0.3f, 0f);
                _motes[i].transform.localPosition = _moteStart[i];
            }

            BuildLight();
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, int order, Transform parent)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(Violet.r, Violet.g, Violet.b, 0f);
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

                var go = new GameObject("RaiseLight");
                go.transform.SetParent(transform, false);
                _light = go.AddComponent(lightType);

                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4.
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, Violet);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, 3f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.8f);
            }
            catch { _light = null; }
        }

        private void Update()
        {
            _age += Time.deltaTime;

            UpdateFlare();
            UpdateRing();
            UpdateMotes();
            UpdateLight();

            if (!_spawned && _age >= T_CONTROL)
            {
                _spawned = true;
                AlliedSummonService.Summon(_definition, _position, _thrallDuration, _healthScale);
            }

            // Outlive the spawn by a beat so the light and motes finish over the creature
            // rather than being cut the frame it appears.
            if (_age >= T_CONTROL + 0.5f) Destroy(gameObject);
        }

        private void UpdateFlare()
        {
            if (_flare == null) return;

            // Beat 1: the mark's light collapses INWARD to a point, then the screen is quiet.
            if (_age <= T_SILENCE)
            {
                float k = 1f - Mathf.Clamp01(_age / T_SILENCE);
                _flare.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.6f, k);
                _flare.color = WithAlpha(Pale, 0.9f * k);
                return;
            }

            _flare.color = WithAlpha(Pale, 0f);
        }

        private void UpdateRing()
        {
            if (_ring == null) return;
            if (_age < T_CRACK) { _ring.color = WithAlpha(Violet, 0f); return; }

            float k = Mathf.Clamp01((_age - T_CRACK) / (T_CONTROL - T_CRACK));
            // ElementalSprites.Ring peaks at normalized radius 0.78, so a ring meant to sit at
            // world radius r is scaled r / 0.39. Here it opens to 2.2 units.
            _ring.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 2.2f / 0.39f, k) * 0.39f;
            _ring.transform.localRotation = Quaternion.Euler(0f, 0f, k * 120f);
            _ring.color = WithAlpha(Violet, 0.85f * (1f - k * 0.55f));
        }

        private void UpdateMotes()
        {
            if (_motes == null) return;

            if (_age < T_GATHER)
            {
                for (int i = 0; i < _motes.Length; i++) _motes[i].color = WithAlpha(Pale, 0f);
                return;
            }

            float k = Mathf.Clamp01((_age - T_GATHER) / (T_RIM - T_GATHER));
            // Converge on chest height. Eased so they accelerate INTO the body rather than
            // drifting to it, which is what makes the arrival feel like an arrival.
            float ease = k * k;
            Vector3 target = new Vector3(0f, 0.8f, 0f);

            for (int i = 0; i < _motes.Length; i++)
            {
                _motes[i].transform.localPosition = Vector3.Lerp(_moteStart[i], target, ease);
                _motes[i].color = WithAlpha(Pale, Mathf.Sin(k * Mathf.PI) * 0.9f);
            }
        }

        private void UpdateLight()
        {
            if (_light == null) return;

            // Dark through the silence, then rising with the gather and punching at the rise.
            float intensity;
            if (_age < T_SILENCE)      intensity = Mathf.Lerp(1.4f, 0f, _age / T_SILENCE);
            else if (_age < T_RISE)    intensity = Mathf.Lerp(0f, 1.1f, (_age - T_SILENCE) / (T_RISE - T_SILENCE));
            else                       intensity = Mathf.Lerp(1.8f, 0.2f, (_age - T_RISE) / (T_CONTROL + 0.5f - T_RISE));

            ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, Mathf.Max(0f, intensity));
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
