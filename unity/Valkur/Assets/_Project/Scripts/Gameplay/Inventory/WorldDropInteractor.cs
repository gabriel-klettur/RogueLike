using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.WorldDrops;

namespace Valkur.Gameplay.Inventory
{
    /// <summary>
    /// In-game world-drop interaction handler attached to the player. Mirrors
    /// the F7 Items Editor's hover / select / RMB-drag UX while the editor is
    /// closed, but bounded by an interaction range so the player can't reach
    /// drops on the other side of the map.
    ///
    /// Mirrors Python's <c>drop_drag_system</c>:
    ///   • Each player class declares a <c>drag_drop_range</c> in pixels
    ///     (128 px for every starter, see <c>config/players_config.py</c>).
    ///   • Hover / drag are gated by that range.
    ///   • While dragging, the cursor is clamped radially to the perimeter
    ///     so the item is "pulled" toward the player rather than escaping.
    ///
    /// At PPU 16, 128 px ≈ 8 world units ≈ 8 ground tiles, which is the
    /// default for <see cref="interactionRange"/>.
    ///
    /// Disables itself while any non-<see cref="IAllowsPlayerMovement"/>
    /// editor is active so F7 keeps full ownership of drops while open.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldDropInteractor : MonoBehaviour
    {
        [Header("Range")]
        [SerializeField, Tooltip("World-space radius around the player inside which drops can be hovered, selected, and moved. Defaults to 8 wu (= 128 px @ PPU 16) for parity with Python's drag_drop_range.")]
        private float interactionRange = 8f;

        [Header("Outline visuals")]
        [SerializeField, Tooltip("Cyan applied while the cursor hovers a reachable drop.")]
        private Color hoverColor = new Color(0.30f, 0.85f, 1.00f, 1f);
        [SerializeField, Tooltip("Yellow applied to the actively-selected drop.")]
        private Color selectedColor = new Color(1.00f, 0.95f, 0.30f, 1f);
        [SerializeField, Tooltip("Outline thickness for the cyan hover. Stays subtle so gameplay highlight doesn't fight VFX.")]
        private float hoverThickness = 0.06f;
        [SerializeField, Tooltip("Outline thickness for the yellow active selection.")]
        private float selectedThickness = 0.10f;

        public float InteractionRange
        {
            get => interactionRange;
            set => interactionRange = Mathf.Max(0f, value);
        }

        public WorldPickup Hovered  => _hovered;
        public WorldPickup Selected => _selected;
        public WorldPickup Dragging => _dragging;

        private Camera _mainCamera;
        private WorldPickup _hovered;
        private WorldPickup _selected;
        private WorldPickup _dragging;
        private string  _draggingDropId;
        private Vector3 _dragStartPos;
        private bool _outOfRangeFlash;

        private ItemOutlineRenderer _hoverFx;
        private ItemOutlineRenderer _selectedFx;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            _mainCamera = Camera.main;
            EnsureOutlineFx();
        }

