using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The Select tool and its two scopes.
    ///
    /// <para><b>Simple</b> is the historical behaviour: one building at a time, a click
    /// replaces the selection. <b>Multiple</b> makes a click TOGGLE membership in a group,
    /// a click on bare ground clear it, and Escape clear it before it closes the editor.
    /// The scope is chosen from a flyout under the Select tool button — the same flyout
    /// shape the Erase tool uses for its scope — and it persists with the workspace.</para>
    ///
    /// <para>THE GROUP HAS ONE PRIMARY AND IT IS <c>_activeBuilding</c>. Every per-instance
    /// surface in this editor — the inspector, the split handle, the resize handle, the
    /// collider brush — reads that one field, and there are eleven writers of it. Rather
    /// than teach each writer about a second list, <see cref="ReconcileSelection"/> runs once
    /// per frame and makes the set agree with the field: whatever set the primary is a
    /// member, whatever nulled it emptied the group. That is why a picker click (which
    /// nulls <c>_activeBuilding</c> directly) drops the whole group without knowing the
    /// group exists.</para>
    ///
    /// <para>What a group DOES: Delete asks once and removes all of them in one undo step,
    /// RMB-drag on any member moves all of them by the same delta in one undo step, and
    /// Ctrl+C / Ctrl+V copy and paste the whole group with their relative layout kept.
    /// What it does NOT do, deliberately: the inspector, split and resize stay on the
    /// primary. A slider that wrote to N buildings at once would need N-way undo per drag
    /// tick and an answer for what "the split ratio" of six different buildings is.</para>
    /// </summary>
    public partial class BuildingsRuntimeEditor
    {
        /// <summary>How a click on a building changes the selection. <c>Area</c> is the
        /// marquee: LMB-drag a box on the map and every building touching it is selected.</summary>
        private enum SelectScope { Simple, Multiple, Area }

        private SelectScope _selectScope = SelectScope.Simple;

        /// <summary>The selection, primary last. Reconciled against <c>_activeBuilding</c>
        /// every frame — see the class summary.</summary>
        private readonly BuildingSelectionSet _selection = new BuildingSelectionSet();

        private GameObject      _selectSubPanel;
        private Image           _selectSimpleBtnImg;
        private Image           _selectMultipleBtnImg;
        private Image           _selectAreaBtnImg;
        private TextMeshProUGUI _selectBtnSubTmp;

        // The marquee. World-space corners (the selection is decided in world units, against
        // building rects) and a canvas Image drawn between their screen projections every
        // frame, so the box stays glued to the map while the camera pans under it.
        private bool          _areaSelecting;
        private Vector2       _areaStartWorld;
        private Vector2       _areaStartScreen;
        private Vector2       _areaEndWorld;
        private GameObject    _areaMarqueeGo;
        private RectTransform _areaMarqueeRt;
        private const float   AREA_CLICK_THRESHOLD_PX = 4f;   // under this the drag was a click
        // Derived from the selection colour rather than authored, so the box and the frame it
        // is about to draw on the buildings can never be two different yellows.
        private static readonly Color AREA_MARQUEE_FILL   = WithAlpha(ACTIVE_YELLOW, 0.14f);
        private static readonly Color AREA_MARQUEE_BORDER = WithAlpha(ACTIVE_YELLOW, 0.95f);

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>True while a box is being dragged. Other map gestures yield to it.</summary>
        internal bool IsAreaSelecting => _areaSelecting;

        // Outlines for every group member EXCEPT the primary, which keeps the thick
        // active outline. Its own pool: the same-template and erase pools carry other
        // meanings (orange), and sharing one would make a colour say two things.
        private readonly List<BuildingOutlineRenderer> _multiSelectFxPool = new List<BuildingOutlineRenderer>();
        private const float MULTI_SELECT_THICKNESS_WORLD = 0.10f;   // ~3 px @ PPU 32, thinner than the primary's 0.15

        // Group move: the members being dragged and where each started, captured on the
        // RMB press so the undo record can put every one of them back.
        private readonly List<BuildingObject> _dragGroup      = new List<BuildingObject>();
        private readonly List<Vector3>        _dragGroupStart = new List<Vector3>();

        internal int SelectionCount => _selection.Count;

        // ── Tool button + flyout ─────────────────────────────────────────────────

        private void OnSelectButtonClicked()
        {
            if (_mode != EditorMode.Select) SetMode(EditorMode.Select);

            bool open = _selectSubPanel != null && _selectSubPanel.activeSelf;
            if (open) HideSelectSubPanel();
            else      ShowSelectSubPanel();
            RefreshSelectScopeHighlights();
        }

        private void ShowSelectSubPanel()
        {
            if (_selectSubPanel == null) return;
            _selectSubPanel.SetActive(true);
            _selectSubPanel.transform.SetAsLastSibling();
        }

        private void HideSelectSubPanel()
        {
            if (_selectSubPanel != null) _selectSubPanel.SetActive(false);
        }

        /// <summary>Pick a scope from the flyout. The flyout closes: it is a menu, not a
        /// mode panel — the chosen scope is printed on the tool button from then on.</summary>
        private void OnSelectScopeChosen(SelectScope scope)
        {
            SetSelectScope(scope);
            HideSelectSubPanel();
            if (_mode != EditorMode.Select) SetMode(EditorMode.Select);
            Toast(scope switch
            {
                SelectScope.Multiple => "Select: MULTIPLE. Click buildings to add or remove them from the group; click empty ground or press Escape to clear it.",
                SelectScope.Area     => "Select: AREA. LMB-drag a box on the map to select every building it touches. Shift+drag adds to the group; a plain click picks one.",
                _                    => "Select: SIMPLE. Click a building to select it.",
            });
        }

        /// <summary>The scope change itself, without the UI verbs, so the workspace restore
        /// and the tests can reach it.</summary>
        private void SetSelectScope(SelectScope scope)
        {
            _selectScope = scope;
            // Narrowing the scope keeps the primary and drops the rest: the author picked
            // "one at a time" and is looking at one thing.
            if (scope == SelectScope.Simple) _selection.CollapseTo(_activeBuilding);
            if (scope != SelectScope.Area)   CancelAreaSelect();
            RefreshSelectScopeHighlights();
        }

        private void RefreshSelectScopeHighlights()
        {
            if (_selectBtnSubTmp != null)
                _selectBtnSubTmp.text = _selectScope.ToString();
            if (_selectSimpleBtnImg)
                _selectSimpleBtnImg.color = _selectScope == SelectScope.Simple
                    ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_selectMultipleBtnImg)
                _selectMultipleBtnImg.color = _selectScope == SelectScope.Multiple
                    ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_selectAreaBtnImg)
                _selectAreaBtnImg.color = _selectScope == SelectScope.Area
                    ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
        }

        // ── The click ────────────────────────────────────────────────────────────

        /// <summary>
        /// A bare left click on the map in Select mode. <paramref name="hovered"/> is the
        /// building under the cursor, or null for open ground.
        /// </summary>
        private void HandleSelectClick(BuildingObject hovered)
        {
            // Ground, in EVERY scope: nothing under the cursor means nothing selected. It
            // used to keep the selection in Simple scope, which left an author with no
            // mouse-only way to deselect - the yellow frame stayed on a building until
            // another was clicked.
            if (hovered == null)
            {
                if (_activeBuilding != null || _selection.Count > 0) ClearSelection("Selection cleared.");
                return;
            }

            // Simple, or an Area click that never became a drag: one building, replacing
            // whatever was selected. Shift+click in Area scope toggles instead - the same
            // modifier that makes a box additive.
            bool singlePick = _selectScope == SelectScope.Simple
                           || (_selectScope == SelectScope.Area && !Valkur.Core.Input.KeyboardInputManager.IsShiftHeld());
            if (singlePick)
            {
                _selection.CollapseTo(hovered);
                SetActiveBuilding(hovered);
                return;
            }

            if (_selection.Contains(hovered))
            {
                _selection.Remove(hovered);
                // The primary may have been the one removed; whoever is last now takes over.
                SetActiveBuilding(_selection.Primary);
            }
            else
            {
                _selection.Add(hovered);
                SetActiveBuilding(hovered);
            }
            ReportSelectionCount();
        }

        private void ClearSelection(string status)
        {
            _selection.Clear();
            SetActiveBuilding(null);
            if (!string.IsNullOrEmpty(status)) Toast(status);
        }

        private void ReportSelectionCount()
        {
            if (_statusTmp == null) return;
            int n = _selection.Count;
            if (n > 1)
                _statusTmp.text = $"{n} buildings selected — primary ID {(_activeBuilding != null ? _activeBuilding.InstanceId : -1)}. " +
                                  "Delete, RMB-drag and Ctrl+C act on all of them.";
        }

        // ── Per-frame reconcile + outlines ───────────────────────────────────────

        /// <summary>
        /// Make the set agree with <c>_activeBuilding</c>, then draw it. Called from
        /// <c>Update</c> after the outline pass, so a group never outlives the thing that
        /// selected it by more than one frame whichever of the eleven writers moved it.
        /// </summary>
        private void UpdateSelectionState()
        {
            ReconcileSelection();
            UpdateMultiSelectFx();
        }

        private void ReconcileSelection()
        {
            _selection.Prune();

            if (_activeBuilding == null)
            {
                if (_selection.Count > 0) _selection.Clear();
                return;
            }

            if (_selectScope == SelectScope.Simple)
            {
                if (_selection.Count != 1 || _selection.Primary != _activeBuilding)
                    _selection.CollapseTo(_activeBuilding);
                return;
            }

            // Multiple: whatever set the primary is a member and IS the primary.
            if (_selection.Primary != _activeBuilding) _selection.Add(_activeBuilding);
        }

        private void UpdateMultiSelectFx()
        {
            // Secondaries only: the primary is drawn by _activeFx at full weight.
            int wanted = _buildingsVisible ? Mathf.Max(0, _selection.Count - 1) : 0;

            while (_multiSelectFxPool.Count < wanted)
            {
                var go = new GameObject("BuildingsEditor.MultiSelectFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<BuildingOutlineRenderer>();
                fx.Configure(ACTIVE_YELLOW, MULTI_SELECT_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
                _multiSelectFxPool.Add(fx);
            }

            var items = _selection.Items;
            int fxIndex = 0;
            for (int i = 0; i < items.Count && fxIndex < wanted; i++)
            {
                var b = items[i];
                if (b == null || b == _activeBuilding) continue;
                _multiSelectFxPool[fxIndex].Follow(b);
                _multiSelectFxPool[fxIndex].SetVisible(true);
                fxIndex++;
            }
            for (; fxIndex < _multiSelectFxPool.Count; fxIndex++)
            {
                if (_multiSelectFxPool[fxIndex] == null) continue;
                _multiSelectFxPool[fxIndex].Follow(null);
                _multiSelectFxPool[fxIndex].SetVisible(false);
            }
        }

        private void HideMultiSelectOutlines()
        {
            for (int i = 0; i < _multiSelectFxPool.Count; i++)
            {
                if (_multiSelectFxPool[i] == null) continue;
                _multiSelectFxPool[i].Follow(null);
                _multiSelectFxPool[i].SetVisible(false);
            }
        }

        // ── The marquee ──────────────────────────────────────────────────────────

        /// <summary>LMB went down on the map in Area scope. Nothing is selected yet: a
        /// press is a drag until it is released, and a release inside the click threshold
        /// is handed to <see cref="HandleSelectClick"/> instead.</summary>
        private void BeginAreaSelect(Vector3 worldPos, Vector2 screenPos)
        {
            _areaSelecting   = true;
            _areaStartWorld  = worldPos;
            _areaEndWorld    = worldPos;
            _areaStartScreen = screenPos;
            EnsureAreaMarquee();
            UpdateAreaMarqueeVisual();
        }

        private void UpdateAreaSelect(Vector3 worldPos)
        {
            _areaEndWorld = worldPos;
            UpdateAreaMarqueeVisual();
        }

        /// <summary>LMB released. Decide between a click and a box, then select.</summary>
        private void FinalizeAreaSelect(Vector2 screenPos)
        {
            bool wasClick = (screenPos - _areaStartScreen).sqrMagnitude
                            < AREA_CLICK_THRESHOLD_PX * AREA_CLICK_THRESHOLD_PX;
            Rect area = BuildingAreaQuery.FromCorners(_areaStartWorld, _areaEndWorld);
            CancelAreaSelect();

            if (wasClick) { HandleSelectClick(_hoveredBuilding); return; }

            SelectFromArea(area, additive: Valkur.Core.Input.KeyboardInputManager.IsShiftHeld());
        }

        /// <summary>Drop the box without selecting anything. Escape, a scope change and
        /// Deactivate all land here.</summary>
        private void CancelAreaSelect()
        {
            _areaSelecting = false;
            if (_areaMarqueeGo != null) _areaMarqueeGo.SetActive(false);
        }

        /// <summary>
        /// The selection half, separate from the gesture so the tests and the console can
        /// box-select without a pointer. <paramref name="additive"/> keeps what was selected
        /// and adds the hits; otherwise the hits ARE the selection. The last hit becomes
        /// primary; an empty box clears (or, additive, changes nothing).
        /// </summary>
        internal int SelectFromArea(Rect area, bool additive)
        {
            var hits = BuildingAreaQuery.Overlapping(GetCachedBuildings(), area);

            if (!additive)
            {
                if (hits.Count == 0)
                {
                    if (_selection.Count > 0) ClearSelection("Selection cleared.");
                    return 0;
                }
                _selection.Clear();
            }
            for (int i = 0; i < hits.Count; i++) _selection.Add(hits[i]);

            var primary = _selection.Primary;
            SetActiveBuilding(primary);
            if (hits.Count > 0)
                Toast($"{hits.Count} building{(hits.Count == 1 ? "" : "s")} in the box — {_selection.Count} selected.");
            return hits.Count;
        }

        private void EnsureAreaMarquee()
        {
            if (_areaMarqueeGo != null || _canvas == null) return;
            _areaMarqueeGo = EditorUIHelpers.CreateUI("AreaSelectMarquee", _canvas.transform);
            _areaMarqueeRt = _areaMarqueeGo.GetComponent<RectTransform>();
            _areaMarqueeRt.anchorMin = _areaMarqueeRt.anchorMax = new Vector2(0.5f, 0.5f);
            _areaMarqueeRt.pivot     = new Vector2(0.5f, 0.5f);
            var img = _areaMarqueeGo.AddComponent<Image>();
            img.color         = AREA_MARQUEE_FILL;
            img.raycastTarget = false;   // a box that ate the release would never finish
            var border = _areaMarqueeGo.AddComponent<Outline>();
            border.effectColor    = AREA_MARQUEE_BORDER;
            border.effectDistance = new Vector2(1.5f, -1.5f);
            var cg = _areaMarqueeGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
        }

        /// <summary>Re-project the world corners every frame: the camera may pan while the
        /// button is held, and the box must stay on the ground it was drawn on.</summary>
        private void UpdateAreaMarqueeVisual()
        {
            if (_areaMarqueeRt == null) return;
            var cam = Camera.main;
            if (cam == null) { _areaMarqueeGo.SetActive(false); return; }
            Rect area = BuildingAreaQuery.FromCorners(_areaStartWorld, _areaEndWorld);
            Vector2 a = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(area.xMin, area.yMin, 0f)));
            Vector2 b = ScreenToCanvasPos(cam.WorldToScreenPoint(new Vector3(area.xMax, area.yMax, 0f)));
            _areaMarqueeGo.SetActive(true);
            _areaMarqueeGo.transform.SetAsLastSibling();
            _areaMarqueeRt.anchoredPosition = (a + b) * 0.5f;
            _areaMarqueeRt.sizeDelta        = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        }

        // ── What the group operations act on ─────────────────────────────────────

        /// <summary>
        /// The buildings a group verb (delete, move, copy) should touch: the whole group
        /// when there is one, otherwise the primary alone, otherwise nothing. Pruned first,
        /// primary LAST — a caller that wants an anchor takes the last element.
        /// </summary>
        private List<BuildingObject> GroupTargets()
        {
            _selection.Prune();
            var list = new List<BuildingObject>();
            if (_selection.Count > 1)
            {
                var items = _selection.Items;
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null) list.Add(items[i]);
                return list;
            }
            if (_activeBuilding != null) list.Add(_activeBuilding);
            return list;
        }

        // ── Group delete ─────────────────────────────────────────────────────────

        private void RequestDeleteGroupWithConfirm(List<BuildingObject> group)
        {
            if (group == null || group.Count == 0) return;
            if (group.Count == 1) { RequestDeleteWithConfirm(group[0]); return; }

            string msg = $"Delete {group.Count} selected buildings?\n\n" +
                         "This removes every building in the group in one step; Ctrl+Z brings all of them back.";
            ShowConfirm(msg, () => DeleteBuildings(group));
        }

        /// <summary>
        /// Deactivate every building in <paramref name="group"/> as ONE undo record. The
        /// same deactivate-not-destroy contract <c>DeleteBuilding</c> uses, so undo is a
        /// reactivation and the instance ids survive.
        /// </summary>
        private void DeleteBuildings(List<BuildingObject> group)
        {
            var targets = new List<GameObject>(group.Count);
            for (int i = 0; i < group.Count; i++)
                if (group[i] != null) targets.Add(group[i].gameObject);
            if (targets.Count == 0) return;

            ExecutePersistedEdit($"Delete {targets.Count} buildings",
                () =>
                {
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i]) targets[i].SetActive(false);
                    _selection.Clear();
                    _activeBuilding = null;
                    _propertiesMode = PropertiesMode.None;
                    InvalidateBuildingCache();
                    RefreshInspector();
                    if (_collidersVisible) RefreshCollidersOverlay();
                },
                () =>
                {
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i]) targets[i].SetActive(true);
                    InvalidateBuildingCache();
                    if (_collidersVisible) RefreshCollidersOverlay();
                });
            Toast($"Deleted {targets.Count} buildings.");
        }

        // ── Group move ───────────────────────────────────────────────────────────

        /// <summary>Begin an RMB drag. With a group, every member rides along; the drag
        /// offset stays relative to the PRIMARY so the maths is the single-building one.</summary>
        private void BeginMoveDrag(Vector3 worldPos)
        {
            _dragging          = true;
            _dragStartWorldPos = _activeBuilding.transform.position;
            _dragOffset        = _activeBuilding.transform.position - worldPos;

            _dragGroup.Clear();
            _dragGroupStart.Clear();
            var group = GroupTargets();
            if (group.Count <= 1) return;
            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] == null || group[i] == _activeBuilding) continue;
                _dragGroup.Add(group[i]);
                _dragGroupStart.Add(group[i].transform.position);
            }
        }

        /// <summary>Called every frame of a drag after the primary has been placed: move the
        /// rest of the group by the same delta.</summary>
        private void ApplyGroupDragDelta()
        {
            if (_dragGroup.Count == 0 || _activeBuilding == null) return;
            Vector3 delta = _activeBuilding.transform.position - _dragStartWorldPos;
            for (int i = 0; i < _dragGroup.Count; i++)
            {
                var b = _dragGroup[i];
                if (b == null) continue;
                b.transform.position = _dragGroupStart[i] + delta;
                b.RefreshSorting();
            }
        }

        /// <summary>The group half of <c>FinalizeMoveDrag</c>: one undo record for every
        /// member. Returns false when there was no group, so the caller runs its
        /// single-building path unchanged.</summary>
        private bool TryFinalizeGroupMove(Vector3 primaryStart, Vector3 primaryFinal)
        {
            if (_dragGroup.Count == 0) return false;

            var members = new List<BuildingObject>(_dragGroup.Count + 1) { _activeBuilding };
            var starts  = new List<Vector3>(_dragGroup.Count + 1) { primaryStart };
            var finals  = new List<Vector3>(_dragGroup.Count + 1) { primaryFinal };
            Vector3 delta = primaryFinal - primaryStart;
            for (int i = 0; i < _dragGroup.Count; i++)
            {
                if (_dragGroup[i] == null) continue;
                members.Add(_dragGroup[i]);
                starts.Add(_dragGroupStart[i]);
                finals.Add(_dragGroupStart[i] + delta);
            }
            _dragGroup.Clear();
            _dragGroupStart.Clear();

            if (delta.sqrMagnitude <= 0.0001f) return true;

            ExecutePersistedEdit($"Move {members.Count} buildings",
                () => PlaceGroup(members, finals, $"Move saved -> {members.Count} buildings by ({delta.x:F2}, {delta.y:F2})"),
                () => PlaceGroup(members, starts, $"Move reverted -> {members.Count} buildings"));
            return true;
        }

        private void PlaceGroup(List<BuildingObject> members, List<Vector3> positions, string status)
        {
            for (int i = 0; i < members.Count; i++)
            {
                var b = members[i];
                if (b == null) continue;
                b.transform.position = positions[i];
                b.RefreshSorting();
                BuildingDoorFactory.RefreshGeometry(b);
            }
            RefreshInspector();
            if (_statusTmp != null) _statusTmp.text = status;
        }
    }
}
