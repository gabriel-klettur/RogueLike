using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins how the combo badge is wired into the HUD, as opposed to how it
    /// behaves once it is there (that is <c>ComboHUDTests</c>).
    ///
    /// The badge is built procedurally by <see cref="HUDManager"/>, so nothing in
    /// a scene file records that it exists or where it sits. Without these tests a
    /// rename, a reordered build step or a dropped call would remove the badge
    /// from the game and every other test would still pass.
    /// </summary>
    [TestFixture]
    public class ComboHudWiringTests
    {
        private GameObject _hudGo;
        private GameObject _playerGo;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            ClearSingletonInstance<HUDManager>();

            _playerGo = new GameObject("Player");
            _playerGo.tag = "Player";
            var health = _playerGo.AddComponent<Health>();
            health.Initialize(100);
            _playerGo.AddComponent<ComboCounter>();

            _hudGo = new GameObject("HUDManager");
            var hud = _hudGo.AddComponent<HUDManager>();
            hud.InitializeForPlayer(health, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            ClearSingletonInstance<HUDManager>();
        }

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private RectTransform FindPanel(string name)
        {
            foreach (var t in _hudGo.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t as RectTransform;
            return null;
        }

        // ── Existence ───────────────────────────────────────────────────────

        [Test]
        public void TheBadgeIsBuiltWhenTheHudInitialises()
        {
            var panel = FindPanel("ComboHUDPanel");
            Assert.IsNotNull(panel, "HUDManager.InitializeForPlayer must build the combo badge.");
            Assert.IsNotNull(panel.GetComponent<ComboHUD>(),
                "The panel exists but carries no ComboHUD — nothing would ever drive it.");
        }

        [Test]
        public void TheBadgeIsBoundToThePlayersCounter()
        {
            var combo = FindPanel("ComboHUDPanel").GetComponent<ComboHUD>();
            Assert.IsNotNull(combo.BoundCounter,
                "An unbound badge silently shows nothing no matter how the player fights.");
            Assert.AreSame(_playerGo.GetComponent<ComboCounter>(), combo.BoundCounter);
        }

        // ── Placement ───────────────────────────────────────────────────────

        [Test]
        public void TheBadgeSitsAboveThePlayerPanelInTheSameColumn()
        {
            var player = FindPanel("PlayerHUDPanel");
            var badge = FindPanel("ComboHUDPanel");
            Assert.IsNotNull(player);
            Assert.IsNotNull(badge);

            Assert.AreEqual(Vector2.zero, badge.anchorMin, "The badge must anchor bottom-left.");
            Assert.AreEqual(Vector2.zero, badge.anchorMax);
            Assert.AreEqual(Vector2.zero, badge.pivot);

            Assert.AreEqual(player.anchoredPosition.x, badge.anchoredPosition.x, 0.01f,
                "Both share the bottom-left column, so their left edges must line up.");
            Assert.AreEqual(player.sizeDelta.x, badge.sizeDelta.x, 0.01f,
                "A badge of a different width breaks the column.");

            float playerTop = player.anchoredPosition.y + player.sizeDelta.y;
            Assert.GreaterOrEqual(badge.anchoredPosition.y, playerTop,
                "The badge must clear the top of the unified player panel. Overlapping it " +
                "would bury the HP/MP bars behind the combo number.");
        }

        [Test]
        public void TheBadgeHasRoomForItsContents()
        {
            var badge = FindPanel("ComboHUDPanel");
            Assert.AreEqual(ComboHUD.PreferredHeight, badge.sizeDelta.y, 0.01f,
                "Sizing the badge by hand instead of by PreferredHeight is how the drain bar " +
                "ends up clipped when the layout constants change.");
            Assert.Greater(badge.sizeDelta.x, 0f);
        }

        // ── It must not eat the player's clicks ─────────────────────────────

        [Test]
        public void TheBadgeNeverBlocksAClick()
        {
            var badge = FindPanel("ComboHUDPanel");
            var group = badge.GetComponent<CanvasGroup>();
            Assert.IsNotNull(group);
            Assert.IsFalse(group.blocksRaycasts,
                "The badge sits over the play area. If it caught raycasts it would swallow " +
                "attacks — and PlayerController now refuses to cast while the pointer is " +
                "over interactive UI, so the player would just stop shooting.");
            Assert.IsFalse(group.interactable);
        }
    }
}
