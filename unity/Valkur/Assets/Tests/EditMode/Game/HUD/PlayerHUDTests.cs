using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="PlayerHUD"/> text format: HP and MP labels use the
    /// condensed "{current}/{max}" format without any "HP:" or "MP:" prefix
    /// (changed during the bottom-left HUD redesign).
    /// </summary>
    [TestFixture]
    public class PlayerHUDTests
    {
        private GameObject _hudGo;
        private PlayerHUD _hud;
        private TextMeshProUGUI _hpText;
        private TextMeshProUGUI _mpText;

        private GameObject _playerGo;
        private Health _health;

        [SetUp]
        public void SetUp()
        {
            // TMP on bare GameObjects emits NREs in EditMode without a Canvas.
            LogAssert.ignoreFailingMessages = true;

            _hudGo = new GameObject("PlayerHUD");
            _hud = _hudGo.AddComponent<PlayerHUD>();

            // Build minimal UI references (no canvas needed for text assignment).
            var hpFillGo = new GameObject("HpFill", typeof(RectTransform));
            hpFillGo.transform.SetParent(_hudGo.transform, false);
            var hpFill = hpFillGo.AddComponent<Image>();

            var hpBgGo = new GameObject("HpBg", typeof(RectTransform));
            hpBgGo.transform.SetParent(_hudGo.transform, false);
            var hpBg = hpBgGo.AddComponent<Image>();

            var hpTextGo = new GameObject("HpText", typeof(RectTransform));
            hpTextGo.transform.SetParent(_hudGo.transform, false);
            _hpText = hpTextGo.AddComponent<TextMeshProUGUI>();

            var mpFillGo = new GameObject("MpFill", typeof(RectTransform));
            mpFillGo.transform.SetParent(_hudGo.transform, false);
            var mpFill = mpFillGo.AddComponent<Image>();

            var mpBgGo = new GameObject("MpBg", typeof(RectTransform));
            mpBgGo.transform.SetParent(_hudGo.transform, false);
            var mpBg = mpBgGo.AddComponent<Image>();

            var mpTextGo = new GameObject("MpText", typeof(RectTransform));
            mpTextGo.transform.SetParent(_hudGo.transform, false);
            _mpText = mpTextGo.AddComponent<TextMeshProUGUI>();

            _hud.SetUIReferences(hpFill, hpBg, _hpText, mpFill, mpBg, _mpText);

            _playerGo = new GameObject("Player");
            _health = _playerGo.AddComponent<Health>();
            _health.Initialize(100);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ── HP format ───────────────────────────────────────────────────────────

        [Test]
        public void Initialize_HpText_IsCurrentSlashMax_NoPrefix()
        {
            _hud.Initialize(_health); // Health(100/100)

            Assert.AreEqual("100/100", _hpText.text,
                "HP label must use the condensed '{current}/{max}' format — no 'HP:' prefix.");
        }

        // ── MP format ───────────────────────────────────────────────────────────

        [Test]
        public void SetMana_MpText_IsCurrentSlashMax_NoPrefix()
        {
            _hud.Initialize(_health); // sets up the HUD first

            _hud.SetMana(40, 50);

            Assert.AreEqual("40/50", _mpText.text,
                "MP label must use the condensed '{current}/{max}' format — no 'MP:' prefix.");
        }
    }
}
