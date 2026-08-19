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

        // Footprint of the unified bottom-left panel, published so widgets that
        // stack above it (combo badge today) don't have to recompute the math.
        private float _playerPanelWidth;
        private float _playerPanelHeight;

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

            // Combo badge — stacks directly above the unified player panel.
            CreateComboHUD(playerHealth != null ? playerHealth.gameObject : null);

            // Boss bar — shares the top-centre slot with the target panel and
            // outranks it. Created after it so it draws on top.
            CreateBossHealthBar();

            // Spell cooldown countdown stack — top-left, below the day/night
            // clock. One row per active cooldown; subscribes to GameEvents.OnSpellCast.
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

        // ── Layout constants for the unified bottom-left HUD panel ─────────
        // Outer panel holds the portrait (left) + a vertical stack (right) of
        // HP / MP / 3 ability slots / XP bar — mirrors the reference layout.
        private const float HudPanelMargin     = 16f;
        private const float HudPanelPadding    = 8f;
        private const float HudPanelInnerSpacing = 8f;
        private const float HudPortraitSize    = 108f;
        private const float HudStackWidth      = 220f;
        private const float HudBarHeight       = 22f;
        private const float HudAbilityRowHeight = 38f;
        private const float HudXpRowHeight     = 18f;
        private const float HudStackSpacing    = 4f;

        private void CreatePlayerHUD(Health playerHealth)
        {
            // --- Container panel (bottom-left) ---
            var panel = CreateUIObject("PlayerHUDPanel", _canvas.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(HudPanelMargin, HudPanelMargin);

            float stackHeight = HudBarHeight * 2f + HudAbilityRowHeight + HudXpRowHeight + HudStackSpacing * 3f;
            float panelHeight = Mathf.Max(HudPortraitSize, stackHeight) + HudPanelPadding * 2f;
            float panelWidth  = HudPortraitSize + HudStackWidth + HudPanelInnerSpacing + HudPanelPadding * 2f;
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            _playerPanelWidth  = panelWidth;
            _playerPanelHeight = panelHeight;

            // Semi-transparent background
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.55f);

            // Horizontal layout: portrait | stack
            var hLayout = panel.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(
                (int)HudPanelPadding, (int)HudPanelPadding,
                (int)HudPanelPadding, (int)HudPanelPadding);
            hLayout.spacing = HudPanelInnerSpacing;
            hLayout.childForceExpandWidth  = false;
            hLayout.childForceExpandHeight = true;
            hLayout.childControlWidth      = true;
            hLayout.childControlHeight     = true;
            hLayout.childAlignment         = TextAnchor.MiddleLeft;

            // --- Portrait (left) ---
            CreatePortrait(panel.transform, playerHealth);

            // --- Stat stack (right) ---
            var stack = CreateUIObject("StatStack", panel.transform);
            var stackLe = stack.AddComponent<LayoutElement>();
            stackLe.preferredWidth  = HudStackWidth;
            stackLe.preferredHeight = stackHeight;
            stackLe.flexibleWidth   = 0f;
            stackLe.flexibleHeight  = 0f;

            var vLayout = stack.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(0, 0, 0, 0);
            vLayout.spacing = HudStackSpacing;
            vLayout.childForceExpandWidth  = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childControlWidth      = true;
            vLayout.childControlHeight     = true;
            vLayout.childAlignment         = TextAnchor.MiddleLeft;

            // HP bar (green, value overlaid)
            var hpRow = CreateOverlayBar(stack.transform, "HpBar", HudBarHeight,
                new Color(0.20f, 0.85f, 0.20f, 1f),
                out var hpFill, out var hpBg, out var hpText);

            // MP bar (blue, value overlaid)
            var mpRow = CreateOverlayBar(stack.transform, "MpBar", HudBarHeight,
                new Color(0.31f, 0.47f, 1.0f, 1f),
                out var mpFill, out var mpBg, out var mpText);

            // 3 ability slots (icons + radial cooldown) — reads SpellCaster.
            CreateAbilityRow(stack.transform, playerHealth != null ? playerHealth.gameObject : null);

            // XP bar (yellow) — last in the stack.
            var xp = playerHealth != null ? playerHealth.GetComponent<Experience>() : null;
            CreateXpBarHUD(xp, stack.transform);

            // Attach PlayerHUD component (drives HP+MP fills).
            _playerHudPanel = panel;
            _playerHUD = panel.AddComponent<PlayerHUD>();
            _playerHUD.SetUIReferences(hpFill, hpBg, hpText, mpFill, mpBg, mpText);
            _playerHUD.Initialize(playerHealth);
        }
    }
}