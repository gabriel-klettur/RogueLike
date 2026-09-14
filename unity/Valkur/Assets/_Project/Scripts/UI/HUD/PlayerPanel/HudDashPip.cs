using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

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
    /// <para><b>It reads the SPELL BOOK's cooldown for "dash", not <c>DashAbility</c>.</b>
    /// <c>DashAbility.TryDash</c> has no callers — the dash is the spell "dash", cast through
    /// <c>SpellCaster.TryCastByKey</c> — so <c>DashAbility.CanDash</c> answered true forever and
    /// this pip never showed the real cooldown, same defect as <see cref="Valkur.Gameplay.Combat.WorldDashBar"/>
    /// over the head. <c>DashAbility</c> is kept only as a fallback and for <c>IsDashing</c>'s
    /// cosmetic "empty during the lunge" read, which is always false today and therefore harmless.</para>
    /// </summary>
    public sealed class HudDashPip
    {
        private const string DashSpellKey = "dash";

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

        public void Tick(float dt, SpellCaster caster, DashAbility dash, PlayerHudStyle style)
        {
            float charge = ResolveCharge(caster, dash);

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

        /// <summary>
        /// 1 while the dash is available, otherwise how far through the "dash" spell's book
        /// cooldown it is. Falls back to <paramref name="dash"/>'s own (currently always-ready)
        /// reading when there is no caster or "dash" is not in the book, so the pip degrades to
        /// its historical behaviour rather than to a blank one.
        /// </summary>
        private static float ResolveCharge(SpellCaster caster, DashAbility dash)
        {
            if (dash != null && dash.IsDashing) return 0f;

            if (caster != null && caster.KnowsSpell(DashSpellKey))
            {
                var spell = caster.GetSpellByKey(DashSpellKey);
                if (spell != null)
                {
                    float total = caster.ResolveCooldown(spell);
                    if (total <= 0f) return 1f;
                    float remaining = caster.GetBookCooldownRemaining(DashSpellKey);
                    return Mathf.Clamp01(1f - remaining / total);
                }
            }

            if (dash == null) return 1f;
            if (!dash.CanDash)
                return dash.CooldownTotal > 0f
                    ? 1f - Mathf.Clamp01(dash.CooldownRemaining / dash.CooldownTotal)
                    : 0f;
            return 1f;
        }
    }
}
