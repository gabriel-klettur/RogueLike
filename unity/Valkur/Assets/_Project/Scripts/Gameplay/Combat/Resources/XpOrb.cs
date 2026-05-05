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

        private static Sprite _cachedOrbSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVisualStaticsOnPlayModeEnter()
        {
            // Cached sprite would point to a Texture2D destroyed in the previous play session.
            _cachedOrbSprite = null;
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
            sr.sprite = GetOrbSprite();
            sr.color = Color.white; // colour now lives in the sprite gradient
            sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            sr.sortingOrder = 5;

            if (root.transform.Find("Sparkles") == null)
                CreateSparkles(root.transform);

            if (root.GetComponent<XpOrbPulse>() == null)
                root.AddComponent<XpOrbPulse>();
        }

        /// <summary>
        /// Procedural radial-gradient sprite — three colour stops (white core,
        /// cyan-blue body, deep-blue halo) on a 32×32 RGBA32 texture with
        /// bilinear filtering for soft edges. Cached per play session.
        /// </summary>
        public static Sprite GetOrbSprite()
        {
            if (_cachedOrbSprite != null) return _cachedOrbSprite;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "XpOrbTexture"
            };

            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxR = size / 2f;

            Color core = new Color(1f,    1f,    1f,    1f); // bright white centre
            Color body = new Color(0.45f, 0.78f, 1f,    1f); // soft cyan-blue
            Color halo = new Color(0.18f, 0.42f, 1f,    0f); // deep blue, transparent at the edge

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = Mathf.Clamp01(dist / maxR);

                    Color c;
                    if (t < 0.25f)
                    {
                        // Bright core (white blending into body cyan).
                        c = Color.Lerp(core, body, t / 0.25f);
                    }
                    else if (t < 0.7f)
                    {
                        // Solid body, alpha gently easing into the halo.
                        c = body;
                        c.a = Mathf.Lerp(1f, 0.85f, (t - 0.25f) / 0.45f);
                    }
                    else
                    {
                        // Halo fade — body colour to deep-blue transparent.
                        c = Color.Lerp(body, halo, (t - 0.7f) / 0.3f);
                    }

                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            _cachedOrbSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                                              new Vector2(0.5f, 0.5f), 32f);
            _cachedOrbSprite.name = "XpOrbSprite";
            return _cachedOrbSprite;
        }

        private static void CreateSparkles(Transform parent)
        {
            var go = new GameObject("Sparkles", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);

            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = 0.7f;
            main.startSpeed = 0.4f;
            main.startSize = 0.07f;
            main.startColor = new Color(0.7f, 0.9f, 1f, 1f);
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;
            shape.radiusThickness = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0f),
                    new GradientColorKey(new Color(0.4f,  0.65f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.4f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sortingLayerName = SortingConfig.LAYER_VFX;
            renderer.sortingOrder = 0;

            ps.Play();
        }
    }
}
