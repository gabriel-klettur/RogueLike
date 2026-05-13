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
        /// </summary>
        private void RegenerateCompositeCollider(Tilemap collision)
        {
            var composite = collision.GetComponent<CompositeCollider2D>();
            if (composite == null) return;
            collision.RefreshAllTiles();
            composite.GenerateGeometry();
            StartCoroutine(DeferredCompositeRebake(collision, composite));
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