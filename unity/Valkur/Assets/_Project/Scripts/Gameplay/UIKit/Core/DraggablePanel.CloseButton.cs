using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// The header close button, and the memory of whether a panel was left closed.
    ///
    /// Built here rather than in the panel builders because there are ELEVEN of those — every
    /// runtime editor carries its own private MakeDrop that duplicates the header from
    /// scratch instead of calling the shared EditorUIHelpers.MakeDropPanel. Adding a button
    /// to each would be eleven chances to get it slightly different, and a twelfth editor
    /// would ship without one. Every one of them does attach a DraggablePanel and set
    /// DragHeader, so the component can furnish its own button and they all inherit it.
    ///
    /// <see cref="ClosePanel"/> and the "Close via header buttons" line in this class's own
    /// documentation predate this file by a long way; the method existed with no production
    /// caller and only a test exercising it.
    /// </summary>
    public partial class DraggablePanel
    {
        private const string CLOSE_BUTTON_NAME   = "PanelCloseButton";
        private const float  CLOSE_BUTTON_SIZE   = 16f;
        private const float  CLOSE_BUTTON_MARGIN = 4f;
        private const string PREFS_PREFIX      = "Valkur.Panel.Closed.";

        /// <summary>
        /// Set false BEFORE the first frame for a panel that must not be closable — a modal,
        /// or a panel whose editor has no other way to bring it back.
        /// </summary>
        public bool ShowCloseButton = true;

        /// <summary>
        /// Key under which this panel's closed state is remembered. Defaults to the
        /// GameObject's name, which the builders already make unique per editor
        /// ("ParticlesPropsPanel", "BuildingsCollidersPanel", …). Assign explicitly if two
        /// panels in different editors would otherwise collide.
        /// </summary>
        public string PersistenceKey;

        /// <summary>
        /// Raised when the panel is restored from the closed state at startup, so the host
        /// editor can keep its menu-bar highlight in agreement with what is on screen.
        /// Without it a panel hidden by persistence would still look "open" in the menu.
        /// </summary>
        public System.Action OnRestoredClosed;

        private Button _closeButton;

        private string ResolvedKey =>
            string.IsNullOrEmpty(PersistenceKey) ? gameObject.name : PersistenceKey;

        /// <summary>Whether this panel was left closed in a previous session.</summary>
        public bool WasClosedLastSession =>
            PlayerPrefs.GetInt(PREFS_PREFIX + ResolvedKey, 0) == 1;

        private void RememberClosed(bool closed)
        {
            PlayerPrefs.SetInt(PREFS_PREFIX + ResolvedKey, closed ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Clear every remembered panel state. Exposed for a "reset layout" action and for
        /// tests, which must not inherit whatever the last play session left behind.
        /// </summary>
        public static void ForgetAllPanelStates(params string[] keys)
        {
            if (keys == null) return;
            foreach (var k in keys)
                if (!string.IsNullOrEmpty(k)) PlayerPrefs.DeleteKey(PREFS_PREFIX + k);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Adds the header chrome if it is missing. Idempotent, and public so a host that
        /// builds a panel by hand — or a test, which cannot rely on OnEnable running outside
        /// Play Mode — can bring it up deterministically.
        /// </summary>
        public void EnsureChrome() => EnsureCloseButton();

        /// <summary>
        /// Adds the button if the header exists and does not already have one. Idempotent.
        /// </summary>
        private void EnsureCloseButton()
        {
            if (!ShowCloseButton || DragHeader == null) return;
            if (_closeButton != null) return;

            var existing = DragHeader.Find(CLOSE_BUTTON_NAME);
            if (existing != null)
            {
                _closeButton = existing.GetComponent<Button>();
                if (_closeButton != null) return;
            }

            var go = new GameObject(CLOSE_BUTTON_NAME,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(DragHeader, false);

            // Anchored to the header's top-right corner rather than laid out, so it does not
            // depend on whether that header uses a layout group — across eleven builders,
            // some do and some do not.
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot     = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE);
            rt.anchoredPosition = new Vector2(-CLOSE_BUTTON_MARGIN, 0f);

            // Opting out of layout is what makes the anchoring above survive. Every editor's
            // MakeDrop puts a HorizontalLayoutGroup on the header with childControlWidth =
            // true, and a layout group sizes a child from its ILayoutElement preferred width
            // — which for a sprite-less Image is ZERO. It also rewrites the child's anchors.
            // The result was a 0x24 rect: invisible, and with no area to click. Measured live
            // in the Particles editor, all five panels reported rect (0.00, 24.00).
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            ReserveHeaderPaddingForButton();

            var img = go.GetComponent<Image>();
            img.color = new Color(0.30f, 0.10f, 0.12f, 0.85f);

            // Label on a CHILD, never on the button GameObject: an Image and a TMP text on
            // the same object throw a NullReferenceException in this project.
            var labelGo = new GameObject("X", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text          = "X";
            tmp.fontSize      = 11f;
            tmp.fontStyle     = FontStyles.Bold;
            tmp.alignment     = TextAlignmentOptions.Center;
            tmp.color         = new Color(0.95f, 0.85f, 0.85f, 1f);
            tmp.raycastTarget = false;

            _closeButton = go.GetComponent<Button>();
            _closeButton.targetGraphic = img;
            _closeButton.onClick.AddListener(CloseFromHeaderButton);
        }

        /// <summary>
        /// Widens the header layout group's right padding so its content — the title, or a
        /// narrow panel's header button — does not run underneath the close button, which no
        /// longer takes part in that layout. Never narrows an existing padding.
        /// </summary>
        private void ReserveHeaderPaddingForButton()
        {
            var group = DragHeader.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (group == null) return;

            int needed = Mathf.CeilToInt(CLOSE_BUTTON_SIZE + CLOSE_BUTTON_MARGIN * 2f);
            if (group.padding.right >= needed) return;

            // Assigned as a new RectOffset rather than mutated in place: writing the field of
            // the existing one does not dirty the group, so the layout would keep the old value.
            group.padding = new RectOffset(
                group.padding.left, needed, group.padding.top, group.padding.bottom);
            LayoutRebuilder.MarkLayoutForRebuild(DragHeader);
        }

        /// <summary>
        /// The X was clicked. Remembers the choice, then hands over to the host through the
        /// existing OnClose contract — the editors already use that to update their menu
        /// highlights, so the panel must not simply hide itself behind their backs.
        /// </summary>
        private void CloseFromHeaderButton()
        {
            RememberClosed(true);
            ClosePanel();
        }

        /// <summary>
        /// Reapplies a remembered closed state. Called once the panel is live, and routed
        /// through the same OnClose the button uses so the editor's own bookkeeping runs
        /// exactly as if the user had clicked it.
        /// </summary>
        public void ApplyRememberedVisibility()
        {
            if (!WasClosedLastSession) return;
            OnRestoredClosed?.Invoke();
            ClosePanel();
        }

        /// <summary>Records that the panel is open again. Call when a host re-opens it.</summary>
        public void MarkOpened() => RememberClosed(false);
    }
}
