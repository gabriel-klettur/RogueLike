using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for <see cref="Mana"/>: consume / insufficient-fail / event
    /// firing / has-mana query / restore-with-clamp. The time-based regen test
    /// lives in <c>PlayMode/Gameplay/CombatSystemPlayTests.cs</c> because it
    /// requires real <see cref="Time.deltaTime"/> advancement.
    ///
    /// Migrated from <c>PlayMode/Gameplay/CombatSystemPlayTests.cs</c>.
    /// </summary>
    [TestFixture]
    public class ManaTests
    {
        private sealed class FreeCastingEditor : GameEditorManager.IGameEditor, IChoosesPrimaryCastSpell
        {
            public string EditorName => "Spells Editor";
            public bool IsActive { get; private set; }
            public string PrimaryCastSpellKey => IsActive ? "fireball" : null;
            public bool PrimaryCastIgnoresManaCost => IsActive;

            public void Activate() => IsActive = true;
            public void Deactivate() => IsActive = false;
        }

        private static readonly FieldInfo s_editorManagerInstanceField =
            typeof(SingletonMonoBehaviour<GameEditorManager>).GetField(
                "_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private Mana CreateMana(int maxMana = 100, float regen = 0f)
        {
            var go = new GameObject("Entity");
            var m = go.AddComponent<Mana>();
            m.Initialize(maxMana, regen);
            return m;
        }

        private static void Destroy(Mana m)
        {
            if (m != null) Object.DestroyImmediate(m.gameObject);
        }

        [Test]
        public void TryConsume_Success_ReducesMana()
        {
            var m = CreateMana(100);
            try
            {
                bool result = m.TryConsume(30);
                Assert.IsTrue(result);
                Assert.AreEqual(70, m.CurrentMana);
            }
            finally { Destroy(m); }
        }

        [Test]
        public void TryConsume_InsufficientMana_ReturnsFalse()
        {
            var m = CreateMana(50);
            try
            {
                m.TryConsume(40);
                Assert.AreEqual(10, m.CurrentMana);

                bool result = m.TryConsume(15);
                Assert.IsFalse(result);
                Assert.AreEqual(10, m.CurrentMana,
                    "A failed TryConsume must not deduct partial mana.");
            }
            finally { Destroy(m); }
        }

        [Test]
        public void OnManaChanged_FiresOnConsume()
        {
            var m = CreateMana(100);
            try
            {
                int lastCurrent = -1, lastMax = -1;
                m.OnManaChanged += (cur, max) =>
                {
                    lastCurrent = cur;
                    lastMax = max;
                };

                m.TryConsume(25);
                Assert.AreEqual(75, lastCurrent);
                Assert.AreEqual(100, lastMax);
            }
            finally { Destroy(m); }
        }

        [Test]
        public void HasMana_ReportsCorrectly()
        {
            var m = CreateMana(50);
            try
            {
                Assert.IsTrue(m.HasMana(50));
                Assert.IsTrue(m.HasMana(1));
                Assert.IsFalse(m.HasMana(51));

                m.TryConsume(30);
                Assert.IsTrue(m.HasMana(20));
                Assert.IsFalse(m.HasMana(21));
            }
            finally { Destroy(m); }
        }

        [Test]
        public void Restore_AddsMana_ClampsToMax()
        {
            var m = CreateMana(100);
            try
            {
                m.TryConsume(60);
                Assert.AreEqual(40, m.CurrentMana);

                m.Restore(30);
                Assert.AreEqual(70, m.CurrentMana);

                m.Restore(999);
                Assert.AreEqual(100, m.CurrentMana);
            }
            finally { Destroy(m); }
        }

        [Test]
        public void TryConsume_SpellsEditorOpenIsFree_ClosingRestoresConsumption()
        {
            GameObject managerGo = null;
            Mana mana = null;

            try
            {
                // Domain Reload is disabled in this project, so isolate the singleton
                // exactly as the other GameEditorManager EditMode fixtures do.
                s_editorManagerInstanceField?.SetValue(null, null);
                managerGo = new GameObject("[GameEditorManager_ManaTest]");
                var manager = managerGo.AddComponent<GameEditorManager>();
                typeof(SingletonMonoBehaviour<GameEditorManager>)
                    .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(manager, null);

                var playerGo = new GameObject("Player_ManaTest");
                playerGo.AddComponent<PlayerController>();
                mana = playerGo.AddComponent<Mana>();
                mana.Initialize(3, regen: 0f);

                var editor = new FreeCastingEditor();
                manager.Register(editor);
                manager.OpenExclusive(editor);

                Assert.IsTrue(mana.TryConsume(3));
                Assert.AreEqual(3, mana.CurrentMana,
                    "Every player mana charge must be ignored while F4 is open, including channel drains.");

                manager.CloseAll();

                Assert.IsTrue(mana.TryConsume(3));
                Assert.AreEqual(0, mana.CurrentMana,
                    "Closing F4 must restore ordinary mana consumption immediately.");
            }
            finally
            {
                if (mana != null) Object.DestroyImmediate(mana.gameObject);
                if (managerGo != null) Object.DestroyImmediate(managerGo);
                s_editorManagerInstanceField?.SetValue(null, null);
            }
        }
    }
}
