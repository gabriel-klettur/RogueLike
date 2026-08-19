using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.HUD;

namespace Valkur.UI.HUD
{
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private GameObject _bossBarPanel;
        private BossHealthBarHUD _bossBar;

        public BossHealthBarHUD BossBar => _bossBar;

        /// <summary>
        /// Hosts the boss bar inside the HUD canvas, created after the target
        /// panel so it draws over the slot they share. Living here (rather than
        /// on a canvas of its own) also means the boss bar hides with the rest
        /// of the HUD when a runtime editor opens.
        ///
        /// The bar binds itself: every <c>BossPhaseController</c> registers while
        /// enabled and the HUD claims the nearest living one in range.
        /// </summary>
        private void CreateBossHealthBar()
        {
            if (BossHealthBarHUD.HasInstance) return;

            var panel = CreateUIObject("BossHealthBarHUD", _canvas.transform);
            var rect  = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _bossBarPanel = panel;
            _bossBar = panel.AddComponent<BossHealthBarHUD>();
        }
    }
}
