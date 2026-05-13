using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>
    /// Sibling component that mirrors a target <see cref="RectTransform"/>'s
    /// measured height into the host's <see cref="LayoutElement.preferredHeight"/>,
    /// optionally clamped by <see cref="MinHeight"/> / <see cref="MaxHeight"/>.
    ///
    /// Use case: a <see cref="ScrollRect"/> container that should size itself to
    /// match its content when the content fits, and only engage scrolling once
    /// the content exceeds <see cref="MaxHeight"/>. Without this helper a scroll
    /// inside a <see cref="VerticalLayoutGroup"/> typically uses
    /// <c>flexibleHeight = 1</c> and stretches to whatever room the parent has,
    /// wasting vertical real estate when the content barely fills one row.
    ///
    /// Sampling: <see cref="UIBehaviour.OnRectTransformDimensionsChange"/> only
    /// fires for the host RectTransform — it does NOT fire when the
    /// <see cref="SourceContent"/>'s rect changes (it's a separate transform).
    /// We therefore poll once per frame in <see cref="Update"/>; the read is a
    /// single rect.height access and the resulting LayoutElement assignment is
    /// short-circuited when the value hasn't changed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LayoutElement))]
    public class LayoutElementFollowsChildHeight : UIBehaviour
    {
        [SerializeField, Tooltip("RectTransform whose height drives the host's preferredHeight.")]
        private RectTransform sourceContent;

        [SerializeField, Tooltip("Floor on the resulting preferredHeight (px). 0 = no floor.")]
        private float minHeight = 0f;

        [SerializeField, Tooltip("Ceiling on the resulting preferredHeight (px). 0 = unlimited.")]
        private float maxHeight = 0f;

        [SerializeField, Tooltip("Extra px to add to the source's height (room for borders / padding).")]
        private float extraPadding = 0f;

        public RectTransform SourceContent { get => sourceContent; set { sourceContent = value; Apply(); } }
        public float MinHeight    { get => minHeight;    set { minHeight    = value; Apply(); } }
        public float MaxHeight    { get => maxHeight;    set { maxHeight    = value; Apply(); } }
        public float ExtraPadding { get => extraPadding; set { extraPadding = value; Apply(); } }

        private LayoutElement _le;
        private float _lastApplied = float.NaN;

        protected override void OnEnable()
        {
            base.OnEnable();
            _lastApplied = float.NaN;
            CacheRefs();
            Apply();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            Apply();
        }

        private void Update() => Apply();

        private void CacheRefs()
        {
            if (_le == null) _le = GetComponent<LayoutElement>();
        }

        private void Apply()
        {
            CacheRefs();
            if (_le == null || sourceContent == null) return;

            float h = sourceContent.rect.height + extraPadding;
            if (minHeight > 0f) h = Mathf.Max(h, minHeight);
            if (maxHeight > 0f) h = Mathf.Min(h, maxHeight);

            if (!float.IsNaN(_lastApplied) && Mathf.Approximately(h, _lastApplied)) return;
            _le.preferredHeight = h;
            _lastApplied = h;
        }
    }
}
