using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Writes an explanation into a status label while the pointer is over a control, and puts
    /// back whatever was there when it leaves.
    ///
    /// <para>WHY RESTORE RATHER THAN CLEAR. The Camera editor's own hover helper clears its
    /// label on exit, which is right for a panel whose label exists only for hovering. Every
    /// other editor's status line carries the result of the last ACTION — "Guardado en …",
    /// "darkball → 5" — and blanking it means the author loses the answer to what they just
    /// did by moving the mouse an inch. Capturing the text on enter costs one string and makes
    /// hover non-destructive.</para>
    ///
    /// <para>It is deliberately generic: a target label and a message, with no reference to any
    /// editor's UIRefs type. The Camera editor's version is typed on its own refs class, which
    /// is why it could not be shared.</para>
    /// </summary>
    public sealed class UIHoverText : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField, Tooltip("Label to write the message into while hovered.")]
        private TextMeshProUGUI target;

        [SerializeField, TextArea, Tooltip("What to say while the pointer is over this control.")]
        private string message;

        private string _restore;
        private bool _showing;

        public static UIHoverText Attach(GameObject host, TextMeshProUGUI target, string message)
        {
            if (host == null || target == null || string.IsNullOrEmpty(message)) return null;
            var help = host.GetComponent<UIHoverText>() ?? host.AddComponent<UIHoverText>();
            help.target = target;
            help.message = message;
            return help;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (target == null || _showing) return;
            _restore = target.text;
            _showing = true;
            target.text = message;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (target == null || !_showing) return;
            _showing = false;
            target.text = _restore;
        }

        /// <summary>
        /// A row that is destroyed while hovered never receives <see cref="OnPointerExit"/>, so
        /// the label would keep the hover message forever. The list is rebuilt on every
        /// keystroke of the search box, which makes that the normal case rather than an edge.
        /// </summary>
        private void OnDisable()
        {
            if (target != null && _showing) target.text = _restore;
            _showing = false;
        }
    }
}
