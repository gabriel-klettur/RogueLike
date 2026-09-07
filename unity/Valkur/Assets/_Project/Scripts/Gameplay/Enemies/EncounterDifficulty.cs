using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// What level a monster should arrive at, given where it was spawned and how far into the
    /// run the player is.
    ///
    /// <para><b>Nothing raised a monster's level before this.</b> The plumbing was all there —
    /// <c>MonsterDefinition.level</c>, <c>levelScaling</c>, and both
    /// <c>EntitySetup.ConfigureMonster</c> and <c>FSMMonsterBrain.Initialize</c> already reading
    /// <c>GetScaledStats()</c> rather than the raw block — and every one of the twenty-five
    /// shipped assets says <c>level: 1</c> with a null curve, so the whole chain resolved to
    /// "unchanged" on every spawn. A difficulty system that is one authored number away from
    /// existing, and is missing that number, is indistinguishable from no difficulty system at
    /// all.</para>
    ///
    /// <para><b>The level travels with the SPAWN, never on the definition.</b> A
    /// <c>MonsterDefinition</c> is shared by every instance of that monster in the scene, so
    /// writing a level onto it to make one camp harder raises it for every monster of that kind
    /// in the world — and Unity keeps a ScriptableObject edited in Play Mode until the next
    /// domain reload, so the edit follows the author back into the Editor and rewrites the
    /// shipped balance behind their back. <c>GetScaledStats(int)</c> exists for this.</para>
    ///
    /// <para><b>Two axes, and they answer different questions.</b> The spawner's own
    /// <c>levelBonus</c> is PLACE — this camp is deeper than that one, and it says so whether
    /// the player is level 1 or 40. <c>scaleWithPlayerLevel</c> is PROGRESS — it keeps a region
    /// from becoming furniture once the player outgrows it. A design wants both and they are
    /// not substitutes: place alone means the world stops mattering, progress alone means
    /// nowhere is more dangerous than anywhere else.</para>
    ///
    /// <para><b>Neutral by default, on purpose.</b> Every existing spawner authors a bonus of 0
    /// and a scale of 0, so this returns <c>def.level</c> and every encounter in the game is
    /// exactly what it was. That is the same contract the tuning struct and the tileset importer
    /// use, and it is what makes a system like this safe to land in one pass.</para>
    /// </summary>
    public static class EncounterDifficulty
    {
        /// <summary>
        /// Ceiling on a resolved level, so a long run cannot drive the curve into numbers
        /// nobody has looked at. Cumulative HP growth is summed per level, so an unbounded
        /// level is an unbounded loop as well as unbounded balance.
        /// </summary>
        public const int MaxLevel = 30;

        /// <summary>
        /// The level for one spawn.
        /// </summary>
        /// <param name="def">What is being spawned. Its own <c>level</c> is the floor.</param>
        /// <param name="levelBonus">Flat bonus from the encounter that placed it.</param>
        /// <param name="playerLevelScale">
        /// Fraction of the player's level ABOVE 1 to add. 0 disables it; 1 makes the encounter
        /// track the player exactly, which is the setting that turns a world into a treadmill —
        /// most content wants something between 0.3 and 0.6.
        /// </param>
        public static int ResolveLevel(MonsterDefinition def, int levelBonus, float playerLevelScale)
        {
            int baseLevel = def != null ? Mathf.Max(1, def.level) : 1;

            int fromPlayer = 0;
            if (playerLevelScale > 0f)
            {
                int playerLevel = ResolvePlayerLevel();
                if (playerLevel > 1)
                    fromPlayer = Mathf.FloorToInt((playerLevel - 1) * playerLevelScale);
            }

            return Mathf.Clamp(baseLevel + Mathf.Max(0, levelBonus) + fromPlayer, 1, MaxLevel);
        }

        /// <summary>
        /// The player's level, or 1 when there is no player, no progression component, or the
        /// call is coming from a test scene.
        ///
        /// <para>Read off <c>Experience</c> rather than <c>PlayerProgression</c>: the level IS
        /// the experience component's, <c>PlayerProgression</c> only reacts to it, and
        /// <c>Experience</c> is the one of the two that a spawn-only test scene is likely to
        /// have. Absent reads as level 1, which is the neutral answer and keeps every fixture
        /// that spawns a monster without a progression rig behaving as it did.</para>
        /// </summary>
        private static int ResolvePlayerLevel()
        {
            var player = EntityRegistry.Player;
            if (player == null) return 1;

            var experience = player.GetComponent<Valkur.Gameplay.Experience>();
            return experience != null ? Mathf.Max(1, experience.Level) : 1;
        }
    }
}
