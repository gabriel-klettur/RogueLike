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
            if (_refs.ZonesListContent == null) return;

            for (int i = _refs.ZonesListContent.childCount - 1; i >= 0; i--)
                Destroy(_refs.ZonesListContent.GetChild(i).gameObject);
            _zoneButtons.Clear();

            for (int i = 0; i < _cachedZones.Length; i++)
            {
                var zone = _cachedZones[i];
                var row = CreatePanel($"Zone_{zone.zoneName}", _refs.ZonesListContent, new Color(0.12f, 0.12f, 0.12f, 0.9f));
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

            // Scroll the list to show the selected zone, deferred one frame so
            // ContentSizeFitter has had a chance to resize the content rect.
            if (!string.IsNullOrEmpty(_state?.SelectedZone) && _refs.ZonesScrollRect != null)
                StartCoroutine(ScrollToSelectedZone());
        }

        private System.Collections.IEnumerator ScrollToSelectedZone()
        {
            // Wait one frame for layout to finish calculating content height.
            yield return null;

            var sr = _refs.ZonesScrollRect;
            if (sr == null || _refs.ZonesListContent == null) yield break;

            int selectedIndex = -1;
            for (int i = 0; i < _cachedZones.Length; i++)
            {
                if (_cachedZones[i].zoneName == _state?.SelectedZone)
                {
                    selectedIndex = i;
                    break;
                }
            }
            if (selectedIndex < 0) yield break;

            // Normalized vertical position: 1 = top, 0 = bottom.
            // We want the selected item centred (or at top when near top).
            float total = _cachedZones.Length;
            if (total <= 1f) yield break;

            float targetNorm = 1f - (selectedIndex / (total - 1f));
            sr.verticalNormalizedPosition = Mathf.Clamp01(targetNorm);
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
            _cachedCanvas = canvas;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _refs = MapEditorUIBuilder.BuildAll(
                canvasGo.transform,
                OnDropdownToggle,
                _onConfirmAddZone,
                _onCancelAddZoneFlow,
                _onBeginAddZoneFlow,
                _onDuplicateSelectedZone,
                _onRequestDeleteSelectedZone,
                _onConfirmDeleteSelectedZone,
                HideDeleteZoneDialog,
                _onRenameSelectedZone,
                _onToggleSelectedZoneEditable,
                _onMoveSelectedZone,
                _onRestrictEditChanged);
        }

    }
}
