using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Opens the character sheet when the player double-clicks the 3-slot
    /// ability row in the bottom-left HUD. Single clicks are left alone so the
    /// row keeps behaving like an ability bar.
    ///
    /// Lives on the row itself rather than on each slot: the slot backgrounds
    /// are the raycast targets, and the pointer event bubbles up to the first
    /// ancestor that handles it — so one component covers all three slots plus
    /// the gaps between them.
    /// </summary>
    public sealed class AbilityRowDoubleClick : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField, Tooltip("Tab the character sheet opens on. 0 = Skills, 1 = Stats.")]
        private int tabOnOpen;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (eventData.clickCount < 2) return;

            OpenSheet();
        }

        /// <summary>Test seam — same path a real double-click takes.</summary>
        internal void OpenSheet()
        {
            var sheet = ResolveSheet();
            if (sheet == null) return;

            if (sheet.IsOpen) sheet.Close();
            else sheet.Open(tabOnOpen);
        }

        private static CharacterSheetController ResolveSheet()
        {
            if (CharacterSheetController.HasInstance) return CharacterSheetController.Instance;

            var existing = FindObjectOfType<CharacterSheetController>(true);
            if (existing != null) return existing;

            var go = new GameObject("CharacterSheetController");
            var uiContainer = GameObject.Find("[UI]");
            if (uiContainer != null) go.transform.SetParent(uiContainer.transform, false);
            return go.AddComponent<CharacterSheetController>();
        }
    }
}
