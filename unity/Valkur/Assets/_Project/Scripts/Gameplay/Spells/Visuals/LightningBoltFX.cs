using System.Reflection;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Epic procedural lightning arc rendered as a multi-segment zig-zag with a
    /// white-hot core <see cref="LineRenderer"/> + blue plasma glow <see cref="LineRenderer"/>
    /// + Light2D pulses at both endpoints + camera shake. Self-destructs after a short
    /// flash. Used by <see cref="LightningExecutor"/> for chain_lightning and by
    /// <see cref="LaserBeamController"/> for the laser visual.
    /// </summary>
    public class LightningBoltFX : MonoBehaviour
    {
        // ── Tuning ───────────────────────────────────────────────────────
        private const float CoreWidth      = 0.10f;
        private const float GlowWidth      = 0.32f;
        private const int   Segments       = 14;          // zig-zag points between A and B
        private const float JaggedAmplitude = 0.18f;       // random perpendicular jitter
        private const float Lifetime       = 0.24f;
        private const float ShakeAmp       = 0.06f;
        private const float ShakeDur       = 0.10f;

        private LineRenderer _coreLr;
        private LineRenderer _glowLr;
        private GameObject _lightAGo, _lightBGo;
        private Component _lightA, _lightB;
        private Color _coreColor;
        private Color _glowColor;
        private float _t;

        public static LightningBoltFX Spawn(Vector3 from, Vector3 to, Color tint, bool shake = true)
        {
            var go = new GameObject("LightningBoltFX");
            go.transform.position = (from + to) * 0.5f;
            var fx = go.AddComponent<LightningBoltFX>();
            fx._coreColor = new Color(1f, 1f, 1f, 1f);
            fx._glowColor = tint.a > 0.05f ? tint : new Color(0.55f, 0.85f, 1f, 0.85f);
            fx.Build(from, to);
            if (shake) CameraShake.Trigger(ShakeAmp, ShakeDur);

            // Audio
            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById("spell_lightning_arc");
            return fx;
        }

        private void Build(Vector3 from, Vector3 to)
        {
            ElementalSprites.EnsureAll();

            _glowLr = BuildLine("Glow",  GlowWidth, _glowColor, SortingConfig.Z_SKY + 9);
            _coreLr = BuildLine("Core",  CoreWidth, _coreColor, SortingConfig.Z_SKY + 11);

            // Generate zig-zag points: linear interpolation + perpendicular noise.
            Vector3 dir = (to - from);
            float len = dir.magnitude;
            if (len < 1e-4f) len = 0.01f;
            Vector3 fwd = dir / len;
            Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);

            int n = Segments;
            var pts = new Vector3[n];
            pts[0] = from;
            pts[n - 1] = to;
            for (int i = 1; i < n - 1; i++)
            {
                float u = i / (float)(n - 1);
                // Larger jitter mid-line, smaller near endpoints
                float fall = Mathf.Sin(u * Mathf.PI);
                float j = (Random.value * 2f - 1f) * JaggedAmplitude * fall * len * 0.25f;
                pts[i] = Vector3.Lerp(from, to, u) + perp * j;
            }

            _coreLr.positionCount = n;
            _glowLr.positionCount = n;
            _coreLr.SetPositions(pts);
            _glowLr.SetPositions(pts);

            // Endpoint Light2D pulses
            SpawnLight(from, ref _lightAGo, ref _lightA);
            SpawnLight(to,   ref _lightBGo, ref _lightB);
        }

        private LineRenderer BuildLine(string name, float width, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.material = ElementalSprites.SharedUnlitMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            lr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            lr.sortingOrder = order;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        private void SpawnLight(Vector3 worldPos, ref GameObject go, ref Component comp)
        {
            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType == null) return;
            go = new GameObject("ArcLight");
            go.transform.position = worldPos;
            try
            {
                comp = go.AddComponent(l2dType);
                var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                if (lt != null) lt.SetValue(comp, System.Enum.ToObject(lt.PropertyType, 2));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(comp, _glowColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(comp, 3.0f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(comp, 2.4f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(comp, 0.3f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(comp, 0.85f);
            }
            catch { /* reflection safety */ }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / Lifetime);

            // Crackle: redraw zig-zag every ~30ms while alive
            if (_t < Lifetime * 0.7f && (Time.frameCount % 2) == 0)
                RecrackleZigzag();

            float a = (1f - u) * (1f - u);
            if (_coreLr != null)
            {
                var c = _coreColor; c.a = a;
                _coreLr.startColor = c; _coreLr.endColor = c;
                float w = CoreWidth * Mathf.Lerp(1.2f, 0.4f, u);
                _coreLr.startWidth = w; _coreLr.endWidth = w;
            }
            if (_glowLr != null)
            {
                var c = _glowColor; c.a = _glowColor.a * a;
                _glowLr.startColor = c; _glowLr.endColor = c;
                float w = GlowWidth * Mathf.Lerp(1.6f, 0.6f, u);
                _glowLr.startWidth = w; _glowLr.endWidth = w;
            }

            // Decay endpoint lights
            float intensity = Mathf.Lerp(3.0f, 0f, u);
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_lightA, intensity); } catch { }
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_lightB, intensity); } catch { }

            if (_t >= Lifetime) Destroy(gameObject);
        }

        private void RecrackleZigzag()
        {
            if (_coreLr == null || _glowLr == null || _coreLr.positionCount < 3) return;
            int n = _coreLr.positionCount;
            Vector3 from = _coreLr.GetPosition(0);
            Vector3 to = _coreLr.GetPosition(n - 1);
            Vector3 dir = (to - from);
            float len = Mathf.Max(0.01f, dir.magnitude);
            Vector3 fwd = dir / len;
            Vector3 perp = new Vector3(-fwd.y, fwd.x, 0f);
            for (int i = 1; i < n - 1; i++)
            {
                float u = i / (float)(n - 1);
                float fall = Mathf.Sin(u * Mathf.PI);
                float j = (Random.value * 2f - 1f) * JaggedAmplitude * fall * len * 0.25f;
                Vector3 p = Vector3.Lerp(from, to, u) + perp * j;
                _coreLr.SetPosition(i, p);
                _glowLr.SetPosition(i, p);
            }
        }
    }
}
