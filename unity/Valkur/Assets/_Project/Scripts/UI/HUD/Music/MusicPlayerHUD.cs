using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The music panel: a small stone plaque in the bottom-right corner saying what is playing,
    /// from which zone, how far in — with the transport, the volume and, on request, the
    /// song's RESONANCE (its live spectrum over the shape of the whole song).
    ///
    /// <para><b>Its own pixel space</b>, exactly like the player panel: authored in texels
    /// (<see cref="MusicHudStyle"/>) and drawn at a whole number of screen pixels per texel
    /// (<see cref="PlayerHudStyle.HudPixelScaleFor"/>). The panel it replaces resized by
    /// stretching <c>localScale</c> on each axis independently — measured at (0.56, 0.87), so
    /// every glyph and circle was drawn 57 % taller than wide — on a canvas scaled against
    /// 800x600 while the rest of the HUD used 1600x800. This one does not resize at all: a
    /// "now playing" plaque has one right size per HUD scale.</para>
    ///
    /// <para><b>What it is NOT.</b> The old panel also carried a tap-tempo strip, a BPM drag and
    /// a waveform with a beat grid: authoring tools, written to PlayerPrefs, calibrating one
    /// machine. The tempo now comes from the catalog (<c>tools/audio/analyze_music.py</c>
    /// fills it for every track) and the player's panel only reads.</para>
    ///
    /// <para><b>Events, not decoration.</b> Motes answer something the player did or something
    /// that changed — a new track, a skip, a seek, unmuting. Nothing on the plaque moves with the
    /// beat: a panel that pulses forever is a screensaver, and its pulse would mean nothing when
    /// it mattered. The resonance is the one place the music moves things, because opening it
    /// is the player asking to see the music.</para>
    ///
    /// <para>The GameObject keeps the name <c>MusicPlayerHUD</c>: the Buildings editor finds it
    /// by that name to hide it while it is open.</para>
    ///
    /// <para>Every widget is a plain class ticked from <see cref="Tick"/>, public so an EditMode
    /// test can build the panel, drive it and read back what the player would see.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class MusicPlayerHUD : MonoBehaviour
    {
        /// <summary>The name HUDBootstrap gives the GameObject, and what the Buildings editor finds.</summary>
        public const string ObjectName = "MusicPlayerHUD";

        /// <summary>The longest step one frame may advance the panel by: a 20 fps floor.</summary>
        public const float MaxFrameStep = 0.05f;

        private MusicHudStyle _style;
        private PlayerHudStyle _theme;
        private MusicHudArt _art;
        private HudArt _hudArt;
        private Canvas _canvas;
        private RectTransform _window;
        private CanvasGroup _group;
        private RectTransform _pixels;
        private RawImage _stone;
        private Texture2D _stoneCompact, _stoneExpanded;
        private Material _additive;
        private bool _built;
        private float _trayRetryT;

        // -- Test / probe surface ------------------------------------------------

        public MusicHudStyle Style => _style;
        public Canvas Canvas => _canvas;
        public RectTransform Window => _window;
        public RectTransform PixelSpace => _pixels;
        public int PixelScale => _pixelScale;
        public Vector2Int SizeTexels => new Vector2Int(_style.widthTexels, _style.HeightTexels(_expanded));
        public bool Hidden => _hidden;
        public bool Expanded => _expanded;
        public float Alpha => _group != null ? _group.alpha : 0f;
        public float AlphaTarget => AlphaGoal();
        public string TitleText => _title != null ? _title.Text : string.Empty;
        public string ZoneText => _zone != null ? _zone.Text : string.Empty;
        public string StatusText => _status != null ? _status.Text : string.Empty;
        public string TimeNowText => _timeNow != null ? _timeNow.Text : string.Empty;
        public string TimeTotalText => _timeTotal != null ? _timeTotal.Text : string.Empty;
        public string VolumeText => _volumeLabel != null ? _volumeLabel.Text : string.Empty;
        public MusicGroove Groove => _groove;
        public MusicVolumeNotches Volume => _volume;
        public MusicHudKey PreviousKey => _prev;
        public MusicHudKey PlayKey => _play;
        public MusicHudKey NextKey => _next;
        public MusicHudKey MuteKey => _mute;
        public MusicHudKey ResonanceKey => _resonanceKey;
        public MusicHudKey CloseKey => _close;
        public HudMoteLayer Motes => _motes;
        public MusicResonanceGraphic ResonanceGraphic => _resonance;
        public MusicSpectrum Spectrum => _spectrum;
        public HudTooltip Tooltip => _tooltip;
        public Image Sigil => _sigil;
        public Image MedallionRim => _medallionRim;

        /// <summary>Frames of real work done since the panel was built. For the hidden-does-nothing test.</summary>
        public int WorkFrames { get; private set; }

        // -- Creation --------------------------------------------------------------

        /// <summary>Builds the panel under <paramref name="parent"/>. What HUDBootstrap and the tests call.</summary>
        public static MusicPlayerHUD Create(Transform parent)
        {
            var go = new GameObject(ObjectName, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var hud = go.AddComponent<MusicPlayerHUD>();
            hud.Build();
            return hud;
        }

        private void Awake() => Build();

        /// <summary>Builds once. Safe to call again.</summary>
        public void Build()
        {
            if (_built) return;
            _built = true;
            _style = MusicHudStyle.Active;
            _theme = PlayerHudStyle.Active;
            _art = MusicHudArt.Get(_theme);
            _hudArt = HudArt.Get(_theme);
            RestorePreferences();
            BuildCanvas();
            BuildWindow();
            BuildLayout();
            ApplyExpanded(force: true);
            ApplyVisibilityImmediately();
            Refit(force: true);
        }

        private void OnEnable()
        {
            BindAudio();
            RegisterTrayButton();
        }

        private void OnDisable()
        {
            UnbindAudio();
            UnbindPlayer();
            UnbindBeatClock();
            UnregisterTrayButton();
            _tooltip?.Hide();
        }

        private void OnDestroy()
        {
            HudLifetime.Release(_stoneCompact);
            HudLifetime.Release(_stoneExpanded);
            HudLifetime.Release(_additive);
        }

        private void Update()
        {
            // Unscaled: the panel is a menu, not the world — a pause must not freeze the song's
            // readout while the song itself goes on playing. Capped, so a hitch slows an effect
            // down instead of skipping it: uncapped, one long frame consumed a track change's
            // whole gold flash, shine and notes before a single one of them was drawn.
            Tick(Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep));
        }

        /// <summary>Advances the whole panel by <paramref name="dt"/>. Public so a test can drive it.</summary>
        public void Tick(float dt)
        {
            if (!_built) return;
            Refit();
            // The tray may be built after the panel, or lose its singleton to a domain reload:
            // look for it once a second until it answers.
            if (!_trayRegistered && (_trayRetryT -= dt) <= 0f) { _trayRetryT = 1f; RegisterTrayButton(); }
            if (_audio == null) BindAudio();
            if (!_consoleRegistered) RegisterConsoleCommand();
            TickPlayer(dt);
            TickVisibility(dt);
            // Before the hidden early-out: the keys control the music with the panel closed.
            HandleHotkeys();

            // Closed and faded out: nothing on it can be seen, so nothing on it is computed.
            // A track that changed while nobody could see it is not news when the panel opens.
            if (_hidden && _group.alpha <= 0f) { _announceTrack = false; return; }

            WorkFrames++;
            TickPlayback(dt);
            TickEffects(dt);
            TickResonance(dt);
            _volume.Tick(dt);
            _motes.Tick(dt);
        }
    }
}
