using UnityEngine;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// Whether this build gets the seventeen in-game authoring editors.
    ///
    /// They used to be built unconditionally: roughly twenty of the boot
    /// sequence's fifty-five steps — about a third of it — created Spawner,
    /// Buildings, FSM, Items, Spells, Entities, Boss, Inventory, Particles,
    /// Lighting, General, Time &amp; Weather, Camera, Controls, Skills and Economy
    /// in EVERY player. A shipped game paid the whole boot of the development
    /// toolbox and the player could not open a single one of them.
    ///
    /// The gate is a RUNTIME check rather than an <c>#if</c> on purpose. Two
    /// reasons, and the second is the one that decides it:
    ///
    /// <list type="bullet">
    ///   <item>the saving that matters is the boot cost, which a runtime branch
    ///   removes just as completely;</item>
    ///   <item>several "editors" carry real runtime responsibilities —
    ///   <c>TileEditorManager</c> is the sink for every zone's collision tags,
    ///   <c>MapEditorManager</c> owns the world teardown that portals and doors
    ///   go through — so a compile-time removal would have to be threaded through
    ///   those dependencies to be safe, while a runtime gate lets each one be
    ///   exempted by name where it is declared.</item>
    /// </list>
    ///
    /// In the Editor everything behaves exactly as before, which is where this
    /// project is played.
    /// </summary>
    public static class RuntimeEditorPolicy
    {
        private static bool _forcedOn;

        /// <summary>
        /// True in the Unity Editor and in a Development Build; false in a release
        /// player unless <see cref="ForceEnable"/> was called.
        /// </summary>
        public static bool AuthoringEditorsAvailable =>
            _forcedOn || Application.isEditor || Debug.isDebugBuild;

        /// <summary>
        /// QA escape hatch: turn the authoring editors on in a release player for
        /// the remainder of the session. Takes effect on the next scene boot,
        /// because the sequence is built once per boot.
        /// </summary>
        public static void ForceEnable() => _forcedOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _forcedOn = false;
        }
    }
}
