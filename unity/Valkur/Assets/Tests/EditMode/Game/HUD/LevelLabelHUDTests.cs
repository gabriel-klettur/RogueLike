using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="LevelLabelHUD"/>: Bind sets the label immediately from
    /// Experience.Level, level-up events update the label only for the bound
    /// entity, and destroying the HUD does not throw on a subsequent event.
    /// </summary>
    [TestFixture]
    public class LevelLabelHUDTests
    {
        private GameObject _hudGo;
        private LevelLabelHUD _hud;
        private TextMeshProUGUI _label;

        private GameObject _playerGo;
        private Health _playerHealth;
        private Experience _xp;

        [SetUp]
        public void SetUp()
        {
            // TMP components on bare GameObjects emit NREs in EditMode without a
            // Canvas hierarchy; suppress them globally for this fixture.
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();

            _hudGo = new GameObject("LevelLabelHUD");
            _hud = _hudGo.AddComponent<LevelLabelHUD>();

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(_hudGo.transform, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();

            _playerGo = new GameObject("Player");
            _playerHealth = _playerGo.AddComponent<Health>();
            _playerHealth.Initialize(100);
            _xp = _playerGo.AddComponent<Experience>();
            _xp.Initialize(0, 0); // level 0, 0 xp
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            GameEvents.Clear();
        }

        // ── Bind ────────────────────────────────────────────────────────────────

        [Test]
        public void Bind_SetsLabelToCurrentLevel()
        {
            // level 0 after Initialize(0, 0)
            _hud.Bind(_playerHealth, _label);

            Assert.AreEqual("Lvl 0", _label.text,
                "Bind must write the current Experience.Level to the label immediately.");
        }

        [Test]
        public void Bind_AfterLevelUp_LabelReflectsNewLevel()
        {
            _hud.Bind(_playerHealth, _label);

            // AddXp triggers Experience.OnLevelUp and GameEvents.FireLevelUp for
            // the same entity the HUD is bound to.
            _xp.AddXp(10_000); // forces at least level 1

            StringAssert.DoesNotContain("Lvl 0 ", _label.text + " ",
                "After a level-up the label must no longer show Lvl 0.");
            StringAssert.StartsWith("Lvl ", _label.text,
                "Label must still carry the 'Lvl ' prefix after the update.");
        }

        [Test]
        public void GlobalLevelUp_OtherEntity_DoesNotChangeLabel()
        {
            _hud.Bind(_playerHealth, _label);
            string before = _label.text; // "Lvl 0"

            var other = new GameObject("OtherEntity");
            try { GameEvents.FireLevelUp(other, 99); }
            finally { Object.DestroyImmediate(other); }

            Assert.AreEqual(before, _label.text,
                "A level-up for an unrelated entity must not modify the bound label.");
        }

        [Test]
        public void Destroy_ThenGlobalLevelUp_DoesNotThrow()
        {
            _hud.Bind(_playerHealth, _label);

            // Destroy the HUD GameObject — OnDestroy should unsubscribe.
            Object.DestroyImmediate(_hudGo);
            _hudGo = null; // prevent double-destroy in TearDown

            // Firing the event after destruction must be harmless.
            Assert.DoesNotThrow(
                () => GameEvents.FireLevelUp(_playerGo, 7),
                "Firing GameEvents.OnLevelUp after the HUD is destroyed must not throw.");
        }

        [Test]
        public void BindNull_DoesNotThrow_AndDoesNotSubscribe()
        {
            // Bind with null Health — must not crash.
            Assert.DoesNotThrow(
                () => _hud.Bind(null, _label),
                "Bind(null, label) must not throw.");

            // The label text was untouched (no Experience component to read from).
            string textAfterBind = _label.text;

            // A global level-up must not write to the label (nothing is bound).
            GameEvents.FireLevelUp(_playerGo, 5);

            Assert.AreEqual(textAfterBind, _label.text,
                "After Bind(null), a global level-up must not write to the label.");
        }
    }
}
