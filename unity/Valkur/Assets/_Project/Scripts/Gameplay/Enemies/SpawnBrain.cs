using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// What the ENCOUNTER says about this spawn's behaviour, as opposed to what its shared
    /// <c>MonsterDefinition</c> says.
    ///
    /// <para><b>Why a component.</b> Exactly the argument <see cref="SpawnLevel"/> makes, and
    /// for the same reason it is a separate one: a spawner used to be able to influence
    /// precisely two facts about what it produced — <c>persistent</c> and the resolved level —
    /// because everything else came off the definition, which is shared by every instance of
    /// that monster in the world. Writing a leash or an FSM set onto the definition to make
    /// one camp defend its point would change every monster of that kind, and — since Unity
    /// keeps a ScriptableObject edited in Play Mode until the next domain reload — would
    /// follow the author back into the Editor and rewrite shipped balance. Written on the
    /// object, it belongs to this body and dies with it.</para>
    ///
    /// <para><b>Absent means "the definition's own behaviour".</b> Every spawn path that
    /// predates this — the console's <c>spawn</c> command, hand-placed entities, the summon
    /// executor, boss adds, every test double — attaches nothing and behaves exactly as it
    /// did.</para>
    ///
    /// <para><b>It must be stamped BEFORE <c>EntitySetup.ConfigureMonster</c>.</b> That is
    /// where <c>FSMMonsterBrain.Initialize</c> builds the machine and fills the context, so a
    /// stamp applied afterwards is a frame late and silently ignored — the same ordering
    /// constraint <see cref="SpawnLevel"/> carries, and the reason both are passed into
    /// <c>MonsterSpawner.SpawnEntity</c> rather than applied to its return value.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnBrain : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("FSM set id this spawn is built with, e.g. 'Monster_Caster'. Empty = resolve " +
                 "as usual.")]
        private string fsmSet = string.Empty;

        [SerializeField]
        [Tooltip("Leash radius in world units — how far from home this spawn chases before " +
                 "breaking off. 0 = the monster's own default (aggroRange x " +
                 "FSMTuning.DefaultLeashRangeFactor).")]
        private float leashRadius;

        /// <summary>The FSM set to build with, or empty for the normal resolution.</summary>
        public string FsmSet => fsmSet;

        /// <summary>The leash override in world units, or 0 for none.</summary>
        public float LeashRadius => leashRadius;

        /// <summary>
        /// Stamp an encounter's behaviour onto <paramref name="go"/>.
        ///
        /// <para>Attaches NOTHING when both values are neutral. A component whose presence
        /// means "no change" is a component whose presence means nothing, and it would cost a
        /// `GetComponent` on every monster in the world to learn that.</para>
        /// </summary>
        public static void Stamp(GameObject go, string fsmSetOverride, float leashOverride)
        {
            if (go == null) return;

            bool hasSet   = !string.IsNullOrWhiteSpace(fsmSetOverride);
            bool hasLeash = leashOverride > 0f;
            if (!hasSet && !hasLeash) return;

            var component = go.GetComponent<SpawnBrain>();
            if (component == null) component = go.AddComponent<SpawnBrain>();

            component.fsmSet      = hasSet ? fsmSetOverride.Trim() : string.Empty;
            component.leashRadius = hasLeash ? leashOverride : 0f;
        }

        /// <summary>The FSM set override for <paramref name="go"/>, or null when there is none.</summary>
        public static string FsmSetOf(GameObject go)
        {
            if (go == null) return null;
            var component = go.GetComponent<SpawnBrain>();
            return component != null && !string.IsNullOrEmpty(component.fsmSet) ? component.fsmSet : null;
        }

        /// <summary>The leash override for <paramref name="go"/> in world units, or 0.</summary>
        public static float LeashOf(GameObject go)
        {
            if (go == null) return 0f;
            var component = go.GetComponent<SpawnBrain>();
            return component != null ? component.leashRadius : 0f;
        }
    }
}
