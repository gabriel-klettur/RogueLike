using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Standard toolbar docked at the top of an editor or HUD window.
    /// Mirrors Python editors' toolbar_graph_panel_view.py pattern: a row of
    /// icon/text buttons (Save, Reload, Undo, Redo, Help, Status) shared by
    /// every editor.
    ///
    /// Usage:
    ///   var bar = UIToolbar.Create(root.transform, "Spells");
    ///   bar.AddButton("Save",  () => SaveAll());
    ///   bar.AddButton("Reload",() => Reload());
    ///   bar.AddUndoRedo(undoStack);
    ///   bar.AddToggle("Help", true, v => helpPanel.SetActive(v));
    ///   bar.SetStatus("Ready");
    /// </summary>
    public sealed class UIToolbar : MonoBehaviour
    {
        private RectTransform _buttonsRow;
        private TextMeshProUGUI _status;
        private Button _undoBtn, _redoBtn;
        private UndoStack _undo;

        public static UIToolbar Create(Transform parent, string title, float height = 34f)
        {
            var go = UIFactory.CreateUI("Toolbar", parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var img = go.AddComponent<Image>();
            img.color = UITheme.BG_HEADER;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f; hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = false; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

            var bar = go.AddComponent<UIToolbar>();

            var tGo = UIFactory.CreateUI("Title", go.transform);
            tGo.AddComponent<LayoutElement>().preferredWidth = 180f;
            var tmp = tGo.AddComponent<TextMeshProUGUI>();
            tmp.text = title; tmp.fontSize = 15f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = UITheme.ACCENT;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.characterSpacing = 2f;

            var spacer = UIFactory.CreateUI("Spacer", go.transform);
            var sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            bar._buttonsRow = (RectTransform)go.transform;
            return bar;
        }

        public Button AddButton(string label, Action onClick, float width = 70f)
        {
            var btn = UIButton.Make(_buttonsRow, label, () => onClick?.Invoke(), 26f, 12f);
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
            var btn = UIButton.MakeToggle(_buttonsRow, label, initial, onChanged, 26f, 12f);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            return btn;
        }

        public TextMeshProUGUI AddStatusLabel(float width = 200f)
        {
            var go = UIFactory.CreateUI("Status", _buttonsRow);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 11f;
            tmp.color = UITheme.TEXT_SECONDARY;
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
