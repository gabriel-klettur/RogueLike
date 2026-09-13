using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells.Debugging;

namespace Valkur.Gameplay.Spells
{
    public partial class SpellCaster : MonoBehaviour
    {
        /// <summary>
        /// The part of a cast's geometry that belongs to the CAST rather than to any one
        /// executor: where it was born, which way it was aimed, how far it was allowed to
        /// reach, and - for a ground-placed spell - where it landed.
        ///
        /// <para>Recorded here because these four are resolved by shared helpers
        /// (<c>ProjectileExecutor.ResolveCastStart</c>, <c>SpellTargeting</c>) that every
        /// executor calls, so instrumenting the executors instead would mean writing the same
        /// four pushes twenty-six times and getting one of them wrong.</para>
        ///
        /// <para>It re-resolves through those same helpers rather than reading the definition:
        /// the anchor, the muzzle, the forward clearance and the cursor clamp are exactly the
        /// steps an author is trying to SEE, and a version of them written here would be a
        /// second implementation that agrees with the asset and not with the game.</para>
        /// </summary>
        private void RecordCastGeometry(SpellDefinition spell, SpellContext ctx)
        {
            if (spell == null) return;

            // The anchor's OWN answer, with any muzzle deliberately ignored, so the two are
            // drawn as two points. Asking the normal resolver here puts both labels on the
            // same pixel whenever a muzzle wins -- true, and useless, because the question a
            // muzzle exists to answer is how far the fallback was off.
            Vector2 anchor = ProjectileExecutor.ResolveAnchorOrigin(transform, spell.castAnchor);
            SpellDebugAreas.Point(anchor, SpellDebugRole.Origin, "ancla " + spell.castAnchor);

            // The creature's OWN muzzle, beside the anchor rather than instead of it. Both
            // together is the readable picture: a muzzle exists precisely because the anchor
            // could not reach where the art needs the cast to be born, so seeing only the
            // winner says nothing about how far off the fallback was.
            var muzzle = GetComponent<CastMuzzle>();
            if (muzzle != null && muzzle.TryResolve(spell.spellKey, out Vector3 muzzleWorld))
            {
                var point = muzzle.ResolvePointForDebug(spell.spellKey);
                SpellDebugAreas.Point(muzzleWorld, SpellDebugRole.Muzzle,
                    point != null && !string.IsNullOrEmpty(point.key)
                        ? "boca: " + point.key
                        : "boca (criatura)");
            }

            // The heading the spell really flies on. For a player this is re-aimed from the
            // ORIGIN, not from the body centre the facing was measured against - the two differ
            // by a constant sideways miss at every range, which is the whole of "it goes near
            // the cursor but not at it".
            Vector2 aim = SpellTargeting.ResolveAimDirection(transform, ctx.Direction, spell);
            Vector2 start = ProjectileExecutor.ResolveCastStart(transform, aim, spell);
            SpellDebugAreas.Point(start, SpellDebugRole.Origin, "salida");

            float reach = spell.range > 0f ? spell.range : 0f;
            if (reach > 0f)
            {
                SpellDebugAreas.Circle(start, reach, SpellDebugRole.Reach, "alcance " + reach.ToString("0.##") + " u");
                SpellDebugAreas.Segment(start, start + aim * reach, 0.01f, SpellDebugRole.Aim, null);
            }
            else
            {
                SpellDebugAreas.Segment(start, start + aim * 2f, 0.01f, SpellDebugRole.Aim, "sin alcance");
            }

            // Where a ground-placed spell actually lands. Asked of the same owner the executors
            // ask, so the marker cannot sit somewhere the spell does not.
            if (spell.spawnAtMouse)
            {
                float fallbackReach = spell.radius > 0f ? spell.radius * 2.5f : 6f;
                float fallbackDistance = spell.radius > 0f ? spell.radius : 2f;
                Vector2 landing = SpellTargeting.ResolveGroundTarget(ctx, fallbackReach, fallbackDistance);
                SpellDebugAreas.Point(landing, SpellDebugRole.Placement, "destino");
            }
        }
    }
}
