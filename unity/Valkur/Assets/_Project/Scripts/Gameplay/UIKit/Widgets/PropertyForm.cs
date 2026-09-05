using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Inspector-style property form for runtime editors and HUD windows.
    /// Mirrors Python's properties panels (entities, spawner, items). Rows
    /// of [label | editor] where editor can be text/int/float/bool/dropdown.
    /// Values are pushed live via onChange; the form does not own the data.
    ///
    /// Rows land as direct children of this transform, in the order they were added.
    /// A form that calls <see cref="BeginTab"/> routes them into per-tab pages instead;
    /// that is entirely opt-in and lives in PropertyForm.Tabs.cs, which is also where
    /// <see cref="RowParent"/> — the single branch the whole feature hangs off — is defined.
    /// </summary>
    public sealed partial class PropertyForm : MonoBehaviour
    {
        private readonly Dictionary<string, Component> _fields = new Dictionary<string, Component>();

        public Action<string, object> ValueChanged;

        public static PropertyForm Create(Transform parent, string name)
        {
            var go = UIFactory.CreateUI(name, parent);
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4f; vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go.AddComponent<PropertyForm>();
        }

        /// <summary>
        /// A non-interactive section header. Forty-odd rows without grouping is a wall;
        /// the Spells editor solves this privately, so the shared form needs its own.
        /// </summary>
        public void AddHeader(string label)
        {
            var go = UIFactory.CreateUI("Header_" + label, RowParent);
            go.AddComponent<LayoutElement>().preferredHeight = 20f;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = label;
            tmp.fontSize      = 10f;
            tmp.fontStyle     = FontStyles.Bold;
            tmp.color         = UITheme.ACCENT;
            tmp.alignment     = TextAlignmentOptions.BottomLeft;
            tmp.raycastTarget = false;
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

        /// <summary>
        /// A checkbox row.
        ///
        /// <para>The box is drawn as a BORDER plus an inset fill rather than as one flat
        /// square, because uGUI hides <see cref="Toggle.graphic"/> while the toggle is off —
        /// so an unchecked box is only its background, and the background used to be
        /// <c>BG_SURFACE</c> (0.13) sitting on <c>BG_PANEL</c> (0.09). Measured on the Spells
        /// editor's Gather tab, that is four hundredths of contrast on every channel with no
        /// edge: the toggles were laid out correctly, hit-tested correctly and were invisible,
        /// which reads as a row that simply has no checkbox. The inset also leaves the tick
        /// clear of the frame, so on and off differ in shape as well as in brightness.</para>
        /// </summary>
        public void AddBool(string key, string label, bool value)
        {
            var row = BuildRow(label);

            // A checkbox row's editor is 24px wide, so the ~140px an input row spends on its
            // field is slack here. Letting the label take it is what keeps a long one on a
            // single line inside a row whose height is fixed at 24 — the Spells editor's
            // Gather tab prints "Departure · ThrowForward (family)" and wrapped to two lines
            // against the shared 120px cap, with the second line clipped out of the row.
            var rowLabel = row.transform.GetChild(0).GetComponent<LayoutElement>();
            if (rowLabel != null) rowLabel.flexibleWidth = 1f;

            var tGo = UIFactory.CreateUI("Toggle", row.transform);
            tGo.AddComponent<LayoutElement>().preferredWidth = 24f;

            var border = tGo.AddComponent<Image>();
            border.color = UITheme.BORDER;
            var toggle = tGo.AddComponent<Toggle>();
            toggle.targetGraphic = border;

            var fill = UIFactory.CreateUI("Fill", tGo.transform);
            UIFactory.StretchFill(fill);
            var frt = (RectTransform)fill.transform;
            frt.offsetMin = new Vector2(1f, 1f);
            frt.offsetMax = new Vector2(-1f, -1f);
            fill.AddComponent<Image>().color = UITheme.BG_SURFACE;

            var check = UIFactory.CreateUI("Check", tGo.transform);
            UIFactory.StretchFill(check);
            var crt = (RectTransform)check.transform;
            crt.offsetMin = new Vector2(5f, 5f);
            crt.offsetMax = new Vector2(-5f, -5f);
            var ci = check.AddComponent<Image>();
            ci.color = UITheme.ACCENT;
            toggle.graphic = ci;

            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => ValueChanged?.Invoke(key, v));
            _fields[key] = toggle;
        }

        /// <summary>
        /// A colour row: hex field plus a live swatch. Hex rather than a picker because a
        /// full colour picker is its own project; designers read RRGGBB, and the swatch
        /// confirms the parse at a glance. Emits the normalised "#RRGGBBAA" string, and
        /// emits nothing on an unparseable value — the swatch simply does not change.
        /// </summary>
        public void AddColor(string key, string label, Color value)
        {
            var input = BuildInputRow(key, label, "#" + ColorUtility.ToHtmlStringRGBA(value));

            var swGo = UIFactory.CreateUI("Swatch", input.transform.parent);
            swGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var sw = swGo.AddComponent<Image>();
            sw.color = value;
            // Between the label and the field: label sits at index 0.
            swGo.transform.SetSiblingIndex(1);

            _fields[key] = input;
            input.onEndEdit.AddListener(v =>
            {
                string hex = string.IsNullOrEmpty(v) ? "" : (v.StartsWith("#") ? v : "#" + v);
                if (ColorUtility.TryParseHtmlString(hex, out var c))
                {
                    sw.color = c;
                    input.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGBA(c));
                    ValueChanged?.Invoke(key, "#" + ColorUtility.ToHtmlStringRGBA(c));
                }
            });
        }

        public void AddDropdown(string key, string label, IList<string> options, int selectedIndex)
        {
            var row = BuildRow(label);

            // Built through UIDropdown rather than a bare AddComponent: a TMP_Dropdown
            // with no template looks correct closed and throws "The dropdown template is
            // not assigned" the instant it is clicked, so the value can never change.
            var host = UIFactory.CreateUI("DropdownHost", row.transform);
            host.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var dd = UIDropdown.Add(host.transform, options, selectedIndex);
            dd.onValueChanged.AddListener(i => ValueChanged?.Invoke(key, i));
            _fields[key] = dd;
        }

        /// <summary>
        /// Pushes a value into a row without firing <see cref="ValueChanged"/>.
        /// Works just as well for a row sitting on a hidden tab: hidden pages are
        /// deactivated, never destroyed, so the component is still in _fields and still
        /// valid. uGUI and TMP resync from their stored state on enable, so the value
        /// shows up correctly the first time that tab is revealed.
        /// </summary>
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

        /// <summary>
        /// Destroys every row and forgets the key map. A tab strip and its pages are
        /// children like any other row, so they go with them — but WHICH tab was on screen
        /// deliberately survives, because editors rebuild this form on every selection
        /// change and the Particles panel rebuilds it on every accepted edit. See
        /// PropertyForm.Tabs.cs.
        /// </summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyRow(transform.GetChild(i).gameObject);
            _fields.Clear();
            ClearTabs();
        }

        /// <summary>
        /// Object.Destroy is deferred to end-of-frame and, outside Play Mode, is not allowed
        /// at all — Unity refuses it with "Destroy may not be called from edit mode!". This
        /// form is built by runtime editors, so Play Mode is the normal case, but it is also
        /// exercised by EditMode tests and can be driven from an editor tool, and there the
        /// deferred call both logs an error and leaves the old rows in the hierarchy for the
        /// rest of the frame — so a Clear() followed by a rebuild would silently double every
        /// row. Immediate destruction is also what the rebuild needs: Clear() is always
        /// followed by re-adding rows in the same call, and a deferred delete would have the
        /// old and new sets overlapping in the layout until the frame ended.
        /// </summary>
        private static void DestroyRow(GameObject go)
        {
            if (go == null) return;

            // DETACH FIRST. The paragraph above says immediate destruction is what the rebuild
            // needs, and then Play Mode took the deferred branch anyway -- so the old rows were
            // still children of this form's VerticalLayoutGroup for the rest of the frame the
            // new ones were added in, and the layout ran over both sets. Measured on the Spells
            // editor, a 150-row form laid out 300 rows on every selection change. Reparenting
            // to null removes it from the layout at once, whichever branch destroys it.
            go.transform.SetParent(null, false);

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private GameObject BuildRow(string label)
        {
            var row = UIFactory.CreateUI("Row_" + label, RowParent);
            row.AddComponent<LayoutElement>().preferredHeight = 24f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.padding = new RectOffset(4, 4, 0, 0);
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var lblTmp = UILabel.Add(row.transform, label, 11f, TextAlignmentOptions.MidlineLeft);
            lblTmp.GetComponent<LayoutElement>().preferredWidth = 120f;
            lblTmp.color = UITheme.TEXT_SECONDARY;
            return row;
        }

        private TMP_InputField BuildInputRow(string key, string label, string value)
        {
            var row = BuildRow(label);
            var iGo = UIFactory.CreateUI("Input", row.transform);
            var le = iGo.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            var bg = iGo.AddComponent<Image>();
            bg.color = UITheme.BG_SURFACE;
            var input = iGo.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            var tg = UIFactory.CreateUI("Text", iGo.transform);
            UIFactory.StretchFill(tg);
            var tmp = tg.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 13f; tmp.color = UITheme.TEXT_PRIMARY;
            tmp.margin = new Vector4(6, 3, 6, 3);
            input.textComponent = tmp;
            input.SetTextWithoutNotify(value ?? "");
            return input;
        }
    }
}
