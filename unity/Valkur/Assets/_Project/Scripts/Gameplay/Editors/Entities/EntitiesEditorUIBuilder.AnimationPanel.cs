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
        // ── Animation Panel ───────────────────────────────────────────────────────
        // The screen Python had and the Unity editor never did: the selected entity,
        // animating, per state and per direction. Python's entities editor showed a 3x3 grid
        // of the eight directions looping at once (GRID_ORDER_3X3); the pad here is that
        // control, with the centre cell — which no direction can occupy — turned into the
        // toggle between one big rig and all eight.
        //
        // Built OUTSIDE BuildAll on purpose: BuildAll already carries eighteen callbacks, and
        // this panel needs six more that no other panel shares. The menu-bar button is the
        // exception — it only needs the generic onDropdownToggle BuildMenuBar already has.

        private const float ANIM_W        = 336f;
        private const float ANIM_H        = 724f + 200f + PANEL_HDR_H;
        private const float ANIM_STAGE_H  = 212f;
        private const float ANIM_DIR_BTN  = 24f;

        /// <summary>Cells the strip realises. Every shipped animation is at most eight frames
        /// per direction (the knight and the mague are the longest), so sixteen is double the
        /// worst case shipped and the overflow line covers whatever a future wave brings.</summary>
        private const int   ANIM_STRIP_SLOTS    = 16;
        private const int   ANIM_STRIP_PER_ROW  = 8;
        private const float ANIM_STRIP_CELL     = 30f;

        /// <summary>Labels of the 3x3 pad in reading order. The centre is the grid toggle.</summary>
        [Valkur.Core.SelfHealingStatic("Constant label table; written once at class init and never mutated.")]
        private static readonly string[] AnimDirLabels =
        {
            "NW", "N", "NE",
            "W",  "8", "E",
            "SW", "S", "SE"
        };

        /// <summary>
        /// Builds the Animation panel. Callbacks are raw indices — what a state index MEANS
        /// belongs to the runtime editor, which owns the preview service.
        /// </summary>
        public static void BuildAnimationPanel(Transform canvasT, ref UIRefs refs,
            Action<int> onStateChanged, Action<int> onVariantChanged,
            Action<int> onLoadoutChanged, Action<int> onDirectionSlot,
            Action onZoomIn, Action onZoomOut,
            Action onTogglePlay, Action onStepBack, Action onStepForward,
            Action onToggleReverse, Action<int> onStripCell,
            Action<string> onEntitySpeed, Action<string> onStateSpeed,
            Action<string> onVariantSpeed, Action<bool> onHoldLastFrame,
            Action<int> onLayoutChanged,
            Action onToggleMuzzle, Action<int> onMuzzleScope, Action<int> onMuzzleSpell,
            Action onMuzzleClear,
            Action<string> onRepeatFrom = null, Action<string> onRepeatCount = null,
            CollisionCallbacks collision = null)
        {
            // Docked to the right of the picker column rather than to a screen corner: both
            // corners are taken (Tools/Categories/Picker on the left, Properties and
            // Add/Remove on the right) and a stage that opens under another panel reads as a
            // panel that failed to open.
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP + CATEGORIES_W + PANEL_GAP
                    + PICKER_W + PANEL_GAP;

            refs.AnimDropdown = MakeDrop("EntitiesAnimationPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ANIM_W, ANIM_H, "Animation",
                out var t, out refs.AnimPanelDrag);

            // Subject line — which entity the stage is showing.
            var subjGo = CreateUI("AnimSubject", t);
            subjGo.AddComponent<LayoutElement>().preferredHeight = 16f;
            refs.AnimSubjectText              = subjGo.AddComponent<TextMeshProUGUI>();
            refs.AnimSubjectText.text         = "No entity selected.";
            refs.AnimSubjectText.fontSize     = 11f;
            refs.AnimSubjectText.fontStyle    = FontStyles.Bold;
            refs.AnimSubjectText.color        = ACCENT;
            refs.AnimSubjectText.alignment    = TextAlignmentOptions.MidlineLeft;
            refs.AnimSubjectText.overflowMode = TextOverflowModes.Truncate;

            // Stage — the RenderTexture the preview camera draws into.
            var stageGo = CreateUI("AnimStage", t);
            stageGo.AddComponent<LayoutElement>().preferredHeight = ANIM_STAGE_H;
            var stageBg   = stageGo.AddComponent<Image>();
            // The 2 px frame around the render texture. The editors' own dark token: it only
            // has to be darker than the stage the camera clears to, which is what lets a
            // black-tinted body read against it.
            stageBg.color = BG_PANEL;

            var rawGo = CreateUI("StageImage", stageGo.transform);
            var rawRt = rawGo.GetComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = new Vector2(2f, 2f);
            rawRt.offsetMax = new Vector2(-2f, -2f);
            refs.AnimStage               = rawGo.AddComponent<RawImage>();
            refs.AnimStage.color         = Color.white;
            refs.AnimStage.raycastTarget = false;

            // Selectors. Options are filled by the runtime editor once an entity is picked —
            // a dropdown built with a fixed list would be lying about entities that carry
            // fewer variants or no loadouts.
            refs.AnimStateDd   = AddLabeledDropdown(t, "State",   onStateChanged);
            refs.AnimVariantDd = AddLabeledDropdown(t, "Variant", onVariantChanged);
            refs.AnimLoadoutDd = AddLabeledDropdown(t, "Loadout", onLoadoutChanged);

            BuildDirectionPad(t, ref refs, onDirectionSlot);
            BuildTransportRow(t, ref refs, onTogglePlay, onStepBack, onStepForward, onToggleReverse);
            BuildFrameStrip(t, ref refs, onStripCell);

            // Zoom row.
            var zoomRow = CreateUI("AnimZoomRow", t);
            zoomRow.AddComponent<LayoutElement>().preferredHeight = 20f;
            var zoomHlg = zoomRow.AddComponent<HorizontalLayoutGroup>();
            zoomHlg.spacing                = 6f;
            zoomHlg.childForceExpandWidth  = true;
            zoomHlg.childForceExpandHeight = true;
            zoomHlg.childControlWidth      = true;
            zoomHlg.childControlHeight     = true;
            AddActionBtn(zoomRow.transform, "Zoom -", 20f, onZoomOut, out _);
            AddActionBtn(zoomRow.transform, "Zoom +", 20f, onZoomIn,  out _);

            BuildPacingEditors(t, ref refs, onEntitySpeed, onStateSpeed, onVariantSpeed,
                               onHoldLastFrame, onLayoutChanged, onRepeatFrom, onRepeatCount);

            BuildMuzzleEditor(t, ref refs, onToggleMuzzle, onMuzzleScope, onMuzzleSpell,
                              onMuzzleClear);

            if (collision != null) BuildCollisionEditor(t, ref refs, collision);

            // Info line — what the preview cannot show by moving: the resolved art, the
            // fallback it landed on, the pacing. Filled from Phase 3 onwards.
            var infoGo = CreateUI("AnimInfo", t);
            infoGo.AddComponent<LayoutElement>().preferredHeight = 88f;
            refs.AnimInfoText                    = infoGo.AddComponent<TextMeshProUGUI>();
            refs.AnimInfoText.text               = "Pick an entity in the Picker.";
            refs.AnimInfoText.fontSize           = 10f;
            refs.AnimInfoText.color              = TEXT_SECONDARY;
            refs.AnimInfoText.alignment          = TextAlignmentOptions.TopLeft;
            refs.AnimInfoText.enableWordWrapping = true;

            refs.AnimDropdown.SetActive(false);
        }

        /// <summary>
        /// The pacing dials, next to the animation they pace.
        ///
        /// <para>They live here rather than in the Properties panel because the two that matter
        /// most are scoped to what the stage is showing: the per-STATE multiplier and the
        /// per-VARIANT one mean nothing without the state and variant selectors right above
        /// them. The entity-wide dial is repeated here for the same reason — the three
        /// multiply, and a designer moving one needs to see the other two.</para>
        /// </summary>
        /// <summary>
        /// The muzzle picker: where a spell is BORN on this creature's art.
        ///
        /// <para>It lives in this panel and not in Properties because the answer depends on the
        /// FRAME on screen. A muzzle is a fraction of the drawn sprite's own bounds, and every
        /// sheet is trimmed to its own alpha -- so "0.8 forward" is a different place on the
        /// idle and on the cast, and the only screen that can show which is the one already
        /// drawing the frame, paused, with a ground line under it.</para>
        ///
        /// <para>The SCOPE is the whole design. An author says "the dragon breathes from its
        /// mouth" about an ANIMATION, not about thirty frames, so the default scope is the
        /// state and variant currently selected. Per-spell exists for the one case an animation
        /// cannot separate: two spells cast from the same pose, one from the hand and one from
        /// the staff tip.</para>
        /// </summary>
        private static void BuildMuzzleEditor(Transform parent, ref UIRefs refs,
            Action onToggle, Action<int> onScope, Action<int> onSpell, Action onClear)
        {
            var header = CreateUI("AnimMuzzleHeader", parent);
            header.AddComponent<LayoutElement>().preferredHeight = 16f;
            var headerTmp       = header.AddComponent<TextMeshProUGUI>();
            headerTmp.text      = "ORIGEN DEL HECHIZO";
            headerTmp.fontSize  = 10f;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.color     = ACCENT;
            headerTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var row = CreateUI("AnimMuzzleRow", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 20f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            refs.AnimMuzzleBtnImg = AddActionBtn(row.transform, "Colocar", 20f, onToggle,
                                                 out refs.AnimMuzzleBtnTmp);
            refs.AnimMuzzleClearImg = AddActionBtn(row.transform, "Borrar", 20f, onClear,
                                                   out refs.AnimMuzzleClearTmp);

            refs.AnimMuzzleScopeDd = AddLabeledDropdown(parent, "Ambito", onScope);
            refs.AnimMuzzleSpellDd = AddLabeledDropdown(parent, "Hechizo", onSpell);

            var readoutGo = CreateUI("AnimMuzzleReadout", parent);
            readoutGo.AddComponent<LayoutElement>().preferredHeight = 26f;
            refs.AnimMuzzleReadout                    = readoutGo.AddComponent<TextMeshProUGUI>();
            refs.AnimMuzzleReadout.text               = "";
            refs.AnimMuzzleReadout.fontSize           = 9f;
            refs.AnimMuzzleReadout.color              = TEXT_SECONDARY;
            refs.AnimMuzzleReadout.alignment          = TextAlignmentOptions.TopLeft;
            refs.AnimMuzzleReadout.enableWordWrapping = true;
        }

        /// <summary>
        /// The collision panel's callbacks, grouped: BuildAnimationPanel already takes
        /// twenty-two, and ten more loose delegates is how one gets wired to the wrong button.
        /// </summary>
        public sealed class CollisionCallbacks
        {
            public Action OnToggleView, OnToggleEdit, OnAddShape, OnRemoveShape,
                          OnCopyToAnimation, OnRemeasure, OnResetFrame;
            public Action<int> OnShapeSelected, OnGestureSelected;
            public Action<string> OnFootprintWidth, OnFootprintDepth, OnHurtScale;
        }

        /// <summary>
        /// The collision layers of the frame on stage: the footprint the creature stands on and
        /// the capsules a blow can land on. Here and not in Properties for the muzzle's reason —
        /// the hurtbox is a statement about the FRAME being shown, and only this panel is drawing
        /// it, paused, with the capsules over it.
        /// </summary>
        private static void BuildCollisionEditor(Transform parent, ref UIRefs refs, CollisionCallbacks cb)
        {
            var header = CreateUI("AnimCollisionHeader", parent);
            header.AddComponent<LayoutElement>().preferredHeight = 16f;
            var headerTmp       = header.AddComponent<TextMeshProUGUI>();
            headerTmp.text      = "COLISIONES (PISADA Y CUERPO)";
            headerTmp.fontSize  = 10f;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.color     = ACCENT;
            headerTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var row = MakeButtonRow(parent, "AnimCollisionRow");
            refs.AnimCollViewImg = AddActionBtn(row, "Ver", 20f, cb.OnToggleView, out refs.AnimCollViewTmp);
            refs.AnimCollEditImg = AddActionBtn(row, "Editar", 20f, cb.OnToggleEdit, out refs.AnimCollEditTmp);
            AddActionBtn(row, "+ Capsula", 20f, cb.OnAddShape, out _);
            AddActionBtn(row, "- Capsula", 20f, cb.OnRemoveShape, out _);

            refs.AnimCollShapeDd   = AddLabeledDropdown(parent, "Capsula", cb.OnShapeSelected);
            refs.AnimCollGestureDd = AddLabeledDropdown(parent, "Arrastre", cb.OnGestureSelected);

            var row2 = MakeButtonRow(parent, "AnimCollisionRow2");
            AddActionBtn(row2, "A la animacion", 20f, cb.OnCopyToAnimation, out _);
            AddActionBtn(row2, "Re-medir", 20f, cb.OnRemeasure, out _);
            AddActionBtn(row2, "Automatico", 20f, cb.OnResetFrame, out _);

            var footRow = MakeFieldRow(parent, "AnimFootprintRow");
            refs.AnimCollFootWInput = AddNumberField(footRow, "Pisada w", cb.OnFootprintWidth);
            refs.AnimCollFootDInput = AddNumberField(footRow, "fondo", cb.OnFootprintDepth);

            var scaleRow = MakeFieldRow(parent, "AnimHurtScaleRow");
            refs.AnimCollHurtScaleInput = AddNumberField(scaleRow, "Cuerpo x", cb.OnHurtScale);

            var readoutGo = CreateUI("AnimCollisionReadout", parent);
            readoutGo.AddComponent<LayoutElement>().preferredHeight = 36f;
            refs.AnimCollReadout                    = readoutGo.AddComponent<TextMeshProUGUI>();
            refs.AnimCollReadout.text               = "";
            refs.AnimCollReadout.fontSize           = 9f;
            refs.AnimCollReadout.color              = TEXT_SECONDARY;
            refs.AnimCollReadout.alignment          = TextAlignmentOptions.TopLeft;
            refs.AnimCollReadout.enableWordWrapping = true;
        }

        private static Transform MakeButtonRow(Transform parent, string name)
        {
            var row = CreateUI(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = 20f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            return row.transform;
        }

        private static void BuildPacingEditors(Transform parent, ref UIRefs refs,
            Action<string> onEntitySpeed, Action<string> onStateSpeed,
            Action<string> onVariantSpeed, Action<bool> onHoldLastFrame,
            Action<int> onLayoutChanged,
            Action<string> onRepeatFrom, Action<string> onRepeatCount)
        {
            var speedRow = MakeFieldRow(parent, "AnimSpeedRow");
            refs.AnimEntitySpeedInput = AddNumberField(speedRow, "Entity x", onEntitySpeed);
            refs.AnimStateSpeedInput  = AddNumberField(speedRow, "State x",  onStateSpeed);

            var variantRow = MakeFieldRow(parent, "AnimVariantPacingRow");
            refs.AnimVariantSpeedInput = AddNumberField(variantRow, "Variant x", onVariantSpeed);

            var holdLblGo = CreateUI("HoldLabel", variantRow);
            holdLblGo.AddComponent<LayoutElement>().preferredWidth = 34f;
            var holdLbl       = holdLblGo.AddComponent<TextMeshProUGUI>();
            holdLbl.text      = "Hold";
            holdLbl.fontSize  = 10f;
            holdLbl.color     = TEXT_SECONDARY;
            holdLbl.alignment = TextAlignmentOptions.MidlineRight;

            var toggleGo = CreateUI("HoldToggle", variantRow);
            toggleGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var toggleBg   = toggleGo.AddComponent<Image>();
            toggleBg.color = BG_SURFACE;
            refs.AnimHoldToggle = toggleGo.AddComponent<Toggle>();
            refs.AnimHoldToggle.targetGraphic = toggleBg;

            var checkGo = CreateUI("Check", toggleGo.transform);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(3f, 3f);
            checkRt.offsetMax = new Vector2(-3f, -3f);
            var checkImg   = checkGo.AddComponent<Image>();
            checkImg.color = ACCENT;
            refs.AnimHoldToggle.graphic = checkImg;
            if (onHoldLastFrame != null)
                refs.AnimHoldToggle.onValueChanged.AddListener(v => onHoldLastFrame(v));

            // The stretch a sustained cast repeats (cast variants only): where the gesture holds
            // while the same spell keeps being cast. Frames = 0 means no repeat.
            var repeatRow = MakeFieldRow(parent, "AnimRepeatRow");
            refs.AnimRepeatFromInput  = AddNumberField(repeatRow, "Repeat @", onRepeatFrom);
            refs.AnimRepeatCountInput = AddNumberField(repeatRow, "Frames",   onRepeatCount);
            refs.AnimRepeatFromInput.contentType  = TMP_InputField.ContentType.IntegerNumber;
            refs.AnimRepeatCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;

            refs.AnimLayoutDd = AddLabeledDropdown(parent, "Layout", onLayoutChanged);
        }

        private static Transform MakeFieldRow(Transform parent, string name)
        {
            var rowGo = CreateUI(name, parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            return rowGo.transform;
        }

        /// <summary>A short label plus a committed number field, sized for two per row.</summary>
        private static TMP_InputField AddNumberField(Transform row, string label,
                                                     Action<string> onCommit)
        {
            var lblGo = CreateUI($"Lbl_{label}", row);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 52f;
            var lbl       = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text      = label;
            lbl.fontSize  = 10f;
            lbl.color     = TEXT_SECONDARY;
            lbl.alignment = TextAlignmentOptions.MidlineLeft;

            var input = UIInputField.AddCommit(row, "1", onCommit, 18f, 10f);
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            var le = input.GetComponent<LayoutElement>();
            if (le != null) le.flexibleWidth = 1f;
            return input;
        }

        /// <summary>A label plus an empty dropdown on one row.</summary>
        private static TMP_Dropdown AddLabeledDropdown(Transform parent, string label,
                                                       Action<int> onChanged)
        {
            var rowGo = CreateUI($"AnimRow_{label}", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 56f;
            var lblTmp       = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label;
            lblTmp.fontSize  = 10f;
            lblTmp.color     = TEXT_SECONDARY;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var ddHost = CreateUI("DropdownHost", rowGo.transform);
            ddHost.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // The cast picks an overload: a string[] satisfies both IList and IReadOnlyList,
            // which are unrelated interfaces, so the call is ambiguous without it.
            var dd = UIDropdown.Add(ddHost.transform,
                (System.Collections.Generic.IReadOnlyList<string>)System.Array.Empty<string>(), -1, 10f);
            if (onChanged != null) dd.onValueChanged.AddListener(i => onChanged(i));
            return dd;
        }

        /// <summary>
        /// Play/pause, one frame either way, and the reverse toggle. Reverse is a real
        /// playback mode rather than a strip trick because the game has one: the dwarf's
        /// sheathe is his draw played backwards.
        /// </summary>
        private static void BuildTransportRow(Transform parent, ref UIRefs refs,
            Action onTogglePlay, Action onStepBack, Action onStepForward, Action onToggleReverse)
        {
            var rowGo = CreateUI("AnimTransport", parent);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 22f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 4f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            AddActionBtn(rowGo.transform, "|<", 22f, onStepBack, out _);
            refs.AnimPlayPauseImg = AddActionBtn(rowGo.transform, "Pause", 22f, onTogglePlay,
                                                 out refs.AnimPlayPauseTmp);
            AddActionBtn(rowGo.transform, ">|", 22f, onStepForward, out _);
            refs.AnimReverseImg = AddActionBtn(rowGo.transform, "Reverse", 22f, onToggleReverse,
                                               out refs.AnimReverseTmp);
        }

        /// <summary>
        /// The frame strip: one clickable cell per frame of the pose on screen, numbered, with
        /// the frame being rendered highlighted.
        ///
        /// <para>Cells are realised ONCE and shown or hidden afterwards. Rebuilding a row list
        /// on every change is what made the Items editor take 3.5 s to open, at a tenth of this
        /// scale — and here the trigger would be every state, direction and variant switch.</para>
        /// </summary>
        private static void BuildFrameStrip(Transform parent, ref UIRefs refs, Action<int> onCell)
        {
            refs.AnimStripCellImgs = new Image[ANIM_STRIP_SLOTS];
            refs.AnimStripThumbs   = new Image[ANIM_STRIP_SLOTS];
            refs.AnimStripLabels   = new TextMeshProUGUI[ANIM_STRIP_SLOTS];

            int rows = (ANIM_STRIP_SLOTS + ANIM_STRIP_PER_ROW - 1) / ANIM_STRIP_PER_ROW;
            for (int row = 0; row < rows; row++)
            {
                var rowGo = CreateUI($"AnimStripRow{row}", parent);
                rowGo.AddComponent<LayoutElement>().preferredHeight = ANIM_STRIP_CELL;

                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing                = 2f;
                hlg.childForceExpandWidth  = true;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth      = true;
                hlg.childControlHeight     = true;

                for (int col = 0; col < ANIM_STRIP_PER_ROW; col++)
                {
                    int slot = row * ANIM_STRIP_PER_ROW + col;
                    if (slot >= ANIM_STRIP_SLOTS) break;
                    BuildStripCell(rowGo.transform, ref refs, slot, onCell);
                }
            }

            var overflowGo = CreateUI("AnimStripOverflow", parent);
            overflowGo.AddComponent<LayoutElement>().preferredHeight = 12f;
            refs.AnimStripOverflowText           = overflowGo.AddComponent<TextMeshProUGUI>();
            refs.AnimStripOverflowText.text      = "";
            refs.AnimStripOverflowText.fontSize  = 9f;
            refs.AnimStripOverflowText.color     = TEXT_MUTED;
            refs.AnimStripOverflowText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void BuildStripCell(Transform parent, ref UIRefs refs, int slot,
                                           Action<int> onCell)
        {
            var cellGo = CreateUI($"Frame{slot}", parent);
            var bg     = cellGo.AddComponent<Image>();
            bg.color   = SLOT_BG;

            var btn = cellGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            if (onCell != null) btn.onClick.AddListener(() => onCell(slot));

            var thumbGo = CreateUI("Thumb", cellGo.transform);
            var thumbRt = thumbGo.GetComponent<RectTransform>();
            thumbRt.anchorMin = Vector2.zero;
            thumbRt.anchorMax = Vector2.one;
            thumbRt.offsetMin = new Vector2(2f, 2f);
            thumbRt.offsetMax = new Vector2(-2f, -8f);
            var thumb            = thumbGo.AddComponent<Image>();
            thumb.preserveAspect = true;
            thumb.raycastTarget  = false;
            thumb.enabled        = false;

            var lblGo = CreateUI("Index", cellGo.transform);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 0f);
            lblRt.anchorMax = new Vector2(1f, 0f);
            lblRt.pivot     = new Vector2(0.5f, 0f);
            lblRt.anchoredPosition = Vector2.zero;
            lblRt.sizeDelta = new Vector2(0f, 9f);
            var lbl              = lblGo.AddComponent<TextMeshProUGUI>();
            lbl.text             = slot.ToString();
            lbl.fontSize         = 8f;
            lbl.color            = TEXT_MUTED;
            lbl.alignment        = TextAlignmentOptions.Center;
            lbl.raycastTarget    = false;

            refs.AnimStripCellImgs[slot] = bg;
            refs.AnimStripThumbs[slot]   = thumb;
            refs.AnimStripLabels[slot]   = lbl;
            cellGo.SetActive(false);
        }

        /// <summary>
        /// The 3x3 facing pad. Nine buttons in reading order so the runtime editor can index
        /// them with one number; slot 4 is the centre and toggles the eight-at-once view.
        /// </summary>
        private static void BuildDirectionPad(Transform parent, ref UIRefs refs,
                                              Action<int> onSlot)
        {
            refs.AnimDirBtnImgs = new Image[9];
            refs.AnimDirBtnTmps = new TextMeshProUGUI[9];

            for (int row = 0; row < 3; row++)
            {
                var rowGo = CreateUI($"AnimDirRow{row}", parent);
                rowGo.AddComponent<LayoutElement>().preferredHeight = ANIM_DIR_BTN;

                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing                = 4f;
                hlg.childForceExpandWidth  = true;
                hlg.childForceExpandHeight = true;
                hlg.childControlWidth      = true;
                hlg.childControlHeight     = true;

                for (int col = 0; col < 3; col++)
                {
                    int slot = row * 3 + col;
                    refs.AnimDirBtnImgs[slot] = AddActionBtn(rowGo.transform, AnimDirLabels[slot],
                        ANIM_DIR_BTN, () => onSlot?.Invoke(slot), out refs.AnimDirBtnTmps[slot]);
                }
            }
        }
    }
}
