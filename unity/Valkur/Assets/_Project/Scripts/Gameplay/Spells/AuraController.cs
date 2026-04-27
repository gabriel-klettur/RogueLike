using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Healing-aura controller. Builds a multi-layered procedural VFX rig
    /// (ground rune + inner glow + rising sparkles + light pillar + Light2D + per-tick
    /// pulse rings + caster halo flash) and ticks healing on the caster.
    ///
    /// All sprites/textures are generated once and cached statically to avoid
    /// per-cast allocations. Light2D is wired via reflection so the assembly does not
    /// need a hard dependency on URP.
    /// </summary>
    public class AuraController : MonoBehaviour
    {
        // --- Tunables (palette + animation) ---
        // Holy gold + nature green: classic "sacred ground" look.
        private static readonly Color GoldCore   = new Color(1.00f, 0.92f, 0.55f, 1f);
        private static readonly Color GreenCore  = new Color(0.55f, 1.00f, 0.70f, 1f);
        private static readonly Color GoldSoft   = new Color(1.00f, 0.85f, 0.45f, 0.55f);
        private static readonly Color GreenSoft  = new Color(0.45f, 1.00f, 0.65f, 0.55f);

        private const float RuneRotSpeed       = 22f;     // deg/s
        private const float RuneCounterRotSpeed = -38f;   // deg/s for the inner star
        private const float TickPulseLifetime  = 0.85f;
        private const float SparkleEmitRate    = 28f;

        // --- Healing logic ---
        private float     _remaining;
        private float     _visualRadius;
        private int       _healPerTick;
        private float     _tickPeriod;
        private float     _tickTimer;
        private Transform _caster;
        private FloatingDamageSpawner _floating;

        // --- Visuals ---
        private Transform      _runeOuter;       // slow rotation
        private Transform      _runeInner;       // counter rotation (hexagram)
        private SpriteRenderer _runeOuterSr;
        private SpriteRenderer _runeInnerSr;
        private SpriteRenderer _innerGlowSr;
        private SpriteRenderer _pillarSr;
        private SpriteRenderer _casterHaloSr;
        private ParticleSystem _sparkles;
        private Component      _light2D;          // URP Light2D via reflection
        private static PropertyInfo _light2DIntensity;
        private static PropertyInfo _light2DColor;
        private static PropertyInfo _light2DOuterRadius;
        private static PropertyInfo _light2DInnerRadius;

        // --- Cached procedural sprites ---
        private static Sprite _runeOuterSprite;
        private static Sprite _runeInnerSprite;
        private static Sprite _innerGlowSprite;
        private static Sprite _pulseRingSprite;
        private static Sprite _pillarSprite;
        private static Sprite _haloSprite;
        private static Sprite _sparkleSprite;

        public void InitializeHealing(
            float duration,
            float gameRadius,
            float visualRadius,
            int healPerTick,
            float tickPeriod,
            Transform caster)
        {
            _ = gameRadius; // reserved for future "heal nearby allies" logic
            _remaining    = duration;
            _visualRadius = visualRadius;
            _healPerTick  = healPerTick;
            _tickPeriod   = tickPeriod;
            _tickTimer    = 0f;   // first tick fires immediately
            _caster       = caster;
            _floating     = caster != null ? caster.GetComponentInChildren<FloatingDamageSpawner>(true) : null;

            EnsureSprites();
            BuildVisualRig();

            // Spawn-burst: an initial pulse + first heal tick.
            SpawnPulseRing(initial: true);
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            float alpha = Mathf.Clamp01(_remaining / 0.6f); // last 0.6s fades out

            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            _tickTimer -= Time.deltaTime;
            if (_tickTimer <= 0f)
            {
                HealTick();
                _tickTimer = _tickPeriod;
            }

            AnimateVisuals(alpha);
        }

        // --------------------------------------------------------------------
        // Logic
        // --------------------------------------------------------------------

        private void HealTick()
        {
            if (_caster == null) return;
            var health = _caster.GetComponent<Health>();
            if (health == null || health.IsDead) return;

            int before = health.CurrentHp;
            health.Heal(_healPerTick);
            int actual = health.CurrentHp - before;

            // Visual feedback per tick.
            SpawnPulseRing(initial: false);
            FlashCasterHalo();
            EmitSparkleBurst(12);
            if (actual > 0 && _floating != null) _floating.ShowHeal(actual);

            Debug.Log($"[SpellDebug] Aura healed {_caster.name} for {actual} HP");
        }

        // --------------------------------------------------------------------
        // Visual rig
        // --------------------------------------------------------------------

        private void BuildVisualRig()
        {
            float visScale = _visualRadius * 2f; // sprite is 1u radius -> diameter 2u

            // 1) Rune outer ring (slow rotation, on ground).
            _runeOuter = MakeChild("Rune_Outer");
            _runeOuter.localPosition = Vector3.zero;
            _runeOuter.localScale = Vector3.one * visScale;
            _runeOuterSr = AddSprite(_runeOuter, _runeOuterSprite, GoldCore,
                SortingConfig.LAYER_FLOOR_DECALS, 50);

            // 2) Inner rune: 2D projection of a regular dodecahedron (Schlegel diagram).
            _runeInner = MakeChild("Rune_Inner_Dodec");
            _runeInner.localPosition = Vector3.zero;
            _runeInner.localScale = Vector3.one * (visScale * 0.78f);
            _runeInnerSr = AddSprite(_runeInner, _runeInnerSprite, GreenCore,
                SortingConfig.LAYER_FLOOR_DECALS, 51);

            // 3) Inner soft glow disk (additive feel via additive-ish color).
            var glow = MakeChild("InnerGlow");
            glow.localPosition = Vector3.zero;
            glow.localScale = Vector3.one * (visScale * 0.95f);
            _innerGlowSr = AddSprite(glow, _innerGlowSprite, GreenSoft,
                SortingConfig.LAYER_FLOOR_DECALS, 49);

            // 4) Vertical light pillar behind the caster (FloorDecals so it never overlaps the player sprite).
            var pillar = MakeChild("LightPillar");
            pillar.localPosition = new Vector3(0f, _visualRadius * 0.55f, 0f);
            pillar.localScale = new Vector3(_visualRadius * 1.1f, _visualRadius * 4.5f, 1f);
            _pillarSr = AddSprite(pillar, _pillarSprite, GoldSoft,
                SortingConfig.LAYER_FLOOR_DECALS, 70);

            // 5) Caster halo flash on the ground under the player (also behind the sprite).
            var halo = MakeChild("CasterHalo");
            halo.localPosition = new Vector3(0f, 0.1f, 0f);
            halo.localScale = Vector3.one * 1.4f;
            _casterHaloSr = AddSprite(halo, _haloSprite, new Color(1f, 1f, 0.85f, 0f),
                SortingConfig.LAYER_FLOOR_DECALS, 75);

            // 6) Rising sparkle particles (rendered on FloorDecals so they always pass behind the player).
            _sparkles = BuildSparkles();

            // 7) Optional URP Light2D for global glow.
            TryAttachLight2D();
        }

        private Transform MakeChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private static SpriteRenderer AddSprite(Transform t, Sprite sprite, Color color, string layer, int order)
        {
            var sr = t.gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color  = color;
            // Set both ID and Name. Setting only Name occasionally fails on freshly
            // created renderers; ID is the authoritative value Unity uses for sorting.
            sr.sortingLayerID   = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private ParticleSystem BuildSparkles()
        {
            var go = new GameObject("Sparkles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            // ParticleSystem auto-plays on AddComponent; stop it so we can configure
            // .main.duration / start* without Unity asserting.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Detach the auto-created MeshRenderer; we want SpriteRenderer-style billboard via PS renderer.
            var psr = go.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.material = new Material(Shader.Find("Sprites/Default"));
            // Render BEHIND the player at all costs: use the lowest gameplay layer
            // (Ground) and set the ID directly (Unity sometimes ignores the Name
            // setter when the renderer was just created).
            int groundId = SortingLayer.NameToID(SortingConfig.LAYER_GROUND);
            psr.sortingLayerID = groundId;
            psr.sortingLayerName = SortingConfig.LAYER_GROUND;
            psr.sortingOrder = 100;
            psr.sortingFudge = 0.5f; // bias slightly forward within Ground but still BEHIND every Entities-layer sprite

            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = 1.4f;
            main.startSpeed = 0.9f;
            main.startSize = 0.18f;
            main.startColor = new ParticleSystem.MinMaxGradient(GoldCore, GreenCore);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            main.gravityModifier = -0.05f; // gentle upward float

            var emission = ps.emission;
            emission.rateOverTime = SparkleEmitRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _visualRadius * 0.95f;
            shape.radiusThickness = 0.6f;

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(GoldCore, 0f), new GradientColorKey(GreenCore, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f),
                        new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(grad);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.4f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // Unity requires x/y/z of velocityOverLifetime to use the same MinMaxCurveMode.
            // Use TwoConstants for all three; only Y has a non-zero range (gentle upward drift).
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            psr.sharedMaterial.mainTexture = _sparkleSprite.texture;

            // Start the configured system.
            ps.Play(true);

            return ps;
        }

        private void EmitSparkleBurst(int count)
        {
            if (_sparkles == null) return;
            var emitParams = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true
            };
            _sparkles.Emit(emitParams, count);
        }

        // --------------------------------------------------------------------
        // Animation
        // --------------------------------------------------------------------

        private void AnimateVisuals(float alpha)
        {
            float t = Time.time;

            // Slow rotations.
            if (_runeOuter != null) _runeOuter.localRotation = Quaternion.Euler(0f, 0f, t * RuneRotSpeed);
            if (_runeInner != null) _runeInner.localRotation = Quaternion.Euler(0f, 0f, t * RuneCounterRotSpeed);

            // Rune subtle pulse.
            if (_runeOuterSr != null)
            {
                var c = GoldCore;
                c.a *= alpha * (0.85f + 0.15f * Mathf.Sin(t * 3.5f));
                _runeOuterSr.color = c;
            }
            if (_runeInnerSr != null)
            {
                var c = GreenCore;
                c.a *= alpha * (0.75f + 0.25f * Mathf.Sin(t * 2.3f + 0.7f));
                _runeInnerSr.color = c;
            }
            if (_innerGlowSr != null)
            {
                var c = GreenSoft;
                c.a *= alpha * (0.35f + 0.20f * Mathf.Sin(t * 2.0f));
                _innerGlowSr.color = c;
            }

            // Pillar gentle vertical wobble + flicker.
            if (_pillarSr != null)
            {
                var c = GoldSoft;
                c.a *= alpha * (0.45f + 0.25f * Mathf.PerlinNoise(t * 1.7f, 0f));
                _pillarSr.color = c;
                if (_pillarSr.transform.parent != null)
                {
                    var s = _pillarSr.transform.localScale;
                    s.x = _visualRadius * (1.0f + 0.06f * Mathf.Sin(t * 4.0f));
                    _pillarSr.transform.localScale = s;
                }
            }

            // Light2D follow.
            if (_light2D != null && _light2DIntensity != null)
            {
                float intensity = (0.9f + 0.25f * Mathf.Sin(t * 4f)) * alpha;
                try { _light2DIntensity.SetValue(_light2D, intensity); }
                catch (Exception) { /* ignore reflection errors */ }
            }

            // Sparkle emission scales with alpha (so it tapers off).
            if (_sparkles != null)
            {
                var em = _sparkles.emission;
                em.rateOverTime = SparkleEmitRate * alpha;
            }
        }

        // --------------------------------------------------------------------
        // Per-tick FX
        // --------------------------------------------------------------------

        private void SpawnPulseRing(bool initial)
        {
            var go = new GameObject(initial ? "PulseRing_Spawn" : "PulseRing");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pulseRingSprite;
            // Render on the floor so the ring slides underneath the player.
            sr.sortingLayerID   = SortingLayer.NameToID(SortingConfig.LAYER_FLOOR_DECALS);
            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 80;
            sr.color = initial ? GoldCore : new Color(GreenCore.r, GreenCore.g, GreenCore.b, 0.95f);

            float startScale = _visualRadius * (initial ? 0.2f : 0.5f);
            float endScale   = _visualRadius * (initial ? 2.4f : 1.5f);
            float life       = initial ? 1.1f : TickPulseLifetime;

            StartCoroutine(AnimatePulseRing(go, sr, startScale, endScale, life));
        }

        private static IEnumerator AnimatePulseRing(GameObject go, SpriteRenderer sr, float s0, float s1, float life)
        {
            float t = 0f;
            Color baseCol = sr.color;
            while (t < life && go != null)
            {
                t += Time.deltaTime;
                float k = t / life;
                float ease = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
                float s = Mathf.Lerp(s0, s1, ease);
                go.transform.localScale = new Vector3(s, s, 1f);
                if (sr != null)
                {
                    Color c = baseCol;
                    c.a = baseCol.a * (1f - k);
                    sr.color = c;
                }
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        private void FlashCasterHalo()
        {
            if (_casterHaloSr == null) return;
            StartCoroutine(HaloFlashRoutine());
        }

        private IEnumerator HaloFlashRoutine()
        {
            float life = 0.45f;
            float t = 0f;
            while (t < life && _casterHaloSr != null)
            {
                t += Time.deltaTime;
                float k = t / life;
                float a = Mathf.Sin(k * Mathf.PI) * 0.65f; // 0->peak->0
                _casterHaloSr.color = new Color(1f, 1f, 0.85f, a);
                _casterHaloSr.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 2.1f, k);
                yield return null;
            }
            if (_casterHaloSr != null)
                _casterHaloSr.color = new Color(1f, 1f, 0.85f, 0f);
        }

        // --------------------------------------------------------------------
        // URP Light2D via reflection (no hard URP dependency)
        // --------------------------------------------------------------------

        private void TryAttachLight2D()
        {
            var t = Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (t == null) return;

            try
            {
                _light2D = gameObject.AddComponent(t) as Component;
                if (_light2D == null) return;
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _light2DIntensity   = _light2DIntensity   ?? t.GetProperty("intensity",              flags);
                _light2DColor       = _light2DColor       ?? t.GetProperty("color",                  flags);
                _light2DOuterRadius = _light2DOuterRadius ?? t.GetProperty("pointLightOuterRadius",  flags);
                _light2DInnerRadius = _light2DInnerRadius ?? t.GetProperty("pointLightInnerRadius",  flags);

                _light2DColor?.SetValue(_light2D, new Color(1f, 0.9f, 0.55f, 1f));
                _light2DIntensity?.SetValue(_light2D, 1.1f);
                _light2DOuterRadius?.SetValue(_light2D, _visualRadius * 2.4f);
                _light2DInnerRadius?.SetValue(_light2D, _visualRadius * 0.4f);
            }
            catch (Exception)
            {
                _light2D = null;
            }
        }

        // --------------------------------------------------------------------
        // Procedural sprite generation (cached)
        // --------------------------------------------------------------------

        private static void EnsureSprites()
        {
            if (_runeOuterSprite == null) _runeOuterSprite = BuildRuneOuter(256);
            if (_runeInnerSprite == null) _runeInnerSprite = BuildRuneDodecahedron(256);
            if (_innerGlowSprite == null) _innerGlowSprite = BuildRadialGlow(128);
            if (_pulseRingSprite == null) _pulseRingSprite = BuildRing(128, 0.86f, 1.0f);
            if (_pillarSprite    == null) _pillarSprite    = BuildPillar(64, 256);
            if (_haloSprite      == null) _haloSprite      = BuildRadialGlow(128);
            if (_sparkleSprite   == null) _sparkleSprite   = BuildSparkleStar(32);
        }

        private static Sprite SpriteFromTex(Texture2D tex, float ppu = 128f)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), ppu);
        }

        private static Sprite BuildRadialGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float maxR = c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a; // soften
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f); // 1u radius
        }

        private static Sprite BuildRing(int size, float innerR, float outerR)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float maxR = c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = 0f;
                    if (r >= innerR && r <= outerR)
                    {
                        float k = Mathf.InverseLerp(innerR, outerR, r);
                        a = Mathf.Sin(k * Mathf.PI); // peak at middle of band
                    }
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// Outer rune circle: thick ring + tick marks + inner thin ring.
        /// </summary>
        private static Sprite BuildRuneOuter(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float maxR = cx;

            // Bands (in normalized radius)
            const float outerHi = 0.99f, outerLo = 0.92f;
            const float midHi   = 0.86f, midLo   = 0.84f;
            const float innerHi = 0.62f, innerLo = 0.60f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;
                    float a = 0f;
                    if (r >= outerLo && r <= outerHi)
                        a = Mathf.Max(a, Mathf.Sin(Mathf.InverseLerp(outerLo, outerHi, r) * Mathf.PI));
                    if (r >= midLo && r <= midHi)
                        a = Mathf.Max(a, 0.7f);
                    if (r >= innerLo && r <= innerHi)
                        a = Mathf.Max(a, 0.85f);

                    // Tick marks between mid and outer ring (12 ticks every 30°)
                    if (r > midHi && r < outerLo)
                    {
                        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float tickAng = Mathf.Repeat(ang + 360f, 30f);
                        if (tickAng < 2.5f || tickAng > 27.5f)
                            a = Mathf.Max(a, 0.85f);
                    }

                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        /// <summary>
        /// 2D projection of a regular dodecahedron (Schlegel diagram): a central
        /// pentagon surrounded by 5 pentagons, each sharing an edge with the centre.
        /// Ten of the 12 faces are visible; the back face is implicit at the centre,
        /// the front face is the outer boundary of the diagram.
        /// </summary>
        private static Sprite BuildRuneDodecahedron(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            // Central pentagon circumradius. Outer pentagons reach ~2.618 * r,
            // so we keep r small enough for the whole diagram to fit.
            float r = cx * 0.30f;
            float lineHalf = Mathf.Max(1.5f, size / 110f);

            // Central pentagon vertices (point-up at 90°).
            Vector2[] pent = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float a = (90f + 72f * i) * Mathf.Deg2Rad;
                pent[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
            }

            // Draw central pentagon.
            for (int i = 0; i < 5; i++)
                DrawLine(px, size, pent[i], pent[(i + 1) % 5], lineHalf);

            // For each edge of the central pentagon, build the surrounding pentagon
            // by reflecting the 3 non-shared vertices across the shared edge.
            for (int edge = 0; edge < 5; edge++)
            {
                int ia = edge;
                int ib = (edge + 1) % 5;
                Vector2 a = pent[ia];
                Vector2 b = pent[ib];

                Vector2[] outer = new Vector2[5];
                for (int k = 0; k < 5; k++)
                {
                    if (k == ia || k == ib) outer[k] = pent[k];
                    else outer[k] = ReflectAcrossLine(pent[k], a, b);
                }

                // Draw the 4 non-shared edges (skip shared edge ia-ib, already drawn).
                for (int k = 0; k < 5; k++)
                {
                    int k2 = (k + 1) % 5;
                    if (k == ia && k2 == ib) continue;     // shared edge
                    if (k == ib && k2 == ia) continue;     // (defensive, won't happen with k+1 mod 5)
                    DrawLine(px, size, outer[k], outer[k2], lineHalf * 0.85f);
                }
            }

            // Bright vertex dots for the central pentagon.
            float dotR = size * 0.012f;
            for (int i = 0; i < 5; i++)
                DrawDot(px, size, pent[i], dotR);

            // Tiny center dot.
            DrawDot(px, size, new Vector2(cx, cy), size * 0.018f);

            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        private static Vector2 ReflectAcrossLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = Mathf.Max(1e-6f, Vector2.Dot(ab, ab));
            float t = Vector2.Dot(p - a, ab) / len2;
            Vector2 proj = a + ab * t;
            return 2f * proj - p;
        }

        private static void DrawDot(Color[] px, int size, Vector2 c, float radius)
        {
            int minX = Mathf.Clamp(Mathf.FloorToInt(c.x - radius - 1f), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt (c.x + radius + 1f), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(c.y - radius - 1f), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt (c.y + radius + 1f), 0, size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x - c.x, dy = y - c.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= radius)
                    {
                        float k = 1f - (d / radius);
                        int idx = y * size + x;
                        var col = px[idx];
                        col.r = col.g = col.b = 1f;
                        col.a = Mathf.Max(col.a, k);
                        px[idx] = col;
                    }
                }
            }
        }

        /// <summary>
        /// Inner hexagram (Star of David): two overlaid equilateral triangles.
        /// </summary>
        private static Sprite BuildRuneHexagram(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float radius = cx * 0.78f;
            float lineHalf = Mathf.Max(1.5f, size / 96f); // line thickness in px

            // Triangle 1 (point up): 90, 210, 330
            var t1 = new Vector2[3];
            // Triangle 2 (point down): 30, 150, 270
            var t2 = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                float a1 = (90f + 120f * i) * Mathf.Deg2Rad;
                float a2 = (30f + 120f * i) * Mathf.Deg2Rad;
                t1[i] = new Vector2(cx + Mathf.Cos(a1) * radius, cy + Mathf.Sin(a1) * radius);
                t2[i] = new Vector2(cx + Mathf.Cos(a2) * radius, cy + Mathf.Sin(a2) * radius);
            }

            void DrawTriangle(Vector2[] verts)
            {
                for (int i = 0; i < 3; i++)
                {
                    var a = verts[i];
                    var b = verts[(i + 1) % 3];
                    DrawLine(px, size, a, b, lineHalf);
                }
            }
            DrawTriangle(t1);
            DrawTriangle(t2);

            // Center bright dot.
            float dotR = size * 0.04f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= dotR)
                    {
                        float k = 1f - (d / dotR);
                        int idx = y * size + x;
                        var c = px[idx];
                        c.a = Mathf.Max(c.a, k);
                        px[idx] = c;
                    }
                }

            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }

        private static void DrawLine(Color[] px, int size, Vector2 a, Vector2 b, float halfThickness)
        {
            // Bounding box.
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - halfThickness - 1f), 0, size - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt (Mathf.Max(a.x, b.x) + halfThickness + 1f), 0, size - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y) - halfThickness - 1f), 0, size - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt (Mathf.Max(a.y, b.y) + halfThickness + 1f), 0, size - 1);

            Vector2 ab = b - a;
            float len2 = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    Vector2 proj = a + ab * t;
                    float d = Vector2.Distance(p, proj);
                    if (d <= halfThickness)
                    {
                        float alpha = 1f - Mathf.Clamp01((d - halfThickness * 0.6f) / (halfThickness * 0.4f + 0.001f));
                        int idx = y * size + x;
                        var c = px[idx];
                        c.a = Mathf.Max(c.a, alpha);
                        c.r = c.g = c.b = 1f;
                        px[idx] = c;
                    }
                }
            }
        }

        /// <summary>
        /// Vertical light pillar: bright bottom, fades to top with soft horizontal edges.
        /// </summary>
        private static Sprite BuildPillar(int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var px = new Color[width * height];
            float cx = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float vy = (float)y / (height - 1); // 0 bottom, 1 top
                float vAlpha = Mathf.Pow(1f - vy, 1.6f); // bright bottom
                for (int x = 0; x < width; x++)
                {
                    float dx = (x - cx) / cx;        // -1..1
                    float hAlpha = Mathf.Clamp01(1f - Mathf.Abs(dx));
                    hAlpha = hAlpha * hAlpha;
                    px[y * width + x] = new Color(1f, 1f, 1f, vAlpha * hAlpha);
                }
            }
            tex.SetPixels(px);
            // Pivot at bottom-center for the pillar so it grows from the ground.
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.05f), height);
        }

        /// <summary>
        /// 4-pointed sparkle star with soft falloff.
        /// </summary>
        private static Sprite BuildSparkleStar(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c;
                    float dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(1f - d);
                    core = core * core;
                    // Cross arms.
                    float armX = Mathf.Clamp01(1f - Mathf.Abs(dx) * 1.0f) * Mathf.Clamp01(1f - Mathf.Abs(dy) * 6f);
                    float armY = Mathf.Clamp01(1f - Mathf.Abs(dy) * 1.0f) * Mathf.Clamp01(1f - Mathf.Abs(dx) * 6f);
                    float a = Mathf.Clamp01(core + 0.85f * Mathf.Max(armX, armY));
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            return SpriteFromTex(tex, ppu: size * 0.5f);
        }
    }
}
