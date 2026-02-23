using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Runtime UI for the map editor.
    /// Focuses on zone CRUD + editable-zone control for tile editor permissions.
    /// </summary>
    public class MapEditorUI : MonoBehaviour
    {
        private MapEditorState _state;

        private System.Action<string> _onZoneSelected;
        private System.Action _onAddZoneAtCursor;
        private System.Action _onDeleteSelectedZone;
        private System.Action<string> _onRenameSelectedZone;
        private System.Action _onToggleSelectedZoneEditable;
        private System.Action<Vector2Int> _onMoveSelectedZone;
        private System.Action<bool> _onRestrictEditChanged;

        private GameObject _root;
        private Transform _zonesListContent;
        private readonly List<Button> _zoneButtons = new List<Button>();

        private TMP_Text _selectedZoneText;
        private TMP_Text _selectedEditableText;
        private TMP_Text _statusText;
        private TMP_InputField _nameInput;
        private Toggle _restrictToggle;

        private static Sprite _whiteSprite;

        public string NameInput => _nameInput != null ? _nameInput.text : string.Empty;
        public bool IsTypingName => _nameInput != null && _nameInput.isFocused;

        public void Initialize(
            MapEditorState state,
            System.Action<string> onZoneSelected,
            System.Action onAddZoneAtCursor,
            System.Action onDeleteSelectedZone,
            System.Action<string> onRenameSelectedZone,
            System.Action onToggleSelectedZoneEditable,
            System.Action<Vector2Int> onMoveSelectedZone,
            System.Action<bool> onRestrictEditChanged)
        {
            _state = state;
            _onZoneSelected = onZoneSelected;
            _onAddZoneAtCursor = onAddZoneAtCursor;
            _onDeleteSelectedZone = onDeleteSelectedZone;
            _onRenameSelectedZone = onRenameSelectedZone;
            _onToggleSelectedZoneEditable = onToggleSelectedZoneEditable;
            _onMoveSelectedZone = onMoveSelectedZone;
            _onRestrictEditChanged = onRestrictEditChanged;

            BuildUI();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null)
                _root.SetActive(visible);
        }

        public void RefreshZones(ZoneManager.ZoneDefinition[] zones)
        {
            if (_zonesListContent == null) return;

            for (int i = _zonesListContent.childCount - 1; i >= 0; i--)
                Destroy(_zonesListContent.GetChild(i).gameObject);
            _zoneButtons.Clear();

            for (int i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var row = CreatePanel($"Zone_{zone.zoneName}", _zonesListContent, new Color(0.12f, 0.12f, 0.12f, 0.9f));
                row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);

                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset(8, 8, 4, 4);
                rowLayout.spacing = 8f;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childForceExpandWidth = true;

                var text = CreateText("Label", row.transform,
                    $"{zone.zoneName}  [{zone.gridOffset.x},{zone.gridOffset.y}] {(zone.editableInTileEditor ? "EDIT" : "LOCK")}",
                    13f,
                    zone.editableInTileEditor ? new Color(0.92f, 0.96f, 1f, 1f) : new Color(1f, 0.7f, 0.7f, 1f));
                text.alignment = TextAlignmentOptions.MidlineLeft;

                var button = row.AddComponent<Button>();
                var colors = button.colors;
                colors.normalColor = zone.zoneName == _state.SelectedZone
                    ? new Color(0.28f, 0.34f, 0.44f, 0.95f)
                    : new Color(0.12f, 0.12f, 0.12f, 0.9f);
                colors.highlightedColor = new Color(0.22f, 0.26f, 0.34f, 0.95f);
                colors.pressedColor = new Color(0.36f, 0.42f, 0.52f, 0.95f);
                colors.selectedColor = colors.normalColor;
                button.colors = colors;
                button.targetGraphic = row.GetComponent<Image>();

                string zoneName = zone.zoneName;
                button.onClick.AddListener(() => _onZoneSelected?.Invoke(zoneName));
                _zoneButtons.Add(button);
            }
        }

        public void SetSelectedZone(string zoneName, bool editable)
        {
            if (_selectedZoneText != null)
                _selectedZoneText.text = string.IsNullOrWhiteSpace(zoneName)
                    ? "Selected: (none)"
                    : $"Selected: {zoneName}";

            if (_selectedEditableText != null)
            {
                _selectedEditableText.text = string.IsNullOrWhiteSpace(zoneName)
                    ? "Editable: n/a"
                    : $"Editable: {(editable ? "YES" : "NO")}";
                _selectedEditableText.color = editable
                    ? new Color(0.62f, 1f, 0.62f, 1f)
                    : new Color(1f, 0.62f, 0.62f, 1f);
            }

            if (_nameInput != null && !string.IsNullOrWhiteSpace(zoneName))
                _nameInput.text = zoneName;
        }

        public void SetRestrictToggle(bool restrict)
        {
            if (_restrictToggle != null)
                _restrictToggle.SetIsOnWithoutNotify(restrict);
        }

        public void SetStatus(string text)
        {
            if (_statusText != null)
                _statusText.text = text;
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

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 320;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = CreatePanel("MapEditorRoot", canvasGo.transform, new Color(0.05f, 0.05f, 0.06f, 0.92f));
            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-18f, -18f);
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
                "Left-click: select zone under cursor | N: add zone | Del: delete | R: rename | E: toggle editable",
                12f,
                new Color(0.8f, 0.86f, 0.96f, 1f));

            _selectedZoneText = CreateText("SelectedZone", _root.transform, "Selected: (none)", 14f, Color.white, FontStyles.Bold);
            _selectedEditableText = CreateText("SelectedEditable", _root.transform, "Editable: n/a", 13f, new Color(0.8f, 0.84f, 0.92f, 1f));

            var inputPanel = CreatePanel("InputPanel", _root.transform, new Color(0.09f, 0.09f, 0.1f, 1f));
            inputPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);
            _nameInput = CreateInputField(inputPanel.transform, "Zone name");

            var actionsRow = CreateRow("ActionsRow", _root.transform, 34f);
            CreateActionButton(actionsRow.transform, "Add @ Cursor", () => _onAddZoneAtCursor?.Invoke());
            CreateActionButton(actionsRow.transform, "Rename", () => _onRenameSelectedZone?.Invoke(NameInput));
            CreateActionButton(actionsRow.transform, "Delete", () => _onDeleteSelectedZone?.Invoke());
            CreateActionButton(actionsRow.transform, "Toggle Editable", () => _onToggleSelectedZoneEditable?.Invoke());

            var moveRow = CreateRow("MoveRow", _root.transform, 34f);
            CreateActionButton(moveRow.transform, "←", () => _onMoveSelectedZone?.Invoke(Vector2Int.left));
            CreateActionButton(moveRow.transform, "↑", () => _onMoveSelectedZone?.Invoke(Vector2Int.up));
            CreateActionButton(moveRow.transform, "↓", () => _onMoveSelectedZone?.Invoke(Vector2Int.down));
            CreateActionButton(moveRow.transform, "→", () => _onMoveSelectedZone?.Invoke(Vector2Int.right));

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
            scrollLayout.preferredHeight = 430f;
            _zonesListContent = content;

            _statusText = CreateText("Status", _root.transform, "Ready", 12f, new Color(0.78f, 0.86f, 0.96f, 1f));
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
            return go;
        }

        private static GameObject CreateRow(string name, Transform parent, float height)
        {
            var row = CreatePanel(name, parent, new Color(0.09f, 0.09f, 0.1f, 1f));
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredHeight = Mathf.CeilToInt(size + 8f);
            return text;
        }

        private static Button CreateActionButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var buttonGo = CreatePanel($"Btn_{label}", parent, new Color(0.16f, 0.18f, 0.22f, 1f));
            var button = buttonGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(0.18f, 0.22f, 0.28f, 1f);
            colors.highlightedColor = new Color(0.26f, 0.31f, 0.4f, 1f);
            colors.pressedColor = new Color(0.34f, 0.4f, 0.5f, 1f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.targetGraphic = buttonGo.GetComponent<Image>();
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", buttonGo.transform, label, 12f, Color.white, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            return button;
        }

        private static TMP_InputField CreateInputField(Transform parent, string placeholder)
        {
            var root = CreatePanel("NameInput", parent, new Color(0.15f, 0.16f, 0.2f, 1f));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(6f, 6f);
            rootRect.offsetMax = new Vector2(-6f, -6f);

            var textViewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            textViewport.transform.SetParent(root.transform, false);
            var viewportRect = textViewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8f, 4f);
            viewportRect.offsetMax = new Vector2(-8f, -4f);

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(textViewport.transform, false);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 14f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(textViewport.transform, false);
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 14f;
            placeholderText.color = new Color(0.68f, 0.72f, 0.8f, 0.75f);
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            var input = root.AddComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 48;

            return input;
        }

        private static Toggle CreateToggle(Transform parent)
        {
            var root = new GameObject("Toggle", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(28f, 28f);

            var bg = CreatePanel("Background", root.transform, new Color(0.13f, 0.14f, 0.18f, 1f));
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.1f, 0.1f);
            bgRect.anchorMax = new Vector2(0.9f, 0.9f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            var check = CreatePanel("Checkmark", bg.transform, new Color(0.5f, 0.9f, 0.55f, 1f));
            var checkRect = check.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.2f, 0.2f);
            checkRect.anchorMax = new Vector2(0.8f, 0.8f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            return toggle;
        }

        private static ScrollRect CreateScrollView(string name, Transform parent, out Transform content)
        {
            var root = CreatePanel(name, parent, new Color(0.08f, 0.08f, 0.09f, 1f));
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(0f, 360f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(4f, 4f);
            viewportRect.offsetMax = new Vector2(-4f, -4f);
            viewport.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.06f, 1f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(3, 3, 3, 3);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            content = contentGo.transform;
            return scroll;
        }

        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            return _whiteSprite;
        }
    }
}
