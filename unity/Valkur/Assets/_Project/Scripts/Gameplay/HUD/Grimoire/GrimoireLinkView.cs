using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// One prerequisite chain, drawn as a bar between two sockets.
    ///
    /// <para><b>A link says where the player can GO.</b> Open when its parent is bought,
    /// half-lit when the parent is the next thing they could buy, dark otherwise. Read across
    /// a school that draws the frontier of what is reachable without a single word — the same
    /// job the minimap's soft fog frontier does, and the reason the board is worth more than
    /// the list it replaces.</para>
    ///
    /// <para><b>It stops at the rim, not at the centre.</b> The socket ring is NOT opaque —
    /// its bevel runs down to 0.42 alpha and the plate under the icon to 0.72 — so a wire
    /// "hidden behind" a node is a wire drawn at up to 58 % straight across its face. That is
    /// <see cref="SpellGraphGeometry.NodeRimRadius"/>'s whole reason for existing, and it is
    /// derived from the sprite so retuning the ring moves the wires with it.</para>
    /// </summary>
    public sealed class GrimoireLinkView : MonoBehaviour
    {
        private RectTransform _rt;
        private Image _body;
        private Image _flow;
        private float _flowLeft;
        private float _flowSeconds;
        private GrimoireStyle _style;

        public static GrimoireLinkView Create(Transform parent, float thickness,
                                              GrimoireStyle style)
        {
            var go = new GameObject("Link", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<GrimoireLinkView>();
            view._style = style;
            view._rt = (RectTransform)go.transform;
            view._rt.anchorMin = view._rt.anchorMax = new Vector2(0.5f, 0.5f);
            view._rt.pivot = new Vector2(0f, 0.5f);
            view._rt.sizeDelta = new Vector2(0f, thickness);

            view._body = Bar(go.transform, SpellGraphSprites.Link);
            view._flow = Bar(go.transform, SpellGraphSprites.LinkFlow);
            view._flow.enabled = false;
            return view;
        }

        /// <summary>
        /// Stretches and turns the bar between two board-local points, trimmed at both rims.
        /// A link shorter than the two rims together is hidden rather than drawn inside out.
        /// </summary>
        public void Span(Vector2 from, Vector2 to, float rimRadius)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            float trimmed = length - rimRadius * 2f;
            if (trimmed <= 1f)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            Vector2 dir = delta / length;
            _rt.anchoredPosition = from + dir * rimRadius;
            _rt.sizeDelta = new Vector2(trimmed, _rt.sizeDelta.y);
            _rt.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Paints the chain for the parent's state. Three readings, not two: "you have this",
        /// "this is one purchase away", "this is somewhere else entirely".
        /// </summary>
        public void Paint(bool parentLearned, bool parentAvailable, Color accent, Color dark)
        {
            if (parentLearned)
                _body.color = new Color(accent.r, accent.g, accent.b, _style.linkOpenAlpha);
            else if (parentAvailable)
                _body.color = new Color(accent.r, accent.g, accent.b, _style.linkNextAlpha);
            else
                _body.color = new Color(dark.r, dark.g, dark.b, _style.linkDarkAlpha);
        }

        /// <summary>
        /// How strongly the chain is drawn, for the role filter. A chain is faded only when
        /// BOTH its ends are excluded; one live end means this is the path to something the
        /// player is looking at.
        /// </summary>
        public void SetFade(float fade)
        {
            fade = Mathf.Clamp01(fade);
            if (Mathf.Approximately(fade, _fade)) return;
            _fade = fade;

            var group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = _fade;
        }

        private float _fade = 1f;

        /// <summary>
        /// Lights the chain once, from parent to child. An EVENT, never a loop: a connector
        /// that flows forever is movement at rest, which R7 forbids, and it would drown the
        /// one thing this board has to announce — that a purchase just opened a path.
        /// </summary>
        public void Flow(float seconds)
        {
            _flowSeconds = Mathf.Max(0.01f, seconds);
            _flowLeft = _flowSeconds;
            _flow.enabled = true;
        }

        /// <summary>Driven by the panel, so a hidden board costs nothing.</summary>
        public void Tick(float dt, Color accent)
        {
            if (_flowLeft <= 0f) return;

            _flowLeft -= dt;
            if (_flowLeft <= 0f)
            {
                _flow.enabled = false;
                return;
            }

            float t = 1f - _flowLeft / _flowSeconds;
            // Bright at the start and gone by the end: the eye follows the fade along the
            // chain rather than watching a bar change colour in place.
            _flow.color = new Color(accent.r, accent.g, accent.b,
                                    Mathf.Lerp(_style.linkFlowAlpha, 0f, t));
            _flow.rectTransform.anchorMin = new Vector2(Mathf.Max(0f, t - 0.35f), 0f);
            _flow.rectTransform.anchorMax = new Vector2(Mathf.Min(1f, t + 0.15f), 1f);
            _flow.rectTransform.offsetMin = Vector2.zero;
            _flow.rectTransform.offsetMax = Vector2.zero;
        }

        private static Image Bar(Transform parent, Sprite sprite)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }
    }
}
