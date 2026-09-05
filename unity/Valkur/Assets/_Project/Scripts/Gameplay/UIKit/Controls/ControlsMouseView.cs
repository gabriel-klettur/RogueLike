using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;

namespace Valkur.UIKit
{
    /// <summary>
    /// A mouse, drawn in code, where every button is a target the Controls editor can bind.
    ///
    /// <para>It exists for the same reason the drawn keyboard does, and for one more: the
    /// mouse is where this project's most dangerous binding lives. Left click both locks a
    /// target AND casts the primary spell, which is why clicking a vendor to trade with her
    /// threw a fireball at her — the whole reason the War/Peace stance was built. A surface
    /// that shows what each button does, per stance, is the only place that fact is
    /// visible.</para>
    ///
    /// <para>The silhouette is rectangles, not art: a body, two top buttons, a wheel column
    /// between them with an up and a down chip, and two side buttons. It has to be
    /// RECOGNISABLE, not photographic, and a sprite would need an artist for something whose
    /// whole content is which region is which button.</para>
    /// </summary>
    public sealed class ControlsMouseView
    {
        public const float DefaultWidth  = 150f;
        public const float DefaultHeight = 230f;

        private readonly Dictionary<MouseControl, Part> _parts = new Dictionary<MouseControl, Part>(8);
        private RectTransform _root;

        private sealed class Part
        {
            public Image Background;
            public Outline Ring;
            public TextMeshProUGUI Label;
        }

        public Vector2 Size { get; private set; }
        public bool IsBuilt => _root != null;
        public IEnumerable<MouseControl> DrawnControls => _parts.Keys;

        public RectTransform Build(RectTransform parent, Action<MouseControl> onButtonClicked,
                                   float width = DefaultWidth, float height = DefaultHeight)
        {
            _parts.Clear();

            var go = new GameObject("Mouse", typeof(RectTransform), typeof(Image));
            _root = (RectTransform)go.transform;
            _root.SetParent(parent, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);
            go.GetComponent<Image>().color = UITheme.BG_SURFACE;

            float pad     = 8f;
            float inner   = width - pad * 2f;
            float wheelW  = inner * 0.24f;
            float sideW   = (inner - wheelW) * 0.5f;
            float topH    = height * 0.34f;
            float wheelH  = topH * 0.42f;

            // Top row: left button, wheel column, right button.
            Add(MouseControl.Left,  pad,                   -pad, sideW, topH, onButtonClicked);
            Add(MouseControl.Right, pad + sideW + wheelW,  -pad, sideW, topH, onButtonClicked);

            float wheelX = pad + sideW;
            Add(MouseControl.WheelUp,   wheelX, -pad,                    wheelW, wheelH, onButtonClicked);
            Add(MouseControl.Middle,    wheelX, -pad - wheelH,           wheelW, topH - wheelH * 2f, onButtonClicked);
            Add(MouseControl.WheelDown, wheelX, -pad - topH + wheelH,    wheelW, wheelH, onButtonClicked);

            // Side buttons, stacked on the left flank below the top row — where a thumb finds
            // them on a real mouse, which is the only thing that makes them identifiable.
            float sideY = -pad - topH - 12f;
            float sideH = height * 0.11f;
            Add(MouseControl.Forward, pad, sideY,                 sideW * 0.9f, sideH, onButtonClicked);
            Add(MouseControl.Back,    pad, sideY - sideH - 6f,    sideW * 0.9f, sideH, onButtonClicked);

            Size = new Vector2(width, height);
            return _root;
        }

        private void Add(MouseControl control, float x, float y, float w, float h,
                         Action<MouseControl> onClicked)
        {
            var go = new GameObject("Btn_" + control,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);

            var image = go.GetComponent<Image>();
            image.color = UITheme.BTN_NORMAL;

            var ring = go.GetComponent<Outline>();
            ring.effectColor = Color.clear;
            ring.effectDistance = new Vector2(1.5f, -1.5f);

            // Image + TMP on one GameObject is a NullReferenceException here; the label is a
            // child for that reason.
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(2f, 2f);
            lrt.offsetMax = new Vector2(-2f, -2f);

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 9f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = UITheme.ACCENT;
            tmp.raycastTarget = false;

            if (onClicked != null)
            {
                var captured = control;
                go.GetComponent<Button>().onClick.AddListener(() => onClicked(captured));
            }

            _parts[control] = new Part { Background = image, Ring = ring, Label = tmp };
        }

        /// <summary>Repaints every button. Same contract as the keyboard's: the caller owns
        /// the whole appearance.</summary>
        public void Refresh(Func<MouseControl, KeyCapVisual> resolve)
        {
            if (_root == null || resolve == null) return;
            foreach (var kv in _parts)
            {
                var v = resolve(kv.Key);
                var p = kv.Value;
                p.Background.color = v.Fill;
                p.Label.color = v.Legend;
                p.Label.text = string.IsNullOrEmpty(v.Subtitle)
                    ? InputControlPaths.LabelForMouse(kv.Key)
                    : v.Subtitle;
                p.Ring.effectColor = v.Ring;
                p.Ring.enabled = v.Ring.a > 0.001f;
            }
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _parts.Clear();
        }
    }
}
