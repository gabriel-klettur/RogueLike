using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The dash charge as a diamond that fills from the floor.
    ///
    /// <para>A charge is a COUNT, so it gets a pip and not a third bar — the same call the bars over
    /// the head made. It replaces <c>DashMeterHUD</c>, which built its own canvas, hard-coded a
    /// one-second cooldown (<c>CooldownRemaining / 1f</c>) against a component that exposes the
    /// real one, and was instantiated by nothing at all.</para>
    ///
    /// <para>The pip reads EMPTY during the dash itself — <c>CanDash</c> is false while the lunge is
    /// in flight — and flashes when the charge returns, the one moment it exists to report.</para>
    /// </summary>
    public sealed class HudDashPip
    {
        private const int FillSteps = 7;

        public RectTransform Root { get; }

        private readonly Image _fill;
        private readonly Image _flash;
        private float _charge = 1f;
        private float _flashLeft;

        /// <summary>The charge being shown, 0..1.</summary>
        public float Charge => _charge;

        /// <summary>Raised on the frame the dash comes back.</summary>
        public event System.Action BecameReady;

        public HudDashPip(Transform parent, HudArt art, int x, int y, Material additive)
        {
            Root = HudRect.Make("DashPip", parent, x, y, 11, 11);
            HudRect.MakeImage("Frame", Root, art.PipFrame, 0, 0, 11, 11);
            _fill = HudRect.MakeImage("Fill", Root, art.PipFill, 2, 2, 7, 7, Image.Type.Filled);
            _fill.fillMethod = Image.FillMethod.Vertical;
            _fill.fillOrigin = (int)Image.OriginVertical.Bottom;
            _flash = HudRect.MakeImage("Flash", Root, art.PipFill, 2, 2, 7, 7);
            _flash.material = additive;
            _flash.color = Color.clear;
            _flash.enabled = false;
        }

        public void Tick(float dt, DashAbility dash, PlayerHudStyle style)
        {
            float charge = 1f;
            if (dash != null)
            {
                if (dash.IsDashing) charge = 0f;
                else if (!dash.CanDash)
                    charge = dash.CooldownTotal > 0f
                        ? 1f - Mathf.Clamp01(dash.CooldownRemaining / dash.CooldownTotal)
                        : 0f;
            }

            bool returned = _charge < 0.999f && charge >= 0.999f;
            _charge = charge;
            if (returned)
            {
                _flashLeft = 0.3f;
                BecameReady?.Invoke();
            }

            // Whole rows of the diamond, never a fraction of one.
            float q = Mathf.Floor(charge * FillSteps) / FillSteps;
            if (!Mathf.Approximately(_fill.fillAmount, q)) _fill.fillAmount = q;
            var c = charge >= 0.999f ? style.dashReady : style.dashCharging;
            if (_fill.color != c) _fill.color = c;

            if (_flashLeft > 0f)
            {
                _flashLeft = Mathf.Max(0f, _flashLeft - dt);
                _flash.enabled = _flashLeft > 0f;
                _flash.color = new Color(1f, 1f, 1f, _flashLeft / 0.3f);
            }
        }

        /// <summary>Centre of the pip in the parent's space.</summary>
        public Vector2 Centre => Root.anchoredPosition + new Vector2(5.5f, 5.5f);
    }
}
