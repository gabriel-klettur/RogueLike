using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The flight rig every projectile spell gets when it has no bespoke one.
    ///
    /// <para>It exists because the previous default, <c>ParticleProjectileVisual</c>, hides the
    /// root SpriteRenderer in <c>Awake</c> and returns early from <c>StartTrail</c> when the
    /// spell authors no <c>vfxPreset</c> — so six of the expansion's seven projectiles drew
    /// nothing at all between the muzzle flash and the impact. A rig that draws only when a
    /// preset happens to be authored is not a default; this one is built from the spell's own
    /// mechanics and cannot come out empty.</para>
    ///
    /// <para>The shape is chosen by <see cref="ProjectileVisualProfile"/>, never by a spell key.
    /// The door to the one bespoke rig in the project is <c>IceLanceArt.Matches</c>, a hardcoded
    /// <c>spellKey == "ice_lance"</c>, and every spell that wanted the same treatment would have
    /// cost another literal branch.</para>
    /// </summary>
    public sealed partial class SpellProjectileVisual : MonoBehaviour, IProjectileVisual
    {
        private ProjectileVisualProfile _profile;

        /// <summary>Turned to face travel. Carries the body, and spins for a blade.</summary>
        private Transform _rig;
        /// <summary>
        /// Aligned to travel but NEVER spun. Anything that says "which way am I going" — the
        /// trail, the ember spray — hangs here. Parenting a trail to a spinning root makes it
        /// orbit the projectile instead of following it, which is the exact defect
        /// <c>ElementalProjectileVisual</c> shipped on the boomerang.
        /// </summary>
        private Transform _travelAnchor;

        private SpriteRenderer _core;
        private SpriteRenderer _shell;
        private SpriteRenderer _rim;
        private SpriteRenderer _glint;
        private readonly SpriteRenderer[] _shards = new SpriteRenderer[4];
        private TrailRenderer _trail;
        private Component _light;

        private float _seed;
        private float _spin;
        private float _glintClock;
        private float _glintFlash;
        private float _power = 1f;
        private bool _built;
        private bool _impacted;
        private Vector3 _lastPosition;
        private Vector2 _travelDirection = Vector2.right;

        public Vector2 TravelDirection => _travelDirection;
        public ProjectileSilhouette Silhouette => _profile.Silhouette;

        /// <summary>
        /// Resolves the profile and rebuilds. Safe to call on a pooled object: the rig is torn
        /// down first, because a recycled shot that kept the previous spell's silhouette would
        /// be a lance drawn as a ball with nothing failing.
        /// </summary>
        public void Configure(SpellDefinition spell)
        {
            var next = ProjectileVisualProfile.Resolve(spell);
            if (_built && next.Silhouette == _profile.Silhouette)
            {
                _profile = next;
                ApplyPalette();
                ResetFlight();
                return;
            }

            _profile = next;
            Rebuild();
            ResetFlight();
        }

        /// <summary>
        /// One body crossed. The falloff has to be legible on the projectile itself or it is a
        /// number that exists only in the asset: the rig dims, narrows and sheds a burst, so a
        /// lance four bodies deep is visibly weaker than the one that just left the hand.
        /// </summary>
        public void OnPierced(Vector3 contact, int remaining, int total)
        {
            float fraction = total > 0 ? Mathf.Clamp01(remaining / (float)total) : 0f;
            _power = Mathf.Lerp(0.55f, 1f, fraction);
            _glintFlash = 1f;
            ApplyPower();
            SpellProjectileBurst.Pierce(contact, _travelDirection, _profile, _power);
        }

        /// <summary>
        /// A lock-on is an event, and without a frame of its own "it happens to be following
        /// me" and "it is hunting me" look the same.
        /// </summary>
        public void OnHomingLocked(Transform target)
        {
            _glintFlash = 1f;
            if (target != null)
                SpellProjectileBurst.Lock(target.position, _profile);
        }

        public void OnImpact(Vector3 worldPos)
        {
            if (_impacted) return;
            _impacted = true;
            SpellProjectileBurst.Impact(worldPos, _travelDirection, _profile, _power);
        }

        private void Awake()
        {
            // The pooled prefab's own renderer is a legacy ball. Every rig here draws into
            // children instead, so leaving it on would put a sprite inside every projectile.
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;
        }

        private void OnEnable() => ResetFlight();

        private void OnDisable()
        {
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        }

        private void ResetFlight()
        {
            _impacted = false;
            _power = 1f;
            _seed = Random.Range(0f, 100f);
            _spin = Random.Range(0f, 360f);
            _glintClock = Random.Range(0f, Mathf.Max(0.01f, _profile.GlintInterval));
            _glintFlash = 0f;
            _lastPosition = transform.position;
            if (_trail != null) { _trail.Clear(); _trail.emitting = true; }
            ApplyPower();
        }

        private void Rebuild()
        {
            // Destroy is an outright ERROR in Edit Mode, and EditMode tests cast projectiles
            // through this path — a plain Destroy here surfaces as an unhandled log message on
            // some unrelated fixture rather than as a readable failure.
            DestroyNode(_rig);
            DestroyNode(_travelAnchor);
            System.Array.Clear(_shards, 0, _shards.Length);
            _core = _shell = _rim = _glint = null;
            _trail = null;
            _light = null;

            _rig = MakeChild(transform, "ProjectileRig");
            _travelAnchor = MakeChild(transform, "TravelAnchor");

            BuildBody();
            BuildTrail();
            if (_profile.HasLight) BuildLight();
            _built = true;
        }

        private void BuildBody()
        {
            var p = _profile;

            // Law L3. The dark core is what says a solid object is travelling rather than a
            // light being carried, and on the blade it is the entire spell.
            if (p.HasOpaqueCore)
            {
                Sprite coreSprite = p.Silhouette switch
                {
                    ProjectileSilhouette.Lance => IceSprites.Body(2),
                    ProjectileSilhouette.Blade => ElementalSprites.Blade,
                    _ => ElementalSprites.Core,
                };
                _core = MakeSprite(_rig, "Core", coreSprite, Color.white, 2, additive: false);
                _core.transform.localScale = new Vector3(p.Length, p.Width, 1f);
            }

            if (p.HasAdditiveShell)
            {
                _shell = MakeSprite(_rig, "Shell", ElementalSprites.Glow, p.Palette.glow, 0, true);
                _shell.transform.localScale = new Vector3(p.Length * 1.85f, p.Width * 1.85f, 1f);

                _rim = MakeSprite(_rig, "Rim", ElementalSprites.HotCore, p.Palette.hotCore, 4, true);
                _rim.transform.localScale = new Vector3(p.Length * 0.72f, p.Width * 0.72f, 1f);
            }

            BuildShards();

            // A single small bright point is what reads at 16 PPU once the body is only a few
            // pixels across; it is also the layer the glint event drives.
            _glint = MakeSprite(_rig, "Glint", ElementalSprites.SparkleStar, p.Palette.hotCore, 6, true);
            _glint.transform.localPosition = new Vector3(p.Length * 0.42f, 0f, 0f);
            _glint.transform.localScale = Vector3.one * (p.Width * 0.62f);
        }

        private void BuildShards()
        {
            var p = _profile;
            int count = Mathf.Clamp(p.Shards, 0, _shards.Length);
            for (int i = 0; i < count; i++)
            {
                Sprite sprite = p.Silhouette switch
                {
                    ProjectileSilhouette.Lance => IceSprites.Facet(i + 1),
                    ProjectileSilhouette.Blade => ElementalSprites.Blade,
                    ProjectileSilhouette.Wisp => ElementalSprites.Wisp,
                    _ => ElementalSprites.Sparkle,
                };

                bool additive = p.Silhouette != ProjectileSilhouette.Blade;
                var sr = MakeSprite(_rig, "Shard" + i, sprite, p.Palette.accent, 1, additive);

                float t = count > 1 ? i / (count - 1f) : 0.5f;
                float along = Mathf.Lerp(-p.Length * 0.42f, p.Length * 0.18f, t);
                float across = (i % 2 == 0 ? 1f : -1f) * p.Width * 0.40f;
                sr.transform.localPosition = new Vector3(along, across, 0f);
                sr.transform.localScale = Vector3.one * (p.Width * (0.52f - 0.08f * i));
                _shards[i] = sr;
            }
        }

        private void BuildTrail()
        {
            var p = _profile;
            if (p.TrailTime <= 0f) return;

            var go = new GameObject("Trail");
            go.transform.SetParent(_travelAnchor, false);
            go.transform.localPosition = new Vector3(-p.Length * 0.32f, 0f, 0f);

            _trail = go.AddComponent<TrailRenderer>();
            _trail.time = p.TrailTime;
            _trail.minVertexDistance = 0.035f;
            _trail.widthMultiplier = p.TrailWidth;
            _trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f));

            Color head = p.Palette.core;
            Color tail = p.Palette.halo;
            _trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(head, 0f),
                    new GradientColorKey(tail, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(p.Opacity * 0.85f, 0f),
                    new GradientAlphaKey(0f, 1f),
                },
            };

            // A blade's trail is a smear of metal, not a glow, so it takes the same unlit
            // material as its body. Everything else genuinely is light.
            _trail.sharedMaterial = p.HasAdditiveShell
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            _trail.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            _trail.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            _trail.sortingOrder = -1;
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            var go = new GameObject("ProjectileLight");
            go.transform.SetParent(_rig, false);
            try
            {
                _light = go.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // URP 14: Freeform=1, Sprite=2, Point=3, Global=4. Passing the wrong literal
                // here is what once left every placed torch a cookie-less Sprite light.
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _profile.Palette.lightColor);
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.35f);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _profile.LightRadius);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.10f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.80f);
            }
            catch
            {
                _light = null;
                Destroy(go);
            }
        }

        private static void DestroyNode(Transform node)
        {
            if (node == null) return;
            if (Application.isPlaying) Destroy(node.gameObject);
            else DestroyImmediate(node.gameObject);
        }

        private static Transform MakeChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static SpriteRenderer MakeSprite(Transform parent, string name, Sprite sprite,
            Color color, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            // Law L6: a world-space effect goes on LAYER_VFX or LAYER_PROJECTILES with a SMALL
            // order. SortingConfig.Z_SKY is a Z depth and passing it here drew every bolt under
            // the wall tops — recorded twice already.
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_PROJECTILES);
            sr.sortingLayerName = SortingConfig.LAYER_PROJECTILES;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
