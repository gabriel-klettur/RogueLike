using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Visualizes the currently active BoxCollider2D shapes of a building.
    ///
    /// The overlay is derived from the real collider state:
    ///   - root footprint collider when it is enabled
    ///   - fine-grained CollTile_* child colliders when they exist
    ///
    /// Visuals are updated in place instead of being recreated every frame. This
    /// keeps Show/Hide stable while buildings move, resize, or repaint colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingColliderDebugOverlay : MonoBehaviour
    {
        private const string VISUAL_PREFIX = "_ColliderDebug_";
        private const string COLL_TILE_PREFIX        = "CollTile_";
        private const string POOLED_COLL_TILE_PREFIX = "_PooledCollTile_";
        private const float OUTLINE_WIDTH = 0.05f;
        private const float Z_OFFSET = -0.1f;

        private sealed class VisualEntry
        {
            public GameObject Host;
            public SpriteRenderer Fill;
            public LineRenderer Line;
        }

        private static Sprite s_whiteSprite;
        private static Material s_lineMaterial;
        private static Material s_fillMaterial;

        private static readonly Color FillColor = new Color(1f, 0f, 0f, 0.48f);
        private static readonly Color LineColor = new Color(1f, 0f, 0f, 1f);

        private readonly List<VisualEntry> _visuals = new List<VisualEntry>();
        private bool _visible;

        // Authoring mode: when set, the overlay renders ONE visual per supplied
        // world-space cell rect (single source of truth = the editor's grid +
        // building rect). When cleared, the overlay falls back to its original
        // behaviour of inferring rects from the building's BoxCollider2D shapes.
        //
        // The buildings editor enables authoring mode for the active building
        // while the colliders panel is open so click-to-paint, grid storage and
        // the visual feedback all share a single coordinate system. This
        // eliminates the historical drift caused by re-deriving collider tile
        // positions through GetBuildingLocalSpriteSize / ResampleGrid.
        private bool        _authoringMode;
        private Rect[]      _authoringCells;
        private int         _authoringCellCount;

        public bool Visible => _visible;
        public int CurrentVisualCount { get; private set; }

        /// <summary>True while the overlay is rendering from supplied cell rects.</summary>
        public bool IsAuthoringMode => _authoringMode;

        /// <summary>Number of authoring cells currently driving the overlay (0 when not in authoring mode).</summary>
        public int AuthoringCellCount => _authoringMode ? _authoringCellCount : 0;

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!_visible)
            {
                SetAllDebugRootsActive(false);
                CurrentVisualCount = 0;
                return;
            }

            SyncVisuals();
        }

        /// <summary>
        /// Switch the overlay into authoring mode and replace its cell list with
        /// the supplied world-space rects. Each rect produces exactly one filled
        /// visual at that world position. Call <see cref="ClearAuthoringCells"/>
        /// to revert to BoxCollider2D-derived rendering.
        /// </summary>
        /// <param name="worldCellRects">
        /// World-space rects, one per visible collider cell. May be null/empty
        /// (in that case the overlay enters authoring mode but renders nothing).
        /// </param>
        public void SetAuthoringCells(IList<Rect> worldCellRects)
        {
            _authoringMode = true;
            int count = worldCellRects != null ? worldCellRects.Count : 0;
            if (_authoringCells == null || _authoringCells.Length < count)
                _authoringCells = new Rect[Mathf.Max(count, 8)];
            for (int i = 0; i < count; i++)
                _authoringCells[i] = worldCellRects[i];
            _authoringCellCount = count;
            if (_visible) SyncVisuals();
        }

        /// <summary>
        /// Leave authoring mode and revert to enumerating live BoxCollider2D
        /// shapes for the visuals. Idempotent.
        /// </summary>
        public void ClearAuthoringCells()
        {
            if (!_authoringMode && _authoringCellCount == 0) return;
            _authoringMode = false;
            _authoringCellCount = 0;
            if (_visible) SyncVisuals();
        }

        private void LateUpdate()
        {
            if (!_visible) return;
            SyncVisuals();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null && _visuals[i].Host != null)
                    DestroyUnityObject(_visuals[i].Host);
            }
            _visuals.Clear();
        }

        private void SyncVisuals()
        {
            EnsureSharedAssets();
            CleanupOrphanedVisualRoots();

            int visualCount = 0;

            if (_authoringMode)
            {
                // Authoring mode: render exactly one visual per supplied world-space cell rect.
                for (int i = 0; i < _authoringCellCount; i++)
                {
                    EnsureVisualCapacity(visualCount + 1);
                    UpdateVisualFromWorldRect(_visuals[visualCount], _authoringCells[i], visualCount);
                    visualCount++;
                }
            }
            else
            {
                // Default mode: infer rects from the building's live BoxCollider2D shapes.
                foreach (var box in EnumerateActiveColliders())
                {
                    EnsureVisualCapacity(visualCount + 1);
                    UpdateVisualFromCollider(_visuals[visualCount], box, visualCount);
                    visualCount++;
                }
            }

            for (int i = visualCount; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null && _visuals[i].Host != null)
                    _visuals[i].Host.SetActive(false);
            }

            CurrentVisualCount = visualCount;
        }

        private IEnumerable<BoxCollider2D> EnumerateActiveColliders()
        {
            // Strict filter: only the building's own root BoxCollider2D and the
            // explicitly-authored CollTile_* / _PooledCollTile_* children. This
            // guarantees we never visualise stray colliders that may have been
            // (accidentally or deliberately) parented under the building, such as
            // pickups, NPCs that walked into a trigger, debug markers, etc.
            // Without this filter the overlay was pickup up unrelated colliders
            // and producing visuals that "followed" other entities.
            var colliders = GetComponentsInChildren<BoxCollider2D>(includeInactive: false);
            for (int i = 0; i < colliders.Length; i++)
            {
                var box = colliders[i];
                if (box == null || !box.enabled) continue;
                var tName = box.transform.name;
                if (tName.StartsWith(VISUAL_PREFIX)) continue;            // our own visuals
                if (box.transform == transform) { yield return box; continue; } // root building collider
                if (tName.StartsWith(COLL_TILE_PREFIX)) { yield return box; continue; }
                if (tName.StartsWith(POOLED_COLL_TILE_PREFIX)) continue;  // pooled & inactive (or active but unused)
                // Any other child collider is intentionally skipped.
            }
        }

        private void EnsureVisualCapacity(int targetCount)
        {
            while (_visuals.Count < targetCount)
                _visuals.Add(CreateVisual(_visuals.Count));
        }

        private VisualEntry CreateVisual(int index)
        {
            var host = new GameObject($"{VISUAL_PREFIX}{index}");
            host.transform.SetParent(transform, worldPositionStays: false);
            host.layer = gameObject.layer;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(host.transform, worldPositionStays: false);
            fillGo.layer = gameObject.layer;

            var fill = fillGo.AddComponent<SpriteRenderer>();
            fill.sprite = s_whiteSprite;
            fill.color = FillColor;
            fill.sortingLayerName = "VFX";
            fill.sortingOrder = 6200;
            if (s_fillMaterial != null)
                fill.sharedMaterial = s_fillMaterial;

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.startWidth = OUTLINE_WIDTH;
            line.endWidth = OUTLINE_WIDTH;
            line.startColor = LineColor;
            line.endColor = LineColor;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.alignment = LineAlignment.View;
            line.sortingLayerName = "VFX";
            line.sortingOrder = 6201;
            if (s_lineMaterial != null)
                line.sharedMaterial = s_lineMaterial;

            host.SetActive(false);

            return new VisualEntry
            {
                Host = host,
                Fill = fill,
                Line = line
            };
        }

        private void UpdateVisualFromCollider(VisualEntry visual, BoxCollider2D box, int index)
        {
            if (visual == null || visual.Host == null || box == null) return;
            Bounds bounds = box.bounds;
            UpdateVisualFromWorldAabb(visual, bounds.center, bounds.size, index);
        }

        private void UpdateVisualFromWorldRect(VisualEntry visual, Rect worldRect, int index)
        {
            if (visual == null || visual.Host == null) return;
            Vector2 center = worldRect.center;
            Vector2 size = worldRect.size;
            UpdateVisualFromWorldAabb(visual, new Vector3(center.x, center.y, 0f), new Vector3(size.x, size.y, 0f), index);
        }

        private void UpdateVisualFromWorldAabb(VisualEntry visual, Vector3 worldCenter, Vector3 worldSize, int index)
        {
            visual.Host.name = $"{VISUAL_PREFIX}{index}";
            visual.Host.layer = gameObject.layer;
            visual.Host.transform.position = new Vector3(worldCenter.x, worldCenter.y, Z_OFFSET);
            visual.Host.transform.rotation = Quaternion.identity;
            visual.Host.transform.localScale = GetInverseLossyScale(transform);

            if (visual.Fill != null)
            {
                visual.Fill.transform.localPosition = Vector3.zero;
                visual.Fill.transform.localRotation = Quaternion.identity;
                visual.Fill.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
                visual.Fill.color = FillColor;
                visual.Fill.enabled = true;
            }

            if (visual.Line != null)
            {
                visual.Line.startWidth = OUTLINE_WIDTH;
                visual.Line.endWidth = OUTLINE_WIDTH;
                visual.Line.startColor = LineColor;
                visual.Line.endColor = LineColor;
                visual.Line.enabled = true;
                float minX = worldCenter.x - worldSize.x * 0.5f;
                float maxX = worldCenter.x + worldSize.x * 0.5f;
                float minY = worldCenter.y - worldSize.y * 0.5f;
                float maxY = worldCenter.y + worldSize.y * 0.5f;
                visual.Line.SetPosition(0, new Vector3(minX, minY, Z_OFFSET));
                visual.Line.SetPosition(1, new Vector3(maxX, minY, Z_OFFSET));
                visual.Line.SetPosition(2, new Vector3(maxX, maxY, Z_OFFSET));
                visual.Line.SetPosition(3, new Vector3(minX, maxY, Z_OFFSET));
            }

            visual.Host.SetActive(_visible);
        }

        private void SetVisualsActive(bool active)
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null && _visuals[i].Host != null)
                    _visuals[i].Host.SetActive(active);
            }
        }

        private void CleanupOrphanedVisualRoots()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(VISUAL_PREFIX)) continue;

                bool tracked = false;
                for (int j = 0; j < _visuals.Count; j++)
                {
                    if (_visuals[j] != null && _visuals[j].Host == child.gameObject)
                    {
                        tracked = true;
                        break;
                    }
                }

                if (tracked) continue;
                child.gameObject.SetActive(false);
                DestroyUnityObject(child.gameObject);
            }
        }

        private void SetAllDebugRootsActive(bool active)
        {
            SetVisualsActive(active);
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith(VISUAL_PREFIX))
                    child.gameObject.SetActive(active);
            }
        }

        private static Vector3 GetInverseLossyScale(Transform target)
        {
            Vector3 lossy = target != null ? target.lossyScale : Vector3.one;
            return new Vector3(
                Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f,
                Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f,
                Mathf.Abs(lossy.z) > 0.0001f ? 1f / lossy.z : 1f);
        }

        private static void EnsureSharedAssets()
        {
            if (s_whiteSprite == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;
                s_whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                s_whiteSprite.hideFlags = HideFlags.HideAndDontSave;
            }

            if (s_lineMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    s_lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (s_fillMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    s_fillMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
