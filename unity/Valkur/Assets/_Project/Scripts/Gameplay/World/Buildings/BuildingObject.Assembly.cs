using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.World
{
    public partial class BuildingObject : MonoBehaviour
    {

        /// <summary>
        /// Build (or rebuild) the building's two sprite halves from its template.
        ///
        /// <paramref name="assetPathOverride"/> renders a different sprite than the template's
        /// own — used by the light fixtures to swap between their dark and burning artwork at
        /// dusk and dawn. It deliberately does NOT touch <c>template.assetPath</c>, so the swap
        /// is presentation only and nothing downstream (save, collision, the F10 palette) sees
        /// a different building.
        /// </summary>
        public void Apply(BuildingTemplateData template, Vector2Int scaleOverride, float splitRatioOverride,
                          string assetPathOverride = null)
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
            string spritePath = string.IsNullOrEmpty(assetPathOverride) ? template.assetPath : assetPathOverride;
            Sprite sourceSprite = Resources.Load<Sprite>(spritePath);
            if (sourceSprite == null && spritePath != template.assetPath)
            {
                // A missing lit variant must not blank the fixture — fall back to its base art.
                Debug.LogWarning($"[BuildingObject] Lit sprite not found at Resources/{spritePath}; " +
                                  "falling back to the template's own sprite.", this);
                spritePath   = template.assetPath;
                sourceSprite = Resources.Load<Sprite>(spritePath);
            }
            if (sourceSprite == null)
            {
                Debug.LogWarning(
                    $"[BuildingObject] Sprite not found at Resources/{spritePath} " +
                    $"(template id={template.templateId}).", this);
                return;
            }

            _sourceSprite = sourceSprite;

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

            // Delegate sortingLayer + sortingOrder assignment to ApplyZOffsets()
            // so the Z-tier layer-promotion logic lives in exactly one place.
            // Both Apply() (initial setup) and the ZBottomOffset / ZTopOffset
            // setters need this — keeping it private+single-source guarantees
            // they can never drift apart.
            ApplyZOffsets();

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

            // Light fixtures carry their own Light2D. Done last so the renderers and the final
            // localScale exist — the light is positioned from the rendered bounds.
            RefreshLightFromTemplate();
        }

        /// <summary>
        /// Replace the art with whatever is left of the building after it was destroyed — a
        /// stump, a rubble pile, a shattered frame — or hide it entirely when the profile
        /// names no remains.
        ///
        /// <para>The canopy is switched OFF unconditionally. It is the half sorted on
        /// WallsTop, over the player: a felled tree that kept it would go on covering the
        /// character from above for the rest of the session, which is the same trap the
        /// split ratio exists to manage.</para>
        ///
        /// <para>The remains render at NATIVE scale rather than inheriting the transform
        /// that stretched the original sprite to its authored pixel dimensions. A stump
        /// squeezed into a tree's footprint reads as a texture swap; one at its own authored
        /// size reads as a different object.</para>
        /// </summary>
        public void ApplyRemainsSprite(Sprite remains)
        {
            if (_topRenderer != null) _topRenderer.gameObject.SetActive(false);
            if (_bottomRenderer == null) return;

            if (remains == null)
            {
                _bottomRenderer.enabled = false;
                return;
            }

            // Everything a regrow has to put back, captured BEFORE it is overwritten. All
            // three matter and losing any one of them is visible: without the sprite the tree
            // comes back as a stump, without the scale it comes back at the stump's size, and
            // without the split ratio it comes back with no canopy at all.
            if (!_hasPristineSnapshot)
            {
                _pristineFootprintSprite = _bottomRenderer.sprite;
                _pristineLocalScale = transform.localScale;
                _pristineSplitRatioOverride = _splitRatioOverride;
                _hasPristineSnapshot = true;
            }

            transform.localScale = Vector3.one;
            _bottomRenderer.sprite = remains;
            _bottomRenderer.enabled = true;

            // Nothing draws above the footprint any more, so the split has to say so too —
            // the save layer reads it back, and a persisted stump must not restore a canopy.
            _splitRatioOverride = 0f;
            ApplyZOffsets();
        }

        /// <summary>
        /// Undo <see cref="ApplyRemainsSprite"/>: the building goes back to the art, scale and
        /// split it was assembled with. What a regrown tree needs.
        ///
        /// <para>It restores a SNAPSHOT rather than re-running the assembly pass, because the
        /// pass reads the template plus this instance's overrides and the remains swap
        /// deliberately clobbered two of those overrides — re-running it would rebuild the
        /// building from the clobbered values and produce the stump again, convincingly.</para>
        ///
        /// <para>Returns false when there is nothing to restore, which is the honest answer
        /// for a building that was never destroyed.</para>
        /// </summary>
        public bool RestorePristine()
        {
            if (!_hasPristineSnapshot || _bottomRenderer == null) return false;

            _bottomRenderer.sprite = _pristineFootprintSprite;
            _bottomRenderer.enabled = true;
            transform.localScale = _pristineLocalScale;
            _splitRatioOverride = _pristineSplitRatioOverride;

            // The canopy is only reinstated when it had art of its own. A building assembled
            // with an empty top half has an inactive Canopy child by construction, and turning
            // that on would draw a blank renderer over the player.
            if (_topRenderer != null && _topRenderer.sprite != null)
                _topRenderer.gameObject.SetActive(true);

            _hasPristineSnapshot = false;
            ApplyZOffsets();
            return true;
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
            // WorldSpriteMaterials picks lit or unlit once for the whole world, so a
            // building darkens with the tiles it stands on instead of staying noon-bright.
            //
            // Cap role: a building collects snow on the edges that have open sky above them —
            // the roof line, the top of a wall, the crown of a tree prop — which the shader
            // reads out of the sprite's own alpha. That is what makes it work for all 969
            // templates without a single snow variant being drawn, and keep working after an
            // instance is rescaled or a new prop wave is imported.
            var mat = Valkur.Core.Rendering.WorldSpriteMaterials.WorldWithSnow(
                Valkur.Core.Rendering.WorldSpriteMaterials.SnowRole.Cap);
            if (mat != null)
                sr.sharedMaterial = mat;
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