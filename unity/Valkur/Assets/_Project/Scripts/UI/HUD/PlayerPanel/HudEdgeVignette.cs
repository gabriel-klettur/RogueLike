using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// A red edge around the whole screen that says "you are in danger" where the eyes are — on
    /// the fight — rather than in the corner.
    ///
    /// <para>Two things drive it and they are different kinds of news. A BLOW flashes it, scaled
    /// by how much of the bar it took, and it fades in a third of a second: an event. LOW HEALTH
    /// holds it on, pulsing with the health bar's heartbeat and deepening towards zero: a state.
    /// Both stay under a third of full alpha — the edge is a warning, and a screen that goes red
    /// is a screen the player cannot see through.</para>
    ///
    /// <para>It sits FIRST in the HUD canvas so every other readout draws over it, it is never a
    /// raycast target, and at rest it is disabled rather than drawn at zero alpha.</para>
    /// </summary>
    public sealed class HudEdgeVignette
    {
        private readonly RawImage _image;
        private readonly Texture2D _texture;
        private float _hit;
        private float _beat;
        private float _alpha;
        private bool _suppressed;

        /// <summary>The alpha the edge is drawn at. For the tests.</summary>
        public float Alpha => _alpha;

        public HudEdgeVignette(Transform canvasRoot, Color tint)
        {
            var go = new GameObject("PlayerDangerEdge", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            go.transform.SetAsFirstSibling();
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _image = go.AddComponent<RawImage>();
            _texture = HudArt.BakeVignette();
            _image.texture = _texture;
            _image.raycastTarget = false;
            // A DARK red: alpha-blended over a blue river a bright red reads as magenta, while a
            // dark one reads as the edges of the screen closing in — which is the message.
            tint.a = 0f;
            _image.color = tint;
            _image.enabled = false;
        }

        /// <summary>Hides the edge while the panel itself is hidden (an editor is open).</summary>
        public void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            if (suppressed && _image != null) _image.enabled = false;
        }

        /// <summary>A blow that took <paramref name="fractionOfMax"/> of max health.</summary>
        public void Hit(float fractionOfMax, PlayerHudStyle style)
        {
            float strength = Mathf.Clamp01(fractionOfMax / 0.25f);
            _hit = Mathf.Max(_hit, Mathf.Lerp(0.35f, 1f, strength) * style.hitVignetteAlpha);
        }

        public void Tick(float dt, float healthRatio, bool dead, PlayerHudStyle style)
        {
            _hit = Mathf.Max(0f, _hit - dt * style.hitVignetteAlpha / Mathf.Max(0.05f, style.hitVignetteSeconds));

            float hold = 0f;
            if (!dead && healthRatio < style.lowThreshold)
            {
                float depth = 1f - Mathf.Clamp01(healthRatio / Mathf.Max(0.01f, style.lowThreshold));
                _beat = Mathf.Repeat(_beat + dt * style.heartbeatHz * Mathf.Lerp(1f, 1.6f, depth), 1f);
                float pulse = Mathf.Exp(-Mathf.Pow((_beat - 0.08f) / 0.09f, 2f)) +
                              0.55f * Mathf.Exp(-Mathf.Pow((_beat - 0.28f) / 0.09f, 2f));
                hold = style.lowHealthVignetteAlpha * Mathf.Lerp(0.35f, 1f, depth) * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(pulse));
            }

            _alpha = Mathf.Clamp01(Mathf.Max(hold, _hit));
            bool on = _alpha > 0.002f && !_suppressed;
            if (_image.enabled != on) _image.enabled = on;
            if (on)
            {
                var c = _image.color;
                c.a = _alpha;
                _image.color = c;
            }
        }

        public void Dispose()
        {
            HudLifetime.Release(_texture);
            if (_image != null) HudLifetime.Release(_image.gameObject);
        }
    }
}
