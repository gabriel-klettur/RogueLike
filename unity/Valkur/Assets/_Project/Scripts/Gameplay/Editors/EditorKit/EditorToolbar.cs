using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Standard toolbar docked at the top of a runtime editor.
    /// Mirrors Python editors' toolbar_graph_panel_view.py pattern: a row of icon/text
    /// buttons (Save, Reload, Undo, Redo, Help, Status) shared by every editor.
    ///
    /// Usage:
    ///   var bar = EditorToolbar.Create(root.transform, "Spells");
    ///   bar.AddButton("Save",  () => SaveAll());
    ///   bar.AddButton("Reload",() => Reload());
    ///   bar.AddUndoRedo(undoStack);
    ///   bar.AddToggle("Help", true, v => helpPanel.SetActive(v));
    ///   bar.SetStatus("Ready");
    /// </summary>
    public sealed class EditorToolbar : MonoBehaviour
    {
        private RectTransform _buttonsRow;
        private TextMeshProUGUI _status;
        private Button _undoBtn, _redoBtn;
        private UndoStack _undo;

        public static EditorToolbar Create(Transform parent, string title, float height = 34f)
        {
            var go = EditorUIHelpers.CreateUI("Toolbar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = false; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            var bar = go.AddComponent<EditorToolbar>();

            // Title
            var tGo = EditorUIHelpers.CreateUI("Title", go.transform);
            tGo.AddComponent<LayoutElement>().preferredWidth = 180f;
            var tmp = tGo.AddComponent<TextMeshProUGUI>();
            tmp.text = title; tmp.fontSize = 15f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = EditorUIHelpers.ACCENT;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.characterSpacing = 2f;

            // Spacer
            var spacer = EditorUIHelpers.CreateUI("Spacer", go.transform);
            var sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            bar._buttonsRow = (RectTransform)go.transform;
            return bar;
        }

        public Button AddButton(string label, Action onClick, float width = 70f)
        {
            var btn = EditorUIHelpers.MakeButton(_buttonsRow, label, () => onClick?.Invoke(), 26f, 12f);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
        }

        public void AddUndoRedo(UndoStack undo)
        {
            _undo = undo;
            _undoBtn = AddButton("Undo", () => _undo?.Undo(), 60f);
            _redoBtn = AddButton("Redo", () => _undo?.Redo(), 60f);
            if (undo != null)
                undo.Changed += UpdateUndoRedoEnabled;
            UpdateUndoRedoEnabled();
        }

        private void UpdateUndoRedoEnabled()
        {
            if (_undoBtn != null) _undoBtn.interactable = _undo != null && _undo.CanUndo;
            if (_redoBtn != null) _redoBtn.interactable = _undo != null && _undo.CanRedo;
        }

        /// <summary>Adds a toggle button. onChanged is invoked with the new state.</summary>
        public Button AddToggle(string label, bool initial, Action<bool> onChanged, float width = 70f)
        {
            bool state = initial;
            Button btn = null;
            btn = EditorUIHelpers.MakeButton(_buttonsRow, label, () =>
            {
                state = !state;
                onChanged?.Invoke(state);
                var bg = btn.GetComponent<Image>();
                if (bg != null) bg.color = state ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
            }, 26f, 12f);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = state ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
            return btn;
        }

        public TextMeshProUGUI AddStatusLabel(float width = 200f)
        {
            var go = EditorUIHelpers.CreateUI("Status", _buttonsRow);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 11f;
            tmp.color = EditorUIHelpers.TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.text = "";
            _status = tmp;
            return tmp;
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        private void OnDestroy()
        {
            if (_undo != null) _undo.Changed -= UpdateUndoRedoEnabled;
        }
    }
}
