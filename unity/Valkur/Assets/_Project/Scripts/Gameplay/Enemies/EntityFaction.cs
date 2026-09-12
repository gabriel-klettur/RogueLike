using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>Which side of a fight an entity is on.</summary>
    public enum FactionSide
    {
        /// <summary>The player, their summons, their thralls.</summary>
        PlayerSide = 0,

        /// <summary>Everything that hunts the player side.</summary>
        Hostile = 1,

        /// <summary>
        /// Fights nobody and is fought by nobody unless it is attacked — vendors, villagers,
        /// wildlife. Distinct from <see cref="PlayerSide"/> so a neutral is not recruited into
        /// the player's fights, and distinct from <see cref="Hostile"/> so nothing initiates
        /// against it.
        /// </summary>
        Neutral = 2,
    }

    /// <summary>
    /// The runtime answer to "whose side is this entity on".
    ///
    /// <para><b>`EntityStats.faction` was authored on all twenty-five shipped definitions and
    /// reached no AI decision.</b> It was read by the loot roll, by the respawn system, and by
    /// one <c>SetContext("faction", …)</c> whose value nothing consumed — so a field that reads
    /// like the game's allegiance model decided nothing, and allegiance was actually decided
    /// twice over somewhere else: by <see cref="AlliedUnit"/> membership, and by whether a
    /// monster's FSM set happened to declare <c>ChaseState</c>. This component is what makes the
    /// authored string live, which is the difference between a data model and a comment.</para>
    ///
    /// <para><b>It DERIVES rather than stores, and that is the whole design.</b> An ally is
    /// already known to <see cref="AlliedUnit"/>; writing "PlayerSide" into a second field when
    /// a summon is created would be two pieces of state that must agree, and CLAUDE.md records
    /// what this project's version of that failure looks like. <see cref="Side"/> asks
    /// <see cref="AlliedUnit"/> first and falls back to the authored string, so there is exactly
    /// one place a side can come from and no way for the two to disagree.</para>
    ///
    /// <para><b>An unauthored faction is HOSTILE, deliberately.</b> Every test double and every
    /// synthetic monster built without a <c>MonsterDefinition</c> reaches this with an empty
    /// string, and the historical behaviour of such an entity is to hunt the player. Defaulting
    /// to Neutral would silently pacify a large part of the existing suite and any hand-placed
    /// entity whose definition nobody finished.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityFaction : MonoBehaviour
    {
        /// <summary>The authored string, verbatim from <c>EntityStats.faction</c>.</summary>
        [SerializeField]
        [Tooltip("Copied from MonsterDefinition.stats.faction by EntitySetup. EVIL is hostile, " +
                 "NEUTRAL fights nobody, GOOD/ALLY/PLAYER are the player's side. Anything else " +
                 "— including empty — reads as hostile, which is what an entity with no " +
                 "definition has always done.")]
        private string faction;

        /// <summary>
        /// The side this entity fights on right now. Allied membership WINS over the authored
        /// string: a charmed monster keeps its own `EVIL` faction and still fights for the
        /// player, and it goes back to being hostile the moment the charm's
        /// <see cref="AlliedUnit"/> is gone — without anything having to remember to rewrite a
        /// field.
        /// </summary>
        public FactionSide Side => SideOf(gameObject);

        /// <summary>The raw authored value, for diagnostics and the debug overlay.</summary>
        public string AuthoredFaction => faction;

        /// <summary>Written once by <c>EntitySetup</c> at spawn.</summary>
        public void SetAuthoredFaction(string value) => faction = value;

        /// <summary>
        /// The side of any GameObject, whether or not it carries this component.
        ///
        /// <para>An object with no component is read through <see cref="AlliedUnit"/> alone and
        /// otherwise assumed hostile, which keeps every synthetic monster in the test suite
        /// behaving as it always has.</para>
        /// </summary>
        public static FactionSide SideOf(GameObject go)
        {
            if (go == null) return FactionSide.Hostile;
            if (AlliedUnit.IsAllied(go)) return FactionSide.PlayerSide;

            // THE PLAYER, before the component, and by TWO independent marks.
            //
            // This is the one entity whose side must be right even when nothing has configured
            // it — a test double, a prefab dropped into a scene, a player built by a bootstrap
            // path older than this component. Reading Hostile there makes
            // AreEnemies(monster, player) FALSE and quietly switches off every hostile in the
            // game: the loudest possible failure, arrived at in silence.
            //
            // The REGISTRY is asked first because it is what the rest of the AI already treats
            // as authoritative (`FactionTargeting` has always resolved the player through it),
            // and the tag second because `SpellTargeting` gates on that and a scene could
            // legitimately have one without the other. Either mark alone is enough.
            if (go == EntityRegistry.Player) return FactionSide.PlayerSide;
            if (go.CompareTag(PlayerTag)) return FactionSide.PlayerSide;

            var component = go.GetComponent<EntityFaction>();
            return component != null ? Parse(component.faction) : FactionSide.Hostile;
        }

        /// <summary>The tag the player carries, and the fallback identity in <see cref="SideOf"/>.</summary>
        private const string PlayerTag = "Player";

        /// <summary>
        /// The side an entity was AUTHORED as, ignoring who it happens to be fighting for right
        /// now. <see cref="SideOf"/> lets <see cref="AlliedUnit"/> win, which is the right answer
        /// for "whom does this attack" and the wrong one for "what is this".
        ///
        /// <para>The distinction is load-bearing wherever a REWARD is decided. The loot and coin
        /// gates already record it: routing them through the derived side would make a charmed
        /// monster silently stop dropping anything, taking the reward away on exactly the enemies
        /// the player worked hardest for. The kill board needs the same answer for the same
        /// reason — a summoned ally dying is not a trophy, and a charmed barbol still is.</para>
        /// </summary>
        public static FactionSide AuthoredSideOf(GameObject go)
        {
            if (go == null) return FactionSide.Hostile;
            if (go == EntityRegistry.Player) return FactionSide.PlayerSide;
            if (go.CompareTag(PlayerTag)) return FactionSide.PlayerSide;

            var component = go.GetComponent<EntityFaction>();
            return component != null ? Parse(component.faction) : FactionSide.Hostile;
        }

        /// <summary>
        /// True when <paramref name="a"/> would attack <paramref name="b"/> unprompted.
        ///
        /// <para>Neutral is on NEITHER side of this: it never initiates and is never chosen as
        /// a target. That is the property that keeps vendors out of fights structurally rather
        /// than by the accident of their aggro range being zero — which is how it used to be
        /// true, and which one edit in the Entities editor could have undone.</para>
        /// </summary>
        public static bool AreEnemies(FactionSide a, FactionSide b)
        {
            if (a == FactionSide.Neutral || b == FactionSide.Neutral) return false;
            return a != b;
        }

        /// <summary>Convenience over <see cref="SideOf"/> for two GameObjects.</summary>
        public static bool AreEnemies(GameObject a, GameObject b)
            => a != null && b != null && a != b && AreEnemies(SideOf(a), SideOf(b));

        /// <summary>
        /// The vocabulary. Matched case-insensitively because the shipped data says `EVIL` and
        /// `NEUTRAL` in caps while nothing forces an author to.
        /// </summary>
        private static FactionSide Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return FactionSide.Hostile;

            if (value.Equals("NEUTRAL", System.StringComparison.OrdinalIgnoreCase))
                return FactionSide.Neutral;

            if (value.Equals("GOOD", System.StringComparison.OrdinalIgnoreCase) ||
                value.Equals("ALLY", System.StringComparison.OrdinalIgnoreCase) ||
                value.Equals("PLAYER", System.StringComparison.OrdinalIgnoreCase))
                return FactionSide.PlayerSide;

            return FactionSide.Hostile;
        }
    }
}
