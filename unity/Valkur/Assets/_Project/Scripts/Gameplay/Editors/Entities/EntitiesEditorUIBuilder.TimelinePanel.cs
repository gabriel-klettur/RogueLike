using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Entities
{
    public static partial class EntitiesEditorUIBuilder
    {
        // ── Timeline Panel ────────────────────────────────────────────────────────
        // The two clocks on one axis: the spell's phases above, the animation's steps below.
        //
        // Its OWN panel rather than another section of the Animation one, and that is a
        // measurement rather than a preference: the Animation panel already stands at 700 px
        // over a 800 px canvas with a 34 px top offset, so the twelve rows this needs do not
        // fit without taking them out of the stage — which is the thing being looked at.

        private const float TL_W          = 380f;
        private const float TL_H          = 330f + PANEL_HDR_H;
        private const float TL_TRACK_H    = 26f;
        private const float TL_PHASE_H    = 18f;

        /// <summary>Steps the track realises. A plan longer than this is still PLAYED in full —
        /// the cap is only how many cells the view draws, and it says so.</summary>
        private const int   TL_MAX_CELLS  = 32;

        /// <summary>In enum order: the dropdown index IS the <c>TimelineSegmentMode</c>.</summary>
        [Valkur.Core.SelfHealingStatic("Constant label table; written once at class init and never mutated.")]
        private static readonly string[] TimelineModeNames = { "Stretch", "Loop", "Hold" };

        public static void BuildTimelinePanel(Transform canvasT, ref UIRefs refs,
            Action onSeed, Action onClear, Action onApplyToSpell,
            Action<int> onStepClicked, Action<string> onStepDuration,
            Action<int> onReleaseChanged, Action<int> onRecoverChanged,
            Action<int> onPrepareMode, Action<int> onChannelMode)
        {
            refs.TimelineDropdown = MakeDrop("EntitiesTimelinePanel", canvasT,
                PanelDock.BottomLeft, PANEL_GAP, PANEL_GAP,
                TL_W, TL_H, "Cast timeline",
                out var t, out refs.TimelinePanelDrag);

            // Header line: which animation and which spell this plan belongs to.
            var subjGo = CreateUI("TlSubject", t);
            subjGo.AddComponent<LayoutElement>().preferredHeight = 15f;
            refs.TimelineSubjectText           = subjGo.AddComponent<TextMeshProUGUI>();
            refs.TimelineSubjectText.text      = "No animation selected.";
            refs.TimelineSubjectText.fontSize  = 10f;
            refs.TimelineSubjectText.fontStyle = FontStyles.Bold;
            refs.TimelineSubjectText.color     = ACCENT;
            refs.TimelineSubjectText.alignment = TextAlignmentOptions.MidlineLeft;

            BuildPhaseTrack(t, ref refs);
            BuildStepTrack(t, ref refs, onStepClicked);

            // Playhead + selection readout.
            var readGo = CreateUI("TlReadout", t);
            readGo.AddComponent<LayoutElement>().preferredHeight = 26f;
            refs.TimelineReadoutText                    = readGo.AddComponent<TextMeshProUGUI>();
            refs.TimelineReadoutText.text               = "";
            refs.TimelineReadoutText.fontSize           = 10f;
            refs.TimelineReadoutText.color              = TEXT_SECONDARY;
            refs.TimelineReadoutText.alignment          = TextAlignmentOptions.TopLeft;
            refs.TimelineReadoutText.enableWordWrapping = true;

            // Per-step duration — the authored number, which Stretch reads as a ratio.
            refs.TimelineStepDurationInput = AddLabeledNumber(t, "Step seconds", onStepDuration);

            refs.TimelineReleaseDd = AddLabeledDropdown(t, "Release at", onReleaseChanged);
            refs.TimelineRecoverDd = AddLabeledDropdown(t, "Recover at", onRecoverChanged);
            refs.TimelinePrepModeDd = AddLabeledDropdown(t, "Wind-up",  onPrepareMode);
            refs.TimelineChanModeDd = AddLabeledDropdown(t, "Launch",   onChannelMode);

            var btnRow = CreateUI("TlButtons", t);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 22f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            AddActionBtn(btnRow.transform, "Seed",     22f, onSeed,  out _);
            AddActionBtn(btnRow.transform, "Clear",    22f, onClear, out _);
            refs.TimelineApplyBtnImg = AddActionBtn(btnRow.transform, "Write to spell", 22f,
                                                    onApplyToSpell, out refs.TimelineApplyBtnTmp);

            var warnGo = CreateUI("TlWarning", t);
            warnGo.AddComponent<LayoutElement>().preferredHeight = 42f;
            refs.TimelineWarningText                    = warnGo.AddComponent<TextMeshProUGUI>();
            refs.TimelineWarningText.text               = "";
            refs.TimelineWarningText.fontSize           = 9f;
            refs.TimelineWarningText.color              = TEXT_MUTED;
            refs.TimelineWarningText.alignment          = TextAlignmentOptions.TopLeft;
            refs.TimelineWarningText.enableWordWrapping = true;

            refs.TimelineDropdown.SetActive(false);
        }

        /// <summary>
        /// The spell's three phases as one bar. Widths are FLEXIBLE and set from the seconds,
        /// so the bar is a real time axis rather than three equal boxes — a 1.2 s wind-up in
        /// front of an instant launch has to LOOK like that.
        /// </summary>
        private static void BuildPhaseTrack(Transform parent, ref UIRefs refs)
        {
            var rowGo = CreateUI("TlPhaseTrack", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = TL_PHASE_H;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 1f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            refs.TimelinePhaseImgs = new Image[3];
            refs.TimelinePhaseTmps = new TextMeshProUGUI[3];
            string[] names = { "WIND-UP", "LAUNCH", "RECOVER" };
            for (int i = 0; i < 3; i++)
            {
                var cell = CreateUI($"Phase{i}", rowGo.transform);
                cell.AddComponent<LayoutElement>().flexibleWidth = 1f;
                refs.TimelinePhaseImgs[i] = cell.AddComponent<Image>();
                refs.TimelinePhaseImgs[i].color = SLOT_BG;
                refs.TimelinePhaseTmps[i] = AddCenteredText(cell.transform, names[i], 8f,
                                                            FontStyles.Bold, TEXT_MUTED);
            }
        }

        /// <summary>
        /// The animation's steps, each as wide as its seconds. Same flexible-width trick, which
        /// is what puts a step and the phase above it on the same axis — the whole reason the
        /// panel exists.
        /// </summary>
        private static void BuildStepTrack(Transform parent, ref UIRefs refs, Action<int> onStepClicked)
        {
            var rowGo = CreateUI("TlStepTrack", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = TL_TRACK_H;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 1f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            refs.TimelineStepImgs = new Image[TL_MAX_CELLS];
            refs.TimelineStepTmps = new TextMeshProUGUI[TL_MAX_CELLS];
            for (int i = 0; i < TL_MAX_CELLS; i++)
            {
                int slot = i;
                var cell = CreateUI($"Step{i}", rowGo.transform);
                cell.AddComponent<LayoutElement>().flexibleWidth = 1f;

                var img = cell.AddComponent<Image>();
                img.color = SLOT_BG;
                var btn = cell.AddComponent<Button>();
                btn.targetGraphic = img;
                if (onStepClicked != null) btn.onClick.AddListener(() => onStepClicked(slot));

                refs.TimelineStepImgs[i] = img;
                refs.TimelineStepTmps[i] = AddCenteredText(cell.transform, i.ToString(), 8f,
                                                           FontStyles.Normal, TEXT_SECONDARY);
                cell.SetActive(false);
            }
        }

        private static TMP_InputField AddLabeledNumber(Transform parent, string label,
                                                       Action<string> onCommit)
        {
            var rowGo = CreateUI($"TlRow_{label}", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 86f;
            var lbl       = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text      = label;
            lbl.fontSize  = 10f;
            lbl.color     = TEXT_SECONDARY;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;

            var input = UIInputField.AddCommit(rowGo.transform, "", onCommit, 18f, 10f);
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            var le = input.GetComponent<LayoutElement>();
            if (le != null) le.flexibleWidth = 1f;
            return input;
        }

        /// <summary>The mode dropdown's labels, in enum order.</summary>
        public static string[] TimelineModeLabels => TimelineModeNames;
    }
}
