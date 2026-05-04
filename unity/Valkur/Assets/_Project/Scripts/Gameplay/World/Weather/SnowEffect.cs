using UnityEngine;

namespace Valkur.Gameplay.World.Weather
{
    /// <summary>
    /// Soft white snowflakes drifting slowly downward across the camera.
    /// Larger / slower particles than rain so the world reads as cold and
    /// still. Slight horizontal sine sway gives each flake a "fluttery"
    /// path rather than a straight fall.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class SnowEffect : WeatherEffect
    {
        [SerializeField, Tooltip("Extra world-unit margin beyond the visible viewport on each side. Keeps flakes fading in/out cleanly past the edges.")]
        private float _viewportMargin = 2f;

        // Average vertical fall speed (used to size dynamic lifetime).
        private const float AVG_FALL_SPEED = 1.4f;

        private static Sprite _snowSprite;
        private static Material _snowMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            _snowSprite   = null;
            _snowMaterial = null;
        }

        protected override void ConfigureParticles()
        {
            _main.startLifetime          = 8f;       // refined per-frame in UpdateEmissionForViewport
            _main.startSpeed             = 0f;
            _main.simulationSpace        = ParticleSystemSimulationSpace.World;
            _main.maxParticles           = 600;
            _main.gravityModifier        = 0f;
            _main.scalingMode            = ParticleSystemScalingMode.Hierarchy;
            _main.startColor             = new Color(1.00f, 1.00f, 1.00f, 0.85f);
            _main.startSize              = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            _main.startRotation          = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emit = _emission;
            emit.rateOverTime = 70f;

            var shape = _ps.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(20f, 0.2f, 0.1f);
            shape.position  = new Vector3(0f, 8f, 0f);

            var velocity = _ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.x       = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y       = new ParticleSystem.MinMaxCurve(-1.8f, -1.0f);
            velocity.z       = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Subtle sine wobble so flakes flutter sideways instead of dropping
            // in a perfect line.
            var noise = _ps.noise;
            noise.enabled    = true;
            noise.strengthX  = 0.4f;
            noise.strengthY  = 0.05f;
            noise.frequency  = 0.25f;
            noise.scrollSpeed = 0.4f;

            var renderer            = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode     = ParticleSystemRenderMode.Billboard;
            renderer.material       = ResolveMaterial();
            renderer.sortingLayerName = SortingLayerExists("VFX") ? "VFX" : "Default";
            renderer.sortingOrder   = 7;

            transform.position = new Vector3(0f, 0f, -1f);
        }

        protected override void UpdateEmissionForViewport(float halfW, float halfH)
        {
            var shape = _ps.shape;
            shape.scale    = new Vector3((halfW + _viewportMargin) * 2f, 0.2f, 0.1f);
            shape.position = new Vector3(0f, halfH + _viewportMargin, 0f);

            float travel = (halfH + _viewportMargin) * 2f;
            _main.startLifetime = travel / AVG_FALL_SPEED;
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
            if (_snowMaterial != null) return _snowMaterial;
            var shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _snowMaterial = new Material(shader);
            _snowMaterial.mainTexture = ResolveTexture();
            if (_snowMaterial.HasProperty("_Mode"))     _snowMaterial.SetFloat("_Mode", 2f); // fade
            if (_snowMaterial.HasProperty("_SrcBlend")) _snowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_snowMaterial.HasProperty("_DstBlend")) _snowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return _snowMaterial;
        }

        private static Texture2D ResolveTexture()
        {
            if (_snowSprite != null && _snowSprite.texture != null) return _snowSprite.texture;
            // 32×32 soft circular flake with a stronger center than the
            // atmospheric dust so individual flakes read clearly.
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px  = new Color32[N * N];
            float r = N * 0.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01(1f - d / r);
                a = Mathf.Pow(a, 0.7f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            _snowSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return tex;
        }
    }
}
