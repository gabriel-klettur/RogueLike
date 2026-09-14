using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.Spells;
using Valkur.UIKit;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The bottom-centre action bar, with one face per posture.
    ///
    /// <para><b>War</b> shows the spells the character knows that one of their keys actually
    /// casts, each with that key read from its live binding, its cooldown from the spell book's
    /// own clock and the five states of <see cref="HudAbilitySlot"/>. <b>Peace</b> shows what the
    /// posture is FOR: talk and work, the bag, the map, the trades, the quests, the talents and
    /// the grimoire. Both end in the posture switch. Changing posture turns the bar over slot by
    /// slot, re-cuts the frame's gem in the new colour and throws a spray of motes — the one
    /// moment the bar is allowed to be loud, because it is the one moment the meaning of every
    /// key under the player's fingers changes.</para>
    ///
    /// <para><b>The player panel's kit, the player panel's grid.</b> Drawn with
    /// <see cref="HudArt"/>, <see cref="HudAbilitySlot"/>, <see cref="HudPixelText"/> and
    /// <see cref="HudMoteLayer"/>, in texels at <see cref="PlayerHudStyle.HudPixelScaleFor"/>
    /// screen pixels each, standing on the same bottom margin as the panel. The bar it replaces
    /// lived in <c>Valkur.Gameplay</c>, where none of that is reachable, and borrowed the tile
    /// editor's theme instead (<c>.github/SPELL_BAR_BEAUTY_AUDIT_2026-09-11.md</c>).</para>
    ///
    /// <para><b>It never casts on its own.</b> A click on a spell goes through
    /// <see cref="PlayerController.TryCastFromHud"/>, which applies the same gates as the key
    /// (posture, the per-slot mask, an open editor, stun, spirit form). The old bar called
    /// <c>SpellCaster.TryCast</c> directly and so fired damage spells in Peace.</para>
    ///
    /// <para>Every widget is ticked from <see cref="Tick"/>, public, so an EditMode test can build
    /// the bar, drive it and read back what the player would see.</para>
    /// </summary>
    public sealed partial class SpellBarHUD : MonoBehaviour
    {
        private const float PollSeconds = 0.25f;
        private const int GemClearance = 7;

        private PlayerHudStyle _hud;
        private SpellBarStyle _style;
        private HudArt _art;
        private SpellBarArt _barArt;
        private Canvas _rootCanvas;
        private RectTransform _root;
        private CanvasGroup _group;
        private RectTransform _pixels;
        private RectTransform _content;
        private RectTransform _slotsRoot;
        private RawImage _stone;
        private Texture2D _stoneTex;
        private Image _gemGlow;
        private Material _additive;
        private HudMoteLayer _motes;
        private HudTooltip _tooltip;
        private HudFloatText _floats;
        private HudPixelText _overflowLabel;
        private RectTransform _leftObstacle;

        private int _widthTexels;
        private int _heightTexels;
        private int _pixelScale;
        private int _screenW = -1, _screenH = -1;
        private float _rootScale = -1f;
        private float _obstacleEdge = -1f;
        private bool _built;

        private bool _visible = true;
        private bool _trayRegistered;

        private GameObject _player;
        private SpellCaster _caster;
        private PlayerStats _stats;
        private Mana _mana;
        private PlayerController _controller;
        private PlayerInteractionController _interaction;
        private bool _castHooked;

        // -- Test / probe surface ------------------------------------------------

        /// <summary>The posture the bar is SHOWING, which lags the real one through a flip.</summary>
        public Stance Face => _face;

        /// <summary>True while the bar is turning from one face to the other.</summary>
        public bool Flipping => _flip != FlipPhase.None;

        /// <summary>Bar size in texels.</summary>
        public Vector2Int SizeTexels => new Vector2Int(_widthTexels, _heightTexels);

        /// <summary>Screen pixels per texel right now.</summary>
        public int PixelScale => _pixelScale;

        /// <summary>The bar's outer rect, in HUD-canvas units.</summary>
        public RectTransform Panel => _root;

        public HudMoteLayer Motes => _motes;
        public HudTooltip Tooltip => _tooltip;
        public int SlotCount => _cells.Count;

        /// <summary>Known spells of the page on screen that the earned bar has no room for.</summary>
        public int Overflow => _overflow;

        /// <summary>The War bar's earned size, in sockets per row and rows. Zero when the bar has
        /// no stat store to ask (a bare test rig), in which case it is unbounded.</summary>
        public Vector2Int EarnedSize => _stats != null
            ? new Vector2Int(_stats.WarBarColumns, _stats.WarBarRows)
            : Vector2Int.zero;
        public HudAbilitySlot Slot(int i) => i >= 0 && i < _cells.Count ? _cells[i].Slot : null;
        public SpellBarEntry Entry(int i) => i >= 0 && i < _cells.Count ? _cells[i].Entry : default;

        /// <summary>True while the bar is shown (the tray button hides it).</summary>
        public bool IsShown => _visible;

        // -- Creation --------------------------------------------------------------

        /// <summary>
        /// Builds the bar into <paramref name="rootCanvas"/> and binds it to the player.
        /// <paramref name="leftObstacle"/> is the player panel: the bar centres on the screen and
        /// never slides under it when it is too wide to.
        /// </summary>
        public static SpellBarHUD Create(Canvas rootCanvas, GameObject player, RectTransform leftObstacle = null)
        {
            var go = new GameObject("SpellBarPanel", typeof(RectTransform));
            go.transform.SetParent(rootCanvas != null ? rootCanvas.transform : null, false);
            var bar = go.AddComponent<SpellBarHUD>();
            bar._leftObstacle = leftObstacle;
            bar.Build(rootCanvas, PlayerHudStyle.Active, SpellBarStyle.Active);
            bar.Bind(player);
            return bar;
        }

        private void Build(Canvas rootCanvas, PlayerHudStyle hud, SpellBarStyle style)
        {
            if (_built) return;
            _built = true;
            _hud = hud;
            _style = style;
            _art = HudArt.Get(hud);
            _barArt = SpellBarArt.Get();
            _rootCanvas = rootCanvas;

            _root = (RectTransform)transform;
            _root.anchorMin = _root.anchorMax = _root.pivot = Vector2.zero;

            // A nested canvas, so a cooldown wipe or a mote rebuilds this bar's batch and not the
            // whole HUD's — the same reason the player panel carries one.
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.referencePixelsPerUnit = HudArt.SpritePixelsPerUnit;
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();

            var shader = hud.hudFxShader != null ? hud.hudFxShader : Shader.Find("Valkur/UI/HudFx");
            if (shader != null)
            {
                _additive = new Material(shader) { name = "SpellBarAdditive", hideFlags = HideFlags.DontSave };
                _additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }

            _pixels = HudRect.Make("Pixels", _root, 0, 0, 1, 1);
            _content = HudRect.Make("Content", _pixels, 0, 0, 1, 1);
            var stoneRt = HudRect.Make("Stone", _content, 0, 0, 1, 1);
            _stone = stoneRt.gameObject.AddComponent<RawImage>();
            // The stone is the raycast target for the gaps between slots: a click there belongs
            // to the bar, not to the world behind it, or it would also throw the primary spell.
            _stone.raycastTarget = true;

            _gemGlow = HudRect.MakeImage("GemGlow", _content, _barArt.GemGlow, 0, 0, 13, 13);
            _gemGlow.material = _additive;
            _gemGlow.color = Color.clear;
            _gemGlow.enabled = false;

            _slotsRoot = HudRect.Make("Slots", _content, 0, 0, 1, 1);
            _floats = new HudFloatText(_content, _art, 2);
            _overflowLabel = HudPixelText.Create(_content, "Overflow", _art, HudFontFace.Small,
                                                 HudTextAlign.Right, 0, 0, 18, 7);
            _overflowLabel.color = HudTheme.Active.textDim;
            _overflowLabel.gameObject.SetActive(false);
            _motes = HudMoteLayer.Create(_pixels, _art, style.moteCapacity, _additive);
            _tooltip = new HudTooltip(_pixels, _art, 0);
        }

        // -- Binding -----------------------------------------------------------------

        /// <summary>Binds to <paramref name="player"/>. Safe to call again on a respawn.</summary>
        public void Bind(GameObject player)
        {
            Unbind();
            _player = player;
            if (_player != null)
            {
                _caster = _player.GetComponent<SpellCaster>();
                _stats = _player.GetComponent<PlayerStats>();
                _mana = _player.GetComponent<Mana>();
                _controller = _player.GetComponent<PlayerController>();
                _interaction = _player.GetComponent<PlayerInteractionController>();
                if (_caster != null) _caster.OnCastRefusedForMana += OnCastRefused;
            }
            if (!_castHooked)
            {
                GameEvents.OnSpellCast += OnSpellCast;
                _castHooked = true;
            }

            // The first face is drawn at once and silently: arriving is not an event.
            _face = PlayerStance.Current;
            _shiftPage = false;
            _flip = FlipPhase.None;
            Rebuild(EntriesFor(_face), celebrate: false);
            Refit(force: true);
        }

        private void Unbind()
        {
            if (_caster != null) _caster.OnCastRefusedForMana -= OnCastRefused;
            if (_castHooked)
            {
                GameEvents.OnSpellCast -= OnSpellCast;
                _castHooked = false;
            }
            _player = null;
            _caster = null;
            _stats = null;
            _mana = null;
            _controller = null;
            _interaction = null;
        }

        // -- Pixel grid ------------------------------------------------------------

        /// <summary>
        /// Re-derives the whole-pixel scale and the bar's place when the screen, the HUD canvas's
        /// scale, the bar's own size or the player panel's edge moved. Centred on the screen, on
        /// the panel's own bottom margin, placed on a whole screen pixel.
        /// </summary>
        public void Refit(bool force = false)
        {
            if (!_built) return;
            int sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            float root = _rootCanvas != null && _rootCanvas.scaleFactor > 0f ? _rootCanvas.scaleFactor : 1f;
            float edge = ObstacleRightPixels(root);
            if (!force && sw == _screenW && sh == _screenH && Mathf.Approximately(root, _rootScale)
                && Mathf.Approximately(edge, _obstacleEdge)) return;
            _screenW = sw;
            _screenH = sh;
            _rootScale = root;
            _obstacleEdge = edge;

            _pixelScale = _hud.HudPixelScaleFor(sw, sh);
            float perTexel = _pixelScale / root;
            _pixels.localScale = new Vector3(perTexel, perTexel, 1f);
            _root.sizeDelta = new Vector2(_widthTexels * perTexel, _heightTexels * perTexel);

            int barPx = _widthTexels * _pixelScale;
            int x = (sw - barPx) / 2;
            if (edge >= 0f) x = Mathf.Max(x, Mathf.CeilToInt(edge) + _style.clearanceTexels * _pixelScale);
            int y = Mathf.RoundToInt(_hud.marginTexels * _pixelScale);
            _root.anchoredPosition = new Vector2(x / root, y / root);
        }

        private float ObstacleRightPixels(float root)
        {
            if (_leftObstacle == null || !_leftObstacle.gameObject.activeInHierarchy) return -1f;
            return (_leftObstacle.anchoredPosition.x + _leftObstacle.sizeDelta.x) * root;
        }

        // -- Frame -------------------------------------------------------------------

        private void Update()
        {
            TryRegisterTray();
            // Game time, like the player panel: the pause menu stops the bar with the world, and a
            // probe can slow it with Time.timeScale to photograph a flip.
            Tick(Time.deltaTime);
        }

        /// <summary>Advances every widget. Public so an EditMode test can drive the bar.</summary>
        public void Tick(float dt)
        {
            if (!_built) return;
            Refit();
            PollFace(dt);
            TickFlip(dt);
            for (int i = 0; i < _cells.Count; i++) _cells[i].Slot.Tick(dt, _caster, _mana, _hud, _pixelScale);
            TickGem(dt);
            _floats.Tick(dt);
            _motes.Tick(dt);
            TickFade(dt);
        }

        // -- Showing and hiding --------------------------------------------------------

        /// <summary>Shows or hides the bar. Fades rather than cuts; a hidden bar takes no clicks.</summary>
        public void SetShown(bool shown)
        {
            _visible = shown;
            if (_group != null)
            {
                _group.blocksRaycasts = shown;
                _group.interactable = shown;
            }
            if (!shown) HideTooltip();
        }

        private void ToggleShown() => SetShown(!_visible);

        private void TickFade(float dt)
        {
            if (_group == null) return;
            float target = _visible ? 1f : 0f;
            float speed = 1f / Mathf.Max(0.01f, _style.showFadeSeconds);
            float a = Mathf.MoveTowards(_group.alpha, target, dt * speed);
            if (!Mathf.Approximately(a, _group.alpha)) _group.alpha = a;
        }

        private void TryRegisterTray()
        {
            if (_trayRegistered || !HUDIconBar.HasInstance) return;
            HUDIconBar.Instance.Register("spellbar", _style.trayIcon, ToggleShown, order: 1);
            _trayRegistered = true;
        }

        // -- Lifetime -------------------------------------------------------------------

        private void OnDisable() => HideTooltip();

        private void OnDestroy()
        {
            Unbind();
            if (_trayRegistered && HUDIconBar.HasInstance) HUDIconBar.Instance.Unregister("spellbar");
            ClearCells();
            HudLifetime.Release(_stoneTex);
            HudLifetime.Release(_additive);
        }
    }
}
