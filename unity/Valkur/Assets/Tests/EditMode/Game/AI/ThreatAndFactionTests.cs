using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// The two questions targeting asks, and the answers that used to be wrong.
    ///
    /// <para><b>WHO DO I FIGHT</b> was "whoever is nearest", recomputed every frame. Damage
    /// bought no attention, so a summon that tanked and a player who nuked were equally
    /// interesting; there was no hysteresis, so two candidates at similar range made the target
    /// flip frame to frame; and nothing could be taunted or peeled.</para>
    ///
    /// <para><b>WHOSE SIDE AM I ON</b> was answered by <c>AlliedUnit</c> membership and by
    /// whether a monster's FSM set happened to declare <c>ChaseState</c>. The authored
    /// <c>stats.faction</c> string — present on all twenty-five shipped definitions — reached no
    /// AI decision at all, so vendors were harmless only by the accident of their aggro range
    /// being zero.</para>
    /// </summary>
    public class ThreatAndFactionTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp] public void SetUp() => EntityRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            EntityRegistry.Clear();
        }

        private GameObject Make(string name, Vector2 at, string faction = null)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            if (faction != null) go.AddComponent<EntityFaction>().SetAuthoredFaction(faction);
            _scene.Add(go);
            return go;
        }

        private GameObject Monster(string name, Vector2 at, string faction = "EVIL")
        {
            var go = Make(name, at, faction);
            go.AddComponent<Health>().Initialize(100);
            EntityRegistry.RegisterMonster(go);
            return go;
        }

        private GameObject Player(Vector2 at)
        {
            var go = Make("Player", at);
            go.AddComponent<Health>().Initialize(100);
            EntityRegistry.RegisterPlayer(go);
            return go;
        }

        // ── Faction ──────────────────────────────────────────────────────────────

        [Test]
        public void AnUnauthoredFaction_ReadsAsHostile()
        {
            // Every test double and every entity spawned without a MonsterDefinition arrives
            // here with nothing set, and the historical behaviour of such a thing is to hunt the
            // player. Defaulting to Neutral would silently pacify half the suite.
            var nobody = Make("Nobody", Vector2.zero);
            Assert.AreEqual(FactionSide.Hostile, EntityFaction.SideOf(nobody));
        }

        [Test]
        public void NeutralFightsNobody_AndNobodyFightsIt()
        {
            var vendor = Make("Vendor", Vector2.zero, "NEUTRAL");
            var monster = Make("Monster", Vector2.one, "EVIL");

            Assert.IsFalse(EntityFaction.AreEnemies(vendor, monster),
                "A neutral never initiates.");
            Assert.IsFalse(EntityFaction.AreEnemies(monster, vendor),
                "and is never chosen as a target. This is what keeps vendors out of fights " +
                "STRUCTURALLY — it used to be true only because their aggroRange was 0, which " +
                "is one edit in the Entities editor away from a shopkeeper hunting the player.");
        }

        [Test]
        public void ThePlayerIsPlayerSide_FromTheTagAlone()
        {
            // The loudest possible failure arrived at silently: if the player read as Hostile,
            // AreEnemies(monster, player) is FALSE and every hostile in the game switches off.
            var player = Make("Player", Vector2.zero);
            player.tag = "Player";
            Assert.AreEqual(FactionSide.PlayerSide, EntityFaction.SideOf(player));
        }

        [Test]
        public void ThePlayerIsPlayerSide_FromTheRegistryAlone()
        {
            // TWO independent marks, and this is the second. A scene can legitimately register a
            // player without tagging it — every fixture in this suite does — and the first draft
            // of EntityFaction read only the tag, which made an untagged-but-registered player
            // hostile to nothing and turned every monster in the test scene passive.
            var player = Make("Registered", Vector2.zero);
            EntityRegistry.RegisterPlayer(player);

            Assert.AreEqual(FactionSide.PlayerSide, EntityFaction.SideOf(player));
        }

        [Test]
        public void AlliedMembershipBeatsTheAuthoredString()
        {
            // A charmed monster keeps its own EVIL faction and still fights for the player, and
            // reverts the moment the charm's AlliedUnit is gone — without anything having to
            // remember to rewrite a field.
            var charmed = Make("Charmed", Vector2.zero, "EVIL");
            Assert.AreEqual(FactionSide.Hostile, EntityFaction.SideOf(charmed));

            charmed.AddComponent<AlliedUnit>().SetLifetime(-1f);
            Assert.AreEqual(FactionSide.PlayerSide, EntityFaction.SideOf(charmed),
                "Side is DERIVED. Storing it in a second field is the two-pieces-of-state " +
                "failure this project keeps finding.");
        }

        [Test]
        public void AnAllyIgnoresNeutrals_EvenTheNearestOne()
        {
            var player = Player(Vector2.zero);
            var vendor = Monster("Vendor", new Vector2(1f, 0f), "NEUTRAL");
            var hostile = Monster("Hostile", new Vector2(9f, 0f), "EVIL");

            var ally = Monster("Ally", new Vector2(0.5f, 0f), "EVIL");
            // SetLifetime, not a bare AddComponent: AlliedUnit joins its registry from Awake and
            // OnEnable, and EditMode runs neither, so `AlliedUnit.Live` would stay empty and the
            // test would measure the no-allies fast path. AlliedUnit documents this method as
            // the one every ally-creating path goes through for exactly that reason.
            ally.AddComponent<AlliedUnit>().SetLifetime(-1f);

            Assert.AreSame(hostile, FactionTargeting.EnemyOf(ally),
                "AlliedUnit.IsAllied answers 'is this one of MY summons' and cannot tell a " +
                "hostile from a vendor, so a summon used to march across town and kill the " +
                $"blacksmith. Nearest was {vendor.name}.");
        }

        // ── Threat ───────────────────────────────────────────────────────────────

        [Test]
        public void AnEmptyTable_AnswersNothing()
        {
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            Assert.IsNull(threat.Top(),
                "Null is what makes this layer additive: every monster before a fight starts " +
                "still opens it with the distance rule, exactly as it always did.");
        }

        [Test]
        public void TheHardestHitterWins_NotTheNearest()
        {
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var near = Make("Near", new Vector2(1f, 0f));
            var far = Make("Far", new Vector2(8f, 0f));

            threat.Record(near, 5f);
            threat.Record(far, 60f);

            Assert.AreSame(far, threat.Top());
        }

        [Test]
        public void ANearMissDoesNotStealTheTarget()
        {
            // The margin is the point. Without it two attackers doing similar damage make the
            // monster oscillate, which is worse than either choice: it spends the fight turning.
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var leader = Make("Leader", new Vector2(2f, 0f));
            var rival = Make("Rival", new Vector2(2f, 1f));

            threat.Record(leader, 100f);
            Assert.AreSame(leader, threat.Top());

            threat.Record(rival, 110f);       // ahead, but under the 1.35x switch margin
            Assert.AreSame(leader, threat.Top(),
                "A challenger must clearly outbid the leader before the monster turns.");
        }

        [Test]
        public void AClearOutbidDoesTakeTheTarget()
        {
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var leader = Make("Leader", new Vector2(2f, 0f));
            var rival = Make("Rival", new Vector2(2f, 1f));

            threat.Record(leader, 100f);
            threat.Top();
            threat.Record(rival, 200f);

            Assert.AreSame(rival, threat.Top(),
                "The margin must be a threshold, not a wall — a peel has to be possible.");
        }

        [Test]
        public void SelfDamageEarnsNothing()
        {
            // A burn tick with no source, or an entity standing in its own hazard, must not
            // make it hunt itself.
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            threat.Record(monster, 500f);
            threat.Record(null, 500f);

            Assert.IsNull(threat.Top());
        }

        [Test]
        public void ADeadAttackerIsForgotten()
        {
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var attacker = Make("Attacker", new Vector2(2f, 0f));
            var health = attacker.AddComponent<Health>();
            health.Initialize(10);
            threat.Record(attacker, 100f);
            Assert.AreSame(attacker, threat.Top());

            health.TakeDamage(999);
            Assert.IsNull(threat.Top(),
                "Otherwise a monster walks to whatever killed it last and stands there.");
        }

        [Test]
        public void ThreatBeyondTheRangeLimitIsIgnored()
        {
            // A single hit from a rooftop must not own a monster's attention forever, past the
            // point where it can even see who threw it.
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();
            threat.SetMaxRange(10f);

            var sniper = Make("Sniper", new Vector2(40f, 0f));
            threat.Record(sniper, 500f);

            Assert.IsNull(threat.Top());
        }

        [Test]
        public void FactionTargeting_PrefersTheAttackerOverTheNearest()
        {
            // The composition, which is the half that can be false while both parts read
            // correctly — the shape SPAWNER_COORDINATE_SPACE_DRIFT is named for.
            var player = Player(new Vector2(12f, 0f));
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var ally = Monster("Ally", new Vector2(1f, 0f), "EVIL");
            ally.AddComponent<AlliedUnit>().SetLifetime(-1f);

            Assert.AreSame(ally, FactionTargeting.EnemyOf(monster),
                "Precondition: with an empty table the nearest player-side entity wins.");

            threat.Record(player, 300f);
            Assert.AreSame(player, FactionTargeting.EnemyOf(monster),
                "Damage has to be able to pull a monster off the thing standing next to it, or " +
                "no summon can ever peel and no player can ever taunt.");
        }

        [Test]
        public void FriendlyFireInTheTableIsNotActedOn()
        {
            // An area spell can put an ally's damage in a monster's table when both sides stand
            // in it. The table records what happened; acting on it would turn a monster on its
            // own side for a reason no player could reproduce.
            var player = Player(new Vector2(12f, 0f));
            var monster = Monster("Monster", Vector2.zero);
            var threat = monster.AddComponent<ThreatMemory>();

            var friend = Monster("Friend", new Vector2(2f, 0f), "EVIL");
            threat.Record(friend, 900f);

            Assert.AreSame(player, FactionTargeting.EnemyOf(monster));
        }
    }
}
