using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Held in place by roots: the feet are refused, nothing else is.
    ///
    /// <para>WHY NOT A SHORT STUN. <see cref="StunEffect"/> is read in four places that all
    /// mean "this entity is out of the fight" — <c>PlayerController.Movement</c> returns
    /// before <c>PollCombatActions</c>, <c>NPCAutoCast</c> refuses to cast, and
    /// <c>AttackState</c> refuses to swing. A rooted entity must still be able to fight back
    /// or the spell is crowd control wearing a zoning spell's name, and the whole point of
    /// standing in a root field is that it is a place you do not want to be, not a place
    /// where you stop existing.</para>
    ///
    /// <para>WHY IT DOES NOT SIMPLY ZERO THE VELOCITY. It does, in <see cref="Tick"/>, but
    /// only as a backstop for entities driven by neither of the two movement owners. Both
    /// owners write <c>Rigidbody2D.velocity</c> unconditionally every frame with no script
    /// execution order defined against this class, so an effect that only wrote velocity
    /// would race them and lose — exactly the fight <c>FSMComponents.SetVelocity</c> was
    /// written to end. The authority is <see cref="StatusEffectManager.IsRooted"/>, which
    /// <c>PlayerController.FixedUpdate</c> and <c>FSMComponents.SetVelocity</c> both read.</para>
    /// </summary>
    public sealed class RootEffect : StatusEffect
    {
        private Rigidbody2D _rb;

        public override StatusEffectKind Kind => StatusEffectKind.Root;

        /// <param name="duration">Seconds the hold lasts.</param>
        /// <param name="applier">The source GameObject.</param>
        public RootEffect(float duration, GameObject applier = null)
            : base(duration, applier) { }

        public override void OnApply(StatusEffectManager target)
        {
            _rb = target.GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.velocity = Vector2.zero;

            var tint = SpriteTintStack.Attach(target);
            if (tint != null)
                target.StartCoroutine(RootTintRoutine(tint, target));
        }

        public override void Tick(StatusEffectManager target)
        {
            if (_rb != null) _rb.velocity = Vector2.zero;
        }

        public override void OnRemove(StatusEffectManager target)
        {
            // Velocity resumes naturally from whichever owner drives this body next frame.
        }

        private System.Collections.IEnumerator RootTintRoutine(SpriteTintStack tint,
                                                               StatusEffectManager target)
        {
            // Bark green. Held flat rather than pulsed: a root is a state the player reads
            // once, and a throbbing body would compete with the tendrils that are doing the
            // actual talking.
            Color rootColor = new Color(0.42f, 0.62f, 0.30f, 1f);

            while (!IsExpired && target != null)
            {
                tint.Set(TintLayer.Root, Color.Lerp(Color.white, rootColor, 0.35f));
                yield return null;
            }

            if (tint != null) tint.Clear(TintLayer.Root);
        }
    }
}
