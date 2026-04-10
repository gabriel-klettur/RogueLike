using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    public partial class MapEditorUI
    {
        private void RebuildZonesList()
        {
            if (_zonesListContent == null) return;

            for (int i = _zonesListContent.childCount - 1; i >= 0; i--)
                Destroy(_zonesListContent.GetChild(i).gameObject);
            _zoneButtons.Clear();

            for (int i = 0; i < _cachedZones.Length; i++)
            {
                var zone = _cachedZones[i];
                var row = CreatePanel($"Zone_{zone.zoneName}", _zonesListContent, new Color(0.12f, 0.12f, 0.12f, 0.9f));
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);

                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset(8, 8, 4, 4);
                rowLayout.spacing = 6f;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childForceExpandWidth = true;

                string zoneName = zone.zoneName;
                if (_inlineRenameZoneName == zoneName)
                    BuildInlineRenameRow(row.transform, zone);
                else
                    BuildDefaultZoneRow(row.transform, zone);

                var button = row.AddComponent<Button>();
                var colors = button.colors;
                colors.normalColor = zoneName == _state.SelectedZone
                    ? new Color(0.28f, 0.34f, 0.44f, 0.95f)
                    : new Color(0.12f, 0.12f, 0.12f, 0.9f);
                colors.highlightedColor = new Color(0.22f, 0.26f, 0.34f, 0.95f);
                colors.pressedColor = new Color(0.36f, 0.42f, 0.52f, 0.95f);
                colors.selectedColor = colors.normalColor;
                button.colors = colors;
                button.targetGraphic = row.GetComponent<Image>();
                button.onClick.AddListener(() => _onZoneSelected?.Invoke(zoneName));
                _zoneButtons.Add(button);
            }
        }

        private void BuildDefaultZoneRow(Transform parent, ZoneManager.ZoneDefinition zone)
        {
            var text = CreateText("Label", parent,
                $"{zone.zoneName}  [{zone.gridOffset.x},{zone.gridOffset.y}] {(zone.editableInTileEditor ? "EDIT" : "LOCK")}",
                12f,
                zone.editableInTileEditor ? new Color(0.92f, 0.96f, 1f, 1f) : new Color(1f, 0.7f, 0.7f, 1f));
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.rectTransform.sizeDelta = new Vector2(300f, 0f);

            var actions = CreateRow("RowActions", parent, 26f);
            actions.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            actions.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 0f);
            var actionsLayout = actions.AddComponent<LayoutElement>();
            actionsLayout.preferredWidth = 96f;
            actionsLayout.flexibleWidth = 0f;

            CreateMiniActionButton(actions.transform, "R", () =>
            {
                _inlineRenameZoneName = zone.zoneName;
                RebuildZonesList();
            });

            CreateMiniActionButton(actions.transform, "E", () =>
            {
                _onZoneSelected?.Invoke(zone.zoneName);
                _onToggleZoneEditableByName?.Invoke(zone.zoneName);
            });
        }

        private void BuildInlineRenameRow(Transform parent, ZoneManager.ZoneDefinition zone)
        {
            var inputHost = CreatePanel("InlineRenameHost", parent, new Color(0.15f, 0.16f, 0.2f, 1f));
            inputHost.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 28f);
            var inputLayout = inputHost.AddComponent<LayoutElement>();
            inputLayout.preferredWidth = 260f;
            inputLayout.flexibleWidth = 0f;
            var input = CreateInputField(inputHost.transform, "New zone name");
            input.text = zone.zoneName;

            var actions = CreateRow("RenameActions", parent, 26f);
            actions.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            actions.GetComponent<RectTransform>().sizeDelta = new Vector2(96f, 0f);
            var actionsLayout = actions.AddComponent<LayoutElement>();
            actionsLayout.preferredWidth = 96f;
            actionsLayout.flexibleWidth = 0f;

            CreateMiniActionButton(actions.transform, "OK", () =>
            {
                _onRenameZoneByName?.Invoke(zone.zoneName, input.text);
            });

            CreateMiniActionButton(actions.transform, "X", () =>
            {
                _inlineRenameZoneName = null;
                RebuildZonesList();
            });
        }

        private void BuildUI()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("MapEditorCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvasRoot = canvasGo.transform;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 320;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = CreatePanel("MapEditorRoot", canvasGo.transform, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(18f, -18f);
            rootRect.sizeDelta = new Vector2(460f, 690f);

            var rootLayout = _root.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = new RectOffset(12, 12, 12, 12);
            rootLayout.spacing = 8f;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = false;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            CreateText("Title", _root.transform, "MAP EDITOR (F7)", 22f, new Color(1f, 0.84f, 0.45f, 1f), FontStyles.Bold);
            CreateText("Hint", _root.transform,
                "Left-click: select zone / mark Add target | N: Add Zone flow | D: duplicate | Del: delete | R: rename | E: toggle editable",
                12f,
                new Color(0.8f, 0.86f, 0.96f, 1f));

            _selectedZoneText = CreateText("SelectedZone", _root.transform, "Selected: (none)", 14f, Color.white, FontStyles.Bold);
            _selectedEditableText = CreateText("SelectedEditable", _root.transform, "Editable: n/a", 13f, new Color(0.8f, 0.84f, 0.92f, 1f));

            var inputPanel = CreatePanel("InputPanel", _root.transform, new Color(0.09f, 0.09f, 0.1f, 1f));
            inputPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);
            _nameInput = CreateInputField(inputPanel.transform, "Zone name");

            var actionsRow = CreateRow("ActionsRow", _root.transform, 34f);
            CreateActionButton(actionsRow.transform, "Add Zone", () => _onBeginAddZoneFlow?.Invoke());
            CreateActionButton(actionsRow.transform, "Duplicate", () => _onDuplicateSelectedZone?.Invoke());
            CreateActionButton(actionsRow.transform, "Rename", () => _onRenameSelectedZone?.Invoke(NameInput));
            CreateActionButton(actionsRow.transform, "Delete", () => _onRequestDeleteSelectedZone?.Invoke());
            CreateActionButton(actionsRow.transform, "Toggle Editable", () => _onToggleSelectedZoneEditable?.Invoke());

            var moveRow = CreateRow("MoveRow", _root.transform, 34f);
            CreateActionButton(moveRow.transform, "â†", () => _onMoveSelectedZone?.Invoke(Vector2Int.left));
            CreateActionButton(moveRow.transform, "â†‘", () => _onMoveSelectedZone?.Invoke(Vector2Int.up));
            CreateActionButton(moveRow.transform, "â†“", () => _onMoveSelectedZone?.Invoke(Vector2Int.down));
            CreateActionButton(moveRow.transform, "â†’", () => _onMoveSelectedZone?.Invoke(Vector2Int.right));

            var toggleRow = CreateRow("RestrictRow", _root.transform, 28f);
            var toggleLabel = CreateText("RestrictLabel", toggleRow.transform, "Restrict Tile Editor to editable zones", 13f, Color.white);
            toggleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            toggleLabel.rectTransform.sizeDelta = new Vector2(300f, 0f);
            _restrictToggle = CreateToggle(toggleRow.transform);
            _restrictToggle.onValueChanged.AddListener(v => _onRestrictEditChanged?.Invoke(v));

            var listHeader = CreateText("ListTitle", _root.transform, "Zones", 15f, new Color(1f, 0.86f, 0.55f, 1f), FontStyles.Bold);
            listHeader.margin = new Vector4(2f, 2f, 2f, 2f);

            var scroll = CreateScrollView("ZonesScroll", _root.transform, out var content);
            var scrollLayout = scroll.gameObject.AddComponent<LayoutElement>();
            scrollLayout.preferredHeight = 380f;
            _zonesListContent = content;

            _statusText = CreateText("Status", _root.transform, "Ready", 12f, new Color(0.78f, 0.86f, 0.96f, 1f));

            BuildAddZoneDialog();
            BuildDeleteZoneDialog();
        }

        private void BuildAddZoneDialog()
        {
            if (_canvasRoot == null)
                return;

            _addZoneDialog = CreatePanel("AddZoneDialog", _canvasRoot, new Color(0.04f, 0.05f, 0.08f, 0.96f));
            var rect = _addZoneDialog.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(494f, -18f);
            rect.sizeDelta = new Vector2(430f, 270f);

            var layout = _addZoneDialog.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            CreateText("Title", _addZoneDialog.transform, "ADD ZONE", 18f, new Color(1f, 0.88f, 0.56f, 1f), FontStyles.Bold);
            _addZoneSourceText = CreateText("Source", _addZoneDialog.transform, "Source: (none)", 12f, new Color(0.84f, 0.91f, 1f, 1f));
            _addZoneTargetText = CreateText("Target", _addZoneDialog.transform, "Target: click map to mark (50x50)", 12f, new Color(0.84f, 0.91f, 1f, 1f));

            var namePanel = CreatePanel("NamePanel", _addZoneDialog.transform, new Color(0.09f, 0.09f, 0.1f, 1f));
            namePanel.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);
            _addZoneNameInput = CreateInputField(namePanel.transform, "new_zone_name");

            var togglesRow = CreateRow("AddToggles", _addZoneDialog.transform, 30f);
            var tplLabel = CreateText("TplLabel", togglesRow.transform, "Use selected as template", 12f, Color.white);
            tplLabel.rectTransform.sizeDelta = new Vector2(230f, 0f);
            _addUseTemplateToggle = CreateToggle(togglesRow.transform);

            var editableRow = CreateRow("EditableRow", _addZoneDialog.transform, 30f);
            var editableLabel = CreateText("EditableLabel", editableRow.transform, "Editable in tile editor", 12f, Color.white);
            editableLabel.rectTransform.sizeDelta = new Vector2(230f, 0f);
            _addEditableToggle = CreateToggle(editableRow.transform);

            var actionsRow = CreateRow("AddDialogActions", _addZoneDialog.transform, 34f);
            CreateActionButton(actionsRow.transform, "Confirm Add", () =>
            {
                _onConfirmAddZone?.Invoke(
                    _addZoneNameInput != null ? _addZoneNameInput.text : string.Empty,
                    _addUseTemplateToggle != null && _addUseTemplateToggle.isOn,
                    _addEditableToggle != null && _addEditableToggle.isOn);
            });

            CreateActionButton(actionsRow.transform, "Cancel", () =>
            {
                _onCancelAddZoneFlow?.Invoke();
                HideAddZoneDialog();
            });

            _addZoneDialog.SetActive(false);
        }

        private void BuildDeleteZoneDialog()
        {
            if (_canvasRoot == null)
                return;

            _deleteZoneDialog = CreatePanel("DeleteZoneDialog", _canvasRoot, new Color(0.08f, 0.04f, 0.04f, 0.96f));
            var rect = _deleteZoneDialog.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(494f, -300f);
            rect.sizeDelta = new Vector2(430f, 130f);

            var layout = _deleteZoneDialog.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            CreateText("DeleteTitle", _deleteZoneDialog.transform, "CONFIRM DELETE", 16f, new Color(1f, 0.74f, 0.66f, 1f), FontStyles.Bold);
            _deleteZonePrompt = CreateText("DeletePrompt", _deleteZoneDialog.transform, "Delete zone?", 13f, Color.white);

            var actions = CreateRow("DeleteActions", _deleteZoneDialog.transform, 34f);
            CreateActionButton(actions.transform, "Delete", () => _onConfirmDeleteSelectedZone?.Invoke());
            CreateActionButton(actions.transform, "Cancel", HideDeleteZoneDialog);

            _deleteZoneDialog.SetActive(false);
        }

    }
}
