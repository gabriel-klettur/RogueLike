using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public sealed partial class BuildingColliderDebugOverlay : MonoBehaviour
    {

        private void SyncVisuals()
        {
            EnsureSharedAssets();
            // Orphan cleanup is only interesting when the set of visuals might
            // have changed (first show, repaint, explicit MarkDirty). Running
            // it on every frame for 142 overlays added up to a measurable
            // overhead. Clearing _dirty is handled at the end of this method.
            if (_dirty)
            {
                CleanupOrphanedVisualRoots();
                // Force every visual to be fully re-applied: clear the cached
                // AABB state so UpdateVisualFromWorldAabb skips its fast-path.
                for (int i = 0; i < _visuals.Count; i++)
                    if (_visuals[i] != null) _visuals[i].HasCachedState = false;
            }

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
                // Default mode: enumerate the building's live BoxCollider2D
                // children, but only rebuild the cached array on dirty frames
                // (transform moves don't change the collider SET, only the
                // world bounds we read from each one).
                if (_dirty) RebuildDefaultColliderCache();
                for (int i = 0; i < _defaultColliderCount; i++)
                {
                    var box = _defaultColliderCache[i];
                    if (box == null || !box.enabled) continue;
                    EnsureVisualCapacity(visualCount + 1);
                    UpdateVisualFromCollider(_visuals[visualCount], box, visualCount);
                    visualCount++;
                }
            }

            for (int i = visualCount; i < _visuals.Count; i++)
            {
                var v = _visuals[i];
                if (v == null || v.Host == null) continue;
                if (v.LastActive)
                {
                    v.Host.SetActive(false);
                    v.LastActive = false;
                }
            }

            CurrentVisualCount = visualCount;
            _dirty = false;
        }

        private void RebuildDefaultColliderCache()
        {
            var colliders = GetComponentsInChildren<BoxCollider2D>(includeInactive: false);
            if (_defaultColliderCache == null || _defaultColliderCache.Length < colliders.Length)
                _defaultColliderCache = new BoxCollider2D[Mathf.Max(colliders.Length, 4)];
            int count = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                var box = colliders[i];
                if (box == null || !box.enabled) continue;
                var tName = box.transform.name;
                if (tName.StartsWith(VISUAL_PREFIX)) continue;
                if (box.transform == transform) { _defaultColliderCache[count++] = box; continue; }
                if (tName.StartsWith(COLL_TILE_PREFIX)) { _defaultColliderCache[count++] = box; continue; }
                if (tName.StartsWith(POOLED_COLL_TILE_PREFIX)) continue;
                // any other child collider is intentionally skipped
            }
            // clear residual slots so GC sees nothing stale
            for (int i = count; i < _defaultColliderCache.Length; i++) _defaultColliderCache[i] = null;
            _defaultColliderCount = count;
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
            // Fast path: if neither center nor size changed since the last
            // apply, just make sure the host is active (might have been hidden
            // when visualCount shrank) and exit. Avoids ~10 property writes
            // per visual per frame across 801 visuals.
            bool activeNow = _visible;
            if (visual.HasCachedState
                && visual.LastCenter == worldCenter
                && visual.LastSize == worldSize
                && visual.LastActive == activeNow)
            {
                return;
            }

            // Only rename when the index slot is actually re-purposed (new).
            string expectedName = $"{VISUAL_PREFIX}{index}";
            if (visual.Host.name != expectedName)
                visual.Host.name = expectedName;
            if (visual.Host.layer != gameObject.layer)
                visual.Host.layer = gameObject.layer;
            visual.Host.transform.position = new Vector3(worldCenter.x, worldCenter.y, Z_OFFSET);
            visual.Host.transform.rotation = Quaternion.identity;
            visual.Host.transform.localScale = GetInverseLossyScale(transform);

            if (visual.Fill != null)
            {
                visual.Fill.transform.localPosition = Vector3.zero;
                visual.Fill.transform.localRotation = Quaternion.identity;
                visual.Fill.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
                if (visual.Fill.color != FillColor) visual.Fill.color = FillColor;
                if (!visual.Fill.enabled) visual.Fill.enabled = true;
            }

            if (visual.Line != null)
            {
                if (visual.Line.startWidth != OUTLINE_WIDTH) visual.Line.startWidth = OUTLINE_WIDTH;
                if (visual.Line.endWidth != OUTLINE_WIDTH) visual.Line.endWidth = OUTLINE_WIDTH;
                visual.Line.startColor = LineColor;
                visual.Line.endColor = LineColor;
                if (!visual.Line.enabled) visual.Line.enabled = true;
                float minX = worldCenter.x - worldSize.x * 0.5f;
                float maxX = worldCenter.x + worldSize.x * 0.5f;
                float minY = worldCenter.y - worldSize.y * 0.5f;
                float maxY = worldCenter.y + worldSize.y * 0.5f;
                visual.Line.SetPosition(0, new Vector3(minX, minY, Z_OFFSET));
                visual.Line.SetPosition(1, new Vector3(maxX, minY, Z_OFFSET));
                visual.Line.SetPosition(2, new Vector3(maxX, maxY, Z_OFFSET));
                visual.Line.SetPosition(3, new Vector3(minX, maxY, Z_OFFSET));
            }

            if (visual.Host.activeSelf != activeNow)
                visual.Host.SetActive(activeNow);

            visual.LastCenter = worldCenter;
            visual.LastSize = worldSize;
            visual.LastActive = activeNow;
            visual.HasCachedState = true;
        }

        private void SetVisualsActive(bool active)
        {
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (_visuals[i] != null && _visuals[i].Host != null)
                    _visuals[i].Host.SetActive(active);
            }
        }

    }
}