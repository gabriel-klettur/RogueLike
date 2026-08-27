using System.Collections.Generic;
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
        // ── Properties Panel ──────────────────────────────────────────────────────
        // Mirrors Python entities_properties_panel: structured form grouped by
        // sections (Identity / Stats / AI / Spawn / Auto-Cast / Assets).
        // Sections are scrollable; each section is filled by the runtime editor
        // when an entity is selected.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
        {
            refs.PropsDropdown = MakeDrop("EntitiesPropsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // Hint text (visible when nothing is selected)
            var hintGo = CreateUI("PropsHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 28f;
            refs.PropsHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.PropsHintText.text               = "Select an entity from the Picker.";
            refs.PropsHintText.fontSize           = 11f;
            refs.PropsHintText.fontStyle          = FontStyles.Italic;
            refs.PropsHintText.color              = TEXT_SECONDARY;
            refs.PropsHintText.alignment          = TextAlignmentOptions.Center;
            refs.PropsHintText.enableWordWrapping = true;

            // Scrollable form
            var (scroll, content) = MakePropsScroll(t);
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 320f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            refs.PropsFormRoot       = content;
            refs.PropsIdentitySection = MakeFormSection(content, "Identity");
            refs.PropsStatsSection    = MakeFormSection(content, "Stats");
            refs.PropsAISection       = MakeFormSection(content, "AI");
            refs.PropsSpawnSection    = MakeFormSection(content, "Spawn");
            refs.PropsAutoCastSection = MakeFormSection(content, "Auto-Cast");
            refs.PropsAssetsSection   = MakeFormSection(content, "Assets");

            // Boss Editor handoff button — hidden until the selected entity is a boss.
            var bossBtn = CreateUI("BossHandoffBtn", t);
            bossBtn.AddComponent<LayoutElement>().preferredHeight = 30f;
            var bossBtnImg = bossBtn.AddComponent<Image>();
            bossBtnImg.color = new Color(0.20f, 0.30f, 0.55f, 1f);
            var bossBtnComponent = bossBtn.AddComponent<Button>();
            bossBtnComponent.targetGraphic = bossBtnImg;
            AddCenteredText(bossBtn.transform, "Open Boss Editor →", 11f, FontStyles.Bold, TEXT_PRIMARY);
            refs.BossHandoffBtnGo = bossBtn;
            bossBtn.SetActive(false);

            refs.PropsDropdown.SetActive(false);
        }

        private static (ScrollRect, RectTransform) MakePropsScroll(Transform parent)
        {
            var srGo = CreateUI("PropsScroll", parent);
            var sr   = srGo.AddComponent<ScrollRect>();
            sr.horizontal             = false;
            sr.vertical               = true;
            sr.movementType           = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity      = 18f;
            srGo.AddComponent<RectMask2D>();

            var viewportGo                   = CreateUI("Viewport", srGo.transform);
            var vr                           = viewportGo.GetComponent<RectTransform>();
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;
            sr.viewport                      = vr;

            var contentGo                    = CreateUI("Content", viewportGo.transform);
            var cr                           = contentGo.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot     = new Vector2(0f, 1f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta        = new Vector2(0f, 0f);

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding             = new RectOffset(2, 2, 2, 6);
            vlg.spacing             = 8f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            var fitter           = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = cr;
            return (sr, cr);
        }

        private static RectTransform MakeFormSection(Transform parent, string title)
        {
            var go = CreateUI($"Section_{title}", parent);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding             = new RectOffset(0, 0, 0, 0);
            vlg.spacing             = 2f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Section header
            var hdrGo = CreateUI("Header", go.transform);
            hdrGo.AddComponent<LayoutElement>().preferredHeight = 18f;
            var hdrImg          = hdrGo.AddComponent<Image>();
            hdrImg.color        = MENUBAR_BG;
            var hdrTmp          = AddCenteredText(hdrGo.transform, title.ToUpper(), 10f, FontStyles.Bold, ACCENT);
            hdrTmp.alignment    = TextAlignmentOptions.MidlineLeft;
            hdrTmp.margin       = new Vector4(8f, 0f, 0f, 0f);
            hdrTmp.characterSpacing = 1.5f;

            // Body container — runtime editor fills this with rows
            var bodyGo = CreateUI("Body", go.transform);
            var body   = bodyGo.GetComponent<RectTransform>();
            var bvlg   = bodyGo.AddComponent<VerticalLayoutGroup>();
            bvlg.padding             = new RectOffset(8, 8, 4, 4);
            bvlg.spacing             = 2f;
            bvlg.childForceExpandWidth  = true;
            bvlg.childForceExpandHeight = false;
            bvlg.childControlWidth      = true;
            bvlg.childControlHeight     = true;
            bodyGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return body;
        }

        // ── Public helper used by the runtime editor to fill rows ────────────────

        public static void AddPropertyRow(RectTransform sectionBody, string label, string value)
        {
            var rowGo = CreateUI($"Row_{label}", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 16f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing             = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 110f;
            var lblTmp           = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text          = label;
            lblTmp.fontSize      = 10f;
            lblTmp.color         = TEXT_SECONDARY;
            lblTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode  = TextOverflowModes.Truncate;

            var valGo = CreateUI("Value", rowGo.transform);
            valGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var valTmp           = valGo.AddComponent<TextMeshProUGUI>();
            valTmp.text          = value ?? "";
            valTmp.fontSize      = 10f;
            valTmp.fontStyle     = FontStyles.Bold;
            valTmp.color         = TEXT_PRIMARY;
            valTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            valTmp.enableWordWrapping = false;
            valTmp.overflowMode  = TextOverflowModes.Truncate;
        }

        /// <summary>
        /// A property row whose value is a committed input field rather than a label.
        ///
        /// The panel used to be built entirely from <see cref="AddPropertyRow"/> — two
        /// TextMeshProUGUI per row and no input widget anywhere in the four UI-builder
        /// partials — so the one screen named after entity authoring could only ever
        /// display a monster's combat numbers. Every balance iteration meant leaving
        /// Play Mode for the Inspector.
        /// </summary>
        /// <param name="onCommit">
        /// Raised on end-of-edit with the raw text. The caller parses and validates:
        /// what counts as a legal value belongs to the field, not to the widget.
        /// </param>
        public static TMP_InputField AddEditableRow(RectTransform sectionBody, string label,
                                                    string value, System.Action<string> onCommit,
                                                    TMP_InputField.ContentType contentType =
                                                        TMP_InputField.ContentType.DecimalNumber)
        {
            var rowGo = CreateUI($"Row_{label}", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 18f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 110f;
            var lblTmp                = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text               = label;
            lblTmp.fontSize           = 10f;
            lblTmp.color              = TEXT_SECONDARY;
            lblTmp.alignment          = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode       = TextOverflowModes.Truncate;

            var input = UIInputField.AddCommit(rowGo.transform, value ?? "", onCommit, 18f, 10f);
            input.contentType = contentType;
            var le = input.GetComponent<LayoutElement>();
            if (le != null) le.flexibleWidth = 1f;
            return input;
        }

        /// <summary>
        /// A property row whose value is a checkbox — added alongside
        /// <see cref="AddEditableRow"/> for boolean fields. First consumer is
        /// <c>MonsterDefinition.autoCast</c>, which used to render as a plain label with no
        /// widget anywhere in the four UI-builder partials: the whole NPC-casting feature
        /// shipped dormant because nothing could turn it on from inside the game.
        /// </summary>
        public static Toggle AddToggleRow(RectTransform sectionBody, string label, bool value,
                                          System.Action<bool> onChanged)
        {
            var rowGo = CreateUI($"Row_{label}", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 18f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 110f;
            var lblTmp                = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text               = label;
            lblTmp.fontSize           = 10f;
            lblTmp.color              = TEXT_SECONDARY;
            lblTmp.alignment          = TextAlignmentOptions.MidlineLeft;
            lblTmp.enableWordWrapping = false;
            lblTmp.overflowMode       = TextOverflowModes.Truncate;

            var toggleGo = CreateUI("Toggle", rowGo.transform);
            toggleGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var bg = toggleGo.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.16f, 0.20f, 1f);
            var toggle = toggleGo.AddComponent<Toggle>();
            toggle.targetGraphic = bg;

            var checkGo = CreateUI("Check", toggleGo.transform);
            var checkRt = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(3f, 3f);
            checkRt.offsetMax = new Vector2(-3f, -3f);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = ACCENT;
            toggle.graphic = checkImg;

            toggle.SetIsOnWithoutNotify(value);
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));

            return toggle;
        }

        /// <summary>
        /// One entry of <c>MonsterDefinition.autoCastList</c>: a dropdown scoped to every key in
        /// the injected <c>SpellCatalog</c> plus a remove button. The dropdown IS the validation —
        /// unlike a free-text field, it cannot produce a key that fails to resolve at spawn time
        /// (<c>EntitySetup.ConfigureMonsterAutoCast</c> silently skips unknown keys with a
        /// console warning, which is exactly the failure mode this widget makes unreachable).
        /// </summary>
        public static TMP_Dropdown AddSpellListRow(RectTransform sectionBody, string label,
            IReadOnlyList<string> catalogKeys, string currentKey,
            System.Action<string> onChanged, System.Action onRemove)
        {
            var rowGo = CreateUI($"Row_{label}", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var lblGo = CreateUI("Label", rowGo.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 32f;
            var lblTmp       = lblGo.AddComponent<TextMeshProUGUI>();
            lblTmp.text      = label;
            lblTmp.fontSize  = 10f;
            lblTmp.color     = TEXT_SECONDARY;
            lblTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var ddHost = CreateUI("DropdownHost", rowGo.transform);
            ddHost.AddComponent<LayoutElement>().flexibleWidth = 1f;

            int selectedIndex = -1;
            if (catalogKeys != null)
            {
                for (int i = 0; i < catalogKeys.Count; i++)
                {
                    if (string.Equals(catalogKeys[i], currentKey, System.StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            var dd = UIDropdown.Add(ddHost.transform, catalogKeys, selectedIndex, 10f);
            dd.onValueChanged.AddListener(i =>
            {
                if (catalogKeys != null && i >= 0 && i < catalogKeys.Count) onChanged?.Invoke(catalogKeys[i]);
            });

            var rmImg = AddActionBtn(rowGo.transform, "x", 18f, onRemove, out _);
            var rmLe  = rmImg.GetComponent<LayoutElement>();
            if (rmLe != null) rmLe.preferredWidth = 22f;

            return dd;
        }

        /// <summary>
        /// The "add a new auto-cast spell" row — a catalog dropdown plus an Add button. The
        /// caller only builds this when the catalog resolved at least one key; an empty catalog
        /// falls back to a plain hint row via <see cref="AddPropertyRow"/> instead.
        /// </summary>
        public static void AddSpellAddRow(RectTransform sectionBody, IReadOnlyList<string> catalogKeys,
            System.Action<string> onAdd)
        {
            var rowGo = CreateUI("Row_AddSpell", sectionBody);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 20f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 6f;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            var ddHost = CreateUI("DropdownHost", rowGo.transform);
            ddHost.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var dd = UIDropdown.Add(ddHost.transform, catalogKeys, 0, 10f);

            string selected = catalogKeys != null && catalogKeys.Count > 0 ? catalogKeys[0] : null;
            dd.onValueChanged.AddListener(i =>
            {
                if (catalogKeys != null && i >= 0 && i < catalogKeys.Count) selected = catalogKeys[i];
            });

            var addImg = AddActionBtn(rowGo.transform, "+ Add", 18f, () => onAdd?.Invoke(selected), out _);
            var addLe  = addImg.GetComponent<LayoutElement>();
            if (addLe != null) addLe.preferredWidth = 54f;
        }

        public static void ClearSection(RectTransform sectionBody)
        {
            if (sectionBody == null) return;
            for (int i = sectionBody.childCount - 1; i >= 0; i--)
            {
                var child = sectionBody.GetChild(i);
                if (Application.isPlaying) Object.Destroy(child.gameObject);
                else                       Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
