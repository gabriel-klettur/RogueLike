using UnityEngine;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The dash driver. Reports a charge fraction to <see cref="WorldBarRig"/>, which draws it as
    /// a square pip at the right end of the resource row.
    ///
    /// <para><b>Why it stopped being a bar.</b> It was a full-width strip built out of a
    /// one-segment loop — <c>_segmentCount = 1</c>, with the gap arithmetic for the segments that
    /// never existed left in — sitting directly under a mana bar of the same width and shape, in
    /// cyan against blue. Two adjacent rectangles that differ only in hue are one rectangle to a
    /// glance and to a colour-blind player. A charge is a COUNT: it gets a shape that is not a
    /// bar, and the row it frees goes to the health bar's frame.</para>
    ///
    /// <para><b>It reads the SPELL BOOK's cooldown for "dash", not <c>DashAbility</c>.</b>
    /// <c>DashAbility.TryDash</c> has no callers anywhere in the project — the dash the player
    /// actually performs is the spell "dash", cast through <c>SpellCaster.TryCastByKey</c> from
    /// <c>PlayerController.Movement</c> — so <c>DashAbility.CanDash</c> answered true forever and
    /// this pip never showed the real cooldown. <c>DashAbility</c> is kept only for the gate (only
    /// the player carries one, via <c>EntitySetup.InitPlayerCombat</c>, which is how this driver
    /// stayed player-only before) and for <c>IsDashing</c>'s cosmetic "empty during the lunge" —
    /// always false today, so it is a harmless no-op rather than a defect this fix introduces.
    /// Reviving <c>DashAbility</c> itself is out of scope here.</para>
    ///
    /// <para>It also polls rather than subscribing, because neither the spell book cooldown nor
    /// <c>DashAbility</c> raises an event for its own passing. The poll is one comparison per
    /// frame and the rig throws away any charge that has not moved.</para>
    /// </summary>
    public class WorldDashBar : MonoBehaviour
    {
        private const string DashSpellKey = "dash";

        private DashAbility _dash;
        private SpellCaster _caster;
        private WorldBarRig _rig;

        private void Awake()
        {
            _dash = GetComponent<DashAbility>();
            if (_dash == null) return;   // no dash, no pip (player only, in practice)
            _caster = GetComponent<SpellCaster>();
            _rig = WorldBarRig.Ensure(gameObject);
            _rig.EnableDash(true);
        }

        private void OnEnable()
        {
            if (_dash == null || _rig == null) return;
            _rig.EnableDash(true);
        }

        private void OnDisable() => _rig?.EnableDash(false);

        private void Update()
        {
            if (_dash == null || _rig == null) return;
            _rig.SetDashCharge(ResolveCharge());
        }

        /// <summary>
        /// 1 while the dash is available, otherwise how far through the "dash" spell's book
        /// cooldown it is.
        ///
        /// <para>The dash ITSELF reads as an empty pip on purpose: <c>DashAbility.IsDashing</c> is
        /// false while it is never set (see the class doc), so this branch is inert today and
        /// exists to pick the reading back up the moment <c>DashAbility</c> is revived.</para>
        /// </summary>
        private float ResolveCharge()
        {
            if (_dash.IsDashing) return 0f;

            if (_caster != null && _caster.KnowsSpell(DashSpellKey))
            {
                var spell = _caster.GetSpellByKey(DashSpellKey);
                if (spell != null)
                {
                    float total = _caster.ResolveCooldown(spell);
                    if (total <= 0f) return 1f;
                    float remaining = _caster.GetBookCooldownRemaining(DashSpellKey);
                    return Mathf.Clamp01(1f - remaining / total);
                }
            }

            // Fallback: no caster, or "dash" is not in the book. This is the OLD reading —
            // DashAbility.CanDash is true forever because nothing ever sets _isDashing — kept so
            // this driver degrades to its previous behaviour rather than to a blank pip.
            if (_dash.CanDash) return 1f;
            float dashTotal = _dash.CooldownTotal;
            if (dashTotal <= 0f) return 0f;
            return Mathf.Clamp01(1f - _dash.CooldownRemaining / dashTotal);
        }
    }
}
