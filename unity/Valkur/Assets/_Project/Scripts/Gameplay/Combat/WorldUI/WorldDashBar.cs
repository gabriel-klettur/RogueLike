using UnityEngine;

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
    /// <para>It also polls rather than subscribing, because <c>DashAbility</c> raises no event —
    /// its cooldown is a float ticking down inside its own <c>Update</c>. The poll is one
    /// comparison per frame and the rig throws away any charge that has not moved.</para>
    /// </summary>
    public class WorldDashBar : MonoBehaviour
    {
        private DashAbility _dash;
        private WorldBarRig _rig;

        private void Awake()
        {
            _dash = GetComponent<DashAbility>();
            if (_dash == null) return;   // no dash, no pip
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
        /// 1 while the dash is available, otherwise how far through its cooldown it is.
        ///
        /// <para>The dash ITSELF reads as an empty pip on purpose: <c>CanDash</c> is false while
        /// the lunge is in flight, and a pip that stayed full through it would report the ability
        /// as available at the one moment it certainly is not.</para>
        /// </summary>
        private float ResolveCharge()
        {
            if (_dash.CanDash) return 1f;
            float total = _dash.CooldownTotal;
            if (total <= 0f) return 0f;
            return Mathf.Clamp01(1f - _dash.CooldownRemaining / total);
        }
    }
}
