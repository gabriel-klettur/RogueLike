using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Spells;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The particles every worked node shares: wood chips, falling leaves, dust and the impact
    /// flash. ONE rig for the whole world, created on the first blow of the session.
    ///
    /// <para><b>WHY SHARED.</b> The previous feedback built a <c>ParticleSystem</c> and a flash
    /// sprite on each node the first time it was hit and never released them, so a player who
    /// felled the 88 trees on the map left 88 particle systems alive until the scene unloaded.
    /// All three systems simulate in WORLD space and are driven by explicit <c>Emit</c> calls
    /// with a position, which is exactly the case where one system serves any number of
    /// emitters: the particles do not care which tree they came from.</para>
    ///
    /// <para><b>OPAQUE DEBRIS, ADDITIVE LIGHT.</b> Chips and leaves are alpha-blended because a
    /// dark chip on an additive surface adds nothing and vanishes — the rule <c>KiAuraFX</c> and
    /// <c>VortexFunnelFX</c> record. Only the flash is additive: it is light, and it is the one
    /// piece that should read on a dark trunk.</para>
    /// </summary>
    public static class HarvestFx
    {
        private const int SPARK_POOL = 8;
        private const float SPARK_SECONDS = 0.11f;

        private static HarvestFxRig _rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _rig = null;

        /// <summary>Whether the rig exists. A test seam; nothing in play needs to ask.</summary>
        public static bool IsBuilt => _rig != null;

        /// <summary>How many particle systems the rig owns. Constant, however many nodes are worked.</summary>
        public static int SystemCount => _rig != null ? 3 : 0;

        public static void Chips(Vector3 at, Color color, int count, Vector2 away)
        {
            var rig = Rig();
            if (rig == null || count <= 0) return;
            rig.Emit(rig.Chips, at, color, count, away, 2.4f);
        }

        public static void Leaves(Vector3 at, Color color, int count, float spread)
        {
            var rig = Rig();
            if (rig == null || count <= 0) return;
            rig.EmitArea(rig.LeafSystem, at, color, count, spread);
        }

        public static void Dust(Vector3 at, Color color, int count, float spread)
        {
            var rig = Rig();
            if (rig == null || count <= 0) return;
            rig.EmitArea(rig.DustSystem, at, color, count, spread);
        }

        public static void Flash(Vector3 at, Color color, float size)
        {
            var rig = Rig();
            if (rig == null) return;
            rig.Flash(at, color, size, SPARK_SECONDS);
        }

        private static HarvestFxRig Rig()
        {
            // Unity-null after a scene unload: rebuild rather than emit into a destroyed system.
            if (_rig != null) return _rig;

            // Edit Mode builds nothing: a fixture that lands a blow must not leave a root
            // GameObject with three particle systems behind in the test scene.
            if (!Application.isPlaying) return null;
            var go = new GameObject("[HarvestFx]");
            _rig = go.AddComponent<HarvestFxRig>();
            _rig.Build(SPARK_POOL);
            return _rig;
        }
    }
}
