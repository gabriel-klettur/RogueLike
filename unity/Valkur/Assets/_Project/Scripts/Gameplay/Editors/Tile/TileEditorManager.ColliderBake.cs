using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorManager
    {        private Tilemap GetCollisionTilemap()
        {
            if (worldGridBuilder == null) return null;
            if (_tilemapCacheFrame == Time.frameCount && _cachedCollisionTilemap != null)
                return _cachedCollisionTilemap;
            _cachedCollisionTilemap = worldGridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
            return _cachedCollisionTilemap;
        }

        /// <summary>
        /// Build (once) and cache an invisible Tile suitable for the Collision tilemap.
        /// Matches WorldLoader.GetWallCollisionTile semantics: alpha-zero sprite, Grid
        /// collider type so TilemapCollider2D + CompositeCollider2D pick it up.
        /// </summary>
        private TileBase GetOrCreateColliderTile()
        {
            if (_colliderTile != null) return _colliderTile;

            var sprite = Resources.Load<Sprite>("Tiles/wall");
            if (sprite == null) sprite = Resources.Load<Sprite>("Tiles/floor");

            _colliderTile = ScriptableObject.CreateInstance<Tile>();
            _colliderTile.sprite = sprite;
            _colliderTile.color = new Color(1f, 1f, 1f, 0f);
            _colliderTile.colliderType = Tile.ColliderType.Grid;
            _colliderTile.hideFlags = HideFlags.HideAndDontSave;
            // Use the sprite name (e.g. "wall") so TileRegistry.GetName returns a
            // name that OverlayLoader.ResolveSprite can resolve on reload. If we used a
            // custom name like "TileEditorColliderTile", the overlay JSON would reference
            // a non-existent sprite and all drawn colliders would be lost after a restart.
            _colliderTile.name = sprite != null ? sprite.name : "wall";
            return _colliderTile;
        }

        /// <summary>
        /// The Collision tilemap is wired with a CompositeCollider2D in
        /// <c>generationType = Manual</c> mode (see WorldGridBuilder). After painting or
        /// erasing collision tiles we must explicitly regenerate the composite shape so
        /// Physics2D queries (raycasts, OverlapBox, etc.) reflect the new geometry.
        ///
        /// Race fix: <see cref="UnityEngine.Tilemaps.TilemapCollider2D"/> queues
        /// <c>SetTile</c> changes and processes them the FOLLOWING frame. Calling
        /// <c>GenerateGeometry()</c> on the same frame as the paint produces a
        /// composite with <c>pathCount = 0</c> even after <c>RefreshAllTiles()</c>.
        /// We therefore call refresh + bake immediately (so editor visualizers see
        /// the in-progress geometry) AND schedule a deferred bake one frame later
        /// (the only call that actually populates Physics2D query results).
        /// Regression guard: <c>PlayerTileCollisionPlayTests.PaintRefresh_BakeNextFrame_*</c>.
        ///
        /// Call-site policy (measured 2026-08-24): this used to run on EVERY
        /// mouse-down/drag frame of a collider stroke — 0.40 ms per call with only
        /// 2 painted cells in the zone, scaling with the zone's collider count, and
        /// with the editor forcing 120 FPS while active that's up to ~120 calls/sec
        /// during a sustained drag. Physics geometry only has to be correct once the
        /// stroke is committed, not frame-by-frame while it's being drawn — callers
        /// now set <see cref="_colliderStrokeDirty"/> during the drag and this method
        /// only actually runs via <see cref="FlushPendingColliderRebake"/> once, at
        /// stroke end (mouse-up, self-toggle-off, or the Layer-Jumps mutex forcing
        /// this mode off). That collapses the per-frame duplicate cost too: the
        /// deferred coroutine below now fires once per stroke instead of once per
        /// drag frame.
        ///
        /// NOT verified, NOT removed: <see cref="Valkur.Gameplay.World.Layering.WorldCollisionBaker.Initialize"/>
        /// disables this same tilemap's <c>TilemapCollider2D</c> ("we own collisions
        /// now") once it binds, and the per-tag sub-tilemap composites it owns
        /// rebake themselves independently off the <c>Tilemap.tilemapTileChanged</c>
        /// event. If that binding has already happened by the time a stroke ends,
        /// this call regenerates a composite backed by a disabled collider — geometry
        /// Physics2D never consults. Left in place because in the pre-bind boot
        /// window (or any test/scene without a WorldCollisionBaker) this source
        /// composite IS the real collision surface; deleting it blind risks the
        /// player walking through walls. Flagged for whoever next audits
        /// WorldCollisionBaker to confirm and then delete both this call and
        /// <see cref="DeferredCompositeRebake"/>.
        /// </summary>
        private void RegenerateCompositeCollider(Tilemap collision)
        {
            var composite = collision.GetComponent<CompositeCollider2D>();
            if (composite == null) return;
            collision.RefreshAllTiles();
            composite.GenerateGeometry();
            StartCoroutine(DeferredCompositeRebake(collision, composite));
        }

        // Set by HandleColliderInput whenever a paint/erase during the current
        // mouse gesture produced at least one committed TileEdit. Consumed (and
        // cleared) by FlushPendingColliderRebake so the composite regenerates
        // exactly once per stroke instead of once per frame.
        private bool _colliderStrokeDirty;

        /// <summary>
        /// Regenerate the composite once for the collider stroke that just ended,
        /// if it actually touched any cells. Call sites: the mouse-release branch
        /// of <see cref="HandleColliderInput"/>, and every place that force-ends a
        /// collider stroke early — self-toggle-off
        /// (<see cref="OnDrawCollidersClicked"/> / <see cref="OnEraseCollidersClicked"/>)
        /// and the Layer-Jumps mutex turning this mode off — so a half-finished
        /// drag never leaves the composite stale.
        /// </summary>
        private void FlushPendingColliderRebake()
        {
            if (!_colliderStrokeDirty) return;
            _colliderStrokeDirty = false;
            var collision = GetCollisionTilemap();
            if (collision != null) RegenerateCompositeCollider(collision);
        }

        private System.Collections.IEnumerator DeferredCompositeRebake(
            Tilemap collision, CompositeCollider2D composite)
        {
            yield return null; // Wait one frame for TilemapCollider2D to ingest queued SetTile changes.
            if (collision == null || composite == null) yield break;
            collision.RefreshAllTiles();
            composite.GenerateGeometry();
        }

        private void DisposeColliderTile()
        {
            if (_colliderTile == null) return;
            if (Application.isPlaying) Destroy(_colliderTile);
            else DestroyImmediate(_colliderTile);
            _colliderTile = null;
        }

    }
}