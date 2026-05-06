using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// XP Orb that gets attracted toward and absorbed by the player.
    /// Maps to Python's OrbAttractionSystem + ExperienceSystem.
    /// Constants: attract_radius=6.25 world units (100px/16), speed=0.3125 world units/frame (5px/16).
    ///
    /// Visuals are built from the static <see cref="BuildVisuals"/> entry so
    /// every spawn site (loot drop, designer-placed reward, scripted gift)
    /// shares the exact same look: a radial gradient sprite with a soft
    /// halo, a sparkle <see cref="ParticleSystem"/>, and a gentle scale
    /// pulse via <see cref="XpOrbPulse"/>.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class XpOrb : MonoBehaviour
    {
        [SerializeField, Tooltip("XP value of this orb.")]
        private int xpValue = 1;

        [SerializeField, Tooltip("Auto-pickup radius in world units. " +
                                 "Tile is 1 wu (PPU=16, 16px tiles), so 1.5 = ~1–2 tiles. " +
                                 "Until the player crosses inside this radius the orb sits idle on the ground.")]
        private float attractRadius = 1.5f;

        [SerializeField, Tooltip("Movement speed toward player in world units/sec when within attract radius.")]
        private float attractSpeed = 18.75f;

        [SerializeField, Tooltip("Distance at which the orb is absorbed (granted to the player).")]
        private float absorbDistance = 0.5f;

        [SerializeField, Tooltip("Grace period in seconds after spawn during which the orb ignores the player. " +
                                 "Prevents instant absorption when an NPC dies in melee range.")]
        private float settleDuration = 0.4f;

        private Transform _playerTransform;
        private bool _absorbed;
        private float _spawnTime;

        public int XpValue => xpValue;
        /// <summary>True while the orb is in its post-spawn grace period.</summary>
        public bool IsSettling => Time.time - _spawnTime < settleDuration;

        public void Initialize(int xp, Vector3 position)
        {
            xpValue = xp;
            transform.position = position;
            _absorbed = false;
            _spawnTime = Time.time;
        }

        private void Update()
        {
            if (_absorbed) return;
            if (IsSettling) return; // sit on the ground for the grace period

            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            Vector3 toPlayer = _playerTransform.position - transform.position;
            float dist = toPlayer.magnitude;

            // Outside the auto-pickup radius the orb stays put — the player has to walk closer.
            if (dist > attractRadius) return;

            if (dist <= absorbDistance)
            {
                Absorb();
                return;
            }

            Vector3 dir = toPlayer.normalized;
            float step = attractSpeed * Time.deltaTime;
            if (step >= dist) step = dist;
            transform.position += dir * step;
        }

        private void Absorb()
        {
            if (_absorbed) return;
            _absorbed = true;

            if (_playerTransform != null)
            {
                var xp = _playerTransform.GetComponent<Experience>();
                if (xp != null)
                {
                    xp.AddXp(xpValue);
                    GameEvents.FireXpGained(_playerTransform.gameObject, xpValue);
                }
            }

            Destroy(gameObject);
        }

        // ── Visuals ─────────────────────────────────────────────────────────────

        private static Sprite   _cachedOrbSprite;
        private static Sprite   _cachedSparkleSprite;
        private static Material _cachedUnlitMaterial;
        private static Material _cachedSparkleMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisualStaticsOnPlayModeEnter()
        {
            // Cached sprites/materials would point at Texture2D / Material
            // instances destroyed by the previous play session.
            _cachedOrbSprite       = null;
            _cachedSparkleSprite   = null;
            _cachedUnlitMaterial   = null;
            _cachedSparkleMaterial = null;
        }

        /// <summary>
        /// Builds the canonical XP-orb visual hierarchy on <paramref name="root"/>:
        /// a centred <see cref="SpriteRenderer"/> with the gradient sprite, a
        /// child sparkle <see cref="ParticleSystem"/>, and a
        /// <see cref="XpOrbPulse"/> for the breathing scale animation.
        /// Idempotent: calling twice is a no-op for already-present pieces.
        /// </summary>
        public static void BuildVisuals(GameObject root)
        {
            if (root == null) return;

            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) sr = root.AddComponent<SpriteRenderer>();
            sr.sprite           = GetOrbSprite();
            sr.color            = Color.white; // palette lives in the sprite gradient
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder     = 5;
            // Sprite-Lit-Default would render black without a Light2D in the
            // scene (URP gotcha documented in CLAUDE.md). Force unlit so the
            // gradient palette comes through unchanged regardless of lighting.
            var unlit = ResolveUnlitSpriteMaterial();
            if (unlit != null) sr.sharedMaterial = unlit;

            if (root.transform.Find("Sparkles") == null)
                CreateSparkles(root.transform);

            if (root.GetComponent<XpOrbPulse>() == null)
                root.AddComponent<XpOrbPulse>();
        }

        /// <summary>
        /// Procedural radial-gradient sprite — bright sky-blue with a white
        /// hot-spot core and a soft transparent halo. 48×48 RGBA32 with
        /// bilinear filtering. Cached per play session.
        /// </summary>
        public static Sprite GetOrbSprite()
        {
            if (_cachedOrbSprite != null) return _cachedOrbSprite;

            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                name       = "XpOrbTexture"
            };

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxR = size / 2f;

            // Four-stop palette tuned for "bright XP gem" reading:
            //   • core   — pure-white hot-spot, almost ignites the centre.
            //   • inner  — saturated sky blue, the gem body.
            //   • outer  — deep saphire, the gem rim that defines the silhouette.
            //   • halo   — same hue, alpha 0, gives the soft glow falloff.
            Color core  = new Color(1.00f, 1.00f, 1.00f, 1.00f);
            Color inner = new Color(0.55f, 0.85f, 1.00f, 1.00f);
            Color outer = new Color(0.22f, 0.55f, 1.00f, 0.95f);
            Color halo  = new Color(0.10f, 0.35f, 0.95f, 0.00f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = Mathf.Clamp01(dist / maxR);

                    Color c;
                    if (t < 0.18f)        c = Color.Lerp(core,  inner, t / 0.18f);
                    else if (t < 0.55f)   c = Color.Lerp(inner, outer, (t - 0.18f) / 0.37f);
                    else                  c = Color.Lerp(outer, halo,  (t - 0.55f) / 0.45f);

                    // Subtle highlight — top-left quadrant gets a tiny extra
                    // brightness so the orb reads as a 3D gem, not a flat disc.
                    float hx = (x + 0.5f - center.x * 0.55f) / maxR;
                    float hy = (y + 0.5f - center.y * 1.40f) / maxR;
                    float hd = Mathf.Sqrt(hx * hx + hy * hy);
                    if (hd < 0.35f && t < 0.5f)
                    {
                        float k = (1f - hd / 0.35f) * 0.35f;
                        c = Color.Lerp(c, Color.white, k);
                    }

                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            _cachedOrbSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                              new Vector2(0.5f, 0.5f), 48f);
            _cachedOrbSprite.name = "XpOrbSprite";
            return _cachedOrbSprite;
        }

        /// <summary>
        /// Soft circular 16×16 sprite for the sparkle particles. Pure white so
        /// the ParticleSystem's <c>startColor</c> can tint it freely.
        /// </summary>
        private static Sprite GetSparkleSprite()
        {
            if (_cachedSparkleSprite != null) return _cachedSparkleSprite;

            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
                name       = "XpSparkleTexture"
            };

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxR = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = Mathf.Clamp01(dist / maxR);
                    // Squared falloff = a hot-spot core with a wide soft skirt.
                    float a = Mathf.Pow(1f - t, 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();

            _cachedSparkleSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                                  new Vector2(0.5f, 0.5f), 16f);
            _cachedSparkleSprite.name = "XpSparkleSprite";
            return _cachedSparkleSprite;
        }

        /// <summary>
        /// Returns a cached <see cref="Material"/> using the unlit
        /// sprite shader. Falls back through several known shader names so the
        /// orb works on URP 2D, built-in, and any project that has shipped
        /// either name.
        /// </summary>
        private static Material ResolveUnlitSpriteMaterial()
        {
            if (_cachedUnlitMaterial != null) return _cachedUnlitMaterial;
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _cachedUnlitMaterial = new Material(shader) { name = "XpOrbUnlit" };
            return _cachedUnlitMaterial;
        }

        /// <summary>
        /// Resolves the material used by the sparkle ParticleSystemRenderer.
        /// Default-Particle (the ParticleSystem default) ships a shader that
        /// renders magenta in URP — this method assigns the URP-safe
        /// <c>Sprites/Default</c> shader against the white sparkle sprite so
        /// particles render as crisp white dots regardless of the active RP.
        /// </summary>
        private static Material ResolveSparkleMaterial()
        {
            if (_cachedSparkleMaterial != null) return _cachedSparkleMaterial;
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Unlit/Transparent");
            if (shader == null) return null;
            _cachedSparkleMaterial = new Material(shader)
            {
                name = "XpSparkleParticleMat",
                mainTexture = GetSparkleSprite().texture
            };
            return _cachedSparkleMaterial;
        }

        private static void CreateSparkles(Transform parent)
        {
            var go = new GameObject("Sparkles", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);

            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration         = 1f;
            main.loop             = true;
            main.startLifetime    = 0.85f;
            main.startSpeed       = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSize        = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor       = Color.white; // pure white — colourOverLife only fades alpha
            main.maxParticles     = 40;
            main.simulationSpace  = ParticleSystemSimulationSpace.Local;
            main.gravityModifier  = 0f;
            main.startRotation    = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 18f;

            var shape = ps.shape;
            shape.shapeType        = ParticleSystemShapeType.Circle;
            shape.radius           = 0.22f;
            shape.radiusThickness  = 1f;

            // Pure-white particles with an alpha curve that fades-in then
            // fades-out — gives the sparkle "twinkle" feel without colour shift.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f,    0f),
                    new GradientAlphaKey(1f,    0.25f),
                    new GradientAlphaKey(0.85f, 0.6f),
                    new GradientAlphaKey(0f,    1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            // Quick burst then long shrink — reads as a twinkling spark.
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f,   0.1f),
                new Keyframe(0.2f, 1f),
                new Keyframe(1f,   0.2f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder     = 0;
            // Default-Particle ships a built-in shader that renders magenta in
            // URP. Override with the canonical Sprites/Default material so
            // particles read as crisp white dots regardless of the active RP.
            var sparkMat = ResolveSparkleMaterial();
            if (sparkMat != null) renderer.sharedMaterial = sparkMat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Play();
        }
    }
}
