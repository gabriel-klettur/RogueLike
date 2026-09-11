using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The level, in a gold medallion pinned to the portrait's corner.
    ///
    /// <para>It replaces a grey pill reading "Lvl 0" — which was a real defect, not a style:
    /// <c>LevelLabelHUD</c> read the level once at bind time and afterwards listened only for
    /// <c>OnLevelUp</c>, while <c>Experience.Initialize</c> (the boot and the save restore) raises
    /// <c>OnStateChanged</c>. So the badge showed whatever the level was BEFORE the save loaded
    /// until the next level-up. The panel now sets the medallion from every Experience event,
    /// including <c>OnStateChanged</c>.</para>
    ///
    /// <para>Two digits in the large face; three fall back to the small one rather than
    /// overflowing the ring.</para>
    /// </summary>
    public sealed class HudMedallion
    {
        public RectTransform Root { get; }

        private readonly Image _glow;
        private readonly HudPixelText _number;
        private int _level = int.MinValue;
        private float _pulse;
        private float _pulseSeconds = 0.9f;

        /// <summary>The level printed on it.</summary>
        public int Level => _level;

        /// <summary>The text printed on it.</summary>
        public string Label => _number.Text;

        /// <summary>True while the level-up glow is running.</summary>
        public bool Pulsing => _pulse > 0f;

        public HudMedallion(Transform parent, HudArt art, int x, int y, int size, Material additive)
        {
            Root = HudRect.Make("Level", parent, x, y, size, size);
            int g = size + 8;
            _glow = HudRect.MakeImage("Glow", Root, art.MedallionGlow, -4, -4, g, g);
            _glow.material = additive;
            _glow.color = Color.clear;
            _glow.enabled = false;
            HudRect.MakeImage("Coin", Root, art.Medallion, 0, 0, size, size);
            _number = HudPixelText.Create(Root, "Number", art, HudFontFace.Large, HudTextAlign.Centre,
                                          0, 0, size, size);
        }

        public void SetLevel(int level)
        {
            if (level == _level) return;
            _level = level;
            string s = Mathf.Max(0, level).ToString();
            _number.SetFace(s.Length <= 2 ? HudFontFace.Large : HudFontFace.Small);
            _number.SetText(s);
        }

        public void SetColour(Color c) => _number.SetColour(c);

        /// <summary>The level-up glow.</summary>
        public void Pulse(float seconds)
        {
            _pulseSeconds = Mathf.Max(0.05f, seconds);
            _pulse = _pulseSeconds;
            _glow.enabled = true;
        }

        public void Tick(float dt, PlayerHudStyle style)
        {
            if (_pulse <= 0f) return;
            _pulse = Mathf.Max(0f, _pulse - dt);
            float t = 1f - _pulse / _pulseSeconds;          // 0 at the start
            // Rise fast, hold, fall: a glow that only fades reads as something switching off.
            float a = t < 0.15f ? t / 0.15f : Mathf.SmoothStep(1f, 0f, (t - 0.15f) / 0.85f);
            var c = style.gold;
            c.a = a;
            _glow.color = c;
            if (_pulse <= 0f) _glow.enabled = false;
        }

        public Vector2 Centre => Root.anchoredPosition + Root.sizeDelta * 0.5f;
    }
}
