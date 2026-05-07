using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    public partial class BuildingObject : MonoBehaviour
    {

        public void Apply(BuildingTemplateData template, Vector2Int scaleOverride, float splitRatioOverride)
        {
            _template           = template;
            _scaleOverride      = scaleOverride;
            _splitRatioOverride = splitRatioOverride;

            if (template == null)
            {
                Debug.LogWarning($"[BuildingObject] Template is null on '{name}'.", this);
                return;
            }

            // ── 1. Resolve effective values ─────────────────────────────────────────
            float effectiveSplitRatio = (splitRatioOverride >= 0f) ? splitRatioOverride : template.splitRatio;

            int origW = template.originalScale.x;
            int origH = template.originalScale.y;

            // Always ensure collider exists (even if sprite is missing) so other systems
            // that reference GetComponent<BoxCollider2D>() don't hit MissingComponentException.
            EnsureCollider();

            // ── 2. Load texture ─────────────────────────────────────────────────────
            Sprite sourceSprite = Resources.Load<Sprite>(template.assetPath);
            if (sourceSprite == null)
            {
                Debug.LogWarning(
                    $"[BuildingObject] Sprite not found at Resources/{template.assetPath} " +
                    $"(template id={template.templateId}).", this);
                return;
            }

            Texture2D tex = sourceSprite.texture;
            // ATLAS-SAFE: sourceSprite.texture is the atlas page when the sprite
            // lives inside a SpriteAtlas; Reading tex.width/height directly would
            // give us the atlas dimensions (e.g. 4096×4096), and Rect(0,0,…) on
            // the atlas slices whatever sprite happens to sit at the atlas
            // origin — that was the "every building shows the same wrong art"
            // regression after Atlas Phase 2. Always intersect with the sprite's
            // own rect on its texture.
            Rect spriteRect = sourceSprite.textureRect;
            int spriteW = Mathf.RoundToInt(spriteRect.width);
            int spriteH = Mathf.RoundToInt(spriteRect.height);
            int spriteOriginX = Mathf.RoundToInt(spriteRect.x);
            int spriteOriginY = Mathf.RoundToInt(spriteRect.y);

            // Authoring data drift: ~200 migrated templates have originalScale=(0,0)
            // because the field was missing from the source Python data and never
            // recomputed during migration. Fall back to the sprite's own dimensions
            // so those buildings still render at their native PNG size — without
            // this fallback ~200 templates (gardens, portals, forest_decoration,
            // etc.) silently rendered as 0×0 quads.
            if (origW <= 0) origW = spriteW;
            if (origH <= 0) origH = spriteH;
            if (origW <= 0 || origH <= 0)
            {
                Debug.LogWarning(
                    $"[BuildingObject] Template {template.templateId} '{template.name}' " +
                    "has zero originalScale AND zero sprite size — cannot render.", this);
                return;
            }

            // Effective pixel dimensions: per-instance override wins as-is (it's
            // designer-authored stretching, e.g. tile-perfect placement). When no
            // override is set we need to handle a second data-drift case: a few
            // templates have originalScale whose aspect ratio does NOT match the
            // PNG (e.g. castle_2 says 3072×2048 but the PNG is square 1024×1024
            // because the asset got re-exported smaller post-migration). Naively
            // applying that origScale stretches square art into a wide rectangle
            // ("achatado"). When the override is absent, fit the PNG inside the
            // origScale "size budget" while preserving the PNG's native aspect.
            int effW, effH;
            if (scaleOverride.x > 0 && scaleOverride.y > 0)
            {
                effW = scaleOverride.x;
                effH = scaleOverride.y;
            }
            else if (Mathf.Abs((float)origW / origH - (float)spriteW / spriteH) < 0.01f)
            {
                // Aspect ratios already match — render at the authored size verbatim.
                effW = origW;
                effH = origH;
            }
            else
            {
                // Aspect drift between authored origScale and actual PNG. Fit the
                // PNG into the authored bounds without squishing. Smaller scale
                // factor wins so the result stays within the origScale budget.
                float fit = Mathf.Min((float)origW / spriteW, (float)origH / spriteH);
                effW = Mathf.Max(1, Mathf.RoundToInt(spriteW * fit));
                effH = Mathf.Max(1, Mathf.RoundToInt(spriteH * fit));
            }

            // ── 3. Compute crop rects in TEXTURE-space (Unity Y=0 is BOTTOM of texture) ──
            // The split ratio divides the sprite visually. Use the sprite's own
            // pixel size (NOT template.originalScale, which may differ; NOT the
            // atlas page size, which would be massively wrong). Python resizes
            // the image to effW×effH via pygame.transform.scale before splitting;
            // in Unity we use localScale instead, so Sprite.Create uses the raw
            // sprite size from the source texture.
            int bottomTexH = Mathf.RoundToInt(spriteH * (1f - effectiveSplitRatio));
            bottomTexH = Mathf.Clamp(bottomTexH, 1, spriteH - 1);
            int topTexH = spriteH - bottomTexH;

            // Sprites with pivot at bottom-center so local Y=0 = the bottom of
            // each portion. Rects are anchored at the sprite's own (x,y) within
            // the (possibly atlased) texture, NOT at (0,0).
            Sprite bottomSprite = Sprite.Create(
                tex,
                new Rect(spriteOriginX, spriteOriginY, spriteW, bottomTexH),
                new Vector2(0.5f, 0f),
                PPU);

            Sprite topSprite = Sprite.Create(
                tex,
                new Rect(spriteOriginX, spriteOriginY + bottomTexH, spriteW, topTexH),
                new Vector2(0.5f, 0f),
                PPU);

            // Heights in local (unscaled) Unity units (based on texture pixels / PPU)
            float bottomH = bottomTexH / PPU;
            float topH    = topTexH    / PPU;

            // Scale transform so the building renders at effW×effH pixels in the world.
            // localScale maps from the raw sprite world-size to the desired display
            // size. Uses spriteW/spriteH (the sprite's own pixel size) — NOT the
            // backing texture, which can be the entire atlas page.
            transform.localScale = new Vector3((float)effW / spriteW, (float)effH / spriteH, 1f);

            // ── 4. Create / reuse child renderers ──────────────────────────────────
            // Parent transform sits at BOTTOM-CENTER of the full sprite.
            // Bottom child at local (0, 0) → its bottom aligns with parent.
            // Top child    at local (0, bottomH) → its bottom aligns with top of footprint.
            EnsureRenderer(ref _bottomRenderer, "Footprint",
                SortingConfig.LAYER_WALLS_BOTTOM, bottomSprite, Vector3.zero);

            EnsureRenderer(ref _topRenderer, "Canopy",
                SortingConfig.LAYER_WALLS_TOP, topSprite, new Vector3(0f, bottomH, 0f));

            // Y-sort within layer: buildings farther down on screen (lower worldY)
            // rank in front. SortingConfig.YToSortingOrder returns -(int)(y*100).
            // Per-instance Z offsets are HARD TIERS on top of that: a building
            // with ZBottomOffset = +N always renders above a building with
            // ZBottomOffset = N-1, regardless of their Y diff. See
            // SortingConfig.Z_TIER_SCALE for the multiplier rationale.
            int ySortOrder = SortingConfig.YToSortingOrder(transform.position.y);
            _bottomRenderer.sortingOrder = ySortOrder + _zBottomOffset * SortingConfig.Z_TIER_SCALE;
            _topRenderer.sortingOrder    = ySortOrder + _zTopOffset    * SortingConfig.Z_TIER_SCALE;

            // ── 5. Collider ─────────────────────────────────────────────────────────
            // No default footprint collider. Buildings only block movement once the
            // user explicitly paints a per-cell collision grid in the F10 editor;
            // BuildingCollisionLoader spawns child BoxCollider2D tiles for each
            // painted cell. The root BoxCollider2D component is kept (and left
            // disabled) so other systems that do GetComponent<BoxCollider2D>() on
            // a BuildingObject don't hit MissingComponentException.
            //
            // The previous default — a single rectangle covering the footprint
            // half of the sprite — was a Python-port carryover that doesn't match
            // the new authoring model: every solid building was forced into a
            // boxy collision shape that didn't follow its actual silhouette, and
            // disabling it required either tagging template.solid=false (global
            // for that template) or painting a fully-walkable grid. Removing it
            // means every collider in the world is one the designer authored on
            // purpose.
            _collider.enabled = false;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private void EnsureRenderer(
            ref SpriteRenderer sr,
            string              childName,
            string              layerName,
            Sprite              sprite,
            Vector3             localPos)
        {
            if (sr == null)
            {
                Transform existing = transform.Find(childName);
                if (existing != null)
                    sr = existing.GetComponent<SpriteRenderer>();
            }

            if (sr == null)
            {
                var go = new GameObject(childName);
                go.transform.SetParent(transform, worldPositionStays: false);
                sr = go.AddComponent<SpriteRenderer>();
            }

            sr.transform.localPosition = localPos;
            sr.sprite                  = sprite;
            sr.sortingLayerName        = layerName;

            // URP 2D: without an explicit URP material, SpriteRenderers get the
            // built-in Sprites-Default shader which renders BLACK in the URP pipeline.
            if (s_urpSpriteMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader != null)
                    s_urpSpriteMat = new Material(shader);
            }
            if (s_urpSpriteMat != null)
                sr.sharedMaterial = s_urpSpriteMat;
        }

        private void EnsureCollider()
        {
            // Unity null check: a destroyed UnityEngine.Object passes C# null check
            // but throws MissingComponentException when accessed. Use the implicit
            // bool operator which handles both cases.
            if ((object)_collider != null && _collider)
                return;

            _collider = GetComponent<BoxCollider2D>();
            if (_collider == null)
                _collider = gameObject.AddComponent<BoxCollider2D>();
        }

        // ── Editor helpers ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Refresh renderers when inspector values change in edit mode.
            if (_template != null)
                Apply(_template, _scaleOverride, _splitRatioOverride);
        }

        private void OnDrawGizmosSelected()
        {
            if (_template == null) return;

            // Use actual collider/renderer data if available, else estimate from scale
            float sx = transform.localScale.x;
            float sy = transform.localScale.y;
            Vector3 pos = transform.position;

            if (_bottomRenderer != null && _bottomRenderer.sprite != null &&
                _topRenderer != null && _topRenderer.sprite != null)
            {
                float bottomH = _bottomRenderer.sprite.rect.height / PPU;
                float topH    = _topRenderer.sprite.rect.height / PPU;
                float spriteW = _bottomRenderer.sprite.rect.width / PPU;

                // Red: footprint / collision zone
                Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.45f);
                Gizmos.DrawWireCube(
                    pos + new Vector3(0f, bottomH * sy * 0.5f, 0f),
                    new Vector3(spriteW * sx, bottomH * sy, 0.05f));

                // Blue: canopy / above-player zone
                Gizmos.color = new Color(0.25f, 0.5f, 1f, 0.3f);
                Gizmos.DrawWireCube(
                    pos + new Vector3(0f, (bottomH + topH * 0.5f) * sy, 0f),
                    new Vector3(spriteW * sx, topH * sy, 0.05f));
            }
        }
#endif
    }
}