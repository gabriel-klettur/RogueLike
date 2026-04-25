using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("BuildingsEditorCanvas", 109);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _uiRefs = BuildingsEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle:  ToggleDropdown,
                onUndo:            () => _undo.Undo(),
                onRedo:            () => _undo.Redo(),
                onSave:            () => SaveInstancesToJson(),
                onReload:          () => ReloadFromJson(),
                onModeSelect:      () => SetMode(EditorMode.Select),
                onModePlace:       () => SetMode(EditorMode.Place),
                onModeResize:      () => SetMode(EditorMode.Resize),
                onModeDelete:      () => SetMode(EditorMode.Delete),
                onAddBuilding:     () => OnAddBuildingClicked(),
                onRemoveBuilding:  () => ToggleRemoveMode(),
                onAddOnSystem:     () => OnAddOnSystemClicked(),
                onToggleTutorial:  () => ToggleTutorial(),
                onSearchChanged:   v  => { _searchFilter = v ?? ""; RefreshPicker(); },
                onSplitChanged:    f  => OnSplitSliderChanged(f),
                onZBottomMinus:    () => AdjustZ(_activeBuilding, bottom: true,  delta: -1),
                onZBottomPlus:     () => AdjustZ(_activeBuilding, bottom: true,  delta: +1),
                onZTopMinus:       () => AdjustZ(_activeBuilding, bottom: false, delta: -1),
                onZTopPlus:        () => AdjustZ(_activeBuilding, bottom: false, delta: +1),
                onColliderScope:   () => ToggleColliderScope(),
                onPaintSolid:      () => SetCollBrushMode(CollBrushMode.Solid),
                onPaintWalk:       () => SetCollBrushMode(CollBrushMode.Walk),
                onSaveCU:          () => SaveColliderAuthoring(),
                onDeleteBuilding:  () => RequestDeleteActiveWithConfirm(),
                onResetBuilding:   () => ResetActiveBuilding(),
                // Colliders panel callbacks (redesigned: ON/OFF + #/. action + scope)
                onToggleCollidersVisible: () => ToggleCollidersVisible(),
                onCollScopeToggle:        () => ToggleColliderScope(),
                onBrushPaint:                () => SetBrushAction(CollBrushMode.Solid),
                onBrushErase:                () => SetBrushAction(CollBrushMode.Walk),
                onCollBrushSizeChanged:      v  => OnCollBrushSizeChanged(v),
                onCollBrushSizeStepDown:     () => OnCollBrushSizeChanged(_collBrushSize - 1),
                onCollBrushSizeStepUp:       () => OnCollBrushSizeChanged(_collBrushSize + 1),
                onPerfToggle:                () => TogglePerfProbe());

            // Wire panel close callbacks to keep dropdown state in sync
            if (_uiRefs.ModesPanelDrag     != null)
                _uiRefs.ModesPanelDrag.OnClose     = () => { _openDropdowns.Remove("modes");     RefreshMenuBtnHighlights(); };
            if (_uiRefs.BuildingsPanelDrag != null)
                _uiRefs.BuildingsPanelDrag.OnClose = () => { _openDropdowns.Remove("buildings"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.CollidersPanelDrag != null)
                _uiRefs.CollidersPanelDrag.OnClose = () => { _openDropdowns.Remove("colliders"); RefreshMenuBtnHighlights(); };
            if (_uiRefs.PropsPanelDrag     != null)
                _uiRefs.PropsPanelDrag.OnClose     = () => { _openDropdowns.Remove("props");     RefreshMenuBtnHighlights(); };

            // Map builder refs to private fields so all downstream logic is unchanged
            _pickerContent = _uiRefs.PickerContent;
            _statusTmp     = _uiRefs.StatusText;
            _searchBox     = _uiRefs.SearchBox;
            _propsTmp      = _uiRefs.PropsText;
            _inspectorRoot = _uiRefs.InspectorRoot;
            _splitSlider   = _uiRefs.SplitSlider;
            _zBottomVal    = _uiRefs.ZBottomVal;
            _zTopVal       = _uiRefs.ZTopVal;
            _scopeBtnImg   = _uiRefs.ScopeBtnImg;
            _scopeBtnLabel = _uiRefs.ScopeBtnLabel;
            _selectBtnImg  = _uiRefs.SelectBtnImg;
            _placeBtnImg   = _uiRefs.PlaceBtnImg;
            _resizeBtnImg  = _uiRefs.ResizeBtnImg;
            _deleteBtnImg  = _uiRefs.DeleteBtnImg;
            _addBtnImg     = _uiRefs.AddBtnImg;
            _removeBtnImg  = _uiRefs.RemoveBtnImg;

            BuildFloatingHandles();
            BuildIdLabel();
            BuildZBadges();
            BuildSplitLine();
            BuildTutorial();
            BuildConfirmModal();
            CreatePerfProbe();

            OpenAllPanels();
            RefreshBrushButtonHighlights();
            RefreshCollidersPanel();
        }

        // ── Dropdown / panel management ────────────────────────────────────────────

        private void ToggleDropdown(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (_openDropdowns.Contains(name))
            {
                SetDropdownOpen(name, false);
                _openDropdowns.Remove(name);
            }
            else
            {
                SetDropdownOpen(name, true);
                _openDropdowns.Add(name);
            }
            RefreshMenuBtnHighlights();
        }

        private void OpenAllPanels()
        {
            foreach (var n in new[] { "modes", "buildings", "colliders", "props" })
            {
                SetDropdownOpen(n, true);
                _openDropdowns.Add(n);
            }
            RefreshMenuBtnHighlights();
        }

        private void SetDropdownOpen(string name, bool open)
        {
            var go = name switch
            {
                "modes"     => _uiRefs.ModesDropdown,
                "buildings" => _uiRefs.BuildingsDropdown,
                "colliders" => _uiRefs.CollidersDropdown,
                "props"     => _uiRefs.PropsDropdown,
                _           => null
            };
            go?.SetActive(open);
        }

        private void RefreshMenuBtnHighlights()
        {
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.ModesMenuBtnImg,     _uiRefs.ModesMenuBtnTmp,     _openDropdowns.Contains("modes"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.BuildingsMenuBtnImg, _uiRefs.BuildingsMenuBtnTmp, _openDropdowns.Contains("buildings"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.CollidersMenuBtnImg, _uiRefs.CollidersMenuBtnTmp, _openDropdowns.Contains("colliders"));
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.PropsMenuBtnImg,     _uiRefs.PropsMenuBtnTmp,     _openDropdowns.Contains("props"));
        }

        /// <summary>
        /// Floating overlay handle: only R (resize) remains — floats at the top-right of the
        /// active building. Delete and Reset have been moved to the Properties inspector panel.
        /// LMB-press+drag on the R handle resizes the building proportionally.
        /// </summary>
        private void BuildFloatingHandles()
        {
            // Container: pivot = (1,1) → top-right of badge anchors to building top-right corner,
            // so the badge sits inside the yellow selection frame at the top-right corner.
            _handlesRoot = EditorUIHelpers.CreateUI("FloatingHandles", _root.transform);
            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(1f, 0f);  // bottom-right → sits ABOVE frame at top-right
            rt.sizeDelta = new Vector2(32f, 32f); // updated proportionally each frame

            // Badge button: dark semi-transparent background + gold Outline (matches selection frame)
            var btnGo = EditorUIHelpers.CreateUI("BtnR", _handlesRoot.transform);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = Vector2.zero;
            btnRt.anchorMax = Vector2.one;
            btnRt.offsetMin = btnRt.offsetMax = Vector2.zero;

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.10f, 0.10f, 0.14f, 0.92f);

            _handleR = btnGo.AddComponent<Button>();
            var colors = _handleR.colors;
            colors.normalColor      = new Color(0.10f, 0.10f, 0.14f, 0.92f);
            colors.highlightedColor = new Color(0.90f, 0.76f, 0.38f, 0.22f); // gold hover glow
            colors.pressedColor     = EditorUIHelpers.BTN_ACTIVE;             // gold on press
            colors.selectedColor    = new Color(0.10f, 0.10f, 0.14f, 0.92f);
            colors.fadeDuration     = 0.08f;
            _handleR.colors = colors;
            _handleR.targetGraphic = img;

            // Gold border — visually ties the badge to the yellow selection outline
            var ol = btnGo.AddComponent<Outline>();
            ol.effectColor    = new Color(0.90f, 0.76f, 0.38f, 0.85f);
            ol.effectDistance = new Vector2(1.5f, -1.5f);

            // "R" label in bold ACCENT gold, auto-sized to fit the badge
            var labelGo = EditorUIHelpers.CreateUI("Lbl", btnGo.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text             = "R";
            tmp.fontStyle        = FontStyles.Bold;
            tmp.color            = EditorUIHelpers.ACCENT;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin      = 8f;
            tmp.fontSizeMax      = 18f;
            tmp.overflowMode     = TextOverflowModes.Overflow;

            // EventTrigger: PointerDown starts the resize drag immediately (onClick fires on
            // release, which is too late for drag-distance tracking).
            var trigger = btnGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry   = new UnityEngine.EventSystems.EventTrigger.Entry
            {
                eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown
            };
            entry.callback.AddListener(_ =>
            {
                if (_activeBuilding != null)
                    _pendingResizeStart = true;
            });
            trigger.triggers.Add(entry);

            _handlesRoot.SetActive(false);
        }

        /// <summary>
        /// Horizontal cyan bar drawn at the split-ratio cut point of the active building.
        /// Mirrors Python split_tool_view.py: 3 px bar + centered draggable handle.
        /// The handle (10×10 square) can be dragged vertically to change split ratio.
        /// </summary>
        private void BuildSplitLine()
        {
            // Bar — 3 px high, width updated each frame
            var go = EditorUIHelpers.CreateUI("SplitLine", _root.transform);
            _splitLineRt = go.GetComponent<RectTransform>();
            _splitLineRt.anchorMin = _splitLineRt.anchorMax = new Vector2(0.5f, 0.5f);
            _splitLineRt.pivot = new Vector2(0.5f, 0.5f);
            _splitLineRt.sizeDelta = new Vector2(80f, 3f);  // width updated each frame
            _splitLineImg = go.AddComponent<Image>();
            _splitLineImg.color = new Color(0f, 200f / 255f, 1f, 0.85f); // cyan #00C8FF
            go.SetActive(false);

            // Handle — 24×8 wide bar at center; wider shape suggests horizontal draggability
            var hgo = EditorUIHelpers.CreateUI("SplitHandle", _root.transform);
            _splitHandleRt = hgo.GetComponent<RectTransform>();
            _splitHandleRt.anchorMin = _splitHandleRt.anchorMax = new Vector2(0.5f, 0.5f);
            _splitHandleRt.pivot = new Vector2(0.5f, 0.5f);
            _splitHandleRt.sizeDelta = new Vector2(24f, 8f);
            _splitHandleImg = hgo.AddComponent<Image>();
            _splitHandleImg.color = new Color(0f, 200f / 255f, 1f, 1f); // solid cyan
            hgo.SetActive(false);
        }

        private void BuildIdLabel()
        {
            var go = EditorUIHelpers.CreateUI("IdLabel", _root.transform);
            _idLabelRt = go.GetComponent<RectTransform>();
            _idLabelRt.anchorMin = _idLabelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _idLabelRt.pivot = new Vector2(0f, 0f);  // bottom-left anchor → sits ABOVE the frame top edge
            _idLabelRt.sizeDelta = new Vector2(80f, 20f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            var labelGo = EditorUIHelpers.CreateUI("Text", go.transform);
            EditorUIHelpers.StretchFill(labelGo);
            _idLabelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            _idLabelTmp.text = "ID -";
            _idLabelTmp.fontSize = 13f;
            _idLabelTmp.fontStyle = FontStyles.Bold;
            _idLabelTmp.alignment = TextAlignmentOptions.Center;
            _idLabelTmp.color = ACTIVE_YELLOW;
            go.SetActive(false);
        }

    }
}