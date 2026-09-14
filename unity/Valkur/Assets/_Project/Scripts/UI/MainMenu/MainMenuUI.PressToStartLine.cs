using UnityEngine;
using UnityEngine.UI;
using Valkur.UI.Frontend;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The threshold's line: the loading bar, full and waiting, under "press any key".
    ///
    /// <para><b>It is the same object the player will watch fill a minute later</b> — housing,
    /// end gems, molten body, flowing bands, a sheen — so the title screen and the loading screen
    /// open and close the same sentence. At rest it breathes with the text (never switches off,
    /// the lesson of the old blink) and the sheen crosses it every few seconds; crossing the
    /// threshold lights both gems and bursts along its length.</para>
    ///
    /// <para><b>It does not EMIT at rest.</b> The breathing and the sheen are motion, not events;
    /// the only particles are the finale's.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private const float LineWidth = 440f;
        private const float LineHeight = 12f;
        private const float LineY = -132f;
        private const float LineSheenPeriod = 3.4f;

        private RectTransform _pressLine;
        private BevelFrameGraphic _pressLineFrame;
        private FrontendFillGraphic _pressLineFill;
        private Image _pressLineSheen;
        private float _pressLineClock;

        private void BuildPressToStartLine(Transform overlay)
        {
            var style = Style;
            var kit = FrontendKit.Get(style);

            _pressLine = MenuUIKit.Rect("PressLine", overlay);
            _pressLine.anchorMin = _pressLine.anchorMax = new Vector2(0.5f, 0.5f);
            _pressLine.pivot = new Vector2(0.5f, 0.5f);
            _pressLine.anchoredPosition = new Vector2(0f, LineY);
            _pressLine.sizeDelta = new Vector2(LineWidth, LineHeight);

            _pressLineFrame = BevelFrameGraphic.Create(_pressLine, "Frame");
            _pressLineFrame.Thickness = 3f;
            _pressLineFrame.ShadowScale = 0.5f;
            _pressLineFrame.Brackets = false;
            _pressLineFrame.SideGems = true;
            _pressLineFrame.Tint = style.Gold;

            _pressLineFill = FrontendFillGraphic.Create(_pressLine, "Fill");
            _pressLineFill.Profile = FrontendFillProfile.Bar;
            _pressLineFill.Tint = style.Gold;
            _pressLineFill.Flow = !ReduceMotion;
            _pressLineFill.rectTransform.offsetMin = new Vector2(3f, 3f);
            _pressLineFill.rectTransform.offsetMax = new Vector2(-3f, -3f);

            var sheenRt = MenuUIKit.Rect("Sheen", _pressLine);
            sheenRt.anchorMin = sheenRt.anchorMax = new Vector2(0f, 0.5f);
            sheenRt.pivot = new Vector2(0.5f, 0.5f);
            sheenRt.sizeDelta = new Vector2(LineHeight * 5f, LineHeight * 2.2f);
            _pressLineSheen = sheenRt.gameObject.AddComponent<Image>();
            _pressLineSheen.sprite = kit.Radial;
            _pressLineSheen.raycastTarget = false;
            if (kit.Additive != null) _pressLineSheen.material = kit.Additive;
            _pressLineSheen.color = Color.clear;

            _pressLineClock = 0f;
            TickPressToStartLine(0f);
        }

        /// <summary>Breathes the line with the text and slides its sheen. Called from the shell's tick.</summary>
        private void TickPressToStartLine(float dt)
        {
            if (_pressLine == null || !_pressToStartActive) return;
            _pressLineClock += dt;
            float phase = ReduceMotion ? 1f : Mathf.Sin(_pressLineClock / BREATH_SECONDS * Mathf.PI * 2f) * 0.5f + 0.5f;

            _pressLineFill.Clock = _pressLineClock;
            _pressLineFill.color = FrontendMesh.WithAlpha(Color.white, Mathf.Lerp(0.55f, 1f, phase));
            _pressLineFrame.StartGemLit = Mathf.Lerp(0.45f, 1f, phase);
            _pressLineFrame.EndGemLit = Mathf.Lerp(0.45f, 1f, phase);

            if (ReduceMotion) { _pressLineSheen.color = Color.clear; return; }
            float s = Mathf.Repeat(_pressLineClock, LineSheenPeriod) / LineSheenPeriod;
            float run = Mathf.Clamp01(s / 0.5f);
            ((RectTransform)_pressLineSheen.transform).anchoredPosition = new Vector2(Mathf.Lerp(-30f, LineWidth + 30f, run), 1f);
            var c = FrontendPalette.WarmWhite;
            c.a = s < 0.5f ? 0.5f * Mathf.Sin(run * Mathf.PI) : 0f;
            _pressLineSheen.color = c;
        }

        /// <summary>The finale: gems lit, and a burst along the whole line out of the shell's layer.</summary>
        private void BurstPressToStartLine()
        {
            if (_pressLine == null) return;
            _pressLineFrame.StartGemLit = 1f;
            _pressLineFrame.EndGemLit = 1f;
            _pressLineFrame.Glow = 1f;
            if (_fx == null) return;
            var tint = Style.Gold;
            Color hot = FrontendMotes.Hot(tint), warm = FrontendMotes.Warm(tint);
            var r = _pressLine.rect;
            for (int i = 0; i < 48; i++)
            {
                float a = Random.Range(30f, 150f) * Mathf.Deg2Rad;
                float v = Random.Range(90f, 300f);
                FrontendMotes.EmitFrom(_fx, _pressLine, new Vector2(Random.Range(r.xMin, r.xMax), r.center.y),
                                       new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * v, Color.Lerp(hot, warm, Random.value),
                                       Random.Range(0.5f, 1.2f), Random.value < 0.5f ? MenuMoteShape.Spark : MenuMoteShape.Dot,
                                       gravity: 170f, drag: 1.3f, size: Random.Range(0.18f, 0.48f));
            }
        }
    }
}
