using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Diagonal rain streaks over the camera frustum + optional rain audio.
    /// Tuned to read as "real rain" without overpowering gameplay readability:
    ///   • Slim vertical sprites that look like falling drops.
    ///   • Slight horizontal velocity so the rain has a wind-driven slant.
    ///   • Cool blue-white tint that composites cleanly over warm worlds.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RainEffect : WeatherEffect
    {
        [SerializeField, Tooltip("Extra world-unit margin beyond the visible viewport on each side. Keeps drops fading in/out cleanly past the edges.")]
        private float _viewportMargin = 2f;

        // Average vertical fall speed (used to size the dynamic lifetime so
        // every spawned drop survives long enough to traverse the viewport).
        private const float AVG_FALL_SPEED = 19f;

        // Procedural sprite cache — soft gradient strip used for every rain
        // drop. Reset on Play Mode entry so Domain-Reload-OFF doesn't keep a
        // stale Texture2D handle around.
        private static Sprite _rainSprite;
        private static Material _rainMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _rainSprite   = null;
            _rainMaterial = null;
        }

        protected override void ConfigureParticles()
        {
            _main.startLifetime          = 1.0f;     // refined per-frame in UpdateEmissionForViewport
            _main.startSpeed             = 0f;
            _main.simulationSpace        = ParticleSystemSimulationSpace.World;
            _main.maxParticles           = 1200;
            _main.gravityModifier        = 0f;
            _main.scalingMode            = ParticleSystemScalingMode.Hierarchy;
            _main.startColor             = new Color(0.78f, 0.86f, 1.00f, 0.55f);
            _main.startSize              = 0.18f;

            // "Splash on contact" approximation: each drop fades to alpha 0
            // over the last 18% of its life. Combined with the dynamic
            // lifetime sized to viewport height in UpdateEmissionForViewport,
            // every drop visibly dissipates as it nears ground level —
            // cheap, no per-tile collision query needed.
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(Color.white, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(1.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 0.82f),
                    new GradientAlphaKey(0.0f, 1.0f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var emit = _emission;
            emit.rateOverTime = 220f;

            var shape = _ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Initial size — gets overwritten every frame by UpdateEmissionForViewport.
            shape.scale     = new Vector3(20f, 0.5f, 0.1f);
            shape.position  = new Vector3(0f, 8f, 0f);

            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.x       = new ParticleSystem.MinMaxCurve(-2f, -1f);
            velocity.y       = new ParticleSystem.MinMaxCurve(-22f, -16f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            var renderer            = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode     = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale    = 6f;       // elongates each particle into a streak
            renderer.velocityScale  = 0.05f;
            renderer.material       = ResolveMaterial();
            renderer.sortingLayerName = SortingLayerExists("VFX") ? "VFX" : "Default";
            renderer.sortingOrder   = 8;

            transform.position = new Vector3(0f, 0f, -1f);
        }

        protected override void UpdateEmissionForViewport(float halfW, float halfH)
        {
            // Spawn from a thin slab right above the visible top edge; drops
            // fall through the entire viewport during their lifetime.
            var shape = _ps.shape;
            shape.scale    = new Vector3((halfW + _viewportMargin) * 2f, 0.5f, 0.1f);
            shape.position = new Vector3(0f, halfH + _viewportMargin, 0f);

            // Randomise lifetime so different drops splash at different Y
            // positions — some "hit the rooftops" early, some make it all the
            // way to street level. Combined with the alpha-fade tail in
            // ConfigureParticles this approximates a ground-collision splash
            // without per-tile physics queries.
            float fullTravel = (halfH + _viewportMargin) * 2f;
            float baseLifetime = fullTravel / AVG_FALL_SPEED;
            _main.startLifetime = new ParticleSystem.MinMaxCurve(
                baseLifetime * 0.55f,
                baseLifetime * 1.00f);
        }

        private static bool SortingLayerExists(string name)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == name) return true;
            return false;
        }

        private static Material ResolveMaterial()
        {
            if (_rainMaterial != null) return _rainMaterial;
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _rainMaterial = new Material(shader);
            _rainMaterial.mainTexture = ResolveTexture();
            if (_rainMaterial.HasProperty("_Mode"))     _rainMaterial.SetFloat("_Mode", 2f); // fade
            if (_rainMaterial.HasProperty("_SrcBlend")) _rainMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_rainMaterial.HasProperty("_DstBlend")) _rainMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return _rainMaterial;
        }

        private static Texture2D ResolveTexture()
        {
            if (_rainSprite != null && _rainSprite.texture != null) return _rainSprite.texture;
            // 4×16 vertical strip with a soft fade at top/bottom for natural rain
            // streaks. Stretch render-mode elongates this into the visible drop.
            const int W = 4, H = 16;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float fadeY = Mathf.Clamp01(Mathf.Sin((y + 0.5f) / H * Mathf.PI));
                for (int x = 0; x < W; x++)
                {
                    float dx     = Mathf.Abs((x + 0.5f) - W * 0.5f) / (W * 0.5f);
                    float fadeX  = 1f - dx;
                    float a      = fadeX * fadeY;
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px); tex.Apply();
            _rainSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
            return tex;
        }
    }
}
