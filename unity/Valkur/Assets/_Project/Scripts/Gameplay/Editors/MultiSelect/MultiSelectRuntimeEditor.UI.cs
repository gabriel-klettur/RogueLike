using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The tool's one panel: which domains are pickable, what is selected right now, and the
    /// four things that can be done to it.
    ///
    /// <para>THE COUNTS ARE THE POINT OF THE PANEL. A cross-domain group looks, on screen,
    /// exactly like a handful of unrelated outlines; "3 edificios, 1 luz, 1 particula" is the
    /// only place the author can read that the next click will write three files.</para>
    ///
    /// <para>Buttons rather than hotkeys for Duplicar and Limpiar, deliberately. An owned tool
    /// key would need an action in <c>ValkurInputActions</c> AND a descriptor in the closed
    /// <c>InputActionCatalog</c> — worth it for a gesture used a hundred times a session, not
    /// for one the panel is already showing. Delete, Undo, Redo and Save keep their shared
    /// keys because those already exist for every editor.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
    {
        private const float PANEL_WIDTH  = 250f;
        private const float PANEL_X      = 8f;
        private const float ROW_H        = 26f;
        private const float CHIP_H       = 24f;
        private const float HDR_H        = 18f;
        private const float COUNT_H      = 20f;
        /// <summary>Measured: the opening hint and the copy confirmation both wrap to THREE
        /// lines at this panel width and need 38 px. At the 26 declared before, the layout had
        /// to find the difference somewhere and shrank every row proportionally — headers to
        /// 9 px from 18, buttons to 13 from 26 — which is the same squash the General Editor's
        /// old 360-px constant produced.</summary>
        private const float STATUS_H     = 40f;
        /// <summary>The clipboard line. Taller than a bare label wants because it now carries
        /// a button, and a 16 px target is one an author misses.</summary>
        private const float CLIP_H       = 20f;

        private Canvas         _canvas;
        private GameObject     _panelRoot;
        private DraggablePanel _drag;

        private TextMeshProUGUI _statusTmp;
        private Image           _deleteBg;
        private Button          _deleteBtn;
        private TextMeshProUGUI _deleteTmp;
        private TextMeshProUGUI _countTmp;
        private TextMeshProUGUI _clipboardTmp;
        private Image           _clearClipBg;
        private TextMeshProUGUI _undoTmp;
        private TextMeshProUGUI _redoTmp;

        private readonly Dictionary<string, (Image bg, Button btn, TextMeshProUGUI tmp)> _chips =
            new Dictionary<string, (Image, Button, TextMeshProUGUI)>(4);

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("SelectionEditorCanvas", sortOrder: 112);
            _canvas.transform.SetParent(transform, false);

            _panelRoot = EditorUIHelpers.MakeDropPanel(
                name:       "SelectionPanel",
                canvasT:    _canvas.transform,
                dock:       TileEditorUIHelpers.PanelDock.TopLeft,
                xOff:       PANEL_X,
                yOff:       TileEditorUIHelpers.PANEL_TOP_OFFSET,
                width:      PANEL_WIDTH,
                height:     ComputePanelHeight(),
                title:      "Seleccion multiple",
                contentOut: out var content,
                dragOut:    out _drag);

            // The panel carries the only controls this tool has, so hiding it would leave the
            // editor open with no way to do anything and no way to bring it back — the
            // soft-lock DraggablePanel.ShowCloseButton exists to prevent and that the Controls
            // editor shipped once.
            _drag.ShowCloseButton = false;
            _drag.Owner = EditorName;

            MakeHeader(content, "DOMINIOS");
            for (int i = 0; i < _domains.Length; i++) BuildChip(content, _domains[i]);

            MakeHeader(content, "SELECCION");
            _countTmp = UILabel.AddCenteredText(
                EditorUIHelpers.CreateUI("Count", content).transform,
                "nada", 11f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            _countTmp.transform.parent.gameObject.AddComponent<LayoutElement>().preferredHeight = COUNT_H;
            _countTmp.raycastTarget = false;

            BuildSelectionList(content);

            MakeHeader(content, "ACCIONES");
            var copyRow = EditorUIHelpers.CreateUI("CopyRow", content);
            var copyLe = copyRow.AddComponent<LayoutElement>();
            copyLe.preferredHeight = ROW_H;
            copyLe.flexibleHeight  = 0f;   // the LayoutElement + HorizontalLayoutGroup trap
            var copyHlg = copyRow.AddComponent<HorizontalLayoutGroup>();
            copyHlg.spacing                = 4f;
            copyHlg.childControlWidth      = true;
            copyHlg.childForceExpandWidth  = true;
            copyHlg.childControlHeight     = true;
            copyHlg.childForceExpandHeight = true;
            // Buttons AND keys. The keys are what an author actually uses; the buttons are what
            // tells them the keys exist, which is the same reason every editor labels its
            // destructive action with its shortcut.
            EditorUIHelpers.AddActionBtn(copyRow.transform, "Copiar (Ctrl+C)", ROW_H, CopySelection, out _, fontSize: 9f);
            EditorUIHelpers.AddActionBtn(copyRow.transform, "Pegar (Ctrl+V)",  ROW_H, PasteAtGhost,  out _, fontSize: 9f);

            // ── What is on the clipboard, and the way out of it ────────────────
            //
            // The clear button lives ON the line that describes the clipboard rather than
            // beside Copiar and Pegar, because it is the control for THAT state: it is only
            // meaningful while something is copied, and it is hidden when nothing is. A button
            // that does nothing is a control reporting a state it does not have, which is the
            // shape this tool has already had to fix twice.
            var clipRow = EditorUIHelpers.CreateUI("ClipboardRow", content);
            var clipLe  = clipRow.AddComponent<LayoutElement>();
            clipLe.preferredHeight = CLIP_H;
            clipLe.flexibleHeight  = 0f;   // the LayoutElement + HorizontalLayoutGroup trap

            var clipHlg = clipRow.AddComponent<HorizontalLayoutGroup>();
            clipHlg.spacing                = 4f;
            clipHlg.childControlWidth      = true;
            // NOT force-expand: the label takes the slack and the button keeps its own width.
            // With force-expand on, the two would split the row evenly and "Vaciar" would be
            // half the panel.
            clipHlg.childForceExpandWidth  = false;
            clipHlg.childControlHeight     = true;
            clipHlg.childForceExpandHeight = true;

            var labelGo = EditorUIHelpers.CreateUI("ClipboardLabel", clipRow.transform);
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;    // the label absorbs the row
            _clipboardTmp = labelGo.AddComponent<TextMeshProUGUI>();
            _clipboardTmp.fontSize      = 10f;
            _clipboardTmp.fontStyle     = FontStyles.Italic;
            _clipboardTmp.color         = UITheme.TEXT_MUTED;
            _clipboardTmp.alignment     = TextAlignmentOptions.MidlineLeft;
            _clipboardTmp.raycastTarget = false;

            _clearClipBg = EditorUIHelpers.AddActionBtn(clipRow.transform, "Vaciar", CLIP_H,
                () => ClearClipboard(announce: true), out _, fontSize: 8f, style: FontStyles.Normal);
            // A button in a HorizontalLayoutGroup with childControlWidth and no force-expand is
            // laid out at its MINIMUM unless it declares a width — which in the Controls editor
            // collapsed two buttons into one overprinted character column.
            var clearLe = _clearClipBg.GetComponent<LayoutElement>();
            clearLe.preferredWidth = 52f;
            clearLe.flexibleWidth  = 0f;

            EditorUIHelpers.AddActionBtn(content, "Duplicar", ROW_H, DuplicateSelection, out _);
            EditorUIHelpers.AddActionBtn(content, "Limpiar seleccion", ROW_H, ClearSelection, out _);
            _deleteBg  = EditorUIHelpers.AddDangerBtn(content, DELETE_LABEL, ROW_H, DeleteSelection, out _deleteTmp);
            _deleteBtn = _deleteBg.GetComponent<Button>();

            var undoRow = EditorUIHelpers.CreateUI("UndoRow", content);
            var undoLe = undoRow.AddComponent<LayoutElement>();
            undoLe.preferredHeight = ROW_H;
            // flexibleHeight = 0, for the reason the General Editor's tab strip records: this
            // row carries a LayoutElement AND a HorizontalLayoutGroup, and without this the
            // group's flexibleHeight of 1 wins and the row swallows every spare pixel in the
            // panel. Measured here at flexible=1.00 — benign only because the panel currently
            // has no slack, which is not a property anything guarantees.
            undoLe.flexibleHeight = 0f;
            var hlg = undoRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                 = 4f;
            hlg.childControlWidth       = true;
            hlg.childForceExpandWidth   = true;
            hlg.childControlHeight      = true;
            hlg.childForceExpandHeight  = true;
            EditorUIHelpers.AddActionBtn(undoRow.transform, "Deshacer", ROW_H, DoUndo, out _undoTmp);
            EditorUIHelpers.AddActionBtn(undoRow.transform, "Rehacer",  ROW_H, DoRedo, out _redoTmp);

            _statusTmp = EditorUIHelpers.MakeStatusText(content);

            RefreshPanel();
        }

        private static void MakeHeader(Transform parent, string text)
        {
            var go  = EditorUIHelpers.CreateUI($"Hdr_{text}", parent);
            go.AddComponent<LayoutElement>().preferredHeight = HDR_H;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text            = text;
            tmp.fontSize        = 10f;
            tmp.fontStyle       = FontStyles.Bold;
            tmp.color           = UITheme.ACCENT;
            tmp.alignment       = TextAlignmentOptions.MidlineLeft;
            tmp.characterSpacing = 1.2f;
            tmp.raycastTarget   = false;
        }

        /// <summary>One filter chip. Its label carries the domain's live count, so the row
        /// answers "is there anything of this kind selected" without a second readout.</summary>
        private void BuildChip(Transform parent, ISelectionDomain domain)
        {
            var img = EditorUIHelpers.AddActionBtn(parent, domain.Label, CHIP_H,
                () => ToggleDomain(domain), out var tmp, fontSize: 10f);

            // A swatch in the domain's own colour, pinned to the left edge. It is what ties
            // the chip to the outlines drawn in the world; the LABEL stays high-contrast text
            // so the count beside it is readable on the gold "enabled" tint.
            var sw = EditorUIHelpers.CreateUI("Swatch", img.transform);
            var srt = (RectTransform)sw.transform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot     = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(4f, CHIP_H - 8f);
            srt.anchoredPosition = new Vector2(4f, 0f);
            var swImg = sw.AddComponent<Image>();
            swImg.color = domain.Tint;
            swImg.raycastTarget = false;

            _chips[domain.Id] = (img, img.GetComponent<Button>(), tmp);
        }

        private void ToggleDomain(ISelectionDomain domain)
        {
            if (domain == null) return;
            if (!_enabled.Remove(domain.Id)) _enabled.Add(domain.Id);
            RefreshPanel();
            SetStatus(_enabled.Contains(domain.Id)
                ? $"{domain.Label}: se pueden seleccionar."
                : $"{domain.Label}: ignorados al seleccionar (los ya seleccionados siguen ahi).");
        }

        /// <summary>
        /// The panel height, DERIVED from what the panel actually holds rather than typed.
        ///
        /// <para>It was a hand-written 330 — the same shape as the General Editor's old 360,
        /// which was sized when the registry held eleven editors and clipped at sixteen. A
        /// constant here is correct exactly until somebody adds a row.</para>
        /// </summary>
        private float ComputePanelHeight()
        {
            float content = 3 * HDR_H                       // DOMINIOS / SELECCION / ACCIONES
                          + _domains.Length * CHIP_H        // one chip per domain
                          + COUNT_H
                          + LIST_H                          // the selected-items list
                          + ROW_H                           // the copy / paste row
                          + CLIP_H                          // what is on the clipboard
                          + 3 * ROW_H                       // Duplicar / Limpiar / Borrar
                          + ROW_H                           // the undo row
                          + STATUS_H;

            int children = 3 + _domains.Length + 1 + 1 + 1 + 1 + 3 + 1 + 1;
            content += Mathf.Max(0, children - 1) * CONTENT_SPACING;
            content += CONTENT_PAD_TOTAL_V;
            return content + TileEditorUIHelpers.PANEL_HDR_H + 1f;   // header + separator
        }

        private const float CONTENT_SPACING     = 4f;
        private const float CONTENT_PAD_TOTAL_V = 12f;

        /// <summary>
        /// Re-apply the derived height to the live panel. Called after a workspace restore.
        ///
        /// <para>The workspace remembers the size the panel HAD, and this panel's size is not
        /// a preference — it is computed from what the panel holds. A document written before
        /// the list and the clipboard row existed restored a 330-px window over 496 px of
        /// content, and uGUI absorbed the difference by halving every row rather than by
        /// clipping, so nothing looked broken enough to report. Position is the author's;
        /// height is the content's.</para>
        /// </summary>
        private void ApplyDerivedPanelHeight()
        {
            if (_panelRoot == null) return;
            var rt = (RectTransform)_panelRoot.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, ComputePanelHeight());
        }

        private void SetPanelVisible(bool visible)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
        }

        private void SetStatus(string text)
        {
            if (_statusTmp != null) _statusTmp.text = text;
        }

        private void RefreshPanel()
        {
            // "1 — 1 edificios" printed the same number twice with a mismatched plural. With
            // one domain in play the breakdown IS the total, so the total is redundant.
            if (_countTmp != null) _countTmp.text = DescribeSelectionByDomain();

            // Any change to WHAT is selected makes a primed Borrar mean something other than
            // what the author armed it for.
            if (_deleteArmed && _selection.Count != _deleteArmedCount) DisarmDelete(repaint: false);
            RefreshDeleteButton();

            // What is on the clipboard is otherwise invisible: the ghost only shows while the
            // pointer is over the map, so with the cursor on this panel there would be nothing
            // at all saying a paste is pending.
            if (_clipboardTmp != null)
                _clipboardTmp.text = HasClipboard
                    ? $"portapapeles: {Elements(ClipboardCount)}"
                    : "portapapeles vacio";

            // Hidden rather than disabled: a greyed button still invites the click that will
            // do nothing, and there is no state here worth advertising as unavailable.
            if (_clearClipBg != null && _clearClipBg.gameObject.activeSelf != HasClipboard)
                _clearClipBg.gameObject.SetActive(HasClipboard);

            RefreshSelectionList();

            for (int i = 0; i < _domains.Length; i++)
            {
                var d = _domains[i];
                if (!_chips.TryGetValue(d.Id, out var chip) || chip.bg == null) continue;

                bool on        = _enabled.Contains(d.Id);
                bool available = d.Available;
                int  selected  = _selection.CountIn(d.Id);

                if (chip.tmp != null)
                {
                    chip.tmp.text = available
                        ? $"{d.Label}  ({selected})"
                        : $"{d.Label}  (no disponible)";
                    // The domain tint names the outlines in the WORLD, where the background is
                    // ground. On the chip it sits on ACCENT_BG (gold at 15 %), and the building
                    // blue on gold was the worst-contrast pair of the three. The chip says
                    // on/off with the button tint and legibility with plain text; the colour
                    // link to the outline is carried by the swatch below instead.
                    chip.tmp.color = available
                        ? (on ? UITheme.TEXT_PRIMARY : UITheme.TEXT_MUTED)
                        : UITheme.TEXT_MUTED;
                }

                // Through UIButton.SetTint, never by writing the Image: a Button on the default
                // ColorTint transition multiplies its ColorBlock into the graphic, so a raw
                // colour write renders as the product and the selected row comes out DARKER
                // than the unselected ones. Measured at 3 vs 108 luminance in the Skills editor.
                UIButton.SetTint(chip.btn, available && on ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL);
            }

            RefreshUndoButtons();
        }

        private void RefreshUndoButtons()
        {
            if (_undoTmp != null)
                _undoTmp.color = _undo.CanUndo ? UITheme.TEXT_PRIMARY : UITheme.TEXT_MUTED;
            if (_redoTmp != null)
                _redoTmp.color = _undo.CanRedo ? UITheme.TEXT_PRIMARY : UITheme.TEXT_MUTED;
        }
    }
}
