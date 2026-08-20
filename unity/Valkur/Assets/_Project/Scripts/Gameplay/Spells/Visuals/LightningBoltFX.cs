using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A lightning arc between two points: a wide plasma halo, a saturated bolt, a white-hot
    /// filament, up to three forks that die on the way, and lights along the path.
    ///
    /// Three things were wrong with the previous version and all three made it hard to see.
    /// It drew on the Entities sorting layer, so wall tops, decorations and every other VFX
    /// covered it. It assigned <c>lr.material</c>, cloning the shared material once per bolt.
    /// And its shape was uniform perpendicular jitter, which reads as a ribbon rather than a
    /// discharge. It now draws on the VFX layer, shares its material, and builds its path by
    /// fractal subdivision.
    ///
    /// The life of the arc is a leader, a flash and a flickering decay, because a bolt that
    /// simply fades looks like a fading line and a bolt that flickers looks like electricity.
    /// </summary>
    public class LightningBoltFX : MonoBehaviour
    {
        private const float LIFETIME = 0.32f;
        private const float LEADER_END = 0.05f;
        private const float FLASH_END = 0.13f;

        private const float HALO_WIDTH = 0.46f;
        private const float BOLT_WIDTH = 0.17f;
        private const float CORE_WIDTH = 0.062f;
        private const float FORK_WIDTH = 0.085f;

        private const float DISPLACEMENT = 0.11f;
        private const float FORK_DISPLACEMENT = 0.16f;
        private const int MAX_FORKS = 3;

        private const float SHAKE_AMPLITUDE = 0.10f;
        private const float SHAKE_DURATION = 0.12f;
        private const float PEAK_LIGHT_INTENSITY = 3.4f;

        /// <summary>Path indices the forks leave from — spread over the middle of the arc.</summary>
        [SelfHealingStatic("Fixed lookup of three path indices, built once from literals. " +
                           "Never written to and holds no Unity objects, so it cannot go stale.")]
        private static readonly int[] ForkAnchors = { 8, 15, 22 };

        private LineRenderer _halo;
        private LineRenderer _bolt;
        private LineRenderer _core;
        private LineRenderer[] _forks;
        private Component[] _lights;
        private SpriteRenderer _originFlare;
        private SpriteRenderer _originGlare;

        private Vector3[] _points;
        private Vector3[] _forkPoints;
        private Vector3 _from;
        private Vector3 _to;
        private Color _tint;
        private Color _coreTint;
        private float _thickness;
        private float _age;

        /// <summary>
        /// Draws an arc from <paramref name="from"/> to <paramref name="to"/>.
        /// <paramref name="thickness"/> scales every width, so a boss discharge can be
        /// heavier than a chained jump without a second effect.
        /// </summary>
        public static LightningBoltFX Spawn(Vector3 from, Vector3 to, Color tint,
                                            bool shake = true, float thickness = 1f)
        {
            var go = new GameObject("LightningBoltFX");
            go.transform.position = (from + to) * 0.5f;

            var fx = go.AddComponent<LightningBoltFX>();
            fx._from = from;
            fx._to = to;
            fx._tint = tint.a > 0.05f ? tint : new Color(0.55f, 0.85f, 1f, 1f);
            fx._coreTint = Color.Lerp(fx._tint, Color.white, 0.88f);
            fx._thickness = Mathf.Max(0.2f, thickness);
            fx.Build();

            if (shake)
                Feel.CameraFeel.Cue(Data.Feel.CameraFeelCue.ImpactLight,
                                    (to - from).normalized);
            ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_lightning_arc");
            return fx;
        }

        private void Build()
        {
            ElementalSprites.EnsureAll();

            _points = new Vector3[LightningPath.POINT_COUNT];
            _forkPoints = new Vector3[LightningPath.POINT_COUNT];

            _halo = BuildLine("Halo", HALO_WIDTH, WithAlpha(_tint, 0.38f), 70);
            _bolt = BuildLine("Bolt", BOLT_WIDTH, _tint, 72);
            _core = BuildLine("Core", CORE_WIDTH, _coreTint, 74);

            _forks = new LineRenderer[MAX_FORKS];
            for (int i = 0; i < MAX_FORKS; i++)
                _forks[i] = BuildLine("Fork_" + i, FORK_WIDTH, WithAlpha(_tint, 0.8f), 71);

            BuildOriginFlare();
            RollPath();
            BuildLights();
        }

        /// <summary>
        /// The pop at the caster's hand. It used to come from the <c>lightning_emitter</c>
        /// particle preset, which is a <c>kind: lightning</c> preset — meaning it drew a
        /// whole second LineRenderer bolt of its own, at the caster, in no particular
        /// direction, and lived for its lifespan plus a second. Four times longer than the
        /// arc it was decorating, it read as a small bolt stuck to the player. The flare
        /// belongs to the bolt, and dies with it.
        /// </summary>
        private void BuildOriginFlare()
        {
            _originFlare = CreateSprite("OriginFlare", ElementalSprites.HotCore, _coreTint, 73);
            _originGlare = CreateSprite("OriginGlare", ElementalSprites.SparkleStar, _tint, 75);
            _originFlare.transform.position = _from;
            _originGlare.transform.position = _from;
            _originGlare.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, worldPositionStays: false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private LineRenderer BuildLine(string objectName, float width, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = LightningPath.POINT_COUNT;
            lr.startWidth = lr.endWidth = width * _thickness;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            lr.startColor = lr.endColor = color;
            lr.sortingLayerName = SortingConfig.LAYER_VFX;
            lr.sortingOrder = order;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>Three lights: the hand, the middle of the arc and the point it lands on.</summary>
        private void BuildLights()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lights = new Component[3];
            Vector3[] at = { _from, (_from + _to) * 0.5f, _to };
            float[] outer = { 2.0f, 2.6f, 3.0f };

            for (int i = 0; i < at.Length; i++)
            {
                var go = new GameObject("ArcLight_" + i);
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.position = at[i];
                try
                {
                    var light = go.AddComponent(lightType);
                    var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (typeProp != null)
                        typeProp.SetValue(light, System.Enum.ToObject(typeProp.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(light, _tint);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(light, outer[i] * _thickness);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(light, 0.25f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(light, 0.85f);
                    _lights[i] = light;
                }
                catch { _lights[i] = null; }
            }
        }

        private void RollPath()
        {
            LightningPath.Generate(_points, _from, _to, DISPLACEMENT);
            _halo.SetPositions(_points);
            _bolt.SetPositions(_points);
            _core.SetPositions(_points);

            Vector3 direction = (_to - _from).normalized;
            float length = (_to - _from).magnitude;

            for (int i = 0; i < _forks.Length; i++)
            {
                LightningPath.GenerateFork(_forkPoints, _points[ForkAnchors[i]], direction,
                    length * Random.Range(0.18f, 0.36f), FORK_DISPLACEMENT);
                _forks[i].SetPositions(_forkPoints);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;

            float energy = Energy(_age);

            // The leader gutters, so it is re-rolled every frame; once the arc has struck it
            // only needs to crackle, and every other frame is enough to sell that.
            if (_age < LEADER_END || Time.frameCount % 2 == 0) RollPath();

            ApplyLine(_halo, HALO_WIDTH, _tint, 0.38f * energy, energy);
            ApplyLine(_bolt, BOLT_WIDTH, _tint, energy, energy);
            ApplyLine(_core, CORE_WIDTH, _coreTint, energy * energy, energy);

            // Forks belong to the strike itself, not to its afterglow.
            float forkEnergy = Mathf.Max(0f, energy - 0.35f) / 0.65f;
            for (int i = 0; i < _forks.Length; i++)
                ApplyLine(_forks[i], FORK_WIDTH, _tint, forkEnergy * 0.8f, forkEnergy);

            ApplyOriginFlare(energy);
            ApplyLights(energy);

            if (_age >= LIFETIME) Destroy(gameObject);
        }

        /// <summary>
        /// Leader, flash, then a flickering decay. The flicker is what separates a bolt from
        /// a line that is being turned down.
        /// </summary>
        private static float Energy(float age)
        {
            if (age < LEADER_END) return Mathf.Lerp(0.22f, 0.55f, age / LEADER_END);
            if (age < FLASH_END) return 1f;

            float decay = Mathf.Clamp01((age - FLASH_END) / (LIFETIME - FLASH_END));
            float falloff = Mathf.Pow(1f - decay, 1.6f);
            float flicker = 0.62f + 0.38f * Mathf.Sin(age * 82f);
            return falloff * flicker;
        }

        private void ApplyLine(LineRenderer line, float baseWidth, Color color,
                               float alpha, float widthEnergy)
        {
            if (line == null) return;
            Color c = WithAlpha(color, Mathf.Clamp01(alpha));
            line.startColor = line.endColor = c;
            float width = baseWidth * _thickness * Mathf.Lerp(0.45f, 1.15f, widthEnergy);
            line.startWidth = line.endWidth = width;
        }

        private void ApplyOriginFlare(float energy)
        {
            if (_originFlare == null) return;
            float size = (0.30f + 0.34f * energy) * _thickness;
            _originFlare.transform.localScale = Vector3.one * size;
            _originFlare.color = WithAlpha(_coreTint, energy * energy);

            float glare = (0.55f + 0.85f * energy) * _thickness;
            _originGlare.transform.localScale = new Vector3(glare, glare * 0.85f, 1f);
            _originGlare.color = WithAlpha(_tint, energy * 0.85f);
        }

        private void ApplyLights(float energy)
        {
            if (_lights == null) return;
            var intensityProp = ElementalProjectileVisual.GetLight2DIntensityProp();
            if (intensityProp == null) return;

            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null) continue;
                try { intensityProp.SetValue(_lights[i], PEAK_LIGHT_INTENSITY * energy); }
                catch { /* URP 2D lighting absent in this project configuration. */ }
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
