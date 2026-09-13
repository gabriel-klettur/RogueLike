using UnityEngine;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// What a drawn shape MEANS. The role is the colour and the reading order, and it is a
    /// closed vocabulary on purpose: an overlay whose author can invent a colour per call site
    /// is one where two spells draw the same thing in two colours and nothing can be compared.
    /// </summary>
    public enum SpellDebugRole
    {
        /// <summary>Where the spell is BORN — the resolved cast origin, after anchor + muzzle.</summary>
        Origin = 0,
        /// <summary>The heading the cast was actually resolved to fly on.</summary>
        Aim = 1,
        /// <summary>The reach the spell is ALLOWED, before anything was found inside it.</summary>
        Reach = 2,
        /// <summary>Where a ground-placed spell landed.</summary>
        Placement = 3,
        /// <summary>The geometry a damage / heal query actually swept. THE one that matters.</summary>
        Damage = 4,
        /// <summary>A secondary query: a mine's proximity trigger, a homing acquisition ring.</summary>
        Trigger = 5,
        /// <summary>A splash / explosion resolved on impact.</summary>
        Splash = 6,
        /// <summary>The path something travelled, or a blocking probe.</summary>
        Path = 7,
        /// <summary>The DRAWN silhouette, when a rig can state one. Compare against Damage.</summary>
        Visual = 8,
        /// <summary>
        /// The creature's OWN authored muzzle, when it has one. Drawn beside the anchor rather
        /// than instead of it: the interesting picture is the two together, because the whole
        /// reason a muzzle exists is that the anchor could not reach where the art needs it.
        /// </summary>
        Muzzle = 9,
    }

    /// <summary>The primitive a shape is made of.</summary>
    public enum SpellDebugKind
    {
        Point = 0,
        Circle = 1,
        /// <summary>Circle sector: centre, heading, radius, full arc in degrees.</summary>
        Sector = 2,
        /// <summary>A thick line from A to B — a beam capsule, a sweep, a ray.</summary>
        Segment = 3,
        /// <summary>An oriented rectangle: centre, size, rotation in degrees.</summary>
        Rect = 4,
    }

    /// <summary>
    /// One recorded shape. A plain struct held in a list: the overlay is a SNAPSHOT of what a
    /// cast did, not a live scene graph, so nothing here references a GameObject that may be
    /// gone by the time the author looks at it.
    /// </summary>
    public struct SpellDebugShape
    {
        public SpellDebugKind Kind;
        public SpellDebugRole Role;
        /// <summary>Centre for Circle / Sector / Rect, start point for Segment, the point for Point.</summary>
        public Vector2 A;
        /// <summary>End point for Segment. Unused otherwise.</summary>
        public Vector2 B;
        /// <summary>Heading for Sector. Unused otherwise.</summary>
        public Vector2 Direction;
        /// <summary>Radius for Circle / Sector, half-width for Segment, unused for Rect.</summary>
        public float Radius;
        /// <summary>Full arc in degrees for Sector, rotation in degrees for Rect.</summary>
        public float Angle;
        /// <summary>Size for Rect.</summary>
        public Vector2 Size;
        /// <summary>What to print beside it. Empty draws no label.</summary>
        public string Label;

        /// <summary>
        /// True when two shapes say the same thing. A ticking field (a puddle, a beam, an aura)
        /// records the same geometry ten times a second; without this the buffer is full of one
        /// circle a fifth of a second after the cast and every later shape is dropped.
        /// </summary>
        public bool SameAs(in SpellDebugShape other)
        {
            const float EPS = 0.02f;
            if (Kind != other.Kind || Role != other.Role) return false;
            if ((A - other.A).sqrMagnitude > EPS * EPS) return false;
            if ((B - other.B).sqrMagnitude > EPS * EPS) return false;
            if (Mathf.Abs(Radius - other.Radius) > EPS) return false;
            if (Mathf.Abs(Angle - other.Angle) > 0.5f) return false;
            if ((Size - other.Size).sqrMagnitude > EPS * EPS) return false;
            if (Kind == SpellDebugKind.Sector &&
                Vector2.Dot(Direction, other.Direction) < 0.999f) return false;
            return true;
        }
    }
}
