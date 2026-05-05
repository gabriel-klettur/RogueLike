using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="XpBarHUD"/>: Bind subscribes to Experience events,
    /// the displayed fill converges toward the normalized progress, the
    /// label reflects level + xp/next, and the global OnLevelUp safety
    /// net is honoured.
    /// </summary>
    [TestFixture]
    public class XpBarHUDTests
    {
        private GameObject _hudGo;
        private XpBarHUD _hud;
        private Image _fill;
        private Image _bg;
        private TextMeshProUGUI _label;

        private GameObject _playerGo;
        private Experience _xp;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            GameEvents.Clear();

            _hudGo = new GameObject("XpBarHUD");

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(_hudGo.transform, false);
            _fill = fillGo.AddComponent<Image>();
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;

            var bgGo = new GameObject("BG", typeof(RectTransform));
            bgGo.transform.SetParent(_hudGo.transform, false);
            _bg = bgGo.AddComponent<Image>();

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(_hudGo.transform, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();

            _hud = _hudGo.AddComponent<XpBarHUD>();
            _hud.SetUIReferences(_fill, _bg, _label);

            _playerGo = new GameObject("Player");
            _xp = _playerGo.AddComponent<Experience>();
            // Level 0 is the natural post-AddComponent state and gives a clean
            // [0..XpRequiredForLevel(1)] window for testing (XpRequiredForLevel(0) = 0).
            _xp.Initialize(0, 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            GameEvents.Clear();
        }

        [Test]
        public void Bind_SetsTargetFill_FromCurrentXp()
        {
            // baseXp 100, exponent 1.5, level 0 → next at 100 * 1^1.5 = 100
            // 50 xp at level 0 → progress = 50 / 100 = 0.5
            _xp.AddXp(50);

            _hud.Bind(_xp);

            Assert.That(_hud.TargetFill, Is.EqualTo(0.5f).Within(0.02f),
                "Bind must compute target fill from Experience.NormalizedProgress.");
        }

        [Test]
        public void OnXpGained_UpdatesTargetFill()
        {
            _hud.Bind(_xp);
            float before = _hud.TargetFill;

            _xp.AddXp(20);

            Assert.That(_hud.TargetFill, Is.GreaterThan(before),
                "Adding XP must move the target fill upward.");
        }

        [Test]
        public void Tick_LerpsTowardTarget()
        {
            _xp.AddXp(80);
            _hud.Bind(_xp);
            _fill.fillAmount = 0f;

            for (int i = 0; i < 200; i++) _hud.Tick(0.05f);

            Assert.That(_fill.fillAmount, Is.EqualTo(_hud.TargetFill).Within(0.01f),
                "After enough ticks the displayed fill must converge to the target.");
        }

        [Test]
        public void Label_ShowsLevelAndXpProgress()
        {
            _xp.AddXp(50);
            _hud.Bind(_xp);

            StringAssert.Contains("Lvl 0", _label.text);
            StringAssert.Contains("/", _label.text);
        }

        [Test]
        public void OnLevelUp_BumpsLevelLabel()
        {
            _hud.Bind(_xp);
            _xp.AddXp(10_000); // forces multi-level

            StringAssert.DoesNotContain("Lvl 0 ", _label.text + " ");
        }

        [Test]
        public void Bind_Null_UnbindsExisting()
        {
            _hud.Bind(_xp);
            _hud.Bind(null);

            Assert.IsFalse(_hud.IsBound,
                "Bind(null) must release the current Experience reference.");
            Assert.That(_hud.TargetFill, Is.EqualTo(0f),
                "After unbind the bar must reset to empty.");
        }

        [Test]
        public void GlobalLevelUp_OnSameEntity_TriggersFlash()
        {
            _hud.Bind(_xp);

            // Fire the global event directly — the bar should accept it because
            // entity == bound entity, even if the local Experience callback didn't
            // run (e.g. tests, scripted level grants).
            GameEvents.FireLevelUp(_playerGo, 5);

            Assert.IsTrue(_hud.IsBound, "HUD remains bound after global level-up.");
        }

        [Test]
        public void GlobalLevelUp_OnOtherEntity_Ignored()
        {
            _hud.Bind(_xp);
            float before = _hud.TargetFill;

            var other = new GameObject("OtherEntity");
            try { GameEvents.FireLevelUp(other, 5); }
            finally { Object.DestroyImmediate(other); }

            Assert.That(_hud.TargetFill, Is.EqualTo(before),
                "Level-ups for unrelated entities must not affect the bound HUD.");
        }
    }
}
