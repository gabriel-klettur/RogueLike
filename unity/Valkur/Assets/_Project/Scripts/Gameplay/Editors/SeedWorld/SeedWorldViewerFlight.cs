using UnityEngine;

namespace Valkur.Gameplay.Editors.SeedWorld
{
    /// <summary>
    /// The floating camera of "Visualizar mapa", as arithmetic: which way the keys point, how far
    /// one frame flies, and where the world ends. Pure, so every rule is provable in Edit Mode
    /// without a camera.
    ///
    /// <para><b>Speed is measured in SCREENS, not in world units.</b> A fixed units-per-second is
    /// a crawl when zoomed out over a 400-tile world and a blur when zoomed in on one street; a
    /// speed proportional to the view height crosses one screen in the same time at every zoom.</para>
    ///
    /// <para><b>A diagonal is not faster</b>, and <b>a long frame is clamped</b>: zones paint while
    /// the camera moves, and a zone that costs a hitch must not throw the camera a screen further
    /// than the author steered it.</para>
    /// </summary>
    public static class SeedWorldViewerFlight
    {
        /// <summary>Screen heights crossed per second at normal speed.</summary>
        public const float ScreensPerSecond = 0.9f;

        /// <summary>How much faster the fast key flies.</summary>
        public const float FastMultiplier = 3f;

        /// <summary>The longest frame a single step is allowed to integrate.</summary>
        public const float MaxStepSeconds = 0.1f;

        /// <summary>
        /// The widest view the viewer allows. The live streamer paints at most a few zones either
        /// side of the camera centre, so a wider lens shows the edge of the painted ground rather
        /// than more world. At a 2:1 viewport this is 160 x 80 tiles.
        /// </summary>
        public const float MaxOrthoSize = 40f;

        /// <summary>The direction the held keys point, normalised; zero when they cancel out.</summary>
        public static Vector2 Direction(bool up, bool down, bool left, bool right)
        {
            float x = (right ? 1f : 0f) - (left ? 1f : 0f);
            float y = (up ? 1f : 0f) - (down ? 1f : 0f);
            var dir = new Vector2(x, y);
            return dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }

        /// <summary>World units per second for a lens of <paramref name="orthoSize"/>.</summary>
        public static float Speed(float orthoSize, bool fast)
            => Mathf.Max(0f, orthoSize) * 2f * ScreensPerSecond * (fast ? FastMultiplier : 1f);

        /// <summary>One frame of flight from <paramref name="position"/>.</summary>
        public static Vector2 Step(Vector2 position, Vector2 direction, float orthoSize, bool fast, float deltaTime)
        {
            if (direction == Vector2.zero) return position;
            float dt = Mathf.Clamp(deltaTime, 0f, MaxStepSeconds);
            return position + direction * (Speed(orthoSize, fast) * dt);
        }

        /// <summary>
        /// Keep the camera CENTRE over the world — the centre and not the view, so an author zoomed
        /// out can still put the corner of the map in the middle of the screen. An empty rect
        /// (no world known) clamps nothing.
        /// </summary>
        public static Vector2 ClampToWorld(Vector2 position, Rect world)
        {
            if (world.width <= 0f || world.height <= 0f) return position;
            return new Vector2(Mathf.Clamp(position.x, world.xMin, world.xMax),
                               Mathf.Clamp(position.y, world.yMin, world.yMax));
        }
    }
}
