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
        private Mana _playerMana;
        private GameObject _playerHudPanel;

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

            CreatePlayerHUD(playerHealth);
            CreateTargetHUD();

            // XP bar — bottom-center, mirrors Python ExperienceRenderSystem.
            // Resolves the Experience component from the same player as Health.
            var xp = playerHealth != null ? playerHealth.GetComponent<Experience>() : null;
            CreateXpBarHUD(xp);

            // Spell cooldown countdown stack — bottom-center, sits above the XP
            // bar. One row per active cooldown; subscribes to GameEvents.OnSpellCast.
            CreateSpellCooldownHUD(playerHealth != null ? playerHealth.gameObject : null);

            UILayerHelper.SetUILayerRecursive(_canvas.gameObject);

            // Wire mana to PlayerHUD
            if (playerMana != null)
            {
                _playerMana = playerMana;
                _playerMana.OnManaChanged += OnPlayerManaChanged;
                OnPlayerManaChanged(_playerMana.CurrentMana, _playerMana.MaxMana);
            }

            Debug.Log("[HUDManager] HUD initialized for player.");
        }

        private void OnPlayerManaChanged(int current, int max)
        {
            if (_playerHUD != null)
                _playerHUD.SetMana(current, max);
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

        private void CreatePlayerHUD(Health playerHealth)
        {
            // --- Container panel (bottom-left) ---
            var panel = CreateUIObject("PlayerHUDPanel", _canvas.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(20f, 20f);
            panelRect.sizeDelta = new Vector2(260f, 80f);

            // Semi-transparent background
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.55f);

            // Vertical layout
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 6f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // HP Row
            var hpRow = CreateBarRow(panel.transform, "HP", out var hpFill, out var hpBg, out var hpText,
                new Color(0.2f, 0.85f, 0.2f, 1f));

            // MP Row
            var mpRow = CreateBarRow(panel.transform, "MP", out var mpFill, out var mpBg, out var mpText,
                new Color(0.31f, 0.47f, 1f, 1f));

            // Attach PlayerHUD component
            _playerHudPanel = panel;
            _playerHUD = panel.AddComponent<PlayerHUD>();

            _playerHUD.SetUIReferences(hpFill, hpBg, hpText, mpFill, mpBg, mpText);
            _playerHUD.Initialize(playerHealth);
        }
    }
}