        private void OnEnable()
        {
            EnsureOutlineFx();
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void Update()
        {
            // Hand control over to F7 (and any other non-movement editor) so we
            // don't double-process the same click. Editors that mark themselves
            // IAllowsPlayerMovement (Buildings, Tiles) coexist with us — those
            // don't own world-drop interaction by themselves.
            if (IsExclusiveEditorActive())
            {
                ClearAll();
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
            UpdateSelection(overUi);
            UpdateDrag(worldCursor, overUi);
        }

        // ── Hover ─────────────────────────────────────────────────────────────

        /// <summary>Pick the closest reachable drop under the cursor and tint
        /// it cyan. Drops outside <see cref="interactionRange"/> never hover.</summary>
        private void UpdateHover(Vector3 worldCursor, bool overUi)
        {
            if (overUi || _dragging != null) { SetHovered(_dragging); return; }

            WorldPickup best = null;
            float bestDistSq = float.PositiveInfinity;
            float rangeSq = interactionRange * interactionRange;
            Vector3 myPos = transform.position;

            // Physics2D.OverlapCircleAll narrows the search to colliders inside
            // the player's reach so a 1000-drop world doesn't pay an N² scan.
            int pickupLayer = LayerMask.NameToLayer("Pickup");
            int mask = pickupLayer != -1 ? (1 << pickupLayer) : ~0;
            var hits = Physics2D.OverlapCircleAll(myPos, interactionRange, mask);
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                var pickup = hit.GetComponent<WorldPickup>();
                if (pickup == null) continue;

                var sr = pickup.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                // Cursor must be inside the sprite footprint.
                if (!sr.bounds.Contains(new Vector3(worldCursor.x, worldCursor.y, sr.bounds.center.z)))
                    continue;

                // Drop must also still be inside the player's reach (defensive
                // duplicate of the OverlapCircle filter — protects against a
                // tunnel collider or a tiny range value).
                float distSq = ((Vector2)(pickup.transform.position - myPos)).sqrMagnitude;
                if (distSq > rangeSq) continue;
                if (distSq < bestDistSq) { bestDistSq = distSq; best = pickup; }
            }

            SetHovered(best);
        }

        // ── Selection (LMB) ───────────────────────────────────────────────────

        private void UpdateSelection(bool overUi)
        {
            if (overUi) return;
            if (_dragging != null) return;
            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;

            // LMB on a hovered reachable drop = select. LMB on empty space =
            // deselect. We intentionally don't grab pickup-on-LMB here so the
            // player can still walk through the drop without auto-grabbing
            // (E + PickupSystem owns the manual grab).
            SetSelected(_hovered);
        }

        // ── RMB drag-to-move with radial clamp ────────────────────────────────

        private void UpdateDrag(Vector3 worldCursor, bool overUi)
        {
            bool rmbDown    = MouseInputManager.WasRightMouseButtonPressedThisFrame();
            bool rmbHeld    = MouseInputManager.IsRightMouseButtonPressed();
            bool rmbRelease = MouseInputManager.WasRightMouseButtonReleasedThisFrame();

            if (rmbDown && _dragging == null && _hovered != null && !overUi)
            {
                _dragging       = _hovered;
                _draggingDropId = _hovered.DropId;
                _dragStartPos   = _hovered.transform.position;
                SetSelected(_hovered);
                return;
            }

            if (rmbHeld && _dragging != null)
            {
                Vector3 clamped = ClampToReach(worldCursor);
                _dragging.SetWorldPosition(new Vector3(clamped.x, clamped.y,
                    _dragging.transform.position.z));
                return;
            }

            if (rmbRelease && _dragging != null)
            {
                Vector3 landed = _dragging.transform.position;
                if (ServiceLocator.TryGet<ItemDropService>(out var service)
                    && !string.IsNullOrEmpty(_draggingDropId))
                {
                    service.UpdatePosition(_draggingDropId, new Vector2(landed.x, landed.y));
                }
                _dragging       = null;
                _draggingDropId = null;
            }
        }

        /// <summary>
        /// Clamp a world point to the perimeter of the interaction circle.
        /// Mirrors Python's <c>drop_drag_system</c> radial clamp so the
        /// dragged drop is "pulled" along the cursor's bearing instead of
        /// escaping to wherever the cursor actually is.
        /// </summary>
        public Vector3 ClampToReach(Vector3 worldPoint)
        {
            Vector3 origin = transform.position;
            Vector3 delta  = worldPoint - origin;
            float   len    = delta.magnitude;
            if (len <= interactionRange) return worldPoint;
            if (len <= 0.0001f)          return origin;
            return origin + (delta / len) * interactionRange;
        }

        // ── State helpers ─────────────────────────────────────────────────────

        private void SetHovered(WorldPickup pickup)
        {
            _hovered = pickup;
            if (_hoverFx == null) return;
            if (pickup != null && pickup != _selected)
            {
                _hoverFx.Configure(hoverColor, hoverThickness);
                _hoverFx.Follow(pickup);
                _hoverFx.SetVisible(true);
            }
            else
            {
                _hoverFx.Follow(null);
                _hoverFx.SetVisible(false);
            }
        }

        private void SetSelected(WorldPickup pickup)
        {
            _selected = pickup;
            if (_selectedFx == null) return;
            if (pickup != null)
            {
                _selectedFx.Configure(selectedColor, selectedThickness, padding: 0.06f);
                _selectedFx.Follow(pickup);
                _selectedFx.SetVisible(true);
            }
            else
            {
                _selectedFx.Follow(null);
                _selectedFx.SetVisible(false);
            }
        }

        private void ClearAll()
        {
            _hovered  = null;
            _selected = null;
            _dragging = null;
            _draggingDropId = null;
            if (_hoverFx    != null) { _hoverFx.Follow(null);    _hoverFx.SetVisible(false); }
            if (_selectedFx != null) { _selectedFx.Follow(null); _selectedFx.SetVisible(false); }
        }

        private void EnsureOutlineFx()
        {
            if (_hoverFx == null)
            {
                var go = new GameObject("WorldDrop_HoverOutline");
                go.transform.SetParent(transform, false);
                _hoverFx = go.AddComponent<ItemOutlineRenderer>();
                _hoverFx.Configure(hoverColor, hoverThickness);
                _hoverFx.SetVisible(false);
            }
            if (_selectedFx == null)
            {
                var go = new GameObject("WorldDrop_SelectedOutline");
                go.transform.SetParent(transform, false);
                _selectedFx = go.AddComponent<ItemOutlineRenderer>();
                _selectedFx.Configure(selectedColor, selectedThickness, padding: 0.06f);
                _selectedFx.SetVisible(false);
            }
        }

        /// <summary>True when an in-game editor that fully takes over input is
        /// active (Items / Spells / Particles / Inventory editors). Editors
        /// marked <see cref="IAllowsPlayerMovement"/> (Buildings / Tile) keep
        /// the player walking and don't gate world-drop interaction.</summary>
        private static bool IsExclusiveEditorActive()
        {
            if (!GameEditorManager.HasInstance) return false;
            var active = GameEditorManager.Instance.ActiveEditor;
            if (active == null) return false;
            return !(active is IAllowsPlayerMovement);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.30f, 0.85f, 1.00f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, interactionRange);
        }
#endif
    }
}
