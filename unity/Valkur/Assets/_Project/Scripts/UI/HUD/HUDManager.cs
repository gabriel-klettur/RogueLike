using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Orchestrates the entire HUD system. Creates the screen-space Canvas,
    /// PlayerHUD, and TargetHUD at runtime. Wires to player Health events.
    /// Attach to a persistent GameObject in the gameplay scene.
    /// </summary>
    public partial class HUDManager : SingletonMonoBehaviour<HUDManager>
    {
        private PlayerHUD _playerHUD;
        private TargetHUD _targetHUD;
        private Canvas _canvas;
        private GameObject _playerHudPanel;

        // Footprint of the unified bottom-left panel, published so widgets that
        // stack above it (combo badge today) don't have to recompute the math.
        private float _playerPanelWidth;
        private float _playerPanelHeight;
        private Vector2 _playerPanelOrigin = new Vector2(HudPanelMargin, HudPanelMargin);

        public TargetHUD TargetHUD => _targetHUD;

        protected override void OnSingletonAwake()
        {
            GameEditorManager.OnEditorStateChanged += OnEditorStateChanged;
        }

        /// <summary>
        /// Called by GameplaySceneSetup after the player is spawned.
        /// </summary>
        public void InitializeForPlayer(Health playerHealth, Mana playerMana = null)
        {
            if (_canvas == null)
                CreateCanvas();

            CreatePlayerHUD(playerHealth, playerMana);
            CreateTargetHUD();

            // Combo badge — stacks directly above the unified player panel.
            CreateComboHUD(playerHealth != null ? playerHealth.gameObject : null);

            // Boss bar — shares the top-centre slot with the target panel and
            // outranks it. Created after it so it draws on top.
            CreateBossHealthBar();

            // Spell cooldown countdown stack — top-left, below the day/night
            // clock. One row per active cooldown; subscribes to GameEvents.OnSpellCast.
            CreateSpellCooldownHUD(playerHealth != null ? playerHealth.gameObject : null);

            UILayerHelper.SetUILayerRecursive(_canvas.gameObject);


            Debug.Log("[HUDManager] HUD initialized for player.");
        }

        private void CreateCanvas()
        {
            var canvasGo = new GameObject("HUDCanvas");
            canvasGo.transform.SetParent(transform);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();
        }

        // Gap between the HUD canvas's bottom-left corner and the panel is owned by
        // PlayerHudStyle.marginTexels now; this is only what the combo badge falls back to before
        // the panel has measured itself.
        private const float HudPanelMargin = 16f;

        /// <summary>
        /// Builds the bottom-left player panel. Everything about it — its art, its pixel grid, what
        /// it listens to — is PlayerHUD's; this only creates it and keeps the combo badge above it.
        /// </summary>
        private void CreatePlayerHUD(Health playerHealth, Mana playerMana)
        {
            _playerHUD = PlayerHUD.Create(_canvas, playerHealth, playerMana);
            _playerHudPanel = _playerHUD.gameObject;
            _playerHUD.GeometryChanged += OnPlayerPanelGeometryChanged;
            OnPlayerPanelGeometryChanged();
        }

        private void OnPlayerPanelGeometryChanged()
        {
            if (_playerHUD == null) return;
            var rt = _playerHUD.Panel;
            _playerPanelWidth = rt.sizeDelta.x;
            _playerPanelHeight = rt.sizeDelta.y;
            _playerPanelOrigin = rt.anchoredPosition;
            ApplyComboPlacement();
        }
    }
}