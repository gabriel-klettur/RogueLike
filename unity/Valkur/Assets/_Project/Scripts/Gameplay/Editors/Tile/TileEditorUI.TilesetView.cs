using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Tileset-view runtime state for the F8 Tile Editor: zoom level, "hide
    /// duplicates" toggle, slot population, and selection semantics.
    ///
    /// A category is treated as a tilesheet when its first entry has
    /// <c>gridR &gt;= 0</c> — populated by <see cref="TileCatalog.BuildFromResources"/>
    /// from <c>Resources/Tiles/&lt;cat&gt;/_manifest.json</c>.
    ///
    /// Selection in the picker honours the global <see cref="TileEditorState.SelectMode"/>:
    ///   • <b>Single</b> — click replaces the selection with one tile and sets
    ///     it as the active brush.
    ///   • <b>Rect</b>   — click+drag defines a rectangle; release replaces the
    ///     selection with that rect.
    ///   • <b>Multi</b>  — each click toggles a tile in the selection; the most
    ///     recent click also becomes the active brush. Drag does nothing.
    /// </summary>
    public partial class TileEditorUI
    {
        private bool _tilesetDedupOn = false;
        private float _tilesetZoom = TileEditorUIBuilder.TILESET_ZOOM_DEFAULT;

        // Cached per-category metadata so IsCurrentCategoryTilesheet doesn't
        // re-scan the catalog every time. Refreshed whenever the picker
        // re-populates for a new category.
        private bool _currentCategoryIsTilesheet;

        // Drag-rect transient state (only meaningful while SelectMode == Rect).
        private Vector2Int? _tilesetDragStart;
        private Vector2Int? _tilesetDragEnd;

        // Persistent selection in the picker. Survives between clicks and only
        // resets when the user clicks "Clear Selection", changes category, or
        // toggles dedup. Coordinates are (col, row) of the tilesheet manifest.
        private readonly HashSet<Vector2Int> _tilesetSelectedSlots = new HashSet<Vector2Int>();

        // Clipboard snapshot for the picker. Populated by CommitTilesetSelection;
        // cleared by ClearTilesetSelection and ResetPickerSelectionState (category
        // change). Coordinates are (col, row) matching _tilesetSelectedSlots.
        private readonly HashSet<Vector2Int> _tilesetCopiedSlots = new HashSet<Vector2Int>();

        // Maps every tilesheet slot back to its (r, c) + tile entry so the
        // selection logic can resolve "which slot is the pointer over".
        private readonly Dictionary<GameObject, TilesetSlotInfo> _tilesetSlotInfo
            = new Dictionary<GameObject, TilesetSlotInfo>();
        // Per-slot highlight overlay GameObject. Activated and re-coloured
        // each frame the selection state changes.
        private readonly Dictionary<GameObject, GameObject> _tilesetSlotHighlight
            = new Dictionary<GameObject, GameObject>();
        // Per-slot COPY-highlight overlay GameObject (thick yellow border).
        // Activated whenever the slot's (col,row) is in _tilesetCopiedSlots.
        private readonly Dictionary<GameObject, GameObject> _tilesetSlotCopyHighlight
            = new Dictionary<GameObject, GameObject>();

        // Reverse lookup: picker (col,row) -> slot GameObject. Lets the drag/
        // selection hot path (OnTilesetSlotEnter / OnTilesetSlotDrag, fired on
        // every mouse-move tick while the author marquee-selects in the
        // picker) resolve individual cells directly instead of walking every
        // registered slot to find them. Populated in RegisterPickerSlot,
        // cleared in ResetPickerSelectionState.
        private readonly Dictionary<Vector2Int, GameObject> _tilesetSlotByPos
            = new Dictionary<Vector2Int, GameObject>();

        // Snapshot of "(position -> was it painted as inDrag) for every slot
        // that was highlighted (inDrag OR isSelected) as of the previous
        // RefreshTilesetSelectionVisuals call" -- NOT a copy of the full slot
        // dictionary. Reused across calls (Clear + refill, never reallocated)
        // so the hot path can diff "what changed since last call" without an
        // allocation and without touching slots whose highlight state didn't
        // change. Cleared in ResetPickerSelectionState -- a repopulated
        // picker has all-new GameObjects, so a stale entry here would either
        // no-op (harmless) or, worse, suppress the repaint of a same-position
        // slot in the new category that legitimately needs to be lit for the
        // first time.
        private readonly Dictionary<Vector2Int, bool> _tilesetPrevHighlighted
            = new Dictionary<Vector2Int, bool>();

        // Scratch buffer for "what should be highlighted this call" (position
        // -> inDrag). Cleared and refilled every RefreshTilesetSelectionVisuals
        // call, never reallocated. Bounded by the drag-rect area plus the
        // persistent selection size -- NOT by the catalog size (3,045 entries
        // total / 2,688 for castle_pandora alone), which is the point of this
        // rewrite: the old version iterated _tilesetSlotInfo (every registered
        // slot) on every drag tick regardless of how small the drag was.
        private readonly Dictionary<Vector2Int, bool> _tilesetHighlightScratch
            = new Dictionary<Vector2Int, bool>();

        private struct TilesetSlotInfo
        {
            public int R;
            public int C;
            public TileCatalog.TileEntry Entry;
            // Cached at registration so the hot picker paths don't pay a
            // GetComponent<RectTransform>() per slot per frame (2,688 slots ×
            // 60 fps = 160k calls/s for castle_pandora before this cache).
            public RectTransform Rt;
            // Cached highlight Image so RefreshTilesetSelectionVisuals can
            // recolour without another GetComponent per slot.
            public Image HighlightImg;
        }

        // Gold-ish — shown only while the user is mid-drag in Rect mode.
        private static readonly Color TILESET_RECT_PREVIEW_COLOR =
            new Color(0.90f, 0.76f, 0.38f, 0.45f);
        // Green — matches the GREEN persistent-selection overlay used on the
        // map (TileEditorGridOverlay) so the two surfaces feel like part of
        // the same selection model.
        private static readonly Color TILESET_SELECTED_COLOR =
            new Color(0.40f, 0.88f, 0.40f, 0.50f);

        /// <summary>Wired from <see cref="BuildUI"/> after the static builder runs.</summary>
        private void WireTilesetControls()
        {
            if (_refs.TilesetZoomSlider != null)
            {
                _refs.TilesetZoomSlider.value = _tilesetZoom;
                _refs.TilesetZoomSlider.onValueChanged.AddListener(OnTilesetZoomChanged);
            }
            if (_refs.TilesetDedupToggleImg != null)
            {
                var btn = _refs.TilesetDedupToggleImg.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(OnTilesetDedupClicked);
            }
            RefreshTilesetDedupVisual();
        }

        private void OnTilesetZoomChanged(float v)
        {
            _tilesetZoom = v;
            if (_refs.TilesetZoomLabel != null)
                _refs.TilesetZoomLabel.text = $"{(int)v}";

            var gl = _refs.TileGridContent != null
                ? _refs.TileGridContent.GetComponent<GridLayoutGroup>()
                : null;
            if (gl != null && IsCurrentCategoryTilesheet())
                gl.cellSize = new Vector2(_tilesetZoom, _tilesetZoom);
        }

        private void OnTilesetDedupClicked()
        {
            _tilesetDedupOn = !_tilesetDedupOn;
            RefreshTilesetDedupVisual();
            // Toggling dedup rebuilds the slot set; clear selection so we don't
            // leave stale (col,row) entries pointing at placeholders.
            _tilesetSelectedSlots.Clear();
            PopulateTileGrid(_currentCategory);
            _currentPickerContent = PickerContentKind.Tiles;
        }

        private void RefreshTilesetDedupVisual()
        {
            if (_refs.TilesetDedupToggleImg != null)
                _refs.TilesetDedupToggleImg.color = _tilesetDedupOn ? ACCENT_BG : BTN_NORMAL;
            if (_refs.TilesetDedupToggleLabel != null)
            {
                _refs.TilesetDedupToggleLabel.color = _tilesetDedupOn ? ACCENT : TEXT_SECONDARY;
                _refs.TilesetDedupToggleLabel.text = _tilesetDedupOn ? "SHOW ALL" : "HIDE DUPS";
            }
        }

        /// <summary>True when the current category's tiles came from a sliced tilesheet manifest.</summary>
        private bool IsCurrentCategoryTilesheet()
        {
            // Reads the cached flag set in PopulateTileGrid — avoids re-scanning
            // the catalog on every zoom-slider tick.
            return _currentCategoryIsTilesheet;
        }

        /// <summary>
        /// Clears only the copy-highlight overlay (yellow CopyHL) without touching
        /// the green selection or clipboard state. Called by the manager's F8-deactivate
        /// branch so the yellow ring is gone after the editor closes, even though the
        /// underlying state is preserved for if the user re-opens the editor.
        /// </summary>
        public void ClearTilesetCopyHighlight()
        {
            _tilesetCopiedSlots.Clear();
            RefreshTilesetCopyVisuals();
        }

        /// <summary>
        /// Public entry-point used by the manager's "Clear Selection" handler so
        /// clearing the map selection also wipes the picker selection in one click.
        /// </summary>
        public void ClearTilesetSelection()
        {
            _tilesetSelectedSlots.Clear();
            _tilesetCopiedSlots.Clear();
            _tilesetDragStart = null;
            _tilesetDragEnd = null;
            RefreshTilesetSelectionVisuals();
            RefreshTilesetCopyVisuals();
            if (_state != null)
            {
                _state.Clipboard = null;
                RefreshClipboardButtons();
            }
        }

        /// <summary>
        /// Populates the tile grid in (row, col) order from a sliced tilesheet.
        /// When dedup is on, all but the first occurrence of each <c>uniqueId</c>
        /// render as empty placeholders so the geometry of the source sheet
        /// stays intact (vs. a reflowed compact list).
        /// </summary>
        /// <returns>Number of fully populated (non-placeholder) slots, for the count label.</returns>
        /// <summary>
        /// Wipe the per-slot selection state. Called by <see cref="PopulateTileGrid"/>
        /// before either path (legacy / tilesheet) repopulates the grid so each
        /// rebuild starts from a clean slate.
        /// </summary>
        internal void ResetPickerSelectionState()
        {
            _tilesetDragStart = null;
            _tilesetDragEnd = null;
            _tilesetSelectedSlots.Clear();
            _tilesetCopiedSlots.Clear();
            _tilesetSlotInfo.Clear();
            _tilesetSlotHighlight.Clear();
            _tilesetSlotCopyHighlight.Clear();
            // The picker is about to be rebuilt with all-new GameObjects (or
            // emptied outright) -- the position lookup and the "highlighted
            // last call" snapshot both reference the OLD slots and must not
            // survive into the new grid.
            _tilesetSlotByPos.Clear();
            _tilesetPrevHighlighted.Clear();
            _tilesetHighlightScratch.Clear();
        }

        /// <summary>
        /// Track a freshly-built slot so the picker's selection logic (drag-rect
        /// hit-tests, highlight refresh, clipboard commit) can find it by
        /// <see cref="GameObject"/>. Invoked by both <see cref="PopulateLegacySlots"/>
        /// and <see cref="PopulateTilesheetSlots"/>. Caches the slot's
        /// <see cref="RectTransform"/> and the highlight overlay's
        /// <see cref="Image"/> so hot picker paths don't pay a GetComponent per
        /// slot per frame.
        /// </summary>
        internal void RegisterPickerSlot(GameObject slot, int r, int c,
            TileCatalog.TileEntry entry, GameObject highlightOverlay)
        {
            var rt = slot != null ? slot.GetComponent<RectTransform>() : null;
            var hImg = highlightOverlay != null ? highlightOverlay.GetComponent<Image>() : null;
            _tilesetSlotInfo[slot] = new TilesetSlotInfo
            {
                R = r,
                C = c,
                Entry = entry,
                Rt = rt,
                HighlightImg = hImg,
            };
            _tilesetSlotHighlight[slot] = highlightOverlay;
            // Keyed the other way round too, so the drag/selection hot path
            // can resolve "the slot at (col,row)" in O(1) instead of walking
            // every registered slot looking for a coordinate match.
            _tilesetSlotByPos[new Vector2Int(c, r)] = slot;
        }

        /// <summary>
        /// Register the copy-highlight overlay for a slot. Separate from
        /// <see cref="RegisterPickerSlot"/> to preserve the existing public API that
        /// tests depend on. Invoked by <see cref="PopulateTilesheetSlots"/> immediately
        /// after the DragHL sibling is registered.
        /// </summary>
        internal void RegisterPickerSlotCopyHighlight(GameObject slot, GameObject copyHighlightOverlay)
        {
            _tilesetSlotCopyHighlight[slot] = copyHighlightOverlay;
        }

        private int PopulateTilesheetSlots(List<TileCatalog.TileEntry> tiles)
        {
            // Sort defensively — Resources.LoadAll returns in undefined order.
            tiles.Sort((a, b) =>
            {
                int rcmp = a.gridR.CompareTo(b.gridR);
                return rcmp != 0 ? rcmp : a.gridC.CompareTo(b.gridC);
            });

            var seenUniques = new HashSet<int>();
            int realSlots = 0;

            // Pre-size the slot list + per-slot dictionaries so adding 2,688
            // entries (castle_pandora) doesn't trigger ~12 internal grow-resizes.
            if (_tileSlots.Capacity < tiles.Count) _tileSlots.Capacity = tiles.Count;

            for (int i = 0; i < tiles.Count; i++)
            {
                var entry = tiles[i];
                bool isFirstOfUnique = entry.uniqueId < 0 || seenUniques.Add(entry.uniqueId);
                bool renderAsPlaceholder = _tilesetDedupOn && !isFirstOfUnique;

                var go = CreateUI($"Slot_{entry.gridR}_{entry.gridC}", _refs.TileGridContent);
                var slotImg = go.AddComponent<Image>();
                slotImg.color = renderAsPlaceholder
                    ? new Color(SLOT_BG.r, SLOT_BG.g, SLOT_BG.b, 0.35f)
                    : SLOT_BG;
                // Placeholders carry no event handlers, so they should not block
                // the UGUI raycast either — at 2,534 placeholders for castle_pandora
                // this trims every pointer event's raycast cost dramatically.
                if (renderAsPlaceholder)
                    slotImg.raycastTarget = false;

                if (!renderAsPlaceholder)
                {
                    int ci = i; var ce = entry;

                    var preview = entry.preview;
                    if (preview == null && entry.tile is Tile t) preview = t.sprite;
                    if (preview != null && !entry.transparent)
                    {
                        var sgo = CreateUI("Prev", go.transform);
                        var sr = sgo.GetComponent<RectTransform>();
                        sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 1f);
                        sr.offsetMin = new Vector2(1f, 1f);
                        sr.offsetMax = new Vector2(-1f, -1f);
                        var si = sgo.AddComponent<Image>();
                        si.sprite = preview; si.preserveAspect = true; si.raycastTarget = false;
                    }

                    // The DragHL (selection) and CopyHL (clipboard) overlays are
                    // BUILT LAZILY by RefreshTilesetSelectionVisuals /
                    // RefreshTilesetCopyVisuals on the few slots that actually need
                    // them. For castle_pandora's 154-real-slot category that means
                    // 6 GameObjects (1 DragHL + 5 CopyHL frame parts) per real slot
                    // are NOT created up-front — net ~900 GameObjects skipped.
                    // Tests still register pre-built overlays via RegisterPickerSlot /
                    // RegisterPickerSlotCopyHighlight; the lazy ensure short-circuits
                    // when the dictionary entry is non-null.
                    RegisterPickerSlot(go, entry.gridR, entry.gridC, entry, highlightOverlay: null);
                    AttachPickerSlotHandlers(go, entry.gridR, entry.gridC, ci, ce);
                    realSlots++;
                }

                _tileSlots.Add(go);
            }

            return realSlots;
        }

        /// <summary>
        /// Builds the CopyHL frame as a parent GO containing four thin Image
        /// strips anchored to the slot's edges (top / bottom / left / right).
        /// Toggling the parent's active state shows or hides the entire frame.
        /// The slot's tile preview stays visible through the open centre.
        /// </summary>
        private GameObject CreateCopyHLFrame(Transform slotParent)
        {
            var frame = CreateUI("CopyHL", slotParent);
            var frt = frame.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 1f);
            frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

            float t = TileEditorConstants.PickerCopyHighlightBorderPx;
            var color = TileEditorConstants.PickerCopyHighlightColor;

            // Top: stretch X, pin to top edge, height = t.
            AddBorderStrip(frame.transform, "Top",
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                pivot:     new Vector2(0.5f, 1f),
                sizeDelta: new Vector2(0f, t),
                anchoredPos: Vector2.zero,
                color: color);

            // Bottom: stretch X, pin to bottom edge, height = t.
            AddBorderStrip(frame.transform, "Bottom",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                pivot:     new Vector2(0.5f, 0f),
                sizeDelta: new Vector2(0f, t),
                anchoredPos: Vector2.zero,
                color: color);

            // Left: stretch Y, pin to left edge, width = t.
            AddBorderStrip(frame.transform, "Left",
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(0f, 1f),
                pivot:     new Vector2(0f, 0.5f),
                sizeDelta: new Vector2(t, 0f),
                anchoredPos: Vector2.zero,
                color: color);

            // Right: stretch Y, pin to right edge, width = t.
            AddBorderStrip(frame.transform, "Right",
                anchorMin: new Vector2(1f, 0f), anchorMax: new Vector2(1f, 1f),
                pivot:     new Vector2(1f, 0.5f),
                sizeDelta: new Vector2(t, 0f),
                anchoredPos: Vector2.zero,
                color: color);

            return frame;
        }

        private static void AddBorderStrip(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 sizeDelta, Vector2 anchoredPos, Color color)
        {
            var go = CreateUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin     = anchorMin;
            rt.anchorMax     = anchorMax;
            rt.pivot         = pivot;
            rt.sizeDelta     = sizeDelta;
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        // Attaches PointerDown/PointerEnter/PointerUp handlers to a picker slot.
        // Uses a custom <see cref="TilesetSlotPointerEvents"/> component instead of
        // EventTrigger so the mouse-wheel reaches the parent ScrollRect, and so
        // drag gestures stay anchored to the slot (preventing the ScrollRect from
        // capturing them as scroll-drags — which would break Rect selection).
        //
        // Caller is responsible for supplying the (r, c) coordinates the slot
        // should occupy in the picker grid: tilesheet categories use the
        // manifest's gridR/gridC; legacy categories compute it from the slot
        // index against the 4-column layout. Either way, the same Single/Rect/
        // Multi semantics fall out from the shared selection-set logic.
        private void AttachPickerSlotHandlers(GameObject slot, int r, int c, int slotIndex, TileCatalog.TileEntry entry)
        {
            var events = slot.AddComponent<TilesetSlotPointerEvents>();
            events.OnDownAction  = () => OnTilesetSlotDown(r, c, slotIndex, entry);
            events.OnEnterAction = () => OnTilesetSlotEnter(r, c);
            events.OnUpAction    = () => OnTilesetSlotUp(slotIndex, entry);
            events.OnDragAction  = OnTilesetSlotDrag;
        }

        private TileEditorState.SelectMode CurrentSelectMode =>
            _state != null ? _state.CurrentSelectMode : TileEditorState.SelectMode.Single;

        private void OnTilesetSlotDown(int r, int c, int slotIndex, TileCatalog.TileEntry entry)
        {
            var pos = new Vector2Int(c, r);
            switch (CurrentSelectMode)
            {
                case TileEditorState.SelectMode.Single:
                    _tilesetSelectedSlots.Clear();
                    _tilesetSelectedSlots.Add(pos);
                    SetActiveBrush(slotIndex, entry);
                    RefreshTilesetSelectionVisuals();
                    CommitTilesetSelection();
                    break;

                case TileEditorState.SelectMode.Rect:
                    _tilesetDragStart = pos;
                    _tilesetDragEnd   = pos;
                    RefreshTilesetSelectionVisuals();
                    break;

                case TileEditorState.SelectMode.Multi:
                    if (!_tilesetSelectedSlots.Add(pos))
                        _tilesetSelectedSlots.Remove(pos); // toggle off
                    SetActiveBrush(slotIndex, entry);      // last-clicked = active brush
                    RefreshTilesetSelectionVisuals();
                    CommitTilesetSelection();
                    break;
            }
        }

        private void OnTilesetSlotEnter(int r, int c)
        {
            // Only Rect mode tracks drag-deltas; Single/Multi are click-only.
            if (CurrentSelectMode != TileEditorState.SelectMode.Rect) return;
            if (!_tilesetDragStart.HasValue) return;
            _tilesetDragEnd = new Vector2Int(c, r);
            RefreshTilesetSelectionVisuals();
        }

        // Drag callback fired by every <see cref="TilesetSlotPointerEvents"/> that
        // currently owns the drag. We use the screen position from the event to
        // raycast the slot under the cursor — IPointerEnter on peer slots does
        // not fire reliably while a drag is in flight, so we resolve "current
        // slot" ourselves on each drag tick.
        private void OnTilesetSlotDrag(PointerEventData ev)
        {
            if (CurrentSelectMode != TileEditorState.SelectMode.Rect) return;
            if (!_tilesetDragStart.HasValue) return;

            if (TryFindSlotInfoAt(ev.position, out var info))
            {
                _tilesetDragEnd = new Vector2Int(info.C, info.R);
                RefreshTilesetSelectionVisuals();
            }
            // If the cursor leaves the picker mid-drag, leave _tilesetDragEnd at
            // its last known value so the rect preview doesn't snap back.
        }

        /// <summary>
        /// Returns the <see cref="TilesetSlotInfo"/> for the picker slot whose
        /// rect contains <paramref name="screenPos"/>, or false if no slot is
        /// under that point. The picker canvas is ScreenSpaceOverlay so we
        /// pass <c>null</c> as the camera.
        /// </summary>
        private bool TryFindSlotInfoAt(Vector2 screenPos, out TilesetSlotInfo info)
        {
            foreach (var kv in _tilesetSlotInfo)
            {
                var rt = kv.Value.Rt;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                {
                    info = kv.Value;
                    return true;
                }
            }
            info = default;
            return false;
        }

        private void OnTilesetSlotUp(int slotIndex, TileCatalog.TileEntry entry)
        {
            if (CurrentSelectMode != TileEditorState.SelectMode.Rect) return;
            if (!_tilesetDragStart.HasValue) return;

            var s = _tilesetDragStart.Value;
            var e = _tilesetDragEnd ?? s;
            _tilesetDragStart = null;
            _tilesetDragEnd   = null;

            // Rect REPLACES the previous selection (matches map behaviour).
            _tilesetSelectedSlots.Clear();
            int cMin = Mathf.Min(s.x, e.x), cMax = Mathf.Max(s.x, e.x);
            int rMin = Mathf.Min(s.y, e.y), rMax = Mathf.Max(s.y, e.y);
            for (int rr = rMin; rr <= rMax; rr++)
            for (int cc = cMin; cc <= cMax; cc++)
                _tilesetSelectedSlots.Add(new Vector2Int(cc, rr));

            // If the rect collapsed to a single cell, also set it as the active
            // brush — keeps the legacy "single click = quick brush switch" UX
            // intact even when the user is in Rect mode.
            int rectW = (cMax - cMin) + 1;
            int rectH = (rMax - rMin) + 1;
            bool isSingleCell = rectW == 1 && rectH == 1;
            if (isSingleCell)
                SetActiveBrush(slotIndex, entry);

            RefreshTilesetSelectionVisuals();
            CommitTilesetSelection();

            // For a 1×1 Rect click, auto-switch to Brush so the user can paint
            // immediately — mirrors the Single-mode behaviour.
            // Multi-cell rects stay in Select/Rect so Ctrl+V can be used to paste.
            // We call _onToolChanged directly instead of going through OnTileSelected
            // because OnTileSelected blocks the auto-switch whenever
            // CurrentSelectMode != Single (intentional for multi-cell flows).
            if (isSingleCell)
                _onToolChanged?.Invoke(TileEditorState.Tool.Brush);
        }

        private void SetActiveBrush(int slotIndex, TileCatalog.TileEntry entry)
        {
            _selectedSlotIndex = slotIndex;
            _onTileSelected?.Invoke(entry);
            HighlightSelectedSlot();
        }

        /// <summary>
        /// Repaints the CopyHL overlay for every tilesheet slot based on
        /// <see cref="_tilesetCopiedSlots"/>: slots whose (col,row) is in the set
        /// show the thick bright-yellow CopyHL; all others are hidden.
        /// Called after every <see cref="CommitTilesetSelection"/>,
        /// <see cref="ClearTilesetSelection"/>, and <see cref="ResetPickerSelectionState"/>.
        ///
        /// The CopyHL frame is BUILT LAZILY here on the first activation —
        /// PopulateTilesheetSlots no longer pre-builds it for every slot to
        /// avoid the 5 GameObjects × 2,688-slot allocation spike when the user
        /// clicks a heavy CATEGORIES button (e.g. castle_pandora).
        /// </summary>
        private void RefreshTilesetCopyVisuals()
        {
            foreach (var kv in _tilesetSlotInfo)
            {
                var info = kv.Value;
                var pos  = new Vector2Int(info.C, info.R);
                bool isCopied = _tilesetCopiedSlots.Contains(pos);

                _tilesetSlotCopyHighlight.TryGetValue(kv.Key, out var cgo);

                if (isCopied)
                {
                    if (cgo == null)
                    {
                        cgo = EnsureCopyHLFrame(kv.Key);
                        if (cgo == null) continue;
                    }
                    cgo.SetActive(true);
                }
                else if (cgo != null)
                {
                    cgo.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Lazily build (and register) the CopyHL frame for <paramref name="slot"/>.
        /// No-ops when the slot already has a frame registered (production reload
        /// or test pre-registration). Returns the frame so the caller can toggle
        /// its active state. Returns null when <paramref name="slot"/> is null.
        /// </summary>
        private GameObject EnsureCopyHLFrame(GameObject slot)
        {
            if (slot == null) return null;
            if (_tilesetSlotCopyHighlight.TryGetValue(slot, out var existing) && existing != null)
                return existing;

            var frame = CreateCopyHLFrame(slot.transform);
            frame.SetActive(false);
            _tilesetSlotCopyHighlight[slot] = frame;
            return frame;
        }

        /// <summary>
        /// Repaints ONLY the picker slots whose highlight state actually
        /// changed since the previous call — not the whole picker.
        ///
        /// Colour rule (unchanged from the original sweep):
        ///   • drag-preview rect (Rect mode, mid-drag) → gold
        ///   • persistent selected (any mode)         → green
        ///   • neither                                → hidden
        ///
        /// This is called from <see cref="OnTilesetSlotEnter"/> and
        /// <see cref="OnTilesetSlotDrag"/> — i.e. on every mouse-move tick
        /// while the author drags a marquee selection in the picker — so it
        /// used to walk the ENTIRE <c>_tilesetSlotInfo</c> dictionary (3,045
        /// catalog entries total, 2,688 for castle_pandora alone) on every
        /// tick regardless of how small the drag was. It now computes only
        /// "what should be highlighted this call" (the drag-rect area plus
        /// the persistent selection — <see cref="_tilesetHighlightScratch"/>,
        /// bounded by the selection size, not the catalog size), diffs that
        /// against <see cref="_tilesetPrevHighlighted"/> (the same shape,
        /// snapshotted from the previous call), and only touches the
        /// GameObjects for positions whose (active, colour) actually flipped.
        ///
        /// The "turn off" pass below is the orphan guard: every position that
        /// drops out of <c>_tilesetHighlightScratch</c> between calls — because
        /// the drag rect moved past it, or a Rect release replaced the
        /// persistent selection — gets an explicit deactivate. Skipping that
        /// pass (or missing a position out of the diff) is what would leave a
        /// slot lit forever after the selection moves on.
        ///
        /// The DragHL Image is still built lazily (see
        /// <see cref="EnsureSlotDragHighlight"/>) — unchanged from before this
        /// rewrite, and orthogonal to it.
        /// </summary>
        private void RefreshTilesetSelectionVisuals()
        {
            bool dragging = _tilesetDragStart.HasValue;
            int cMin = 0, cMax = 0, rMin = 0, rMax = 0;
            if (dragging)
            {
                var s = _tilesetDragStart.Value;
                var e = _tilesetDragEnd ?? s;
                cMin = Mathf.Min(s.x, e.x); cMax = Mathf.Max(s.x, e.x);
                rMin = Mathf.Min(s.y, e.y); rMax = Mathf.Max(s.y, e.y);
            }

            // What SHOULD be highlighted this call: every cell in the live
            // drag rect (gold, inDrag=true) plus every persistently selected
            // cell not already covered by the rect (green, inDrag=false).
            // inDrag always wins the colour when a selected cell also sits
            // inside the rect — matches the pre-existing `inDrag ? gold : green`
            // rule exactly.
            _tilesetHighlightScratch.Clear();
            if (dragging)
            {
                for (int rr = rMin; rr <= rMax; rr++)
                for (int cc = cMin; cc <= cMax; cc++)
                    _tilesetHighlightScratch[new Vector2Int(cc, rr)] = true;
            }
            foreach (var pos in _tilesetSelectedSlots)
            {
                if (!_tilesetHighlightScratch.ContainsKey(pos))
                    _tilesetHighlightScratch[pos] = false;
            }

            // Paint every position that is newly highlighted, or that stayed
            // highlighted but flipped between gold (inDrag) and green
            // (selected-only) — e.g. a selected cell the drag rect just grew
            // over, or just receded from. Positions whose (active, colour)
            // didn't change are skipped entirely.
            foreach (var kv in _tilesetHighlightScratch)
            {
                var pos = kv.Key;
                bool inDrag = kv.Value;
                if (_tilesetPrevHighlighted.TryGetValue(pos, out var wasInDrag) && wasInDrag == inDrag)
                    continue; // unchanged since last call — already painted correctly

                if (_tilesetSlotByPos.TryGetValue(pos, out var slotGo))
                    SetSlotHighlightActive(slotGo, active: true, inDrag: inDrag);
            }

            // Orphan guard: anything highlighted last call that is NOT in this
            // call's set any more gets an explicit deactivate. Without this a
            // slot the rect (or the replaced selection) moved past would stay
            // lit forever — worse than the sweep this rewrite removes.
            foreach (var kv in _tilesetPrevHighlighted)
            {
                if (_tilesetHighlightScratch.ContainsKey(kv.Key)) continue;
                if (_tilesetSlotByPos.TryGetValue(kv.Key, out var slotGo))
                    SetSlotHighlightActive(slotGo, active: false, inDrag: false);
            }

            // Snapshot this call's state as "previous" for the next call.
            // Reused dictionary — Clear + refill, never reallocated.
            _tilesetPrevHighlighted.Clear();
            foreach (var kv in _tilesetHighlightScratch)
                _tilesetPrevHighlighted[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Activates/recolours or deactivates the DragHL overlay for a single
        /// picker slot. Extracted from the old whole-dictionary sweep so
        /// <see cref="RefreshTilesetSelectionVisuals"/> can call it only for
        /// the handful of slots whose state actually changed, instead of
        /// visiting every registered slot every call.
        /// </summary>
        private void SetSlotHighlightActive(GameObject slotGo, bool active, bool inDrag)
        {
            if (slotGo == null) return;

            if (!active)
            {
                if (_tilesetSlotHighlight.TryGetValue(slotGo, out var offGo) && offGo != null)
                    offGo.SetActive(false);
                return;
            }

            _tilesetSlotHighlight.TryGetValue(slotGo, out var hgo);
            if (hgo == null)
            {
                hgo = EnsureSlotDragHighlight(slotGo);
                if (hgo == null) return;
                // Cache the lazily-created Image back into the slot info so
                // subsequent recolours don't pay another GetComponent.
                if (_tilesetSlotInfo.TryGetValue(slotGo, out var freshInfo))
                {
                    freshInfo.HighlightImg = hgo.GetComponent<Image>();
                    _tilesetSlotInfo[slotGo] = freshInfo;
                }
            }
            hgo.SetActive(true);

            Image img = null;
            if (_tilesetSlotInfo.TryGetValue(slotGo, out var info))
                img = info.HighlightImg;
            if (img == null) img = hgo.GetComponent<Image>();
            if (img != null)
                img.color = inDrag ? TILESET_RECT_PREVIEW_COLOR : TILESET_SELECTED_COLOR;
        }

        /// <summary>
        /// Lazily build (and register) the DragHL highlight overlay for
        /// <paramref name="slot"/>. No-ops when an overlay already exists
        /// (production reload or test pre-registration). Returns the overlay
        /// GameObject so the caller can toggle its active state.
        /// </summary>
        private GameObject EnsureSlotDragHighlight(GameObject slot)
        {
            if (slot == null) return null;
            if (_tilesetSlotHighlight.TryGetValue(slot, out var existing) && existing != null)
                return existing;

            var hgo = CreateUI("DragHL", slot.transform);
            var hrt = hgo.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            var hImg = hgo.AddComponent<Image>();
            hImg.color = TILESET_SELECTED_COLOR;
            hImg.raycastTarget = false;
            hgo.SetActive(false);

            _tilesetSlotHighlight[slot] = hgo;
            return hgo;
        }

        /// <summary>
        /// Materialises the current <see cref="_tilesetSelectedSlots"/> set as a
        /// rectangular <see cref="TileClipboard"/>. Non-rectangular selections
        /// fill the bbox and leave gaps as <c>null</c> entries — the map's
        /// <c>OnPasteClicked</c> already skips null tiles, so the holes paint
        /// nothing rather than overwriting existing tiles on the map.
        /// </summary>
        private void CommitTilesetSelection()
        {
            if (_state == null) return;
            if (_tilesetSelectedSlots.Count == 0)
            {
                _state.Clipboard = null;
                _tilesetCopiedSlots.Clear();
                RefreshTilesetCopyVisuals();
                RefreshClipboardButtons();
                return;
            }

            int cMin = int.MaxValue, cMax = int.MinValue;
            int rMin = int.MaxValue, rMax = int.MinValue;
            foreach (var p in _tilesetSelectedSlots)
            {
                if (p.x < cMin) cMin = p.x; if (p.x > cMax) cMax = p.x;
                if (p.y < rMin) rMin = p.y; if (p.y > rMax) rMax = p.y;
            }
            int w = (cMax - cMin) + 1;
            int h = (rMax - rMin) + 1;

            // Index slots by (r, c) for O(1) lookup.
            var byRC = new Dictionary<long, TileCatalog.TileEntry>();
            foreach (var info in _tilesetSlotInfo.Values)
                byRC[((long)info.R << 32) | (uint)info.C] = info.Entry;

            var grid = new TileBase[w, h];
            foreach (var p in _tilesetSelectedSlots)
            {
                if (!byRC.TryGetValue(((long)p.y << 32) | (uint)p.x, out var entry)) continue;
                if (entry.transparent) continue;
                int dx = p.x - cMin;
                int dy = (rMax - p.y); // tilesheet row top → highest dy after Y flip
                grid[dx, dy] = entry.tile;
            }

            _state.Clipboard = new TileClipboard
            {
                Tiles        = grid,
                SourceBounds = new BoundsInt(0, 0, 0, w, h, 1),
                SourceLayer  = _state.CurrentLayer,
                IsCut        = false,
            };
            // Snapshot selected slots into the copy-set and repaint the CopyHL overlays.
            _tilesetCopiedSlots.Clear();
            foreach (var p in _tilesetSelectedSlots)
                _tilesetCopiedSlots.Add(p);
            RefreshTilesetCopyVisuals();
            RefreshClipboardButtons();
            SetStatus($"Selected {_tilesetSelectedSlots.Count} tile(s) from picker — V to paste.");

            // For multi-tile picker copies (Rect / Multi with >1 slot), invalidate
            // the map's pending Select-tool selection. Without this, a stale green
            // selection on the map would be picked up by Ctrl+C (OnCopyClicked
            // reads _state.SelectedCells) and overwrite the picker clipboard we
            // just populated, making subsequent Ctrl+V paste the wrong tiles.
            // Single-tile commits (the typical "pick a brush" gesture) leave the
            // map selection alone so brush-then-paint workflows aren't disrupted.
            if (_tilesetSelectedSlots.Count > 1 && TileEditorManager.HasInstance)
                TileEditorManager.Instance.ClearMapSelectionFromPickerCommit();
        }

        /// <summary>
        /// Counterpart of <c>TileEditorManager.ClearMapSelectionFromPickerCommit</c>:
        /// when the map's Copy / Cut succeeds, the picker's green selection +
        /// yellow CopyHL frame represent a stale (older) clipboard source. Wipe
        /// them so the user sees a single active source. Does NOT touch
        /// <see cref="TileEditorState.Clipboard"/> — the map-side caller has just
        /// written the map tiles into it.
        /// </summary>
        public void ClearPickerSelectionFromMapCopy()
        {
            _tilesetSelectedSlots.Clear();
            _tilesetCopiedSlots.Clear();
            _tilesetDragStart = null;
            _tilesetDragEnd   = null;
            RefreshTilesetSelectionVisuals();
            RefreshTilesetCopyVisuals();
        }
    }
}
