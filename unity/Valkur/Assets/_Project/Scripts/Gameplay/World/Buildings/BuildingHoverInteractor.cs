using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Buildings;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Player-mode hover highlight for interactable buildings. Mirrors
    /// <see cref="Valkur.Gameplay.Inventory.WorldDropInteractor"/>: cursor → world,
    /// ignore-UI early-out, a TTL-cached <see cref="BuildingObject"/> scan, and a
    /// single reusable <see cref="BuildingSilhouetteOutline"/> that follows the
    /// hovered building's full silhouette in yellow.
    ///
    /// Only buildings whose <see cref="BuildingObject.Interactable"/> is true are
    /// highlighted. That flag resolves per-instance
    /// (<see cref="BuildingObject.InteractableOverride"/>) or falls back to the
    /// template's <c>interactable</c>.
    ///
    /// Disables itself while the Buildings editor (F10) is open so the two hover
    /// systems never draw competing outlines on the same building.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingHoverInteractor : MonoBehaviour
    {
        [Header("Highlight")]
        [SerializeField, Tooltip("Yellow applied to the silhouette of a hovered interactable building.")]
        private Color hoverColor = new Color(1f, 0.85f, 0.20f, 1f);
        [SerializeField, Tooltip("Outline thickness in world units (~2 px at PPU 32).")]
        private float hoverThickness = 0.06f;

        private Camera _mainCamera;

        private BuildingObject[] _cache = System.Array.Empty<BuildingObject>();
        private float _cacheNextRefresh;
        private const float CACHE_TTL_SECONDS = 0.25f;

        private BuildingObject _hovered;
        private BuildingSilhouetteOutline _outline;

        private void Awake()
        {
            _mainCamera = Camera.main;
            EnsureOutline();
        }

        private void OnEnable() => EnsureOutline();

        private void OnDisable() => ClearHover();

        private void Update()
        {
            // F10 owns its own cyan/yellow outlines while open.
            if (IsBuildingsEditorActive())
            {
                ClearHover();
                return;
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z);
            Vector3 worldCursor = _mainCamera.ScreenToWorldPoint(sp);
            worldCursor.z = 0f;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            UpdateHover(worldCursor, overUi);
        }

        private void UpdateHover(Vector3 worldCursor, bool overUi)
        {
            if (overUi) { SetHovered(null); return; }

            RefreshCacheIfStale();

            BuildingObject best = null;
            float bestDistSq = float.PositiveInfinity;
            Vector2 cursor = new Vector2(worldCursor.x, worldCursor.y);

            for (int i = 0; i < _cache.Length; i++)
            {
                var b = _cache[i];
                if (b == null || b.gameObject == null) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (!b.Interactable) continue;

                // Hover hit-test against the building's full world AABB (not the
                // collider, which only covers the below-split footprint).
                if (!b.TryGetWorldRect(out var rect)) continue;
                if (!rect.Contains(cursor)) continue;

                float distSq = (cursor - new Vector2(rect.center.x, rect.center.y)).sqrMagnitude;
                if (distSq < bestDistSq) { bestDistSq = distSq; best = b; }
            }

            SetHovered(best);
        }

        private void RefreshCacheIfStale()
        {
            if (Time.unscaledTime < _cacheNextRefresh) return;
            _cache = FindObjectsOfType<BuildingObject>();
            _cacheNextRefresh = Time.unscaledTime + CACHE_TTL_SECONDS;
        }

        /// <summary>Force the next hover scan to re-enumerate the scene.</summary>
        public void InvalidateCache() => _cacheNextRefresh = 0f;

        private void SetHovered(BuildingObject building)
        {
            if (_hovered == building) return;
            _hovered = building;

            if (_outline == null) return;
            if (building != null)
            {
                _outline.Configure(hoverColor, hoverThickness);
                _outline.Follow(building);
                _outline.SetVisible(true);
            }
            else
            {
                _outline.Follow(null);
                _outline.SetVisible(false);
            }
        }

        private void ClearHover()
        {
            _hovered = null;
            if (_outline != null)
            {
                _outline.Follow(null);
                _outline.SetVisible(false);
            }
        }

        private void EnsureOutline()
        {
            if (_outline != null) return;
            var go = new GameObject("Building_HoverOutline");
            go.transform.SetParent(transform, false);
            _outline = go.AddComponent<BuildingSilhouetteOutline>();
            _outline.Configure(hoverColor, hoverThickness);
            _outline.SetVisible(false);
        }

        private static bool IsBuildingsEditorActive()
        {
            if (!GameEditorManager.HasInstance) return false;
            var active = GameEditorManager.Instance.ActiveEditor;
            return active != null && active is BuildingsRuntimeEditor;
        }
    }
}
