using System;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The bottom-left player panel: portrait, level, health, mana, the three mouse slots, the
    /// dash charge, status effects and experience — and the red screen edge that goes with them.
    ///
    /// <para><b>Its own pixel space.</b> The panel is authored in TEXELS (<see cref="PlayerHudStyle"/>)
    /// and drawn at a WHOLE number of screen pixels per texel, chosen from the resolution by
    /// <see cref="PlayerHudStyle.HudPixelScaleFor"/>. The HUD canvas scales everything by a free
    /// factor (1.27 at 1080p), so a panel drawn directly into it resamples every frame edge and
    /// every glyph; here a <c>Pixels</c> child counter-scales by <c>scale / canvas.scaleFactor</c>
    /// and the panel's corner is placed on a whole screen pixel, so every texel of it is exactly
    /// <c>scale</c> pixels at 1600x800, 1080p and 4K alike. The outer RectTransform still lives
    /// in canvas units, which is what the combo badge above it stacks against.</para>
    ///
    /// <para><b>A nested canvas</b>, so the bars gliding and the motes flying rebuild this panel's
    /// batch and not the whole HUD's.</para>
    ///
    /// <para><b>Events, not polling, for the model; polling only for clocks.</b> Health, mana and
    /// experience changes arrive through their events and decide what KIND of change happened
    /// (a blow, a heal, a spend, a level); cooldowns, the dash charge and status timers are clocks
    /// with no event for their passing, so those are read every frame.</para>
    ///
    /// <para>Every widget is a plain class ticked from here, so an EditMode test can build the
    /// panel, call <see cref="Tick"/> and read back what the player would see — Unity calls no
    /// <c>Update</c> in Edit Mode.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class PlayerHUD : MonoBehaviour
    {
        private const int SlotCount = 3;
        private const int SlotGap = 2;
        private const int IconColumn = 10;
        private const int StatusTilesMax = 4;

        private PlayerHudStyle _style;
        private HudArt _art;
        private Canvas _rootCanvas;
        private RectTransform _panel;
        private RectTransform _pixels;
        private RectTransform _content;
        private RawImage _stone;
        private Texture2D _stoneTex;
        private Material _additive;

        private HudPortrait _portrait;
        private HudMedallion _medallion;
        private HudBar _healthBar;
        private HudBar _manaBar;
        private HudBar _xpBar;
        private Image _heart;
        private Image _drop;
        private HudAbilitySlot[] _slots;
        private HudDashPip _pip;
        private HudStatusRow _status;
        private HudFloatText _floats;
        private HudTooltip _tooltip;
        private HudMoteLayer _motes;
        private HudEdgeVignette _edge;

        private int _widthTexels;
        private int _heightTexels;
        private int _pixelScale;
        private int _screenW = -1, _screenH = -1;
        private float _rootScale = -1f;
        private float _knockLeft;
        private float _knockSign = 1f;
        private float _deadT;
        private bool _built;

        /// <summary>Raised whenever the panel's footprint in the HUD canvas changes.</summary>
        public event Action GeometryChanged;

        // -- Test / probe surface ------------------------------------------------

        /// <summary>Screen pixels per texel right now.</summary>
        public int PixelScale => _pixelScale;

        /// <summary>Panel size in texels.</summary>
        public Vector2Int SizeTexels => new Vector2Int(_widthTexels, _heightTexels);

        /// <summary>The panel's outer rect, in HUD-canvas units.</summary>
        public RectTransform Panel => _panel;

        public HudBar HealthBar => _healthBar;
        public HudBar ManaBar => _manaBar;
        public HudBar XpBar => _xpBar;
        public HudMedallion Medallion => _medallion;
        public HudPortrait Portrait => _portrait;
        public HudDashPip DashPip => _pip;
        public HudStatusRow StatusRow => _status;
        public HudMoteLayer Motes => _motes;
        public HudEdgeVignette DangerEdge => _edge;
        public HudTooltip Tooltip => _tooltip;
        public HudFloatText FloatingText => _floats;
        public int SlotCountShown => _slots != null ? _slots.Length : 0;
        public HudAbilitySlot Slot(int i) => _slots != null && i >= 0 && i < _slots.Length ? _slots[i] : null;

        // -- Creation --------------------------------------------------------------

        /// <summary>Builds the panel into <paramref name="rootCanvas"/> and binds it to the player.</summary>
        public static PlayerHUD Create(Canvas rootCanvas, Health health, Mana mana = null)
        {
            var go = new GameObject("PlayerHUDPanel", typeof(RectTransform));
            go.transform.SetParent(rootCanvas != null ? rootCanvas.transform : null, false);
            var hud = go.AddComponent<PlayerHUD>();
            hud.Build(rootCanvas, PlayerHudStyle.Active);
            hud.Bind(health, mana);
            return hud;
        }

        private void Build(Canvas rootCanvas, PlayerHudStyle style)
        {
            if (_built) return;
            _built = true;
            _style = style;
            _art = HudArt.Get(style);
            _rootCanvas = rootCanvas;

            _panel = (RectTransform)transform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = Vector2.zero;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            gameObject.AddComponent<GraphicRaycaster>();

            var shader = style.hudFxShader != null ? style.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "HudAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            _widthTexels = style.PanelWidthTexels;
            _heightTexels = style.PanelHeightTexels;

            _pixels = HudRect.Make("Pixels", _panel, 0, 0, _widthTexels, _heightTexels);
            _content = HudRect.Make("Content", _pixels, 0, 0, _widthTexels, _heightTexels);

            _stoneTex = HudArt.BakePanel(_widthTexels, _heightTexels, style);
            var stoneRt = HudRect.Make("Stone", _content, 0, 0, _widthTexels, _heightTexels);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            _stone.texture = _stoneTex;
            _stone.raycastTarget = false;

            BuildLayout(style, shader);

            _motes = HudMoteLayer.Create(_pixels, _art, style.moteCapacity, _additive);
            _tooltip = new HudTooltip(_pixels, _art, _heightTexels);
            if (rootCanvas != null) _edge = new HudEdgeVignette(rootCanvas.transform, style.dangerEdge);

            ApplyPalette();
            Refit(force: true);
        }

        private void BuildLayout(PlayerHudStyle s, Shader fx)
        {
            int pad = s.paddingTexels;
            int xpY = pad;
            int topY = pad + s.xpBarTexels + s.rowGapTexels + 1;
            int top = s.TopRowTexels;

            // Portrait, left column. A double-click on it opens the character sheet.
            _portrait = new HudPortrait(_content, _art, s, pad, topY, s.portraitTexels, fx);
            var portraitHit = _portrait.Root.gameObject.AddComponent<Image>();
            portraitHit.color = Color.clear;
            portraitHit.raycastTarget = true;
            _portrait.Root.gameObject.AddComponent<AbilityRowDoubleClick>();

            // Stack, right column: health, mana, then the slot row.
            int x0 = pad + s.portraitTexels + s.columnGapTexels;
            int stackW = s.stackWidthTexels;
            int barX = x0 + IconColumn;
            int barW = stackW - IconColumn;

            int healthY = topY + top - s.healthBarTexels;
            _heart = HudRect.MakeImage("HeartIcon", _content, _art.Heart, x0, healthY + (s.healthBarTexels - 9) / 2, 9, 9);
            _healthBar = new HudBar(_content, "HealthBar", _art, barX, healthY, barW, s.healthBarTexels,
                                    HudFontFace.Large, withLabel: true, notchSegments: 4, _additive);

            int manaY = healthY - s.rowGapTexels - s.manaBarTexels;
            _drop = HudRect.MakeImage("ManaIcon", _content, _art.Drop, x0 + 1, manaY + (s.manaBarTexels - 9) / 2, 7, 9);
            _manaBar = new HudBar(_content, "ManaBar", _art, barX, manaY, barW, s.manaBarTexels,
                                  HudFontFace.Small, withLabel: true, notchSegments: 0, _additive);

            // The slot row is its own container so one double-click handler covers every slot
            // and the gaps between them.
            var row = HudRect.Make("SlotRow", _content, 0, 0, _widthTexels, _heightTexels);
            row.gameObject.AddComponent<AbilityRowDoubleClick>();
            _slots = new HudAbilitySlot[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                var slot = new HudAbilitySlot(row, _art, i, x0 + i * (s.slotTexels + SlotGap), topY, s.slotTexels,
                                              () => SpellKeyForSlot(index), () => ActionForSlot(index), _additive);
                slot.BecameReady += OnSlotReady;
                var hover = slot.Root.gameObject.AddComponent<HudSlotHover>();
                hover.Entered = () => ShowSlotTooltip(index);
                hover.Exited = HideTooltip;
                _slots[i] = slot;
            }

            int afterSlots = x0 + SlotCount * s.slotTexels + (SlotCount - 1) * SlotGap + 4;
            _pip = new HudDashPip(_content, _art, afterSlots, topY + (s.slotTexels - 11) / 2, _additive);
            _pip.BecameReady += OnDashReady;

            int statusX = afterSlots + 11 + 4;
            int tiles = Mathf.Clamp((x0 + stackW - statusX + 1) / 10, 0, StatusTilesMax);
            _status = new HudStatusRow(_content, _art, statusX, topY + (s.slotTexels - 10) / 2, Mathf.Max(1, tiles));

            // Experience: a thin gold line the full width of the panel, under everything.
            int xpW = _widthTexels - pad * 2;
            _xpBar = new HudBar(_content, "XpBar", _art, pad, xpY, xpW, s.xpBarTexels,
                                HudFontFace.Small, withLabel: false, notchSegments: 10, _additive);
            var xpHit = _xpBar.Root.gameObject.AddComponent<Image>();
            xpHit.color = Color.clear;
            xpHit.raycastTarget = true;
            var xpHover = _xpBar.Root.gameObject.AddComponent<HudSlotHover>();
            xpHover.Entered = ShowXpTooltip;
            xpHover.Exited = HideTooltip;

            // The level sits on the portrait's bottom-right corner, over the frame.
            int m = Mathf.Clamp(s.medallionTexels | 1, 11, 25);
            _medallion = new HudMedallion(_content, _art, pad + s.portraitTexels - m + 4, topY - 3, m, _additive);

            _floats = new HudFloatText(_content, _art, 4);
        }

        /// <summary>Colours that follow <c>WorldBarStyle</c> when the style says so.</summary>
        private void ApplyPalette()
        {
            var s = _style;
            Color health = s.health, low = s.healthLow, chip = s.healthChip, mana = s.mana,
                  manaChip = s.manaChip;
            if (s.followWorldBarPalette)
            {
                var w = WorldBarStyle.Active;
                if (w != null)
                {
                    health = w.HealthFor(Valkur.Core.UI.WorldBarRank.Player);
                    chip = w.healthChip;
                    mana = w.mana;
                    manaChip = w.manaSpent;
                }
            }
            _healthBar.SetColours(health, low, s.lowThreshold, chip, s.heal, s.healthLow);
            _healthBar.SetNotchColour(s.notch);
            _healthBar.SetLabelColour(s.text);
            _manaBar.SetColours(mana, mana, -1f, manaChip, Color.Lerp(mana, Color.white, 0.55f), mana);
            _manaBar.SetLabelColour(s.text);
            _xpBar.SetColours(s.xp, s.xp, -1f, s.xpChip, s.xpChip, s.xp);
            var xpNotch = s.notch;
            xpNotch.a *= 0.6f;
            _xpBar.SetNotchColour(xpNotch);
            _heart.color = health;
            _drop.color = mana;
            _medallion.SetColour(s.text);
        }

        // -- Pixel grid ------------------------------------------------------------

        /// <summary>
        /// Re-derives the whole-pixel scale and the panel's footprint when the screen or the HUD
        /// canvas's own scale changed. The corner is placed on a whole screen pixel so every texel
        /// edge inside lands on one too.
        /// </summary>
        public void Refit(bool force = false)
        {
            int sw = Screen.width, sh = Screen.height;
            float root = _rootCanvas != null && _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale)) return;
            _screenW = sw;
            _screenH = sh;
            _rootScale = root;

            _pixelScale = _style.HudPixelScaleFor(sw, sh);
            float perTexel = _pixelScale / root;
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            _panel.sizeDelta = new Vector2(_widthTexels * perTexel, _heightTexels * perTexel);
            float margin = Mathf.Round(_style.marginTexels * _pixelScale) / root;
            _panel.anchoredPosition = new Vector2(margin, margin);
            GeometryChanged?.Invoke();
        }

        // -- Frame -------------------------------------------------------------------

        private void Update()
        {
            Refit();
            // Game time, like the bars over the head: the pause menu stops the panel with the
            // world it reports on, and a probe can slow it with Time.timeScale to photograph a
            // transient — a chip holds for 0.3 s, which no screenshot through the MCP bridge
            // catches at full speed.
            Tick(Time.deltaTime);
        }

        /// <summary>Advances every widget. Public so an EditMode test can drive the panel.</summary>
        public void Tick(float dt)
        {
            if (!_built) return;
            var s = _style;
            bool dead = _health != null && _health.IsDead;
            float hp = _health != null && _health.MaxHp > 0 ? (float)_health.CurrentHp / _health.MaxHp : 1f;

            float depth = 1f - Mathf.Clamp01(hp / Mathf.Max(0.01f, s.lowThreshold));
            _healthBar.SetHeartbeat(!dead && hp < s.lowThreshold && hp > 0f, depth);
            _heart.color = _healthBar.FillColour;

            _healthBar.Tick(dt, s);
            _manaBar.Tick(dt, s);
            _xpBar.Tick(dt, s);
            _portrait.Tick(dt, hp, dead);
            for (int i = 0; i < _slots.Length; i++) _slots[i].Tick(dt, _caster, _mana, s, _pixelScale);
            _pip.Tick(dt, _dash, s);
            _status.Tick(_statuses);
            _medallion.Tick(dt, s);
            _floats.Tick(dt);
            _motes.Tick(dt);
            _edge?.Tick(dt, hp, dead, s);
            TickLevelUp(dt);
            TickKnock(dt, s);

            // A spirit's panel goes cold: the stone drifts to a pale blue-grey.
            _deadT = Mathf.MoveTowards(_deadT, dead ? 1f : 0f, dt * 2f);
            var stone = Color.Lerp(Color.white, s.spiritStone, _deadT);
            if (_stone.color != stone) _stone.color = stone;
        }

        private void TickKnock(float dt, PlayerHudStyle s)
        {
            if (_knockLeft <= 0f) return;
            _knockLeft -= dt;
            if (_knockLeft <= 0f)
            {
                _content.anchoredPosition = Vector2.zero;
                return;
            }
            // Whole texels, alternating sides, decaying: the panel is KNOCKED, not blurred.
            float t = _knockLeft / Mathf.Max(0.01f, s.knockSeconds);
            int step = Mathf.CeilToInt(s.knockTexels * t);
            int phase = Mathf.FloorToInt(_knockLeft * 40f) & 1;
            _content.anchoredPosition = new Vector2((phase == 0 ? 1 : -1) * step * _knockSign, 0f);
        }

        private void Knock()
        {
            if (_style.knockTexels <= 0 || _style.knockSeconds <= 0f) return;
            _knockLeft = _style.knockSeconds;
            _knockSign = -_knockSign;
        }

        // -- Slots --------------------------------------------------------------------

        private string SpellKeyForSlot(int i)
        {
            switch (i)
            {
                case 0: return _controller != null ? _controller.PrimarySpellKeyNow : "fireball";
                case 1: return PlayerController.SecondarySpellKey;
                default: return PlayerController.MiddleSpellKey;
            }
        }

        private static UnityEngine.InputSystem.InputAction ActionForSlot(int i)
        {
            var gameplay = InputService.Instance != null ? InputService.Instance.Gameplay : null;
            if (gameplay == null) return null;
            switch (i)
            {
                case 0: return gameplay.PrimaryAttack;
                case 1: return gameplay.SecondaryAttack;
                default: return gameplay.MiddleClick;
            }
        }

        private void ShowSlotTooltip(int i)
        {
            var slot = Slot(i);
            if (slot == null) return;
            _tooltip.Show(slot, _widthTexels, slot.SpellColour(_style));
        }

        private void ShowXpTooltip()
        {
            if (_xp == null) return;
            int need = Mathf.Max(1, _xp.XpForNextLevel - _xp.XpRequiredForLevel(_xp.Level));
            string body = _xp.IsAtLevelCap ? "Nivel máximo" : _xp.XpInCurrentLevel + " / " + need + " experiencia";
            _tooltip.ShowText("Nivel " + _xp.Level, body, _style.xp, _widthTexels * 0.5f,
                              _xpBar.Root.anchoredPosition.y + _xpBar.Root.sizeDelta.y, _widthTexels);
        }

        private void HideTooltip() => _tooltip?.Hide();

        // -- Lifetime -------------------------------------------------------------------

        // The panel is hidden with SetActive while a runtime editor is open; the red edge lives in
        // the root canvas, outside it, and has to be told.
        private void OnEnable() => _edge?.SetSuppressed(false);
        private void OnDisable() => _edge?.SetSuppressed(true);

        private void OnDestroy()
        {
            Unbind();
            _portrait?.Dispose();
            _edge?.Dispose();
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(_additive);
        }
    }
}
