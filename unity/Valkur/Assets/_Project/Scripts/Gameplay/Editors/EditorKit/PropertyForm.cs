using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Inspector-style property form for runtime editors.
    /// Mirrors Python's properties panels (entities, spawner, items).
    /// Rows of [label | editor] where editor can be text/int/float/bool/dropdown.
    /// Values are pushed live via onChange; the form does not own the data.
    /// </summary>
    public sealed class PropertyForm : MonoBehaviour
    {
        private readonly Dictionary<string, Component> _fields = new Dictionary<string, Component>();

        public Action<string, object> ValueChanged;

        public static PropertyForm Create(Transform parent, string name)
        {
            var go = EditorUIHelpers.CreateUI(name, parent);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f; vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.AddComponent<PropertyForm>();
        }

        public void AddText(string key, string label, string value)
        {
            var input = BuildInputRow(key, label, value);
            _fields[key] = input;
            input.onEndEdit.AddListener(v => ValueChanged?.Invoke(key, v));
        }

        public void AddInt(string key, string label, int value)
        {
            var input = BuildInputRow(key, label, value.ToString());
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            _fields[key] = input;
            input.onEndEdit.AddListener(v =>
            {
                if (int.TryParse(v, out var i)) ValueChanged?.Invoke(key, i);
            });
        }

        public void AddFloat(string key, string label, float value)
        {
            var input = BuildInputRow(key, label, value.ToString("0.###"));
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            _fields[key] = input;
            input.onEndEdit.AddListener(v =>
            {
                if (float.TryParse(v, out var f)) ValueChanged?.Invoke(key, f);
            });
        }

        public void AddBool(string key, string label, bool value)
        {
            var row = BuildRow(label);
            var tGo = EditorUIHelpers.CreateUI("Toggle", row.transform);
            tGo.AddComponent<LayoutElement>().preferredWidth = 24f;
            var tImg = tGo.AddComponent<Image>();
            tImg.color = EditorUIHelpers.BG_SURFACE;
            var toggle = tGo.AddComponent<Toggle>();
            toggle.targetGraphic = tImg;
            var check = EditorUIHelpers.CreateUI("Check", tGo.transform);
            EditorUIHelpers.StretchFill(check);
            var ci = check.AddComponent<Image>();
            ci.color = EditorUIHelpers.ACCENT;
            toggle.graphic = ci;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => ValueChanged?.Invoke(key, v));
            _fields[key] = toggle;
        }

        public void AddDropdown(string key, string label, IList<string> options, int selectedIndex)
        {
            var row = BuildRow(label);
            var dGo = EditorUIHelpers.CreateUI("Dropdown", row.transform);
            var le = dGo.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var bg = dGo.AddComponent<Image>();
            bg.color = EditorUIHelpers.BG_SURFACE;
            var dd = dGo.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bg;
            dd.ClearOptions();
            dd.AddOptions(new List<string>(options));
            if (selectedIndex >= 0 && selectedIndex < options.Count) dd.value = selectedIndex;
            dd.onValueChanged.AddListener(i => ValueChanged?.Invoke(key, i));
            _fields[key] = dd;
        }

        public void SetValue(string key, object value)
        {
            if (!_fields.TryGetValue(key, out var c) || c == null) return;
            switch (c)
            {
                case TMP_InputField inp: inp.SetTextWithoutNotify(value?.ToString() ?? ""); break;
                case Toggle t when value is bool b: t.SetIsOnWithoutNotify(b); break;
                case TMP_Dropdown dd when value is int i: dd.SetValueWithoutNotify(i); break;
            }
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);
            _fields.Clear();
        }

        // ── Internals ──

        private GameObject BuildRow(string label)
        {
            var row = EditorUIHelpers.CreateUI("Row_" + label, transform);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            var lblTmp = EditorUIHelpers.AddLabel(row.transform, label, 12f, TextAlignmentOptions.Left);
            lblTmp.GetComponent<LayoutElement>().preferredWidth = 110f;
            lblTmp.color = EditorUIHelpers.TEXT_PRIMARY;
            return row;
        }

        private TMP_InputField BuildInputRow(string key, string label, string value)
        {
            var row = BuildRow(label);
            var iGo = EditorUIHelpers.CreateUI("Input", row.transform);
            var le = iGo.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var bg = iGo.AddComponent<Image>();
            bg.color = EditorUIHelpers.BG_SURFACE;
            var input = iGo.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            var tg = EditorUIHelpers.CreateUI("Text", iGo.transform);
            EditorUIHelpers.StretchFill(tg);
            var tmp = tg.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 13f; tmp.color = EditorUIHelpers.TEXT_PRIMARY;
            tmp.margin = new Vector4(6, 3, 6, 3);
            input.textComponent = tmp;
            input.SetTextWithoutNotify(value ?? "");
            return input;
        }
    }
}
