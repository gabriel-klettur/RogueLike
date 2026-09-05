using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — the cheap editable cell.
    ///
    /// <para>WHY. An editable column used to draw a live <c>TMP_InputField</c> in every row:
    /// a background Image, a viewport, a text mesh and a placeholder mesh, four Graphics
    /// where a value needs one. Measured across the 29 visible columns that is 21 input
    /// fields per row and 76 Graphics, and it dominated everything the table does — a wheel
    /// tick that crossed into 24 fresh rows cost 726 ms, about 30 ms a row, on a table that
    /// was already virtualised.</para>
    ///
    /// <para>THE INTERACTION IS UNCHANGED, which is the whole point. A cell rests as a label
    /// and is PROMOTED to a real input field on click — and the promotion also focuses it and
    /// places the caret, so the gesture from the author's side is still click-then-type. What
    /// disappears is the 20 fields on that row nobody clicked. It also removes a hazard the
    /// live fields carried: a stray click into a numeric cell could commit an edit to a spell
    /// the author never meant to touch.</para>
    ///
    /// <para>A promoted cell STAYS promoted until its row is de-realised by scrolling, so
    /// tabbing along a row to fix four numbers pays for four fields and never rebuilds.</para>
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        /// <summary>
        /// A resting editable cell: the value as a label, over a transparent Button that
        /// swaps in the real field.
        ///
        /// <para>The Button and the label are separate GameObjects on purpose — an Image and
        /// a TextMeshProUGUI on the SAME GameObject is a NullReferenceException in this
        /// project, and the cell root is where the Image has to live for the click to cover
        /// the whole cell.</para>
        /// </summary>
        private void BuildLazyEditableCell(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            var hit = cellT.gameObject.GetComponent<Image>();
            if (hit == null) hit = cellT.gameObject.AddComponent<Image>();
            hit.color = new Color(0.10f, 0.12f, 0.14f, 0.55f);   // reads as a field, costs one Graphic

            var label = UILabel.AddCenteredText(cellT,
                col.GetString(def), 10f, FontStyles.Normal, UITheme.TEXT_PRIMARY);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Truncate;
            label.margin = new Vector4(SPELL_TABLE_CELL_PAD_H, 0f, SPELL_TABLE_CELL_PAD_H, 0f);
            label.raycastTarget = false;

            var button = cellT.gameObject.GetComponent<Button>();
            if (button == null) button = cellT.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.35f, 1f);
            colors.pressedColor = new Color(1.4f, 1.4f, 1.5f, 1f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;

            var capturedCell = cellT;
            button.onClick.AddListener(() => PromoteCellToInput(capturedCell, col, def));
        }

        /// <summary>
        /// Replace a resting cell with a live <see cref="TMP_InputField"/> and hand it the
        /// keyboard, so the click that promoted it is also the click that started the edit.
        /// </summary>
        private void PromoteCellToInput(Transform cellT, SpellTableColumn col, SpellDefinition def)
        {
            if (cellT == null || def == null) return;

            // The click that got here belongs to the Button being removed; tear the resting
            // parts down first so the field is the only thing in the cell.
            var button = cellT.gameObject.GetComponent<Button>();
            if (button != null) SafeDestroy.Of(button);
            var restingImage = cellT.gameObject.GetComponent<Image>();
            if (restingImage != null) SafeDestroy.Of(restingImage);
            for (int i = cellT.childCount - 1; i >= 0; i--)
            {
                var child = cellT.GetChild(i);
                child.SetParent(null, false);
                SafeDestroy.Of(child.gameObject);
            }

            var input = UIInputField.AddCommit(cellT,
                col.GetString(def),
                v => OnSpellCellCommit(col, def, v),
                SPELL_TABLE_ROW_H, 10f);
            input.contentType = col.EditorKind == SpellTableEditorKind.Int
                ? TMP_InputField.ContentType.IntegerNumber
                : col.EditorKind == SpellTableEditorKind.Float
                    ? TMP_InputField.ContentType.DecimalNumber
                    : TMP_InputField.ContentType.Standard;

            var inputRt = input.GetComponent<RectTransform>();
            inputRt.anchorMin = Vector2.zero;
            inputRt.anchorMax = Vector2.one;
            inputRt.sizeDelta = Vector2.zero;

            // Focus on the NEXT frame: the field was created inside a click callback that
            // uGUI is still dispatching, and selecting it now is undone when that dispatch
            // finishes clearing the selection of the object it just destroyed.
            StartCoroutine(FocusNextFrame(input));
        }

        private static System.Collections.IEnumerator FocusNextFrame(TMP_InputField input)
        {
            yield return null;
            if (input == null) yield break;

            var events = EventSystem.current;
            if (events != null) events.SetSelectedGameObject(input.gameObject);
            input.ActivateInputField();
            input.caretPosition = input.text != null ? input.text.Length : 0;
        }
    }
}
