using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Game.AI
{
    /// <summary>
    /// One monster spotting the player tells the monsters around it.
    ///
    /// <para>Before <see cref="AggroBroadcast"/> a pack was twenty monsters that happened to
    /// agree: each acquired independently, at its own radius, with no notion that anything
    /// else in the room had already seen anyone. Grepping the project for aggro sharing,
    /// threat, reinforcement or squads returned nothing at all.</para>
    ///
    /// <para>The two rules that make it safe are asserted here as hard as the feature itself:
    /// a listener that cannot ENTER <see cref="AlertChaseState"/> is never written to (a
    /// vendor must not be recruited by the monster outside her shop, and writing an alert she
    /// cannot act on would produce a refusal warning she cannot be fixed out of), and the
    /// shout never crosses sides.</para>
    /// </summary>
    public class AggroBroadcastTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            EntityRegistry.Clear();
            _player = new GameObject("Player");
            _player.AddComponent<Health>().Initialize(100);
            EntityRegistry.RegisterPlayer(_player);
            _scene.Add(_player);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            EntityRegistry.Clear();
        }

        /// <summary>
        /// A registered monster with a real <see cref="FSMMonsterBrain"/>, because the shout
        /// resolves its listeners through that component. Awake is invoked by hand: EditMode
        /// does not fire it, and <c>Initialize</c> NREs on the Health cache without it.
        /// </summary>
        private FSMMonsterBrain MakeMonster(Vector2 at, params string[] allowedStates)
        {
            var go = new GameObject("Monster");
            go.transform.position = at;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();
            _scene.Add(go);

            var brain = go.AddComponent<FSMMonsterBrain>();
            typeof(FSMMonsterBrain)
                .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(brain, null);

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = "test_listener";
            def.displayName = "Listener";
            def.stats = new EntityStats { hp = 10, aggroRange = 5f, meleeRange = 1f };
            brain.Initialize(def);

            if (allowedStates != null && allowedStates.Length > 0)
                brain.FSM.SetAllowedStates(new HashSet<string>(allowedStates));

            EntityRegistry.RegisterMonster(go);
            return brain;
        }

        private static readonly string[] HostileVocabulary =
        {
            nameof(IdleState), nameof(PatrolState), nameof(ChaseState),
            nameof(AlertChaseState), nameof(AttackState),
        };

        // ── The shout ────────────────────────────────────────────────────────────

        [Test]
        public void ANearbyMonster_IsTold()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);
            var listener = MakeMonster(new Vector2(4f, 0f), HostileVocabulary);

            int told = AggroBroadcast.Alert(shouter.gameObject, new Vector2(1f, 1f), 8f);

            Assert.AreEqual(1, told);
            Assert.IsTrue(FSMAlert.IsPending(listener.FSM));
            Assert.AreEqual(new Vector2(1f, 1f), FSMAlert.Position(listener.FSM));
        }

        [Test]
        public void AMonsterOutsideTheRadius_IsNot()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);
            var listener = MakeMonster(new Vector2(20f, 0f), HostileVocabulary);

            Assert.AreEqual(0, AggroBroadcast.Alert(shouter.gameObject, Vector2.one, 8f));
            Assert.IsFalse(FSMAlert.IsPending(listener.FSM));
        }

        [Test]
        public void TheShouterDoesNotTellItself()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);

            Assert.AreEqual(0, AggroBroadcast.Alert(shouter.gameObject, Vector2.one, 8f));
        }

        [Test]
        public void ARadiusOfZero_TellsNobody()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);
            MakeMonster(new Vector2(1f, 0f), HostileVocabulary);

            Assert.AreEqual(0, AggroBroadcast.Alert(shouter.gameObject, Vector2.one, 0f),
                "aggro_share_radius 0 is how a monster is authored to fight alone.");
        }

        // ── Who may be recruited ─────────────────────────────────────────────────

        [Test]
        public void AVendorWithNoAlertState_IsNeverWrittenTo()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);
            // The shipped NPC_Passive vocabulary: no chase, no alert.
            var vendor = MakeMonster(new Vector2(2f, 0f),
                nameof(IdleState), nameof(UnconsciousState), nameof(DeathState));

            Assert.AreEqual(0, AggroBroadcast.Alert(shouter.gameObject, Vector2.one, 8f));
            Assert.IsFalse(FSMAlert.IsPending(vendor.FSM),
                "The allowed-state guard would refuse the transition on her next tick and " +
                "warn once per From>To pair, for a message nobody can act on.");
        }

        [Test]
        public void AMonsterAlreadyEngaged_IsNotReset()
        {
            var shouter = MakeMonster(Vector2.zero, HostileVocabulary);
            var busy = MakeMonster(new Vector2(3f, 0f), HostileVocabulary);
            busy.FSM.ChangeState(new ChaseState());

            Assert.AreEqual(0, AggroBroadcast.Alert(shouter.gameObject, Vector2.one, 8f),
                "Re-alerting a monster mid-chase would send it to investigate a position it " +
                "has long since passed.");
        }

        // ── The alert itself ─────────────────────────────────────────────────────

        [Test]
        public void AnAlertExpires()
        {
            var listener = MakeMonster(Vector2.zero, HostileVocabulary);
            FSMAlert.Raise(listener.FSM, Vector2.one);
            Assert.IsTrue(FSMAlert.IsPending(listener.FSM));

            // Rather than waiting out FSMAlert.ValidSeconds in a test, backdate the stamp.
            listener.FSM.SetContext(FSMAlert.KeyTime, Time.time - FSMAlert.ValidSeconds - 1f);

            Assert.IsFalse(FSMAlert.IsPending(listener.FSM));
        }

        [Test]
        public void AConsumedAlert_IsNotPendingAgain()
        {
            var listener = MakeMonster(Vector2.zero, HostileVocabulary);
            FSMAlert.Raise(listener.FSM, Vector2.one);

            FSMAlert.Consume(listener.FSM);

            Assert.IsFalse(FSMAlert.IsPending(listener.FSM),
                "A monster that investigates and returns to patrol must not re-enter the " +
                "alert on its next tick and never patrol again.");
        }

        [Test]
        public void AnAlertedMonsterAtRest_GoesToInvestigate()
        {
            var listener = MakeMonster(Vector2.zero, HostileVocabulary);
            listener.FSM.ChangeState(new IdleState());
            FSMAlert.Raise(listener.FSM, new Vector2(6f, 0f));

            listener.FSM.Update(0.016f);

            Assert.IsInstanceOf<AlertChaseState>(listener.FSM.CurrentState);
        }
    }
}
