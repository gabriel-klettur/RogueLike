using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Valkur.UIKit
{
    /// <summary>How one key cap should look this frame.</summary>
    public readonly struct KeyCapVisual
    {
        public readonly Color Fill;
        public readonly Color Legend;

        /// <summary>Ring colour. <see cref="Color.clear"/> draws no ring.</summary>
        public readonly Color Ring;

        /// <summary>The small line under the legend — what is bound to this key. Empty leaves
        /// the cap showing only its own letter.</summary>
        public readonly string Subtitle;

        public KeyCapVisual(Color fill, Color legend, Color ring, string subtitle)
        {
            Fill = fill; Legend = legend; Ring = ring; Subtitle = subtitle ?? "";
        }
    }

    /// <summary>
    /// A keyboard, drawn in code from <see cref="KeyboardLayoutModel"/>, where every cap is a
    /// button and every cap can be tinted and labelled by whatever is bound to it.
    ///
    /// <para>WHY A PICTURE RATHER THAN A LIST. A list of actions answers "what is jump bound
    /// to"; it cannot answer "what is free", "what did I put on F5 six months ago", or "are
    /// two things on the same key" — and the last of those is not hypothetical here. The
    /// shipped asset has four F-key collisions and had two pairs of bindings sharing an id;
    /// all of them are one glance on a drawn board and an afternoon in a list.</para>
    ///
    /// <para>NO LAYOUT GROUPS. Caps are placed at absolute positions from the row/width model.
    /// A <c>HorizontalLayoutGroup</c> would fight the per-cap widths that make a keyboard look
    /// like one, and a <c>ContentSizeFitter</c> would shrink the board to whatever happens to
    /// be realised — the pair of mistakes <c>ItemsTableVirtualizationTests</c> exists to
    /// refuse.</para>
    /// </summary>
    public sealed class ControlsKeyboardView
    {
        public const float DefaultCapUnit = 34f;
        private const float CapGap    = 4f;
        private const float BlockGap  = 18f;

        private readonly Dictionary<string, Cap> _caps = new Dictionary<string, Cap>(128);
        private RectTransform _root;

        private sealed class Cap
        {
            public Image Background;
            public Outline Ring;
            public TextMeshProUGUI Legend;
            public TextMeshProUGUI Subtitle;
        }

        /// <summary>Overall size of the drawn board, in pixels. Valid after
        /// <see cref="Build"/>.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Every control name the board drew. The Controls editor uses it to report
        /// bindings whose key is not on this layout — a real state, since ISO and ANSI do not
        /// draw the same set.</summary>
        public IEnumerable<string> DrawnControls => _caps.Keys;

        public bool IsBuilt => _root != null;

        /// <summary>
        /// Builds the board under <paramref name="parent"/>. Returns the root, sized to
        /// <see cref="Size"/> with a top-left pivot so the caller can drop it into a scroll
        /// rect's content without a second layout pass.
        /// </summary>
        public RectTransform Build(RectTransform parent, KeyboardLayoutKind kind,
                                   Action<string> onKeyClicked, float capUnit = DefaultCapUnit)
        {
            _caps.Clear();

            var go = new GameObject("Keyboard", typeof(RectTransform));
            _root = (RectTransform)go.transform;
            _root.SetParent(parent, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;

            float x = 0f, maxHeight = 0f;
            foreach (var block in KeyboardLayoutModel.Build(kind))
            {
                float blockWidth = 0f;
                float y = 0f;

                foreach (var row in block.Rows)
                {
                    float rowX = 0f;
                    foreach (var cap in row.Caps)
                    {
                        float w = cap.Width * capUnit + (cap.Width - 1f) * CapGap;
                        if (!cap.IsSpacer)
                            AddCap(cap.ControlName, x + rowX, y, w, capUnit, onKeyClicked);
                        rowX += w + CapGap;
                    }
                    if (rowX > blockWidth) blockWidth = rowX;
                    y -= capUnit + CapGap;
                }

                if (-y > maxHeight) maxHeight = -y;
                x += blockWidth + BlockGap;
            }

            Size = new Vector2(Mathf.Max(0f, x - BlockGap), Mathf.Max(0f, maxHeight - CapGap));
            _root.sizeDelta = Size;
            return _root;
        }

        private void AddCap(string controlName, float x, float y, float width, float height,
                            Action<string> onKeyClicked)
        {
            // A duplicated control name would silently give one key two views and leave the
            // second one un-refreshable. It cannot happen from the shipped layouts; it is
            // reported rather than swallowed so it cannot start happening quietly.
            if (_caps.ContainsKey(controlName))
            {
                Debug.LogWarning($"[ControlsKeyboardView] '{controlName}' is drawn twice by " +
                                 "the layout; the second cap will not update.");
                return;
            }

            var go = new GameObject("Cap_" + controlName,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);

            var image = go.GetComponent<Image>();
            image.color = UITheme.BTN_NORMAL;

            var ring = go.GetComponent<Outline>();
            ring.effectColor = Color.clear;
            ring.effectDistance = new Vector2(1.5f, -1.5f);

            // Image + TMP on the SAME GameObject is a NullReferenceException in this project.
            // The legend and the subtitle are children for that reason, not for layout.
            var legend = MakeText(rt, "Legend", KeyboardLayoutModel.CapLabel(controlName),
                                  Mathf.Clamp(height * 0.36f, 9f, 14f),
                                  new Vector2(0f, 0.34f), new Vector2(1f, 1f));
            var subtitle = MakeText(rt, "Bound", "",
                                    Mathf.Clamp(height * 0.26f, 7f, 10f),
                                    new Vector2(0f, 0f), new Vector2(1f, 0.36f));
            subtitle.color = UITheme.ACCENT;

            if (onKeyClicked != null)
            {
                string captured = controlName;
                go.GetComponent<Button>().onClick.AddListener(() => onKeyClicked(captured));
            }

            _caps[controlName] = new Cap
            {
                Background = image, Ring = ring, Legend = legend, Subtitle = subtitle,
            };
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, string text,
                                                float size, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(2f, 0f);
            rt.offsetMax = new Vector2(-2f, 0f);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = UITheme.TEXT_PRIMARY;
            tmp.raycastTarget = false;   // the cap's own Button owns the click
            return tmp;
        }

        /// <summary>
        /// Repaints every cap. <paramref name="resolve"/> is asked once per drawn control, so
        /// the caller decides the whole appearance — category tint, conflict ring, what is
        /// bound — without this class knowing anything about actions.
        /// </summary>
        public void Refresh(Func<string, KeyCapVisual> resolve)
        {
            if (_root == null || resolve == null) return;
            foreach (var kv in _caps)
            {
                var v = resolve(kv.Key);
                var cap = kv.Value;
                cap.Background.color = v.Fill;
                cap.Legend.color = v.Legend;
                cap.Subtitle.text = v.Subtitle;
                cap.Ring.effectColor = v.Ring;
                cap.Ring.enabled = v.Ring.a > 0.001f;
            }
        }

        /// <summary>Refreshes exactly one cap — what a rebind needs, so a 105-key repaint is
        /// not the cost of moving one binding.</summary>
        public void RefreshOne(string controlName, KeyCapVisual visual)
        {
            if (_root == null || !_caps.TryGetValue(controlName, out var cap)) return;
            cap.Background.color = visual.Fill;
            cap.Legend.color = visual.Legend;
            cap.Subtitle.text = visual.Subtitle;
            cap.Ring.effectColor = visual.Ring;
            cap.Ring.enabled = visual.Ring.a > 0.001f;
        }

        /// <summary>Re-reads every cap's legend from the OS. Needed when the layout kind
        /// changes and after a device change — a board drawn before the keyboard arrived shows
        /// the fallback labels.</summary>
        public void RefreshLegends()
        {
            foreach (var kv in _caps)
                kv.Value.Legend.text = KeyboardLayoutModel.CapLabel(kv.Key);
        }

        public bool Draws(string controlName) => controlName != null && _caps.ContainsKey(controlName);

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _caps.Clear();
        }
    }
}
