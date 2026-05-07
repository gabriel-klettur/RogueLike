using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Arc slash with epic VFX: physics arc-overlap + per-target combat feedback,
    /// plus a Light2D flash, swoosh particles, and screen shake.
    /// </summary>
    public class SlashExecutor : ISpellExecutor
    {
        /// <summary>
        /// Minimum max-channel brightness for the slash tint. A tint below this is
        /// boosted (preserving hue) so dark-themed slashes (e.g. hostile_slash_dark)
        /// stay visible against the world / preview background instead of rendering
        /// as a near-invisible silhouette.
        /// </summary>
        private const float MIN_SLASH_BRIGHTNESS = 0.35f;

        /// <summary>
        /// Time-to-live (seconds) applied to a spawned catalog prefab. Generous so
        /// any internal sub-emitter trail still finishes naturally. The prefab
        /// itself controls when its particles stop emitting; this just bounds the
        /// GameObject's lifetime so we never leak instances if a sub-emitter loops.
        /// </summary>
        private const float CATALOG_PREFAB_TTL = 3f;

        /// <summary>
        /// Multiplier from <c>SpellDefinition.hitRadius</c> to local scale of the
        /// catalog prefab. The "Free Slash VFX" prefabs are sized for ~1 m human
        /// reach in 3D; Valkur slashes use hitRadius up to ~7 in world units, so
        /// 0.18 scales the visual to roughly span the gameplay arc.
        /// </summary>
        private const float CATALOG_PREFAB_SCALE_PER_RADIUS = 0.18f;

        /// <summary>
        /// World-unit offset (≈ tiles) from the caster's body centre to the slash
        /// VFX spawn point along the cast direction. Valkur tiles are 1 world unit;
        /// 1.25 puts the slash visibly in front of the player (about a tile away)
        /// without overlapping the body sprite. Hit-detection still uses the same
        /// origin so the visual matches the damage area.
        /// </summary>
        private const float SLASH_FORWARD_OFFSET = 1.25f;

        // Lazy-loaded slash VFX catalog. Cached so we don't hit Resources on every cast.
        private static SlashVfxCatalog _vfxCatalog;
        private static bool _vfxCatalogTried;

        private static SlashVfxCatalog ResolveVfxCatalog()
        {
            if (_vfxCatalogTried) return _vfxCatalog;
            _vfxCatalog = Resources.Load<SlashVfxCatalog>("SlashVfxCatalog");
            _vfxCatalogTried = true;
            return _vfxCatalog;
        }

        public void Execute(SpellContext ctx)
        {
            float arc = ctx.Spell.arcRangeDegrees > 0 ? ctx.Spell.arcRangeDegrees : 90f;
            float hitRadius = ctx.Spell.hitRadius > 0 ? ctx.Spell.hitRadius : ctx.Spell.range;
            if (hitRadius <= 0) hitRadius = 1.5f;

            // Resolve the caster's body centre rather than its pivot. 2D character
            // sprites use a bottom-centre pivot, so caster.position sits at the feet —
            // casting from there makes every spell appear to spawn under the boots.
            // Convention (per CLAUDE.md): every spell except Dash originates from the
            // body centre. ProjectileExecutor.ResolveCasterCenter inspects sprite /
            // collider bounds with a guaranteed minimum lift above the pivot.
            Vector2 casterCenter = ProjectileExecutor.ResolveCasterCenter(ctx.Caster);

            Vector2 hitCenter = casterCenter + ctx.Direction * (hitRadius * 0.5f);
            var hits = Physics2D.OverlapCircleAll(hitCenter, hitRadius, ctx.TargetLayers);

            int hitCount = 0;
            foreach (var hit in hits)
            {
                if (hit.gameObject == ctx.Caster.gameObject) continue;
                var health = hit.GetComponent<Health>();
                if (health == null || health.IsDead) continue;

                Vector2 toTarget = ((Vector2)hit.transform.position - casterCenter).normalized;
                float angle = Vector2.Angle(ctx.Direction, toTarget);
                if (angle <= arc * 0.5f)
                {
                    health.TakeDamage(Mathf.RoundToInt(ctx.Spell.damage));
                    var feedback = hit.GetComponent<CombatFeedback>();
                    if (feedback != null) feedback.ApplyKnockback(casterCenter);
                    hitCount++;
                }
            }

            // Color: prefer SpellDefinition tint
            Color slashColor = ctx.Spell.particleColor != Color.clear
                ? ctx.Spell.particleColor
                : new Color(1f, 1f, 1f, 0.85f);

            // Brightness floor: a near-black tint (e.g. hostile_slash_dark uses
            // particleColor ≈ (0.04, 0.04, 0.04, 1)) renders the curved Blade sprite
            // as a barely-visible blob against the world. Lift dark colours to a
            // minimum perceptual brightness while preserving their hue so a "Dark"
            // slash still reads as dark — just not invisible.
            slashColor = EnsureMinBrightness(slashColor, MIN_SLASH_BRIGHTNESS);

            // Try the designer-authored catalog prefab first (Free Slash VFX pack).
            // Falls through to the procedural SlashArcFX when no prefab resolves so
            // we never end up with an invisible slash if the catalog is missing.
            var catalogPrefab = ResolveVfxCatalog()?.Resolve(ctx.Spell.spellKey);
            if (catalogPrefab != null)
            {
                SpawnCatalogSlashVfx(catalogPrefab, casterCenter, ctx.Direction, hitRadius, slashColor);
            }
            else
            {
                SlashArcFX.Spawn(casterCenter, ctx.Direction, hitRadius, arc, slashColor);
            }

            if (hitCount > 0) CameraShake.Trigger(0.18f, 0.18f);

            var audio = ServiceLocator.Get<IAudioService>();
            if (audio != null) audio.PlaySfxById(hitCount > 0 ? "spell_slash_hit" : "spell_slash_swing");
        }

        /// <summary>
        /// Instantiates a slash VFX prefab from the catalog at <paramref name="casterCenter"/>
        /// + a fixed forward offset along <paramref name="direction"/>, rotated to face
        /// the cast vector, scaled to roughly span <paramref name="hitRadius"/>, and
        /// tinted with <paramref name="tint"/>. The prefab auto-destroys after
        /// <see cref="CATALOG_PREFAB_TTL"/>.
        /// </summary>
        private static void SpawnCatalogSlashVfx(GameObject prefab, Vector3 casterCenter,
                                                 Vector2 direction, float hitRadius,
                                                 Color tint)
        {
            // Spawn ~1.25 tiles ahead of the body centre along the cast direction so
            // the slash always appears in front of the player toward the cursor,
            // never on top of the body sprite. Using a fixed tile offset (rather
            // than a hitRadius fraction) keeps small slashes visible too.
            Vector3 spawnPos = casterCenter + (Vector3)(direction.normalized * SLASH_FORWARD_OFFSET);

            // Rotation: align the prefab's +X axis with the slash direction (prefabs
            // are authored facing +X by default). This rotates the slash plane around
            // the world Z so the swing reads correctly from a top-down 2D camera.
            float zAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var go = Object.Instantiate(prefab, spawnPos, Quaternion.Euler(0f, 0f, zAngle));

            // Uniform scale so the visual roughly matches the gameplay arc radius.
            float s = Mathf.Max(0.4f, hitRadius * CATALOG_PREFAB_SCALE_PER_RADIUS);
            go.transform.localScale = new Vector3(s, s, s);

            // Tint every ParticleSystem on the spawned hierarchy. ShaderGraph slash
            // materials in the pack multiply vertex colour, so setting startColor on
            // each module is enough to recolour the whole effect (fire = red, water
            // = cyan, dark = grey, …) without modifying any material asset.
            ApplyTintToParticles(go, tint);

            // The pack ships its renderers on the "Default" sorting layer (index 0),
            // which sits BELOW every Valkur world layer (Background → … → VFX → …).
            // Without this re-bind, the slash spawns at the player's feet but renders
            // behind tiles/entities and looks invisible in gameplay. The View panel
            // doesn't have this problem because its camera culls only SpellPreview.
            ApplySortingLayerToRenderers(go, Valkur.Core.SortingConfig.LAYER_VFX, 60);

            // Make sure all sub-emitters actually play (some prefab modules ship with
            // playOnAwake disabled). Play(true) recurses into children.
            var rootPs = go.GetComponent<ParticleSystem>();
            if (rootPs != null)
            {
                rootPs.Play(withChildren: true);
            }
            else
            {
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                    ps.Play(withChildren: false);
            }

            Object.Destroy(go, CATALOG_PREFAB_TTL);
        }

        private static void ApplyTintToParticles(GameObject root, Color tint)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                if (ps == null) continue;
                var main = ps.main;
                // Preserve per-particle alpha curves but multiply the base RGB by tint.
                main.startColor = new ParticleSystem.MinMaxGradient(tint);
            }
        }

        /// <summary>
        /// Re-binds every <see cref="Renderer"/> under <paramref name="root"/> to the
        /// given sorting layer + a base sorting order, preserving each renderer's
        /// relative offset within the prefab so multi-layer effects (glow, core,
        /// trail, sparks) keep their authored draw order.
        /// </summary>
        private static void ApplySortingLayerToRenderers(GameObject root, string sortingLayerName, int baseOrder)
        {
            int layerId = SortingLayer.NameToID(sortingLayerName);
            foreach (var r in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (r == null) continue;
                // Preserve the prefab-authored relative order so the bright core stays
                // on top of the soft glow even after we shift every renderer up.
                int relative = r.sortingOrder;
                r.sortingLayerID = layerId;
                r.sortingLayerName = sortingLayerName;
                r.sortingOrder = baseOrder + relative;
            }
        }

        /// <summary>
        /// Lifts <paramref name="c"/>'s brightness so its strongest channel reaches
        /// at least <paramref name="floor"/>, while preserving the original hue
        /// ratio between channels and the alpha. A pure-black input is promoted to
        /// a neutral grey at the floor brightness.
        /// </summary>
        private static Color EnsureMinBrightness(Color c, float floor)
        {
            float maxComp = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (maxComp >= floor) return c;
            if (maxComp <= 0.0001f) return new Color(floor, floor, floor, c.a);
            float scale = floor / maxComp;
            return new Color(
                Mathf.Min(1f, c.r * scale),
                Mathf.Min(1f, c.g * scale),
                Mathf.Min(1f, c.b * scale),
                c.a);
        }
    }

    /// <summary>Procedural slash arc: swept ring strip + spark particles + Light2D pop.</summary>
    internal class SlashArcFX : MonoBehaviour
    {
        private const float Life = 0.30f;
        // How much the inner highlight is shrunk vs the outer blade — gives the
        // "metallic edge with bright core" silhouette in the reference image.
        private const float HIGHLIGHT_SCALE_X = 0.78f;
        private const float HIGHLIGHT_SCALE_Y = 0.62f;
        // How far the highlight is lerped toward white. Higher = brighter inner core.
        private const float HIGHLIGHT_LIGHTEN = 0.75f;

        private float _age;
        private SpriteRenderer _arc;
        private SpriteRenderer _highlight;
        private Color _color;
        private Color _highlightColor;
        private float _radius;
        private GameObject _lightGo;
        private Component _light;

        public static void Spawn(Vector2 origin, Vector2 dir, float radius, float arcDeg, Color color)
        {
            var go = new GameObject("SlashArcFX");
            go.transform.position = origin + dir * (radius * 0.5f);
            go.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, Vector3.forward);
            var fx = go.AddComponent<SlashArcFX>();
            fx._color = color;
            fx._radius = radius;
            fx.Build(arcDeg);
        }

        private void Build(float arcDeg)
        {
            ElementalSprites.EnsureAll();

            // Outer blade — the spell's tinted curve.
            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(transform, false);
            arcGo.transform.localScale = new Vector3(_radius, _radius * 0.8f, 1f);
            _arc = arcGo.AddComponent<SpriteRenderer>();
            _arc.sprite = ElementalSprites.Blade != null ? ElementalSprites.Blade : ElementalSprites.Glow;
            _arc.color = _color;
            _arc.sortingLayerID = SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_VFX);
            _arc.sortingLayerName = Valkur.Core.SortingConfig.LAYER_VFX;
            _arc.sortingOrder = 60;
            _arc.material = ElementalSprites.SharedUnlitMaterial;

            // Inner highlight — the same blade shape shrunk and lerped toward white,
            // so every slash reads as a curved blade with a bright core (matching the
            // reference: a coloured outline with whitish tones inside).
            _highlightColor = Color.Lerp(_color, Color.white, HIGHLIGHT_LIGHTEN);
            _highlightColor.a = _color.a;

            var highlightGo = new GameObject("ArcHighlight");
            highlightGo.transform.SetParent(transform, false);
            highlightGo.transform.localScale = new Vector3(_radius * HIGHLIGHT_SCALE_X,
                                                           _radius * 0.8f * HIGHLIGHT_SCALE_Y, 1f);
            _highlight = highlightGo.AddComponent<SpriteRenderer>();
            _highlight.sprite = _arc.sprite;
            _highlight.color = _highlightColor;
            _highlight.sortingLayerID = _arc.sortingLayerID;
            _highlight.sortingLayerName = _arc.sortingLayerName;
            _highlight.sortingOrder = _arc.sortingOrder + 1;   // drawn ON TOP of outer
            _highlight.material = ElementalSprites.SharedUnlitMaterial;

            var l2dType = ElementalProjectileVisual.GetLight2DType();
            if (l2dType != null)
            {
                _lightGo = new GameObject("SlashLight");
                _lightGo.transform.SetParent(transform, false);
                try
                {
                    _light = _lightGo.AddComponent(l2dType);
                    var lt = ElementalProjectileVisual.GetLight2DLightTypeProp();
                    if (lt != null) lt.SetValue(_light, System.Enum.ToObject(lt.PropertyType, 2));
                    ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _color);
                    ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.6f);
                    ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 1.2f);
                    ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                    ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                }
                catch { }
            }
            _ = arcDeg; // future: shape arc to match degrees
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Life;
            if (t >= 1f) { if (_lightGo != null) Destroy(_lightGo); Destroy(gameObject); return; }
            float fade = 1f - t;
            float swing = Mathf.Lerp(-0.4f, 0.4f, t);
            transform.localRotation = Quaternion.AngleAxis(swing * 30f, Vector3.forward) * transform.localRotation;
            if (_arc != null)
            {
                var c = _arc.color; c.a = _color.a * fade; _arc.color = c;
                _arc.transform.localScale = new Vector3(_radius * (0.9f + 0.2f * t), _radius * 0.8f * fade, 1f);
            }
            if (_highlight != null)
            {
                // Inner highlight fades a bit faster so the bright core "burns out"
                // into the outer rim, giving a satisfying flash-then-trail feel.
                float hiFade = Mathf.Pow(fade, 1.4f);
                var hc = _highlight.color; hc.a = _highlightColor.a * hiFade; _highlight.color = hc;
                _highlight.transform.localScale = new Vector3(
                    _radius * HIGHLIGHT_SCALE_X * (0.9f + 0.2f * t),
                    _radius * 0.8f * HIGHLIGHT_SCALE_Y * hiFade,
                    1f);
            }
            if (_light != null)
            {
                try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 1.6f * fade); }
                catch { }
            }
        }
    }
}
