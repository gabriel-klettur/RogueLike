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
        [SerializeField, Tooltip("World-space radius around the player inside which drops can be hovered, selected, and moved while F7 is closed. Defaults to 4 wu — half of Python's 128 px (= 8 wu) drag_drop_range, so in-game interaction is intentionally tighter than the F7 authoring reach.")]
        private float interactionRange = 4f;

        [Header("Drop-on-player pickup")]
        [SerializeField, Tooltip("Extra slack (world units) added to the player SpriteRenderer bounds when deciding whether a finished drag landed 'on the player'. The pickup zone matches the cyan outline drawn around the sprite, not the small foot collider used for movement.")]
        private float dropOnPlayerPickupSlack = 0.10f;
        [SerializeField, Tooltip("Fallback pickup radius (around the player root transform) used only when the player has no SpriteRenderer yet.")]
        private float dropOnPlayerFallbackRadius = 1.0f;

        [Header("Outline visuals")]
        [SerializeField, Tooltip("Cyan applied while the cursor hovers a reachable drop.")]
        private Color hoverColor = new Color(0.30f, 0.85f, 1.00f, 1f);
        [SerializeField, Tooltip("Yellow applied to the actively-selected drop.")]
        private Color selectedColor = new Color(1.00f, 0.95f, 0.30f, 1f);
        [SerializeField, Tooltip("Outline thickness for the cyan hover. Stays subtle so gameplay highlight doesn't fight VFX.")]
        private float hoverThickness = 0.06f;
        [SerializeField, Tooltip("Outline thickness for the yellow active selection.")]
        private float selectedThickness = 0.10f;

        [Header("Player pickup highlight")]
        [SerializeField, Tooltip("Cyan rectangle drawn around the player sprite while a dragged item is inside the player collider — telegraphs that releasing now will save the item to the inventory.")]
        private Color playerPickupOutlineColor = new Color(0.30f, 0.85f, 1.00f, 1f);
        [SerializeField, Tooltip("Stroke thickness (world units) of the player pickup outline.")]
        private float playerPickupOutlineThickness = 0.08f;
        [SerializeField, Tooltip("Extra padding around the player sprite bounds so the outline doesn't kiss the silhouette.")]
        private float playerPickupOutlinePadding = 0.06f;

        public float InteractionRange
        {
            get => interactionRange;
            set => interactionRange = Mathf.Max(0f, value);
        }

        public WorldPickup Hovered  => _hovered;
        public WorldPickup Selected => _selected;
        public WorldPickup Dragging => _dragging;

        private Camera _mainCamera;
        private Collider2D _playerCollider;
        private SpriteRenderer _playerSprite;
        private Inventory _playerInventoryRef;
        private LineRenderer _playerOutlineLine;
        private static Material s_playerOutlineMat;
        private int _hoveredDepositSlot = -1;
        private WorldPickup _hovered;
        private WorldPickup _selected;
        private WorldPickup _dragging;
        private string  _draggingDropId;
        private Vector3 _dragStartPos;
        private bool _outOfRangeFlash;

        private ItemOutlineRenderer _hoverFx;
        private ItemOutlineRenderer _selectedFx;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_playerOutlineMat = null;
        }

        private void Awake()
        {
            _mainCamera = Camera.main;
            _playerCollider     = GetComponent<Collider2D>();
            _playerSprite       = GetComponentInChildren<SpriteRenderer>();
            _playerInventoryRef = GetComponent<Inventory>();
            EnsureOutlineFx();
            EnsurePlayerOutline();
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
            UpdateLmbInteraction(worldCursor, overUi);
        }

        // ── Hover ─────────────────────────────────────────────────────────────

        // Cache for the WorldPickup scan. Refreshed every CACHE_TTL seconds so a
        // brand-new drop appears in the hover scan within at most that many
        // frames, but the typical Update() doesn't pay the FindObjectsOfType
        // cost.
        private WorldPickup[] _pickupCache = System.Array.Empty<WorldPickup>();
        private float _pickupCacheNextRefresh;
        private const float PICKUP_CACHE_TTL_SECONDS = 0.25f;

        /// <summary>Pick the closest reachable drop under the cursor and tint
        /// it cyan. Drops outside <see cref="interactionRange"/> never hover.
        ///
        /// Iterates a cached <c>FindObjectsOfType&lt;WorldPickup&gt;()</c> rather
        /// than going through <c>Physics2D.OverlapCircleAll</c>: the pickup's
        /// kinematic Rigidbody2D combined with its sub-unit localScale (~0.0156,
        /// since a 256 px icon is normalised to 1 tile) leaves the collider
        /// invisible to physics queries even though the GameObject is on the
        /// right layer and renders fine. FindObjectsOfType is allocation-y but
        /// only re-runs every PICKUP_CACHE_TTL_SECONDS, so the per-frame cost
        /// stays in the low microseconds for typical drop counts.</summary>
        private void UpdateHover(Vector3 worldCursor, bool overUi)
        {
            if (overUi || _dragging != null) { SetHovered(_dragging); return; }

            RefreshPickupCacheIfStale();

            WorldPickup best = null;
            float bestDistSq = float.PositiveInfinity;
            float rangeSq = interactionRange * interactionRange;
            Vector3 myPos = transform.position;
            Vector3 cursorOnZ = new Vector3(worldCursor.x, worldCursor.y, 0f);

            for (int i = 0; i < _pickupCache.Length; i++)
            {
                var pickup = _pickupCache[i];
                if (pickup == null || pickup.gameObject == null) continue;
                if (!pickup.gameObject.activeInHierarchy) continue;

                float distSq = ((Vector2)(pickup.transform.position - myPos)).sqrMagnitude;
                if (distSq > rangeSq) continue;

                var sr = pickup.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;

                // Cursor must be inside the sprite footprint. Project both
                // points onto the same plane (z = bounds.center.z) so the
                // 2D contains check ignores camera depth.
                cursorOnZ.z = sr.bounds.center.z;
                if (!sr.bounds.Contains(cursorOnZ)) continue;

                if (distSq < bestDistSq) { bestDistSq = distSq; best = pickup; }
            }

            SetHovered(best);
        }

        private void RefreshPickupCacheIfStale()
        {
            if (Time.unscaledTime < _pickupCacheNextRefresh && _pickupCache.Length > 0) return;
            _pickupCache = FindObjectsOfType<WorldPickup>();
            _pickupCacheNextRefresh = Time.unscaledTime + PICKUP_CACHE_TTL_SECONDS;
        }

        /// <summary>Force the next <see cref="UpdateHover"/> to re-scan the
        /// scene. Call when something just spawned a pickup that needs to be
        /// hoverable immediately (e.g. F7 SpawnAt while the player is nearby).</summary>
        public void InvalidatePickupCache() { _pickupCacheNextRefresh = 0f; }

        // ── LMB selection + drag-to-move with threshold ───────────────────────
        // A single LMB does two jobs:
        //   • Press → release without significant cursor travel = "click"  → select.
        //   • Press → cursor moves more than DRAG_THRESHOLD_PX = "drag-to-move".
        // Same UX as every desktop file manager / map tool: short click selects,
        // hold + drag moves. Threshold is checked in screen-space pixels so it
        // feels identical at every zoom level.

        private const float DRAG_THRESHOLD_PX = 6f;

        private WorldPickup _pendingDragTarget;
        private Vector2     _pendingDragStartScreen;

        private void UpdateLmbInteraction(Vector3 worldCursor, bool overUi)
        {
            bool lmbDown    = MouseInputManager.WasLeftMouseButtonPressedThisFrame();
            bool lmbHeld    = MouseInputManager.IsLeftMouseButtonPressed();
            bool lmbRelease = MouseInputManager.WasLeftMouseButtonReleasedThisFrame();

            // ── Press: arm a potential drag if a hovered drop is under cursor.
            //          We don't commit yet — the gesture only becomes a drag
            //          once the cursor crosses DRAG_THRESHOLD_PX.
            if (lmbDown && !overUi && _dragging == null)
            {
                if (_hovered != null)
                {
                    _pendingDragTarget      = _hovered;
                    _pendingDragStartScreen = MouseInputManager.GetScreenMousePosition();
                    _dragStartPos           = _hovered.transform.position;
                }
                else
                {
                    // LMB on empty world → clear current selection.
                    SetSelected(null);
                }
                return;
            }

            // ── Hold: promote the pending press to an active drag once the
            //          cursor has moved past the screen-space threshold; then
            //          keep the pickup glued to the cursor (clamped to reach).
            //          Skip when the same frame ALSO reports a release (InputSystem
            //          can stamp held+released together on fast clicks); falling
            //          through guarantees the release branch runs.
            if (lmbHeld && !lmbRelease)
            {
                if (_dragging == null && _pendingDragTarget != null)
                {
                    Vector2 cur = MouseInputManager.GetScreenMousePosition();
                    if (Vector2.Distance(cur, _pendingDragStartScreen) > DRAG_THRESHOLD_PX)
                    {
                        _dragging       = _pendingDragTarget;
                        _draggingDropId = _pendingDragTarget.DropId;
                        SetSelected(_dragging);
                    }
                }
                if (_dragging != null)
                {
                    Vector3 clamped = ClampToReach(worldCursor);
                    _dragging.SetWorldPosition(new Vector3(clamped.x, clamped.y,
                        _dragging.transform.position.z));

                    // Light up the player silhouette when the dragged drop is
                    // inside the player collider, so the user sees exactly when
                    // releasing will save the item to the inventory.
                    UpdatePlayerPickupOutline(IsLandedOnPlayer(_dragging.transform.position));

                    // Telegraph which inventory slot the item will land in (yellow
                    // border around the predicted destination cell).
                    UpdateInventoryDepositTarget();
                }
                return;
            }

            // ── Release: a "click" (no drag fired) selects; a finished drag
            //             either picks up (when the drop landed on the player)
            //             or commits the new position to the persistence service.
            if (lmbRelease)
            {
                if (_dragging != null)
                {
                    Vector3 landed = _dragging.transform.position;

                    // Two pickup gestures are accepted on release:
                    //   • Drop on the player (within dropOnPlayerPickupRadius).
                    //   • Drop on the inventory panel while it's open.
                    // Either routes through WorldPickup.TryPickup, which handles
                    // tag check, capacity overflow, and the persistence
                    // OnDestroyed(PickedUp) event when the drop is fully consumed.
                    bool releasedOverInventory = IsCursorOverInventoryPanel();
                    bool fullyConsumed = TryPickupOntoPlayer(_dragging, landed)
                                      || (releasedOverInventory && TryPickupViaInventoryPanel(_dragging));

                    // Skip UpdatePosition only when the drop was fully picked up:
                    // WorldPickup.OnDestroy already fires the PickedUp persistence
                    // event in that case, and the GameObject is queued for destroy.
                    // On partial pickup the drop survives at its landed position
                    // with reduced quantity, so we still need to persist the move.
                    if (!fullyConsumed
                        && ServiceLocator.TryGet<ItemDropService>(out var service)
                        && !string.IsNullOrEmpty(_draggingDropId))
                    {
                        service.UpdatePosition(_draggingDropId, new Vector2(landed.x, landed.y));
                    }
                    UpdatePlayerPickupOutline(false);
                    ClearInventoryDepositTarget();
                    _dragging       = null;
                    _draggingDropId = null;
                }
                else if (_pendingDragTarget != null)
                {
                    // No drag fired → treat as a plain click on a hovered drop.
                    SetSelected(_pendingDragTarget);
                }
                _pendingDragTarget = null;
            }
        }

        // Drag-to-pickup: when a drag releases with the drop sitting on top of
        // the player, route the item into the inventory via WorldPickup.TryPickup
        // (which handles tag check, capacity overflow, and the persistence
        // OnDestroyed event). Returns true only when the drop's full stack was
        // consumed — partial pickups leave the drop alive at its landed spot.
        private bool TryPickupOntoPlayer(WorldPickup pickup, Vector3 landed)
        {
            if (pickup == null) return false;
            if (!IsLandedOnPlayer(landed)) return false;

            int before = pickup.Quantity;
            pickup.TryPickupIntoSlot(gameObject, _hoveredDepositSlot);
            string label = pickup.Item != null ? pickup.Item.displayName : "?";
            if (pickup.Quantity < before)
                Debug.Log($"[WorldDropInteractor] Drag-to-player → +{before - pickup.Quantity}x {label} (slot={_hoveredDepositSlot})");
            else
                Debug.LogWarning($"[WorldDropInteractor] Drag-to-player gesture detected on '{label}' but pickup was rejected (inventory full, missing Player tag, or no Inventory component).");
            return pickup.Quantity <= 0;
        }

        // True when the landed point is inside the player's *sprite* bounds plus
        // a small slack. We deliberately use the SpriteRenderer rather than the
        // Collider2D: the collider is a tiny capsule at the feet (for movement /
        // Y-sort / NPC contact) but visually the player covers head-to-feet, and
        // the pickup zone must match the cyan outline drawn around the sprite.
        // Falls back to the foot-collider, then to a fixed radius, only if no
        // SpriteRenderer is available yet.
        private bool IsLandedOnPlayer(Vector3 landed)
        {
            Vector2 p = landed;

            if (_playerSprite == null) _playerSprite = GetComponentInChildren<SpriteRenderer>();
            if (_playerSprite != null && _playerSprite.sprite != null)
            {
                Bounds b = _playerSprite.bounds;
                b.Expand(dropOnPlayerPickupSlack * 2f); // Expand grows by 'amount' on each side
                return b.Contains(new Vector3(p.x, p.y, b.center.z));
            }

            if (_playerCollider == null) _playerCollider = GetComponent<Collider2D>();
            if (_playerCollider != null && _playerCollider.enabled)
            {
                Bounds b = _playerCollider.bounds;
                b.Expand(dropOnPlayerPickupSlack * 2f);
                return b.Contains(new Vector3(p.x, p.y, b.center.z));
            }

            float r = dropOnPlayerFallbackRadius;
            return ((Vector2)(landed - transform.position)).sqrMagnitude <= r * r;
        }

        // Drop-into-inventory-panel: same destination as TryPickupOntoPlayer,
        // but triggered by releasing the drag while the cursor is over the open
        // inventory window. Returns true only on full consumption.
        private bool TryPickupViaInventoryPanel(WorldPickup pickup)
        {
            if (pickup == null) return false;
            int before = pickup.Quantity;
            pickup.TryPickupIntoSlot(gameObject, _hoveredDepositSlot);
            string label = pickup.Item != null ? pickup.Item.displayName : "?";
            if (pickup.Quantity < before)
                Debug.Log($"[WorldDropInteractor] Drag-to-panel → +{before - pickup.Quantity}x {label} (slot={_hoveredDepositSlot})");
            else
                Debug.LogWarning($"[WorldDropInteractor] Drag-to-panel gesture detected on '{label}' but pickup was rejected (inventory full, missing Player tag, or no Inventory component).");
            return pickup.Quantity <= 0;
        }

        private bool IsCursorOverInventoryPanel()
        {
            if (!InventoryUI.HasInstance) return false;
            return InventoryUI.Instance.IsScreenPointOverPanel(MouseInputManager.GetScreenMousePosition());
        }

        // Yellow-border highlight on the slot the dragged item would deposit
        // into. While the cursor is hovering a specific cell that can accept
        // the item (empty OR same-item with stack room), we honour that exact
        // cell. Otherwise we fall back to the auto-pick prediction so the user
        // still sees where AddItem would deliver it.
        private void UpdateInventoryDepositTarget()
        {
            if (!InventoryUI.HasInstance) { _hoveredDepositSlot = -1; return; }
            var ui = InventoryUI.Instance;

            if (!ui.IsVisible || _dragging == null || _dragging.Item == null)
            {
                ui.SetDepositTargetSlot(-1);
                _hoveredDepositSlot = -1;
                return;
            }

            if (_playerInventoryRef == null) _playerInventoryRef = GetComponent<Inventory>();
            if (_playerInventoryRef == null)
            {
                ui.SetDepositTargetSlot(-1);
                _hoveredDepositSlot = -1;
                return;
            }

            Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
            int hovered = ui.IsScreenPointOverPanel(screenPos)
                ? ui.HitTestSlotByScreenPos(screenPos)
                : -1;

            int target;
            if (hovered >= 0 && IsValidDepositSlot(hovered, _dragging.Item))
            {
                target = hovered;
                _hoveredDepositSlot = hovered;
            }
            else
            {
                target = _playerInventoryRef.PredictAddTargetSlot(_dragging.Item, _dragging.Quantity);
                _hoveredDepositSlot = -1;
            }

            ui.SetDepositTargetSlot(target);
        }

        private bool IsValidDepositSlot(int idx, ItemDefinition item)
        {
            if (item == null) return false;
            if (_playerInventoryRef == null) return false;
            if (idx < 0) return false;

            // Bag and equipment cells share the same accept rule: empty cell
            // accepts anything; same-item cell accepts more if stackable.
            bool inBag    = idx < _playerInventoryRef.Capacity;
            bool inEquip  = _playerInventoryRef.IsEquipmentIndex(idx);
            if (!inBag && !inEquip) return false;

            var slot = _playerInventoryRef.GetSlotByIndex(idx);
            if (slot.IsEmpty) return true;
            if (slot.Item != item) return false;
            if (!item.stackable) return false;
            return slot.Quantity < item.maxStack;
        }

        private void ClearInventoryDepositTarget()
        {
            if (InventoryUI.HasInstance) InventoryUI.Instance.SetDepositTargetSlot(-1);
            _hoveredDepositSlot = -1;
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
            _draggingDropId    = null;
            _pendingDragTarget = null;
            UpdatePlayerPickupOutline(false);
            ClearInventoryDepositTarget();
            if (_hoverFx    != null) { _hoverFx.Follow(null);    _hoverFx.SetVisible(false); }
            if (_selectedFx != null) { _selectedFx.Follow(null); _selectedFx.SetVisible(false); }
        }

        // Cyan rectangle drawn around the player's own sprite — visible only
        // while a dragged world drop is inside the player collider, so the
        // gesture "release on the player to save the item" has a clear
        // affordance. Built once in Awake; toggled per frame by
        // UpdatePlayerPickupOutline.
        private void EnsurePlayerOutline()
        {
            if (_playerOutlineLine != null) return;

            var go = new GameObject("PlayerPickupOutline");
            go.transform.SetParent(transform, false);

            _playerOutlineLine = go.AddComponent<LineRenderer>();
            _playerOutlineLine.useWorldSpace     = true;
            _playerOutlineLine.loop              = true;
            _playerOutlineLine.positionCount     = 4;
            _playerOutlineLine.numCornerVertices = 0;
            _playerOutlineLine.numCapVertices    = 0;
            _playerOutlineLine.alignment         = LineAlignment.View;
            _playerOutlineLine.sortingLayerName  = "VFX";
            _playerOutlineLine.sortingOrder      = 5000;
            _playerOutlineLine.startColor        = playerPickupOutlineColor;
            _playerOutlineLine.endColor          = playerPickupOutlineColor;
            _playerOutlineLine.startWidth        = playerPickupOutlineThickness;
            _playerOutlineLine.endWidth          = playerPickupOutlineThickness;
            _playerOutlineLine.enabled           = false;

            if (s_playerOutlineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (sh != null) s_playerOutlineMat = new Material(sh) { name = "PlayerPickupOutline_Line" };
            }
            if (s_playerOutlineMat != null) _playerOutlineLine.sharedMaterial = s_playerOutlineMat;
        }

        // Toggle the cyan outline around the player. When `ready` is true, the
        // four corners are anchored to the player SpriteRenderer bounds so the
        // outline tracks animation-driven size changes (idle/run/hit frames).
        private void UpdatePlayerPickupOutline(bool ready)
        {
            if (_playerOutlineLine == null) return;
            if (!ready)
            {
                if (_playerOutlineLine.enabled) _playerOutlineLine.enabled = false;
                return;
            }

            if (_playerSprite == null) _playerSprite = GetComponentInChildren<SpriteRenderer>();
            if (_playerSprite == null || _playerSprite.sprite == null)
            {
                _playerOutlineLine.enabled = false;
                return;
            }

            var b = _playerSprite.bounds;
            float p = playerPickupOutlinePadding;
            Vector3 bl = new Vector3(b.min.x - p, b.min.y - p, 0f);
            Vector3 br = new Vector3(b.max.x + p, b.min.y - p, 0f);
            Vector3 tr = new Vector3(b.max.x + p, b.max.y + p, 0f);
            Vector3 tl = new Vector3(b.min.x - p, b.max.y + p, 0f);
            _playerOutlineLine.SetPosition(0, bl);
            _playerOutlineLine.SetPosition(1, br);
            _playerOutlineLine.SetPosition(2, tr);
            _playerOutlineLine.SetPosition(3, tl);
            _playerOutlineLine.enabled = true;
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
