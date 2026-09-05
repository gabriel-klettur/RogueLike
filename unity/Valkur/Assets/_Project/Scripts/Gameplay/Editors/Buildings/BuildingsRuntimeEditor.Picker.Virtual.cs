using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The template picker's virtualised grid.
    ///
    /// <para>The grid used to be rebuilt in full — every slot destroyed and created again —
    /// on open, on every slot click, on every drag start, on every keystroke in the search
    /// box and on every tab change. With 1474 templates that is 4423 GameObjects and 53 MB
    /// per rebuild, measured at 772 ms plus 185 ms of layout; the same shape as the Items
    /// table freeze, and for the same reason: the cost was volume, for a viewport that shows
    /// eighteen slots.</para>
    ///
    /// <para>Now the content rect is sized to <c>rows x pitch</c> with NO layout group and NO
    /// size fitter, slots are placed by index at absolute positions, and only the rows in the
    /// viewport plus <see cref="PICKER_OVERSCAN_ROWS"/> either side exist — realised from a
    /// pool as the scroll moves and recycled as they leave. Selecting a slot repaints tints
    /// through <see cref="EditorUIHelpers.SetSlotTint"/> and rebuilds nothing.</para>
    ///
    /// <para>uGUI performs no layout in Edit Mode, so a viewport can report zero height: the
    /// fallback window is a fixed row count, never "all rows".</para>
    /// </summary>
    public partial class BuildingsRuntimeEditor
    {
        internal const int   PICKER_COLUMNS               = 3;
        internal const float PICKER_CELL                  = 80f;
        internal const float PICKER_SPACING               = 4f;
        internal const float PICKER_PAD                   = 4f;
        internal const int   PICKER_OVERSCAN_ROWS         = 2;
        internal const int   PICKER_FALLBACK_VISIBLE_ROWS = 6;
        /// <summary>Longest side of a slot thumbnail. The icon is 64 canvas px; 128 keeps headroom for canvas scaling.</summary>
        internal const int   PICKER_THUMB_PX              = 128;

        internal static float PickerRowPitch => PICKER_CELL + PICKER_SPACING;

        private sealed class PickerSlot
        {
            public GameObject      Go;
            public RectTransform   Rt;
            public Button          Btn;
            public Image           Icon;
            public TextMeshProUGUI Label;
            public int             TemplateId = -1;
            public int             Index      = -1;
        }

        private readonly List<BuildingTemplateData>  _pickerVisible      = new List<BuildingTemplateData>();
        private readonly Dictionary<int, PickerSlot> _pickerSlotsByIndex = new Dictionary<int, PickerSlot>();
        private readonly Stack<PickerSlot>           _pickerSlotPool     = new Stack<PickerSlot>();
        private readonly List<int>                   _pickerScratch      = new List<int>();

        private ScrollRect _pickerScroll;
        private int        _pickerFirst = -1;
        private int        _pickerLast  = -1;
        private bool       _pickerVirtualPrepared;

        /// <summary>Templates that pass the category and search gates, in catalog order.</summary>
        internal int PickerVisibleCount => _pickerVisible.Count;

        /// <summary>Slots that currently exist as GameObjects — the window, not the list.</summary>
        internal int PickerRealizedSlotCount => _pickerSlotsByIndex.Count;

        /// <summary>The GameObject realised for a visible index, or null when it is outside the window.</summary>
        internal GameObject PickerSlotObjectAt(int index)
            => _pickerSlotsByIndex.TryGetValue(index, out var slot) ? slot.Go : null;

        /// <summary>Content height that fits <paramref name="count"/> slots at three per row.</summary>
        internal static float PickerContentHeight(int count)
        {
            int rows = (count + PICKER_COLUMNS - 1) / PICKER_COLUMNS;
            if (rows == 0) return 0f;
            return PICKER_PAD * 2f + rows * PICKER_CELL + (rows - 1) * PICKER_SPACING;
        }

        // ── Preparation ─────────────────────────────────────────────────────────

        /// <summary>
        /// Strips the layout components the shared grid factory installs and hooks the
        /// scroll. Idempotent; called from BuildUI and again defensively from RefreshPicker
        /// for the test paths that inject a content rect without building the UI.
        /// </summary>
        private void PreparePickerVirtualization()
        {
            if (_pickerVirtualPrepared || _pickerContent == null) return;
            _pickerVirtualPrepared = true;

            // A layout group stacks whatever slots exist from the top and puts row 150 where
            // row 0 belongs; a size fitter shrinks the content to the realised window and
            // makes the rest unreachable. Both are the obvious thing to "add back".
            var grid = _pickerContent.GetComponent<GridLayoutGroup>();
            if (grid != null) DestroyImmediate(grid);
            var fitter = _pickerContent.GetComponent<ContentSizeFitter>();
            if (fitter != null) DestroyImmediate(fitter);
            var vertical = _pickerContent.GetComponent<VerticalLayoutGroup>();
            if (vertical != null) DestroyImmediate(vertical);

            _pickerContent.anchorMin        = new Vector2(0f, 1f);
            _pickerContent.anchorMax        = new Vector2(1f, 1f);
            _pickerContent.pivot            = new Vector2(0.5f, 1f);
            _pickerContent.anchoredPosition = Vector2.zero;

            if (_pickerScroll == null)
                _pickerScroll = _pickerContent.GetComponentInParent<ScrollRect>(true);
            if (_pickerScroll != null)
            {
                _pickerScroll.onValueChanged.AddListener(_ => UpdatePickerVisibleSlots());
                // One wheel notch moves one row. The default 20 px on a 41,000 px list was
                // two thousand notches from top to bottom.
                _pickerScroll.scrollSensitivity = PickerRowPitch;
            }
        }

        private float ResolvePickerViewportHeight()
        {
            if (_pickerScroll != null && _pickerScroll.viewport != null)
                return _pickerScroll.viewport.rect.height;
            return 0f;
        }

        // ── The list ─────────────────────────────────────────────────────────────

        /// <summary>Category gate, then search gate — the same two gates the old rebuild applied per slot.</summary>
        private void BuildPickerVisibleList()
        {
            _pickerVisible.Clear();
            if (_catalog == null) return;

            string filter = _searchFilter?.Trim().ToLowerInvariant() ?? "";
            foreach (var tmpl in _catalog.Templates)
            {
                if (tmpl == null) continue;
                if (!MatchesCategoryFilter(tmpl)) continue;
                if (filter.Length > 0)
                {
                    string idStr = tmpl.templateId.ToString();
                    string ap    = (tmpl.assetPath ?? "").ToLowerInvariant();
                    if (!idStr.Contains(filter) && !ap.Contains(filter)) continue;
                }
                _pickerVisible.Add(tmpl);
            }
        }

        // ── The window ───────────────────────────────────────────────────────────

        private void UpdatePickerVisibleSlots()
        {
            if (_pickerContent == null) return;

            int count = _pickerVisible.Count;
            if (count == 0)
            {
                RecycleAllPickerSlots();
                _pickerFirst = _pickerLast = -1;
                return;
            }

            float pitch     = PickerRowPitch;
            float viewportH = ResolvePickerViewportHeight();
            int visibleRows = viewportH > 1f
                ? Mathf.CeilToInt(viewportH / pitch) + 1
                : PICKER_FALLBACK_VISIBLE_ROWS;

            // Top-anchored, top pivot: scrolling down moves the content up and
            // anchoredPosition.y grows positive.
            float scrollY = Mathf.Max(0f, _pickerContent.anchoredPosition.y);
            int firstRow  = Mathf.Max(0, Mathf.FloorToInt((scrollY - PICKER_PAD) / pitch) - PICKER_OVERSCAN_ROWS);
            int lastRow   = firstRow + visibleRows + PICKER_OVERSCAN_ROWS * 2;

            int first = Mathf.Clamp(firstRow * PICKER_COLUMNS, 0, count - 1);
            int last  = Mathf.Clamp((lastRow + 1) * PICKER_COLUMNS - 1, 0, count - 1);
            if (first == _pickerFirst && last == _pickerLast) return;

            _pickerScratch.Clear();
            foreach (var kv in _pickerSlotsByIndex)
                if (kv.Key < first || kv.Key > last) _pickerScratch.Add(kv.Key);
            for (int i = 0; i < _pickerScratch.Count; i++)
            {
                RecyclePickerSlot(_pickerSlotsByIndex[_pickerScratch[i]]);
                _pickerSlotsByIndex.Remove(_pickerScratch[i]);
            }

            for (int i = first; i <= last; i++)
            {
                if (_pickerSlotsByIndex.ContainsKey(i)) continue;
                _pickerSlotsByIndex[i] = RealizePickerSlot(i);
            }

            _pickerFirst = first;
            _pickerLast  = last;
        }

        private PickerSlot RealizePickerSlot(int index)
        {
            var tmpl = _pickerVisible[index];
            var slot = _pickerSlotPool.Count > 0 ? _pickerSlotPool.Pop() : CreatePickerSlot();

            slot.Index      = index;
            slot.TemplateId = tmpl.templateId;
            slot.Go.name    = $"B{tmpl.templateId}";

            int row = index / PICKER_COLUMNS;
            int col = index % PICKER_COLUMNS;
            slot.Rt.anchoredPosition = new Vector2(
                PICKER_PAD + col * PickerRowPitch,
                -(PICKER_PAD + row * PickerRowPitch));

            var thumb = SpriteThumbnailCache.Get(tmpl.previewSprite, PICKER_THUMB_PX);
            slot.Icon.sprite  = thumb;
            slot.Icon.enabled = thumb != null;
            slot.Label.text   = $"#{tmpl.templateId}";

            slot.Go.SetActive(true);
            ApplyPickerSlotTint(slot);
            return slot;
        }

        private PickerSlot CreatePickerSlot()
        {
            var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(_pickerContent, "", PICKER_CELL, null);
            var rt = (RectTransform)btn.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(PICKER_CELL, PICKER_CELL);

            var slot = new PickerSlot { Go = btn.gameObject, Rt = rt, Btn = btn, Icon = icon, Label = label };

            // Both callbacks read the slot's CURRENT template rather than capturing an id,
            // because a pooled slot is reused for a different template every time it is
            // realised and a captured id would select whatever it showed first.
            btn.onClick.AddListener(() => { if (slot.TemplateId >= 0) SelectTemplate(slot.TemplateId); });

            // Drag-from-picker: PointerDown arms the drag so LMB-dragging the slot onto the
            // map places the building directly.
            var trigger = btn.gameObject.AddComponent<EventTrigger>();
            var down    = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { if (slot.TemplateId >= 0) OnPickerSlotPointerDown(slot.TemplateId); });
            trigger.triggers.Add(down);

            return slot;
        }

        private void RecyclePickerSlot(PickerSlot slot)
        {
            if (slot == null || slot.Go == null) return;
            slot.TemplateId = -1;
            slot.Index      = -1;
            slot.Go.SetActive(false);
            _pickerSlotPool.Push(slot);
        }

        private void RecycleAllPickerSlots()
        {
            foreach (var kv in _pickerSlotsByIndex) RecyclePickerSlot(kv.Value);
            _pickerSlotsByIndex.Clear();
        }

        // ── Selection tint ───────────────────────────────────────────────────────

        /// <summary>
        /// Repaints the selected/unselected tint on every realised slot. Goes through
        /// <see cref="EditorUIHelpers.SetSlotTint"/>: a colour written straight onto the
        /// Image survives exactly until the Button's next transition, which is why the old
        /// selection highlight vanished on the first hover.
        /// </summary>
        private void RefreshPickerSelection()
        {
            foreach (var kv in _pickerSlotsByIndex) ApplyPickerSlotTint(kv.Value);
        }

        private void ApplyPickerSlotTint(PickerSlot slot)
        {
            if (slot == null || slot.Btn == null) return;
            bool selected = slot.TemplateId >= 0 && slot.TemplateId == _selectedTemplateId;
            EditorUIHelpers.SetSlotTint(slot.Btn, selected ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.SLOT_BG);
        }
    }
}
