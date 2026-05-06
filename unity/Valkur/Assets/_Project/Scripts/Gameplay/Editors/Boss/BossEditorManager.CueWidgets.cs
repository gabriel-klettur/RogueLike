using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Cue-row widget builders for the Boss Editor Cue Inspector panel.
    /// Each cue renders as a compact 3-row card: (bar/beat/frac/type/actions),
    /// (targeting/targetKey), (payload/note). All field changes go through
    /// <see cref="ApplyCueEdit"/> for undo support.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        private void BuildCueRow(RectTransform parent, int ci)
        {
            var chart = _selectedChart;
            var cue   = chart.cues[ci];
            bool isSel = ci == _selectedCueIndex;

            var rowGo = EditorUIHelpers.CreateUI($"Cue_{ci}", parent);
            var rowBg = rowGo.AddComponent<Image>();
            rowBg.color = isSel
                ? new Color(0.15f, 0.20f, 0.30f, 0.95f)
                : new Color(0.12f, 0.12f, 0.14f, 0.85f);

            var outerBtn = rowGo.AddComponent<Button>();
            int capturedCi = ci;
            outerBtn.onClick.AddListener(() => SelectCue(capturedCi));

            var vlg = rowGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 4, 4); vlg.spacing = 3f;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            rowGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildCueRowLine1(rowGo.transform, chart, cue, capturedCi);
            BuildCueRowLine2(rowGo.transform, chart, cue, capturedCi);
            BuildCueRowLine3(rowGo.transform, chart, cue, capturedCi);

            EditorUIHelpers.BuildSeparator(rowGo.transform);
        }

        private void BuildCueRowLine1(Transform rowT, BossChart chart, BossCue cue, int capturedCi)
        {
            var r1 = EditorUIHelpers.CreateUI("R1", rowT);
            r1.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h = r1.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            var numLbl = EditorUIHelpers.AddLabel(r1.transform, $"#{capturedCi + 1}", 9f);
            numLbl.color = EditorUIHelpers.TEXT_MUTED;
            (numLbl.gameObject.GetComponent<LayoutElement>() ??
             numLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 22f;

            AddSmallIntField(r1.transform, "Bar", cue.bar, 34f, v =>
            {
                var e = chart.cues[capturedCi]; e.bar = v;
                ApplyCueEdit(capturedCi, e); RefreshCuesPanel();
            });
            AddSmallIntField(r1.transform, "Beat", cue.beat, 34f, v =>
            {
                var e = chart.cues[capturedCi]; e.beat = v;
                ApplyCueEdit(capturedCi, e); RefreshCuesPanel();
            });
            AddSmallSlider(r1.transform, "Frac", cue.beatFraction, 60f, v =>
            {
                var e = chart.cues[capturedCi]; e.beatFraction = v;
                ApplyCueEdit(capturedCi, e);
            });
            AddTypeDropdown(r1.transform, cue.type, v =>
            {
                var e = chart.cues[capturedCi]; e.type = v;
                ApplyCueEdit(capturedCi, e);
                RefreshCuesPanel();
            });

            var flex = EditorUIHelpers.CreateUI("Flex", r1.transform);
            flex.AddComponent<LayoutElement>().flexibleWidth = 1f;

            EditorUIHelpers.AddActionBtn(r1.transform, "⊕", 22f, () => DuplicateCue(capturedCi), out _, 9f);
            EditorUIHelpers.AddDangerBtn(r1.transform, "×", 22f, () => RequestDeleteCue(capturedCi), out _);
        }

        private void BuildCueRowLine2(Transform rowT, BossChart chart, BossCue cue, int capturedCi)
        {
            var r2 = EditorUIHelpers.CreateUI("R2", rowT);
            r2.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h = r2.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            AddTargetingDropdown(r2.transform, cue.targeting, v =>
            {
                var e = chart.cues[capturedCi]; e.targeting = v;
                ApplyCueEdit(capturedCi, e);
            });

            string keyLabel = cue.type switch
            {
                BossCueType.CastSpell   => "Spell:",
                BossCueType.PlaySfx     => "Sfx:",
                BossCueType.SwitchPhase => "Phase:",
                BossCueType.SpawnAdd    => "Adds:",
                _                       => "Key:",
            };
            var tkLbl = EditorUIHelpers.AddLabel(r2.transform, keyLabel, 9f);
            tkLbl.color = EditorUIHelpers.TEXT_MUTED;
            (tkLbl.gameObject.GetComponent<LayoutElement>() ??
             tkLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 38f;

            // CastSpell cues use a dropdown sourced from the SpellCatalog so
            // designers can't typo a spellKey; every other type stays as a
            // free-form input field (sfx ids / phase labels / monster keys /
            // animator triggers don't have a single canonical catalog yet).
            string[] spellKeys = cue.type == BossCueType.CastSpell
                ? GetSpellKeysForDropdown()
                : Array.Empty<string>();

            if (cue.type == BossCueType.CastSpell && spellKeys.Length > 0)
            {
                var ddGo = AddSpellKeyDropdown(r2.transform, cue.targetKey ?? string.Empty, spellKeys, v =>
                {
                    var e = chart.cues[capturedCi]; e.targetKey = v;
                    ApplyCueEdit(capturedCi, e);
                });
                (ddGo.GetComponent<LayoutElement>() ??
                 ddGo.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            }
            else
            {
                var tkFld = EditorUIHelpers.AddInputField(r2.transform, cue.targetKey ?? "",
                    v => { var e = chart.cues[capturedCi]; e.targetKey = v; ApplyCueEdit(capturedCi, e); },
                    20f, 9f);
                (tkFld.gameObject.GetComponent<LayoutElement>() ??
                 tkFld.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            }
        }

        // Spell-key dropdown sourced from SpellCatalog. Pre-pends an empty
        // option so the cue can express "no spell yet" without falling back
        // to the input field.
        private static GameObject AddSpellKeyDropdown(Transform parent, string current,
            string[] keys, Action<string> onChange)
        {
            var go = EditorUIHelpers.CreateUI("SpellKeyDD", parent);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.22f, 1f);

            var dd = go.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bg;
            dd.ClearOptions();

            var opts = new List<string>(keys.Length + 1) { "(none)" };
            opts.AddRange(keys);
            dd.AddOptions(opts);

            int idx = 0;
            for (int i = 0; i < keys.Length; i++)
                if (string.Equals(keys[i], current, StringComparison.OrdinalIgnoreCase)) { idx = i + 1; break; }
            dd.SetValueWithoutNotify(idx);

            var lblGo = EditorUIHelpers.CreateUI("Label", go.transform);
            UIFactory.StretchFill(lblGo);
            var lr = lblGo.GetComponent<RectTransform>();
            lr.offsetMin = new Vector2(4f, 2f); lr.offsetMax = new Vector2(-4f, -2f);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.fontSize          = 9f;
            lblTmp.color             = EditorUIHelpers.TEXT_PRIMARY;
            lblTmp.alignment         = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode      = TextOverflowModes.Truncate;
            dd.captionText           = lblTmp;
            lblTmp.text              = idx == 0 ? "(none)" : keys[idx - 1];

            dd.onValueChanged.AddListener(v =>
            {
                string picked = v == 0 ? string.Empty : keys[v - 1];
                onChange?.Invoke(picked);
            });
            return go;
        }

        private void BuildCueRowLine3(Transform rowT, BossChart chart, BossCue cue, int capturedCi)
        {
            var r3 = EditorUIHelpers.CreateUI("R3", rowT);
            r3.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h = r3.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 4f; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            var plLbl = EditorUIHelpers.AddLabel(r3.transform, "Payload:", 9f);
            plLbl.color = EditorUIHelpers.TEXT_MUTED;
            (plLbl.gameObject.GetComponent<LayoutElement>() ??
             plLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 48f;

            var plFld = EditorUIHelpers.AddInputField(r3.transform, cue.payload.ToString("F2"),
                v => { if (float.TryParse(v, out float f)) { var e = chart.cues[capturedCi]; e.payload = f; ApplyCueEdit(capturedCi, e); } },
                20f, 9f);
            (plFld.gameObject.GetComponent<LayoutElement>() ??
             plFld.gameObject.AddComponent<LayoutElement>()).preferredWidth = 52f;

            var noteLbl = EditorUIHelpers.AddLabel(r3.transform, "Note:", 9f);
            noteLbl.color = EditorUIHelpers.TEXT_MUTED;
            (noteLbl.gameObject.GetComponent<LayoutElement>() ??
             noteLbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 34f;

            var noteFld = EditorUIHelpers.AddInputField(r3.transform, cue.note ?? "",
                v => { var e = chart.cues[capturedCi]; e.note = v; ApplyCueEdit(capturedCi, e); },
                20f, 9f);
            (noteFld.gameObject.GetComponent<LayoutElement>() ??
             noteFld.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
        }

        // Lightweight row-only refresh.
        private void RefreshCueRow(int ci) => RefreshCuesPanel();

        // ── Widget helpers ─────────────────────────────────────────────────────

        private static void AddSmallIntField(Transform parent, string label,
            int value, float width, Action<int> onCommit)
        {
            var go = EditorUIHelpers.CreateUI($"IntField_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;

            var lbl = EditorUIHelpers.AddLabel(go.transform, label, 8f);
            lbl.color = EditorUIHelpers.TEXT_MUTED;
            (lbl.gameObject.GetComponent<LayoutElement>() ??
             lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 18f;

            var fld = EditorUIHelpers.AddInputField(go.transform, value.ToString(),
                v => { if (int.TryParse(v, out int n)) onCommit?.Invoke(n); }, 20f, 9f);
            (fld.gameObject.GetComponent<LayoutElement>() ??
             fld.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
        }

        private static void AddSmallSlider(Transform parent, string label,
            float value, float width, Action<float> onChanged)
        {
            var go = EditorUIHelpers.CreateUI($"Slider_{label}", parent);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;

            var lbl = EditorUIHelpers.AddLabel(go.transform, label, 8f);
            lbl.color = EditorUIHelpers.TEXT_MUTED;
            (lbl.gameObject.GetComponent<LayoutElement>() ??
             lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 20f;

            var sliderGo = EditorUIHelpers.CreateUI("S", go.transform);
            sliderGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UISlider.Make(sliderGo.transform, 0f, 1f, value, v => onChanged?.Invoke(v), 18f, 10f);
        }

        private static void AddTypeDropdown(Transform parent, BossCueType current,
            Action<BossCueType> onChange)
        {
            var go = EditorUIHelpers.CreateUI("TypeDD", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 90f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.22f, 1f);

            var dd = go.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bg;
            dd.ClearOptions();
            var opts = new List<string>();
            foreach (BossCueType t in Enum.GetValues(typeof(BossCueType)))
                opts.Add(t.ToString());
            dd.AddOptions(opts);
            dd.SetValueWithoutNotify((int)current);

            var lblGo = EditorUIHelpers.CreateUI("Label", go.transform);
            UIFactory.StretchFill(lblGo);
            var lr = lblGo.GetComponent<RectTransform>();
            lr.offsetMin = new Vector2(4f, 2f); lr.offsetMax = new Vector2(-4f, -2f);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.fontSize = 9f;
            lblTmp.color = EditorUIHelpers.TEXT_PRIMARY;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode = TextOverflowModes.Truncate;
            dd.captionText = lblTmp;
            lblTmp.text = current.ToString();
            dd.onValueChanged.AddListener(v => onChange?.Invoke((BossCueType)v));
        }

        private static void AddTargetingDropdown(Transform parent, BossCueTargeting current,
            Action<BossCueTargeting> onChange)
        {
            var go = EditorUIHelpers.CreateUI("TargetDD", parent);
            go.AddComponent<LayoutElement>().preferredWidth = 76f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.18f, 0.22f, 1f);

            var dd = go.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = bg;
            dd.ClearOptions();
            var opts = new List<string>();
            foreach (BossCueTargeting t in Enum.GetValues(typeof(BossCueTargeting)))
                opts.Add(t.ToString());
            dd.AddOptions(opts);
            dd.SetValueWithoutNotify((int)current);

            var lblGo = EditorUIHelpers.CreateUI("Label", go.transform);
            UIFactory.StretchFill(lblGo);
            var lr = lblGo.GetComponent<RectTransform>();
            lr.offsetMin = new Vector2(4f, 2f); lr.offsetMax = new Vector2(-4f, -2f);
            var lblTmp = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.fontSize = 9f;
            lblTmp.color = EditorUIHelpers.TEXT_PRIMARY;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode = TextOverflowModes.Truncate;
            dd.captionText = lblTmp;
            lblTmp.text = current.ToString();
            dd.onValueChanged.AddListener(v => onChange?.Invoke((BossCueTargeting)v));
        }
    }
}
