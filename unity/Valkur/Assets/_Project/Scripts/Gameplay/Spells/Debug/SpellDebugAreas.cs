using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// The record of what ONE cast did, geometrically.
    ///
    /// <para><b>This exists because the areas could not be seen.</b> Every impact area in this
    /// project is a <c>Physics2D</c> query the player never sees, drawn beside an effect that
    /// was sized by a different number - and this repository's own history is a list of exactly
    /// that failure: a wall 0.78 units wide because <c>wallWidth</c> was in Python pixels, a
    /// vortex whose light rendered at 367 units, an aura whose gameplay radius was 0.039 units
    /// and was then discarded, a cone that damaged a wedge and drew two strokes. Each was
    /// internally consistent and disagreed only with the screen, which is precisely the class of
    /// defect a number cannot report and a picture can.</para>
    ///
    /// <para><b>A shape is pushed by the code that ACTUALLY QUERIES, never re-derived from the
    /// asset.</b> An overlay built by reading <c>SpellDefinition.radius</c> would draw what the
    /// author typed while the executor swept something else - it would paint over the very bug
    /// it was built to expose. <see cref="SpellProbe"/> exists so the query and the drawing are
    /// one call and cannot drift.</para>
    ///
    /// <para><b>The record survives until the next cast</b>, which is the whole point: an area
    /// that vanishes with its effect can only be compared against memory. <see cref="BeginCast"/>
    /// is the one place it is cleared, and it is called from <c>SpellCaster.ExecuteSpell</c> -
    /// the single seam every cast in the game passes through, monsters included.</para>
    ///
    /// <para>OFF by default and free when off: every push returns on its first line, so nothing
    /// here costs a normal play session anything. It is a debugging instrument, not a feature.</para>
    /// </summary>
    public static class SpellDebugAreas
    {
        /// <summary>
        /// Hard cap on one cast's record. A meteor shower resolves eight impacts and a beam
        /// ticks for as long as it is held; without a cap a channelled spell grows the buffer
        /// for the length of the channel. Dedup does most of the work - see
        /// <see cref="SpellDebugShape.SameAs"/> - and this is the floor under it.
        /// </summary>
        public const int MaxShapes = 384;

        private static readonly List<SpellDebugShape> Shapes = new List<SpellDebugShape>(MaxShapes);

        private static bool _enabled;
        private static int _version;
        private static int _dropped;
        private static string _spellKey = string.Empty;
        private static string _casterName = string.Empty;

        /// <summary>
        /// Master switch. Turning it OFF clears the record as well: leaving the last cast's
        /// shapes behind means switching the overlay back on shows a picture of something that
        /// happened at an unknown point in the past, which is worse than an empty screen.
        /// </summary>
        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!value) Clear();
                _version++;
            }
        }

        /// <summary>Bumped whenever the record changes, so the renderer rebuilds only then.</summary>
        public static int Version { get { return _version; } }

        /// <summary>The shapes of the last cast, oldest first. Read-only by contract.</summary>
        public static IReadOnlyList<SpellDebugShape> Current { get { return Shapes; } }

        /// <summary>Which spell drew them, for the legend.</summary>
        public static string SpellKey { get { return _spellKey; } }

        /// <summary>Who cast it, for the legend.</summary>
        public static string CasterName { get { return _casterName; } }

        /// <summary>How many shapes were refused by the cap since the last cast.</summary>
        public static int Dropped { get { return _dropped; } }

        /// <summary>
        /// Start a new record, discarding the previous cast's.
        ///
        /// <para>Called from the executor seam and not from the input layer: a cast refused for
        /// mana, for cooldown or for the stance never reaches an executor, and clearing the
        /// previous picture for a cast that did not happen is how an author comes to believe an
        /// area moved when nothing moved.</para>
        /// </summary>
        public static void BeginCast(SpellDefinition spell, Transform caster)
        {
            if (!_enabled) return;

            Shapes.Clear();
            _dropped = 0;
            _spellKey = spell != null ? spell.spellKey : "(sin hechizo)";
            _casterName = caster != null ? caster.name : "(sin lanzador)";
            _version++;
        }

        /// <summary>Empty the record without starting a new one.</summary>
        public static void Clear()
        {
            Shapes.Clear();
            _dropped = 0;
            _spellKey = string.Empty;
            _casterName = string.Empty;
            _version++;
        }

        // -- Pushes ---------------------------------------------------------------

        public static void Point(Vector2 at, SpellDebugRole role, string label = null)
        {
            var shape = new SpellDebugShape
            {
                Kind = SpellDebugKind.Point, Role = role, A = at, Label = label
            };
            Push(shape);
        }

        public static void Circle(Vector2 centre, float radius, SpellDebugRole role, string label = null)
        {
            var shape = new SpellDebugShape
            {
                Kind = SpellDebugKind.Circle, Role = role, A = centre, Radius = radius,
                Label = label ?? FormatDistance(radius)
            };
            Push(shape);
        }

        public static void Sector(Vector2 centre, Vector2 direction, float radius,
                                  float arcDegrees, SpellDebugRole role, string label = null)
        {
            var shape = new SpellDebugShape
            {
                Kind = SpellDebugKind.Sector, Role = role, A = centre,
                Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right,
                Radius = radius, Angle = arcDegrees,
                Label = label ?? (FormatDistance(radius) + " / " + arcDegrees.ToString("0") + " grados")
            };
            Push(shape);
        }

        public static void Segment(Vector2 from, Vector2 to, float halfWidth,
                                   SpellDebugRole role, string label = null)
        {
            var shape = new SpellDebugShape
            {
                Kind = SpellDebugKind.Segment, Role = role, A = from, B = to, Radius = halfWidth,
                Label = label ?? FormatDistance(Vector2.Distance(from, to))
            };
            Push(shape);
        }

        public static void Rect(Vector2 centre, Vector2 size, float rotationDegrees,
                                SpellDebugRole role, string label = null)
        {
            var shape = new SpellDebugShape
            {
                Kind = SpellDebugKind.Rect, Role = role, A = centre, Size = size,
                Angle = rotationDegrees,
                Label = label ?? (size.x.ToString("0.##") + " x " + size.y.ToString("0.##"))
            };
            Push(shape);
        }

        private static void Push(SpellDebugShape shape)
        {
            if (!_enabled) return;

            // Walk from the END: a ticking effect repeats its own last shape, so the match is
            // almost always the previous entry and the scan costs one comparison.
            for (int i = Shapes.Count - 1; i >= 0; i--)
                if (Shapes[i].SameAs(shape)) return;

            if (Shapes.Count >= MaxShapes) { _dropped++; return; }

            Shapes.Add(shape);
            _version++;
        }

        private static string FormatDistance(float r)
        {
            return r.ToString("0.##") + " u";
        }

        /// <summary>
        /// Domain Reload is OFF, so every static here outlives a Play session. The record is a
        /// snapshot of world positions from the PREVIOUS run: left alone it would draw the last
        /// cast of the last session over a world that has been rebuilt underneath it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Shapes.Clear();
            _enabled = false;
            _version = 0;
            _dropped = 0;
            _spellKey = string.Empty;
            _casterName = string.Empty;
        }
    }
}
