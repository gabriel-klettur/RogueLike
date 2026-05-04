using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Visible wind: drifting horizontal streaks of dust / leaves moving across
    /// the camera. Subtler than rain or snow — wind is mostly *audio*; this
    /// effect just gives the player a visual hint that the gust is real.
    ///
    /// (Future: would also bend tree sprites if we add per-tile wind sway.)
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class WindEffect : WeatherEffect
    {
        [SerializeField, Tooltip("Extra world-unit margin beyond the visible viewport on each side. Keeps gusts fading in/out cleanly past the edges.")]
        private float _viewportMargin = 2f;

        // Average horizontal blow speed (used to size dynamic lifetime).
        private const float AVG_BLOW_SPEED = 10f;

        private static Sprite   _windSprite;
        private static Material _windMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _windSprite   = null;
            _windMaterial = null;
        }

        protected override void ConfigureParticles()
        {
            _main.startLifetime          = 2.5f;     // refined per-frame in UpdateEmissionForViewport
            _main.startSpeed             = 0f;
            _main.simulationSpace        = ParticleSystemSimulationSpace.World;
            _main.maxParticles           = 300;
            _main.gravityModifier        = 0f;
            _main.scalingMode            = ParticleSystemScalingMode.Hierarchy;
            _main.startColor             = new Color(0.95f, 0.92f, 0.80f, 0.30f);
            _main.startSize              = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);

            var emit = _emission;
            emit.rateOverTime = 35f;

            var shape = _ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Tall thin slab on the right side spawning streaks that blow LEFT.
            shape.scale     = new Vector3(0.5f, 12f, 0.1f);
            shape.position  = new Vector3(10f, 0f, 0f);

            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.x       = new ParticleSystem.MinMaxCurve(-12f, -8f);
            velocity.y       = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            var renderer            = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode     = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale    = 4f;
            renderer.velocityScale  = 0.04f;
            renderer.material       = ResolveMaterial();
            renderer.sortingLayerName = SortingLayerExists("VFX") ? "VFX" : "Default";
            renderer.sortingOrder   = 6;

            transform.position = new Vector3(0f, 0f, -1f);
        }

        protected override void UpdateEmissionForViewport(float halfW, float halfH)
        {
            // Spawn from a thin column just past the right edge of the viewport;
            // gusts blow leftward across the entire visible width during their
            // lifetime.
            var shape = _ps.shape;
            shape.scale    = new Vector3(0.5f, (halfH + _viewportMargin) * 2f, 0.1f);
            shape.position = new Vector3(halfW + _viewportMargin, 0f, 0f);

            float travel = (halfW + _viewportMargin) * 2f;
            _main.startLifetime = travel / AVG_BLOW_SPEED;
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
            if (_windMaterial != null) return _windMaterial;
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _windMaterial = new Material(shader);
            _windMaterial.mainTexture = ResolveTexture();
            if (_windMaterial.HasProperty("_Mode"))     _windMaterial.SetFloat("_Mode", 2f); // fade
            if (_windMaterial.HasProperty("_SrcBlend")) _windMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_windMaterial.HasProperty("_DstBlend")) _windMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return _windMaterial;
        }

        private static Texture2D ResolveTexture()
        {
            if (_windSprite != null && _windSprite.texture != null) return _windSprite.texture;
            // 16×4 horizontal soft strip — Stretch render mode elongates this
            // into a streaky gust. Same conceptual texture as rain but rotated.
            const int W = 16, H = 4;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float fadeY = Mathf.Clamp01(Mathf.Sin((y + 0.5f) / H * Mathf.PI));
                for (int x = 0; x < W; x++)
                {
                    float fadeX = Mathf.Clamp01(Mathf.Sin((x + 0.5f) / W * Mathf.PI));
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(fadeX * fadeY * 255));
                }
            }
            tex.SetPixels32(px); tex.Apply();
            _windSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
            return tex;
        }
    }
}
