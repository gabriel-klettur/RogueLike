using System;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Kit
{
    /// <summary>
    /// The menu's slider: a grooved track, a filled bar, notches and a lozenge handle, all from
    /// <see cref="MenuArt"/>.
    ///
    /// <para><b>Why not <c>UISlider.MakeSlimTrack</c>.</b> That one is the EDITORS' slider and is
    /// drawn in flat colours with no sprite — correct there, and a fifth palette here (the
    /// shipped audio panel's cyan track appears nowhere else in the game). The geometry idea is
    /// the same and is the good part: the visible track stays slim while the whole ROW height
    /// accepts the click, so nobody needs pixel-perfect aim.</para>
    ///
    /// <para><b>The value is snapped to the step on the way IN, not on the way out.</b> A slider
    /// that stores 0.3719 and displays 37 is a control whose two halves disagree, and the
    /// disagreement surfaces the first time the value is written to disk and read back.</para>
    /// </summary>
    public sealed class MenuSlider
    {
        public readonly Slider Slider;
        private readonly float _step;
        private readonly Action<float> _onChanged;
        private bool _suppress;

        /// <summary>The live value, already snapped.</summary>
        public float Value => Slider != null ? Slider.value : 0f;

        public MenuSlider(RectTransform parent, MenuArt art, MenuStyle style,
                          float min, float max, float initial, float step,
                          Action<float> onChanged, int notches = 0)
        {
            _step = step;
            _onChanged = onChanged;

            var host = MenuUIKit.Rect("Slider", parent);
            // Stops short of the value column. Content starts at 0.54 of the row and the value
            // at 0.56, so a host reaching 1.0 of Content would run to 1.0 of the row and put the
            // track under the number it is being read against.
            host.anchorMin = new Vector2(0f, 0.5f);
            host.anchorMax = new Vector2(0.66f, 0.5f);
            host.pivot = new Vector2(0.5f, 0.5f);
            host.offsetMin = new Vector2(0f, -style.rowHeight * 0.5f);
            host.offsetMax = new Vector2(0f, style.rowHeight * 0.5f);

            // Transparent, raycast-on: the whole row-height band is the drag area.
            var hit = host.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            Slider = host.gameObject.AddComponent<Slider>();
            Slider.direction = Slider.Direction.LeftToRight;
            Slider.minValue = min;
            Slider.maxValue = max;
            Slider.wholeNumbers = false;
            Slider.transition = Selectable.Transition.None;

            var track = MenuUIKit.Sprite("Track", host, art.SliderTrack, Color.white);
            var trackRt = (RectTransform)track.transform;
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, 11f);
            trackRt.anchoredPosition = Vector2.zero;

            if (notches > 1)
            {
                // Notches make an unlabelled slider legible at a glance, which is what turns
                // "somewhere past the middle" into "three of five".
                for (int i = 0; i < notches; i++)
                {
                    float t = i / (float)(notches - 1);
                    var n = MenuUIKit.Sprite($"Notch_{i}", track.transform, art.Notch,
                                             new Color(1f, 1f, 1f, 0.22f), Image.Type.Simple);
                    var nrt = (RectTransform)n.transform;
                    nrt.anchorMin = new Vector2(t, 0.5f);
                    nrt.anchorMax = new Vector2(t, 0.5f);
                    nrt.pivot = new Vector2(0.5f, 0.5f);
                    nrt.sizeDelta = new Vector2(1f, 7f);
                    nrt.anchoredPosition = Vector2.zero;
                }
            }

            var fillArea = MenuUIKit.Rect("FillArea", track.transform);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.pivot = new Vector2(0.5f, 0.5f);
            fillArea.offsetMin = new Vector2(2f, -4f);
            fillArea.offsetMax = new Vector2(-2f, 4f);

            var fill = MenuUIKit.Sprite("Fill", fillArea, art.SliderFill, style.Gold);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var handleArea = MenuUIKit.Rect("HandleArea", host);
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.offsetMin = new Vector2(7f, -11f);
            handleArea.offsetMax = new Vector2(-7f, 11f);

            var handle = MenuUIKit.Sprite("Handle", handleArea, art.SliderHandle, style.Gold,
                                          Image.Type.Simple);
            var handleRt = (RectTransform)handle.transform;
            handleRt.sizeDelta = new Vector2(13f, 21f);

            Slider.fillRect = fillRt;
            Slider.handleRect = handleRt;
            Slider.targetGraphic = handle;
            Slider.SetValueWithoutNotify(Snap(Mathf.Clamp(initial, min, max)));
            Slider.onValueChanged.AddListener(OnSliderMoved);
        }

        /// <summary>Sets the value without calling back — how a nudge from the keyboard lands.</summary>
        public void SetValue(float v)
        {
            if (Slider == null) return;
            _suppress = true;
            Slider.SetValueWithoutNotify(Snap(Mathf.Clamp(v, Slider.minValue, Slider.maxValue)));
            _suppress = false;
        }

        /// <summary>Moves by one step in <paramref name="dir"/>. Returns the new value.</summary>
        public float Nudge(int dir)
        {
            if (Slider == null) return 0f;
            float v = Snap(Mathf.Clamp(Slider.value + dir * _step, Slider.minValue, Slider.maxValue));
            SetValue(v);
            _onChanged?.Invoke(v);
            return v;
        }

        private void OnSliderMoved(float raw)
        {
            if (_suppress) return;
            float v = Snap(raw);
            if (!Mathf.Approximately(v, raw))
            {
                _suppress = true;
                Slider.SetValueWithoutNotify(v);
                _suppress = false;
            }
            _onChanged?.Invoke(v);
        }

        private float Snap(float v)
        {
            if (_step <= 0f) return v;
            float snapped = Mathf.Round((v - Slider.minValue) / _step) * _step + Slider.minValue;
            return Mathf.Clamp(snapped, Slider.minValue, Slider.maxValue);
        }
    }
}
