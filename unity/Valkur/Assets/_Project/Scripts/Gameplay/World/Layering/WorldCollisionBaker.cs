using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Owns the 10 per-physics-layer sub-tilemaps that drive M2's per-visual-layer
    /// collision filtering. Each painted Collision cell is distributed at bake time
    /// to the sub-tilemap matching its <see cref="CollisionTagMap"/> tag:
    ///
    ///   • Tag <c>"0".."8"</c> → sub-tilemap on physics layer <c>WorldL{tag}</c>.
    ///   • Tag <c>"*"</c> or unset → sub-tilemap on <c>WorldAll</c> (collides with every entity).
    ///
    /// Entities that filter by layer (Player today; NPCs / projectiles in M2.2)
    /// use <see cref="Collider2D.includeLayers"/> to enable exactly one
    /// <c>WorldL{N}</c> slot + <c>WorldAll</c>, so the Physics2D solver naturally
    /// skips contacts against the other 9 sub-tilemaps.
    ///
    /// Each sub-tilemap reuses Unity's <see cref="TilemapCollider2D"/> +
    /// <see cref="CompositeCollider2D"/> pair — auto-bakes on tile change.
    /// That avoids the per-cell BoxCollider2D explosion that a naive split would
    /// produce, and keeps the rebake hot path proportional to "cells touched"
    /// rather than "total cells".
    ///
    /// Visual <c>Collision</c> tilemap stays the single source of truth — its own
    /// <see cref="TilemapCollider2D"/> is DISABLED by the baker so cells aren't
    /// double-counted (once via the source, once via the sub-tilemaps).
    /// </summary>
    public sealed class WorldCollisionBaker : SingletonMonoBehaviour<WorldCollisionBaker>
    {
        protected override bool Persist => false;

        public const int CompositeCount = WorldCollisionLayers.LayerCount + 1; // 9 WorldL{N} + 1 WorldAll
        public const int WorldAllCompositeIndex = WorldCollisionLayers.LayerCount; // index 9

        private Tilemap _sourceCollision;
        private CollisionTagMap _tagMap;
        private Tilemap[] _subTilemaps = new Tilemap[CompositeCount];
        private CompositeCollider2D[] _subComposites = new CompositeCollider2D[CompositeCount];
        private bool _isReady;
        private bool _dirty;

        /// <summary>
        /// Idempotent spawner + best-effort wire-up. Safe to call from anywhere:
        /// spawns the singleton if missing, and attempts to bind the source
        /// Collision tilemap + tag map if the grid + TileEditorManager are
        /// available. Returns the singleton.
        ///
        /// Hot-reload safety: <see cref="RuntimeInitializeOnLoadMethod"/> doesn't
        /// fire on script reload while Play Mode is active; calling this from
        /// <see cref="TileEditor.TileEditorManager.OnSingletonAwake"/> as
        /// belt-and-suspenders guarantees the baker is alive whenever the Tile
        /// Editor is.
        /// </summary>
        public static WorldCollisionBaker EnsureExists()
        {
            WorldCollisionBaker instance;
            if (HasInstance)
            {
                instance = Instance;
            }
            else
            {
                var go = new GameObject(nameof(WorldCollisionBaker));
                instance = go.AddComponent<WorldCollisionBaker>();
            }

            // Try to wire up immediately if the grid + tilemap already exist.
            // If they're not ready yet (e.g. async scene setup), the dirty
            // listener catches the first SetTile and the deferred rebake fires
            // a frame later.
            if (!instance._isReady)
            {
                var gridBuilder = FindObjectOfType<WorldGridBuilder>();
                if (gridBuilder != null)
                {
                    var collision = gridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
                    if (collision != null)
                    {
                        // Parent the sub-tilemaps under the Grid component (NOT
                        // the builder) so they share the Grid's cell coordinate
                        // system. Without that the sub-tilemaps would operate
                        // in a different coord space and colliders would land
                        // at the wrong world positions.
                        var gridTransform = gridBuilder.Grid != null
                            ? gridBuilder.Grid.transform
                            : gridBuilder.transform;
                        var tagMap = TileEditorManager.HasInstance
                            ? TileEditorManager.Instance.CollisionTags : null;
                        instance.Initialize(gridTransform, collision, tagMap);
                        instance.ScheduleRebake();
                    }
                }
            }

            return instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterSceneLoad() => EnsureExists();

        private void OnEnable()
        {
            Tilemap.tilemapTileChanged += OnAnyTilemapChanged;
        }

        private void OnDisable()
        {
            Tilemap.tilemapTileChanged -= OnAnyTilemapChanged;
        }

        /// <summary>
        /// Mark the baker dirty whenever the source Collision tilemap changes.
        /// Catches every paint path automatically: Tile Editor edits,
        /// OverlayLoader applying zone JSON, Move-To-Layer erase phase C, etc.
        /// LateUpdate flushes the dirty bit — at most one rebake per frame even
        /// under a flood of <c>SetTile</c> calls.
        /// </summary>
        private void OnAnyTilemapChanged(Tilemap tm, Tilemap.SyncTile[] _)
        {
            if (tm == null || tm != _sourceCollision) return;
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            RebuildAll();
        }

        /// <summary>
        /// One-time setup. <paramref name="gridParent"/> is the Grid that owns
        /// the existing Collision tilemap; we add the 10 sub-tilemaps as
        /// siblings so they share the same world-space cell coordinates
        /// without extra transform math. Also disables the source Collision
        /// tilemap's <see cref="TilemapCollider2D"/> so its colliders don't
        /// fight the per-tag composites.
        /// </summary>
        public void Initialize(Transform gridParent, Tilemap sourceCollision, CollisionTagMap tagMap)
        {
            _sourceCollision = sourceCollision;
            _tagMap = tagMap;

            // Disable the source tilemap's collider — we own collisions now.
            var srcCollider = sourceCollision.GetComponent<TilemapCollider2D>();
            if (srcCollider != null) srcCollider.enabled = false;

            // Build the 10 sub-tilemaps (or reuse if Initialize was called twice).
            for (int i = 0; i < CompositeCount; i++)
            {
                if (_subTilemaps[i] != null) { _subTilemaps[i].ClearAllTiles(); continue; }

                string suffix = i < WorldCollisionLayers.LayerCount ? $"L{i}" : "All";
                int physicsLayer = i < WorldCollisionLayers.LayerCount
                    ? WorldCollisionLayers.GetWorldLayerIndex(i)
                    : WorldCollisionLayers.GetWorldAllIndex();

                var go = new GameObject($"CollisionPhysics_{suffix}");
                go.transform.SetParent(gridParent, false);
                if (physicsLayer >= 0) go.layer = physicsLayer;

                var tm = go.AddComponent<Tilemap>();
                tm.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

                // No renderer — these are collision-only. Tile we set is invisible
                // anyway, but skipping the component saves a draw call per zone.

                var coll = go.AddComponent<TilemapCollider2D>();
                coll.usedByComposite = true;

                var comp = go.AddComponent<CompositeCollider2D>();
                comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
                comp.generationType = CompositeCollider2D.GenerationType.Synchronous;

                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null) rb.bodyType = RigidbodyType2D.Static;

                _subTilemaps[i] = tm;
                _subComposites[i] = comp;
            }

            _isReady = true;
        }

        /// <summary>
        /// Sweep every cell of the source Collision tilemap + its tag map, and
        /// distribute cells to the matching sub-tilemap. Called after Tile
        /// Editor edits and (deferred) after scene load. Idempotent.
        /// </summary>
        public void RebuildAll()
        {
            if (!_isReady || _sourceCollision == null) return;

            for (int i = 0; i < CompositeCount; i++)
                _subTilemaps[i]?.ClearAllTiles();

            var bounds = _sourceCollision.cellBounds;
            int w = bounds.size.x;
            int h = bounds.size.y;
            if (w <= 0 || h <= 0) return;

            // GetTilesBlock is one managed call vs N GetTile calls — big win on
            // sparse zones because most cells are empty.
            var tiles = _sourceCollision.GetTilesBlock(bounds);
            int total = tiles?.Length ?? 0;
            if (total == 0) return;

            for (int i = 0; i < total; i++)
            {
                var tile = tiles[i];
                if (tile == null) continue;
                int cx = bounds.xMin + (i % w);
                int cy = bounds.yMin + (i / w);

                string tag = _tagMap != null
                    ? _tagMap.Get(new Vector2Int(cx, cy))
                    : CollisionTagMap.Wildcard;
                int compositeIdx = ResolveCompositeIndex(tag);

                // Reuse the exact source tile (already the project's invisible
                // "wall") rather than tracking a separate reference here.
                _subTilemaps[compositeIdx].SetTile(new Vector3Int(cx, cy, 0), tile);
            }
        }

        /// <summary>
        /// Schedule a deferred <see cref="RebuildAll"/> on the next frame. Used
        /// by paint paths inside the Tile Editor and by zone-load bootstraps —
        /// Unity's <see cref="TilemapCollider2D"/> ingests SetTile changes the
        /// following frame, so an immediate-only bake leaves the composites
        /// with zero geometry until the queue flushes. Mirrors the existing
        /// double-bake pattern in <see cref="TileEditor.TileEditorManager.RegenerateCompositeCollider"/>.
        /// </summary>
        public void ScheduleRebake()
        {
            RebuildAll();
            StartCoroutine(DeferredRebake());
        }

        private System.Collections.IEnumerator DeferredRebake()
        {
            yield return null;
            RebuildAll();
        }

        private static int ResolveCompositeIndex(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag == CollisionTagMap.Wildcard) return WorldAllCompositeIndex;
            if (tag.Length == 1 && tag[0] >= '0' && tag[0] <= '8') return tag[0] - '0';
            return WorldAllCompositeIndex; // fallback for garbage
        }
    }
}
