using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.World.Sky
{
    /// <summary>
    /// The sky's shadow on the ground: one camera-following quad on the topmost world sorting
    /// layer, multiplying the world by a slow field of cloud noise sampled by world position.
    /// It is also the owner of the sun — it evaluates <see cref="SunShadowState"/> once per
    /// frame for every caster in the world, because the sky is the one thing that knows the
    /// hour, the weather and whether the player is under a roof.
    ///
    /// Three inputs and the layer knows nothing else:
    ///   • the hour, through <see cref="DayNightCycle"/>: no daylight, no shadows of any kind;
    ///   • the weather, through <see cref="WeatherManager"/>: rain and snow close the sky over
    ///     (more cloud, softer) while the grade does the darkening, so the strength drops;
    ///   • indoors, through the same manager: under a roof there is no sky.
    ///
    /// The quad lives on the sorting layer immediately BELOW <c>Projectiles</c>, resolved from
    /// <see cref="SortingLayer.layers"/> at build time. Projectiles and VFX are the two layers
    /// the ambient light deliberately never touches (<c>AmbientUnlitSortingLayers</c>: they are
    /// self-luminous), and a cloud must respect the same line — a shadow dimming a fireball is
    /// exactly what the day/night cycle refused to do, for the same reason. So the quad darkens
    /// the ground, the buildings and the creatures under that line and nothing above it: no
    /// shot, no spell, no bar. The price is that the rare tile painted on <c>Overhead</c> or a
    /// tier above it is never shadowed, which is the smaller wrong. Resolved rather than named
    /// because a new sorting layer is a TagManager edit outside Play Mode plus a denylist entry,
    /// and a name that does not resolve lands the quad on Default, behind the world, silently.
    ///
    /// Inside that layer the order is the top of the short range: entities on it sort by their
    /// Y term (up to ~31,700 at the Y-sort budget), and the quad has to clear every one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudShadowLayer : MonoBehaviour
    {
        private const string ShaderName = "Valkur/CloudShadow";
        private const int    SortingOrder = short.MaxValue - 16;

        /// <summary>How much larger than the viewport the quad is, so a frame of camera lag never shows its edge.</summary>
        private const float ViewportMargin = 1.6f;

        private static readonly int CloudParamsId = Shader.PropertyToID("_CloudParams");
        private static readonly int CloudOffsetId = Shader.PropertyToID("_CloudOffset");

        private SkyStyle      _style;
        private MeshRenderer  _renderer;
        private MeshFilter    _filter;
        private Material      _material;
        private Camera        _camera;
        private DayNightCycle _cycle;
        private Vector2       _offset;
        private bool          _built;

        /// <summary>The sorting layer the quad draws on. Test seam.</summary>
        public string SortingLayerName => _renderer != null ? _renderer.sortingLayerName : "";

        /// <summary>Whether the quad is drawing this frame (strength above the floor).</summary>
        public bool IsDrawing => _renderer != null && _renderer.enabled;

        /// <summary>The darkening strength last pushed to the shader. Test seam.</summary>
        public float Strength { get; private set; }

        /// <summary>The coverage threshold last pushed to the shader. Test seam.</summary>
        public float Coverage { get; private set; }

        private void Awake() => EnsureBuilt();

        /// <summary>
        /// Build the quad and its material. Public because Unity calls no Awake on a component
        /// added in Edit Mode, and the fixture has to reach the same code Play Mode does.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            _style = SkyStyle.Active;

            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            _filter.sharedMesh = BuildQuad();

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows       = false;
            _renderer.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _renderer.sortingLayerName     = ResolveCloudSortingLayer();
            _renderer.sortingOrder         = SortingOrder;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[CloudShadowLayer] Shader '{ShaderName}' not found — cloud shadows disabled.");
                _renderer.enabled = false;
                return;
            }
            _material = new Material(shader) { name = "CloudShadow", hideFlags = HideFlags.HideAndDontSave };
            _renderer.sharedMaterial = _material;
            _renderer.enabled = false;   // switched on by the first tick that has daylight
        }

        /// <summary>
        /// The sorting layer directly beneath <c>Projectiles</c> — the last lit world layer
        /// before the self-luminous ones. Falls back to <c>WallsTop</c> for a TagManager
        /// without a Projectiles layer.
        /// </summary>
        public static string ResolveCloudSortingLayer()
        {
            var layers = SortingLayer.layers;
            for (int i = 1; i < layers.Length; i++)
                if (layers[i].name == SortingConfig.LAYER_PROJECTILES) return layers[i - 1].name;
            return SortingConfig.LAYER_WALLS_TOP;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "CloudShadowQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices  = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            mesh.uv        = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Update()
        {
            // The sun first: every caster reads SunShadowState in LateUpdate, and the sky is
            // what decides it. Update runs before every LateUpdate in the frame.
            float overcast = Overcast01();
            bool  indoors  = WeatherManager.Instance != null && WeatherManager.Instance.IsIndoors;
            float t        = ResolveTime();
            SunShadowState.Tick(_style, t, overcast, indoors);

            TickClouds(Time.deltaTime, overcast, indoors);
        }

        private void LateUpdate() => FollowCamera();

        /// <summary>
        /// Advance the cloud field. Public with an explicit delta so a test can drive it.
        /// </summary>
        public void TickClouds(float dt, float overcast01, bool indoors)
        {
            if (_renderer == null || _material == null) return;

            // Accumulated, never derived from the wind at the time of sampling: a gust that
            // doubles the wind would otherwise jump the whole sky sideways.
            _offset += new Vector2(WeatherWind.VelocityX * _style.cloudSpeedFactor * _style.cloudScale,
                                   _style.cloudDriftY) * dt;

            Coverage = Mathf.Lerp(_style.coverageClear, _style.coverageOvercast, overcast01);

            float strength = _style.cloudStrength * SunShadowState.Daylight;
            // Under an overcast sky the grade already lifts and desaturates the whole frame;
            // shadows stronger than that read as a second, contradicting weather.
            strength *= 1f - overcast01 * 0.65f;
            if (indoors || !WorldLookSettings.CloudShadows) strength = 0f;
            Strength = strength;

            bool draw = strength > 0.004f;
            if (_renderer.enabled != draw) _renderer.enabled = draw;
            if (!draw) return;

            _material.SetVector(CloudParamsId, new Vector4(_style.cloudScale, Coverage, _style.cloudSoftness, strength));
            _material.SetVector(CloudOffsetId, new Vector4(_offset.x, _offset.y, 0f, 0f));
        }

        private void FollowCamera()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            float h = _camera.orthographicSize * 2f * ViewportMargin;
            float w = h * _camera.aspect;
            var   p = _camera.transform.position;
            transform.position   = new Vector3(p.x, p.y, 0f);
            transform.localScale = new Vector3(w, h, 1f);
        }

        private float ResolveTime()
        {
            if (_cycle == null)
            {
                _cycle = DayNightCycle.HasInstance ? DayNightCycle.Instance : FindObjectOfType<DayNightCycle>();
                if (_cycle == null) return 0.5f;   // no cycle: permanent noon
            }
            return _cycle.TimeNormalized;
        }

        private static float Overcast01()
        {
            var wm = WeatherManager.Instance;
            if (wm == null) return 0f;
            float rain = wm.DensityOf(WeatherType.Rain);
            float snow = wm.DensityOf(WeatherType.Snow);
            return Mathf.Clamp01(Mathf.Max(rain, snow));
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_filter != null && _filter.sharedMesh != null) Destroy(_filter.sharedMesh);
        }
    }
}
