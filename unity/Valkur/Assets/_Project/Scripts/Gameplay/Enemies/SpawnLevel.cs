using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The level THIS spawn arrived at, as opposed to the level its shared definition says.
    ///
    /// <para><b>Why a component instead of a parameter.</b> Two independent places scale a
    /// monster's stats — <c>EntitySetup.ConfigureMonster</c> for hp and defense, and
    /// <c>FSMMonsterBrain.Initialize</c> for hp and melee damage — and they run at different
    /// moments, from different callers, on paths that do not all pass through each other.
    /// Threading a level argument down both would make the level a fact that has to be handed
    /// over correctly twice, and this project's own history is a list of things that were
    /// internally consistent on each side and wrong in the composition. Written on the object
    /// itself, there is one value and both readers ask the same question.</para>
    ///
    /// <para><b>Absent means "the definition's own level".</b> Every spawn path that predates
    /// encounter difficulty — the console's <c>spawn</c> command, hand-placed entities, the
    /// summon executor, every test double — attaches nothing and therefore keeps behaving
    /// exactly as it did. That is what lets the difficulty layer land without re-testing every
    /// caller.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnLevel : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Resolved by EncounterDifficulty at spawn. 0 means nothing resolved one and " +
                 "the MonsterDefinition's own level is used.")]
        private int level;

        /// <summary>The resolved level, or 0 when none was set.</summary>
        public int Level => level;

        /// <summary>
        /// Stamp a resolved level onto <paramref name="go"/>. A level at or below 1 attaches
        /// nothing: level 1 IS the unscaled baseline, and an object carrying a component that
        /// says "no change" is a component whose presence means nothing.
        /// </summary>
        public static void Stamp(GameObject go, int resolvedLevel)
        {
            if (go == null || resolvedLevel <= 1) return;

            var component = go.GetComponent<SpawnLevel>();
            if (component == null) component = go.AddComponent<SpawnLevel>();
            component.level = resolvedLevel;
        }

        /// <summary>
        /// The level to scale <paramref name="def"/> by for this particular entity.
        /// The single question both stat-scaling sites ask.
        /// </summary>
        public static int Of(GameObject go, MonsterDefinition def)
        {
            int fallback = def != null ? Mathf.Max(1, def.level) : 1;
            if (go == null) return fallback;

            var component = go.GetComponent<SpawnLevel>();
            return component != null && component.level > 0 ? component.level : fallback;
        }
    }
}
