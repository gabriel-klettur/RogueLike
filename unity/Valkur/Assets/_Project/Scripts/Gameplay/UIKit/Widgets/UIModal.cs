using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Lightweight screen-space modal for runtime editors and HUD widgets.
    /// Mirrors Python editors' add/remove/confirm/form dialogs (e.g.
    /// entities/panels/add_remove).
    ///
    /// Usage:
    ///   UIModal.Confirm(canvas.transform, "Delete?", "This action cannot be undone.",
    ///       onOk: () => DoDelete(),
    ///       onCancel: null);
    ///
    ///   UIModal.Prompt(canvas.transform, "New name:", "default",
    ///       onOk: val => rename(val),
    ///       onCancel: null);
    ///
    ///   UIModal.Form(canvas.transform, "Add entity",
    ///       new [] { UIModal.FormField.Text("Name", ""),
    ///                UIModal.FormField.Int("HP", 10),
    ///                UIModal.FormField.Dropdown("Type", new[]{"A","B"}, 0) },
    ///       result => { ... });
    /// </summary>
    public static class UIModal
    {
        public static void Message(Transform parentCanvas, string title, string body, Action onOk = null)
        {
            Show(parentCanvas, title, body, null, ok =>
            {
                onOk?.Invoke();
            }, showCancel: false);
        }

        public static void Confirm(Transform parentCanvas, string title, string body,
            Action onOk, Action onCancel = null)
        {
            Show(parentCanvas, title, body, null, ok =>
            {
                if (ok) onOk?.Invoke(); else onCancel?.Invoke();
            });
        }

        public static void Prompt(Transform parentCanvas, string title, string defaultValue,
            Action<string> onOk, Action onCancel = null)
        {
            var field = new TMP_InputField[1];
            Show(parentCanvas, title, null, body =>
            {
                var iGo = UIFactory.CreateUI("Input", body);
                var le = iGo.AddComponent<LayoutElement>();
                le.preferredHeight = 32f;
                var bg = iGo.AddComponent<Image>();
                bg.color = UITheme.BG_SURFACE;
                var input = iGo.AddComponent<TMP_InputField>();
                input.targetGraphic = bg;
                var textGo = UIFactory.CreateUI("Text", iGo.transform);
                UIFactory.StretchFill(textGo);
                var text = textGo.AddComponent<TextMeshProUGUI>();
                text.fontSize = 13f; text.color = UITheme.TEXT_PRIMARY;
                text.margin = new Vector4(6, 3, 6, 3);
                input.textComponent = text;
                input.text = defaultValue ?? string.Empty;
                field[0] = input;
            }, ok =>
            {
                if (ok) onOk?.Invoke(field[0]?.text ?? string.Empty);
                else onCancel?.Invoke();
            });
        }

        public static void Form(Transform parentCanvas, string title,
            IList<FormField> fields, Action<FormResult> onOk, Action onCancel = null)
        {
            var widgets = new FormFieldWidgets[fields.Count];
            Show(parentCanvas, title, null, body =>
            {
                for (int i = 0; i < fields.Count; i++)
                    widgets[i] = BuildField(body, fields[i]);
            }, ok =>
            {
                if (!ok) { onCancel?.Invoke(); return; }
                var result = new FormResult();
                for (int i = 0; i < fields.Count; i++)
                    result.Values[fields[i].Key] = widgets[i].Read(fields[i]);
                onOk?.Invoke(result);
            });
        }

        private static GameObject Show(Transform parentCanvas, string title, string body,
            Action<Transform> buildExtras, Action<bool> done, bool showCancel = true)
        {
            var modal = UIFactory.CreateUI("__UIModal", parentCanvas);
            var mr = modal.GetComponent<RectTransform>();
            mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one; mr.sizeDelta = Vector2.zero;
            var shade = modal.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, 0.65f);
            shade.raycastTarget = true;

            var card = UIPanel.Make("Card", modal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(420f, 200f));
            var cr = card.GetComponent<RectTransform>();
            cr.localScale = Vector3.one;

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f; vlg.padding = new RectOffset(14, 14, 12, 12);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UILabel.MakeTitleBar(card.transform, title ?? "", 28f);
            if (!string.IsNullOrEmpty(body))
            {
                var lbl = UILabel.Add(card.transform, body, 13f, TextAlignmentOptions.Left);
                lbl.color = UITheme.TEXT_PRIMARY;
                var le = lbl.GetComponent<LayoutElement>() ?? lbl.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 40f;
            }

            buildExtras?.Invoke(card.transform);

            var row = UIFactory.CreateUI("Row", card.transform);
            row.AddComponent<LayoutElement>().preferredHeight = 34f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f; hlg.childForceExpandWidth = true;

            if (showCancel)
            {
                UIButton.Make(row.transform, "Cancel", () =>
                {
                    if (modal != null) UnityEngine.Object.Destroy(modal);
                    done?.Invoke(false);
                });
            }
            UIButton.Make(row.transform, "OK", () =>
            {
                if (modal != null) UnityEngine.Object.Destroy(modal);
                done?.Invoke(true);
            });

            return modal;
        }

        public enum FieldKind { Text, Int, Float, Bool, Dropdown }

        public readonly struct FormField
        {
            public readonly string Key;
            public readonly FieldKind Kind;
            public readonly object Default;
            public readonly string[] Options;
            public FormField(string key, FieldKind kind, object def, string[] options)
            { Key = key; Kind = kind; Default = def; Options = options; }

            public static FormField Text(string key, string def = "") => new FormField(key, FieldKind.Text, def, null);
            public static FormField Int(string key, int def = 0) => new FormField(key, FieldKind.Int, def, null);
            public static FormField Float(string key, float def = 0f) => new FormField(key, FieldKind.Float, def, null);
            public static FormField Bool(string key, bool def = false) => new FormField(key, FieldKind.Bool, def, null);
            public static FormField Dropdown(string key, string[] options, int defaultIndex = 0)
                => new FormField(key, FieldKind.Dropdown, defaultIndex, options ?? new string[0]);
        }

        public class FormResult
        {
            public Dictionary<string, object> Values { get; } = new Dictionary<string, object>();
            public string GetString(string k) => Values.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
            public int GetInt(string k, int def = 0)
            {
                if (Values.TryGetValue(k, out var v) && v is int i) return i;
                if (v is string s && int.TryParse(s, out var p)) return p;
                return def;
            }
            public float GetFloat(string k, float def = 0f)
            {
                if (Values.TryGetValue(k, out var v) && v is float f) return f;
                if (v is string s && float.TryParse(s, out var p)) return p;
                return def;
            }
            public bool GetBool(string k, bool def = false)
                => Values.TryGetValue(k, out var v) && v is bool b ? b : def;
        }

        private struct FormFieldWidgets
        {
            public TMP_InputField Input;
            public Toggle Toggle;
            public TMP_Dropdown Dropdown;
            public object Read(FormField f)
            {
                switch (f.Kind)
                {
                    case FieldKind.Bool: return Toggle != null && Toggle.isOn;
                    case FieldKind.Dropdown: return Dropdown != null ? Dropdown.value : 0;
                    case FieldKind.Int:
                        if (Input != null && int.TryParse(Input.text, out var i)) return i;
                        return f.Default is int di ? di : 0;
                    case FieldKind.Float:
                        if (Input != null && float.TryParse(Input.text, out var fl)) return fl;
                        return f.Default is float df ? df : 0f;
                    default: return Input != null ? Input.text : (f.Default?.ToString() ?? "");
                }
            }
        }

        private static FormFieldWidgets BuildField(Transform parent, FormField f)
        {
            var wrap = UIFactory.CreateUI("Field_" + f.Key, parent);
            wrap.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = wrap.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.childForceExpandWidth = false;
            hlg.childControlWidth = true; hlg.childControlHeight = true;

            var lbl = UILabel.Add(wrap.transform, f.Key + ":", 12f, TextAlignmentOptions.Left);
            lbl.GetComponent<LayoutElement>().preferredWidth = 120f;
            lbl.color = UITheme.TEXT_PRIMARY;

            var w = new FormFieldWidgets();
            switch (f.Kind)
            {
                case FieldKind.Bool:
                    var tGo = UIFactory.CreateUI("Toggle", wrap.transform);
                    tGo.AddComponent<LayoutElement>().preferredWidth = 24f;
                    var tImg = tGo.AddComponent<Image>();
                    tImg.color = UITheme.BG_SURFACE;
                    w.Toggle = tGo.AddComponent<Toggle>();
                    w.Toggle.targetGraphic = tImg;
                    var check = UIFactory.CreateUI("Check", tGo.transform);
                    UIFactory.StretchFill(check);
                    var ci = check.AddComponent<Image>();
                    ci.color = UITheme.ACCENT;
                    w.Toggle.graphic = ci;
                    w.Toggle.isOn = f.Default is bool db && db;
                    break;
                case FieldKind.Dropdown:
                    var dGo = UIFactory.CreateUI("Dropdown", wrap.transform);
                    dGo.AddComponent<LayoutElement>().preferredWidth = 220f;
                    var dImg = dGo.AddComponent<Image>();
                    dImg.color = UITheme.BG_SURFACE;
                    w.Dropdown = dGo.AddComponent<TMP_Dropdown>();
                    w.Dropdown.targetGraphic = dImg;
                    w.Dropdown.ClearOptions();
                    var opts = new List<string>(f.Options ?? new string[0]);
                    w.Dropdown.AddOptions(opts);
                    if (f.Default is int ddi && ddi >= 0 && ddi < opts.Count) w.Dropdown.value = ddi;
                    break;
                default:
                    var iGo = UIFactory.CreateUI("Input", wrap.transform);
                    iGo.AddComponent<LayoutElement>().preferredWidth = 220f;
                    var iBg = iGo.AddComponent<Image>();
                    iBg.color = UITheme.BG_SURFACE;
                    w.Input = iGo.AddComponent<TMP_InputField>();
                    w.Input.targetGraphic = iBg;
                    var tg = UIFactory.CreateUI("Text", iGo.transform);
                    UIFactory.StretchFill(tg);
                    var tmp = tg.AddComponent<TextMeshProUGUI>();
                    tmp.fontSize = 13f; tmp.color = UITheme.TEXT_PRIMARY;
                    tmp.margin = new Vector4(6, 3, 6, 3);
                    w.Input.textComponent = tmp;
                    w.Input.text = f.Default?.ToString() ?? "";
                    if (f.Kind == FieldKind.Int) w.Input.contentType = TMP_InputField.ContentType.IntegerNumber;
                    if (f.Kind == FieldKind.Float) w.Input.contentType = TMP_InputField.ContentType.DecimalNumber;
                    break;
            }
            return w;
        }
    }
}
