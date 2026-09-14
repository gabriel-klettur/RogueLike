using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.World;
using Valkur.Gameplay.World.Weather;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The top-right minimap: a bevelled gold dial over a live miniature of the world.
    ///
    /// <para><b>What is on the dial.</b> The world's own art, baked by
    /// <see cref="MinimapWorldBaker"/> and drawn through the composite shader with fog of war,
    /// a gold frontier, a dimmed "remembered" band beyond what the player can currently see,
    /// the day/night light, the weather and a sonar ping. Over it, one mesh of outlined glyphs
    /// (<see cref="MinimapScene"/>) and one of additive glows and particles
    /// (<see cref="MinimapFx"/>). The world map (<see cref="WorldMapPanel"/>) is the same
    /// renderer at a different size.</para>
    ///
    /// <para><b>Everything moves every frame and nothing costs a SetPixel.</b> The dial this
    /// replaced repainted a CPU texture twelve times a second, so its dots stepped and each
    /// repaint cost 4.3 ms. The terrain is a GPU texture now, the glyph mesh is rebuilt per
    /// frame, and the zoom eases instead of jumping.</para>
    ///
    /// Mounted by <see cref="HUDBootstrap"/> into the <c>[UI]</c> container, so
    /// <c>HUDVisibilityController</c> hides it — and pauses its bake — while an editor is open.
    /// </summary>
    public sealed partial class MinimapHUD : MonoBehaviour
    {
        private const float MARGIN_TOP   = Valkur.Core.UI.HudLayout.ScreenMargin;
        private const float MARGIN_RIGHT = Valkur.Core.UI.HudLayout.ScreenMargin;
        private const float ZOOM_HALF_LIFE = 0.07f;
        private const float REVEAL_INTERVAL = 0.08f;

        // ── Model ───────────────────────────────────────────────────────────
        private MinimapManager    _manager;
        private MinimapStyle      _style;
        private MinimapWorldBaker _baker;
        private readonly MinimapScene _scene = new MinimapScene();
        private readonly MinimapFx    _fx    = new MinimapFx();
        private MinimapView       _view;
        private Material          _mapMaterial;
        private Material          _additiveMaterial;
        private WorldMapPanel     _worldMap;

        // ── World ───────────────────────────────────────────────────────────
        private ZoneManager       _zoneManager;
        private PlayerController  _playerController;
        private Health            _playerHealth;
        private Valkur.Gameplay.Quests.QuestManager _quests;
        private float             _nextWorldLookup;

        // ── State ───────────────────────────────────────────────────────────
        private float  _displayRadius;
        private float  _flash;
        private string _worldKey;
        private string _zoneShown;
        private float  _zoneFlash;
        private float  _nextInfo;
        private float  _nextReveal;
        private float  _moteCredit;
        private int    _waypointRevision = -1;
        private bool   _boardPrimed;
        private bool   _wasSpirit;
        private float  _hoverAlpha;
        private readonly HashSet<int> _boardKeys = new HashSet<int>();
        private readonly HashSet<int> _boardKeysNext = new HashSet<int>();
        private readonly List<Vector2> _revealed = new List<Vector2>(64);

        /// <summary>The dial's view. Exposed for diagnostics and tests.</summary>
        public MinimapView View => _view;

        /// <summary>The terrain baker. Exposed for diagnostics and the console.</summary>
        public MinimapWorldBaker Baker => _baker;

        /// <summary>The per-frame item collection shared with the world map.</summary>
        public MinimapScene Scene => _scene;

        /// <summary>True while the world map is open.</summary>
        public bool WorldMapOpen => _worldMap != null && _worldMap.IsOpen;

        private void Awake()
        {
            _manager = GetComponent<MinimapManager>();
            if (_manager == null) _manager = gameObject.AddComponent<MinimapManager>();
            _style = MinimapStyle.Active;
        }

        private void Start()
        {
            CreateMaterials();
            BuildUI();
            _view = new MinimapView(_mapImage, _fxUnder, _glyphs, _fxOver, _mapMaterial, circle: true);
            _baker = new MinimapWorldBaker(transform, _style);
            _displayRadius = _manager.ViewRadius;
            _worldMap = WorldMapPanel.Create(transform, this, _mapMaterial.shader, _additiveMaterial);
        }

        private void OnDestroy()
        {
            UnbindPlayerHealth();
            if (_quests != null) _quests.OnQuestCompleted -= HandleQuestCompleted;
            _baker?.Dispose();
            if (_mapMaterial != null) Destroy(_mapMaterial);
            if (_additiveMaterial != null) Destroy(_additiveMaterial);
        }

        private void CreateMaterials()
        {
            var composite = MinimapStyle.ResolveShader(_style.compositeShader, "Valkur/UI/MinimapComposite");
            var additive  = MinimapStyle.ResolveShader(_style.additiveShader, "Valkur/UI/MinimapAdditive");
            if (composite == null || additive == null)
                Debug.LogError("[Minimap] The minimap shaders are missing — assign them on Resources/UI/MinimapStyle.asset.");
            _mapMaterial = new Material(composite != null ? composite : Shader.Find("UI/Default")) { name = "MinimapComposite (dial)" };
            _additiveMaterial = new Material(additive != null ? additive : Shader.Find("UI/Default")) { name = "MinimapAdditive" };
        }

        // ── Frame ───────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_view == null) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            RegisterConsoleCommand();

            _manager.TickPersistence(now);
            ResolveWorld(now);

            var playerT = EntityRegistry.PlayerTransform;
            bool hasPlayer = playerT != null;
            Vector2 player = hasPlayer ? (Vector2)playerT.position : _view.Centre;

            string key = ResolveWorldKey();
            if (key != _worldKey)
            {
                _worldKey = key;
                _manager.Fog.SetActiveKey(key);
                _fx.Clear();
                _baker.MarkAllDirty();
            }

            if (hasPlayer && _manager.FogOfWarEnabled && now >= _nextReveal)
            {
                _nextReveal = now + REVEAL_INTERVAL;
                _revealed.Clear();
                _manager.Fog.Reveal(player, _style.revealRadius, _revealed, 24);
                EmitRevealMotes(dt);
            }

            Vector2 bakeFocus = WorldMapOpen ? _worldMap.View.Centre : player;
            _baker.Tick(bakeFocus, key);
            RevealInteriorOnce();
            _scene.Collect(_style, now);

            TrackEvents(player, hasPlayer);
            TickAmbient(now);
            TickWeather(dt);
            _fx.Tick(dt);
            _flash = Mathf.Max(0f, _flash - dt / Mathf.Max(0.05f, _style.damageFlashSeconds));

            _displayRadius = MinimapProjection.Damp(_displayRadius, _manager.ViewRadius, ZOOM_HALF_LIFE, dt);
            _view.Centre = player;
            _view.HalfHeightWorld = _displayRadius;

            var frame = BuildFrame(now, hasPlayer, player);
            _view.Draw(in frame, _scene, _fx);
            if (WorldMapOpen) _worldMap.Draw(in frame, _scene);

            HandleZoomWheel();
            HandleMapHotkey();
            UpdateChrome(dt, now);
            UpdateOverlays();
            if (now >= _nextInfo)
            {
                _nextInfo = now + 0.5f;
                UpdateInfoPlate();
            }

            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _frameMs = Mathf.Lerp(_frameMs, (float)ms, 0.1f);
        }

        private MinimapFrame BuildFrame(float now, bool hasPlayer, Vector2 player)
        {
            var f = new MinimapFrame
            {
                Style = _style,
                Atlas = _baker.Atlas,
                AtlasRect = _baker.AtlasWorldRect,
                Fog = _manager.FogOfWarEnabled ? _manager.Fog.GetTexture() : Texture2D.whiteTexture,
                FogRect = _manager.FogOfWarEnabled ? _manager.Fog.WorldRect : new Vector4(-100000f, -100000f, 200000f, 200000f),
                HasPlayer = hasPlayer,
                Player = player,
                Facing = _playerController != null ? _playerController.FacingDirection : Vector2.up,
                Time = now,
                DayTint = ResolveDayTint(),
                Spirit = _scene.PlayerIsSpirit,
                FogMap = _manager.FogOfWarEnabled ? _manager.Fog : null,
                SightRadius = _style.revealRadius * 1.15f,
                ConfineToBounds = IsInterior,
            };
            var ar = _baker.AtlasWorldRect;
            f.Bounds = new Rect(ar.x, ar.y, ar.z, ar.w);

            ResolveWeather(out f.Desaturate, out f.Whiten);
            // A spirit sees the world in grey (SpiritWorldGrayscale); a full-colour map beside a
            // grey world reads as two different places. Most of the colour goes, not all of it,
            // so the altars' glow and the frontier still carry.
            if (f.Spirit) f.Desaturate = Mathf.Max(f.Desaturate, 0.7f);

            float period = _style.sonarPeriod;
            if (period > 0.1f && hasPlayer)
            {
                float t = (now % period) / period;
                f.PingRadius = t * _style.revealRadius * 1.5f;
                f.PingStrength = _style.sonarStrength * Mathf.Pow(1f - t, 1.6f) * Mathf.Clamp01(t * 8f);
            }

            var cam = Camera.main;
            if (cam != null && cam.orthographic)
            {
                f.CameraHalfExtent = new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
                f.CameraCentre = cam.transform.position;
            }
            return f;
        }

        // ── Events worth a particle ─────────────────────────────────────────

        private void EmitRevealMotes(float dt)
        {
            if (_revealed.Count == 0) return;
            _moteCredit = Mathf.Min(_moteCredit + _style.revealMotesPerSecond * REVEAL_INTERVAL, 8f);
            var c = _style.frontierGlow;
            c.a = 0.9f;
            for (int i = 0; i < _revealed.Count && _moteCredit >= 1f; i++)
            {
                if (Random.value > 0.35f) continue;
                _fx.RevealMote(_revealed[i], c);
                _moteCredit -= 1f;
            }
        }

        private void TrackEvents(Vector2 player, bool hasPlayer)
        {
            // New quest marks announce themselves with a ring. The first pass only primes the
            // set: every mark already on the board at load is not "new".
            _boardKeysNext.Clear();
            var items = _scene.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.Layer != MinimapLayer.Quest) continue;
                _boardKeysNext.Add(it.Key);
                if (_boardPrimed && !_boardKeys.Contains(it.Key)) _fx.Pulse(it.World, it.Color, it.Size * 1.2f, 0.9f);
            }
            _boardKeys.Clear();
            foreach (int k in _boardKeysNext) _boardKeys.Add(k);
            _boardPrimed = true;

            // The pin: arriving clears it with a burst; setting it (from the world map) pings it.
            if (hasPlayer && MinimapWaypoint.HasWaypoint)
            {
                Vector2 pin = MinimapWaypoint.Position;
                if (MinimapWaypoint.ClearIfReached(player))
                {
                    _fx.Burst(pin, _style.waypointColor, 12, 70f);
                    _fx.Pulse(pin, _style.waypointColor, 18f, 0.8f);
                }
            }
            if (MinimapWaypoint.Revision != _waypointRevision)
            {
                if (_waypointRevision >= 0 && MinimapWaypoint.HasWaypoint)
                    _fx.Pulse(MinimapWaypoint.Position, _style.waypointColor, 16f, 0.8f);
                _waypointRevision = MinimapWaypoint.Revision;
            }

            // Becoming a spirit: the dial breathes cold, and every altar rings once.
            bool spirit = _scene.PlayerIsSpirit;
            if (spirit && !_wasSpirit)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Icon == MinimapIcon.Ankh) _fx.Pulse(items[i].World, _style.altarColor, 22f, 1.2f);
            }
            _wasSpirit = spirit;
        }

        private void TickWeather(float dt)
        {
            var w = WeatherManager.Instance;
            if (w == null || w.IsIndoors || IsInterior) return;
            _fx.Weather(w.DensityOf(WeatherType.Rain), w.DensityOf(WeatherType.Snow), w.DensityOf(WeatherType.Wind),
                        _view.Radius, dt);
        }

        private void TickAmbient(float now)
        {
            int want = IsNight() ? _style.nightMotes : 0;
            if (_fx.AmbientCount < want && Random.value < 0.05f)
                _fx.AmbientMote(_view.Radius, new Color(0.75f, 0.85f, 1f, 0.22f));
        }

        // ── Player health (the rim flash) ───────────────────────────────────

        private void BindPlayerHealth(Health h)
        {
            if (h == _playerHealth) return;
            UnbindPlayerHealth();
            _playerHealth = h;
            if (_playerHealth != null) _playerHealth.OnDamagedBy += HandlePlayerDamaged;
        }

        private void UnbindPlayerHealth()
        {
            if (_playerHealth != null) _playerHealth.OnDamagedBy -= HandlePlayerDamaged;
            _playerHealth = null;
        }

        private void HandlePlayerDamaged(int amount, GameObject attacker)
        {
            if (amount <= 0) return;
            _flash = 1f;
            var pt = EntityRegistry.PlayerTransform;
            if (attacker == null || pt == null || _view == null) return;
            Vector2 d = (Vector2)attacker.transform.position - (Vector2)pt.position;
            if (d.sqrMagnitude < 1e-4f) return;
            float bearing = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            _fx.RimStrike(bearing, _view.Radius, _style.damageFlash);
        }

        /// <summary>
        /// A finished errand bursts gold from the player — the one moment the map can say "that
        /// is done" in the place the eye already is, before the mark that sent them disappears.
        /// </summary>
        private void HandleQuestCompleted(string questId)
        {
            var pt = EntityRegistry.PlayerTransform;
            if (pt == null) return;
            _fx.Burst(pt.position, _style.questOfferColor, 16, 85f);
            _fx.Pulse(pt.position, _style.questOfferColor, 26f, 1.0f);
        }

        // ── World lookups ───────────────────────────────────────────────────

        private void ResolveWorld(float now)
        {
            if (now < _nextWorldLookup) return;
            _nextWorldLookup = now + 1f;

            if (_zoneManager == null) _zoneManager = FindObjectOfType<ZoneManager>();
            if (_quests == null)
            {
                _quests = FindObjectOfType<Valkur.Gameplay.Quests.QuestManager>();
                if (_quests != null) _quests.OnQuestCompleted += HandleQuestCompleted;
            }

            var p = EntityRegistry.Player;
            if (p == null) { _playerController = null; UnbindPlayerHealth(); return; }
            if (_playerController == null || _playerController.gameObject != p)
                _playerController = p.GetComponent<PlayerController>();
            BindPlayerHealth(p.GetComponent<Health>());
        }

        /// <summary>
        /// Which world the player is in: the outdoor world, one interior, or another map visited on
        /// a trip from Pepitoria. Each keeps its own fog, and a change rebakes the terrain.
        /// </summary>
        private const string InteriorKeyPrefix = "interior:";
        private const string MapKeyPrefix = "map:";

        private bool IsInterior => _worldKey != null && _worldKey.StartsWith(InteriorKeyPrefix);

        private Vector4 _revealedInteriorRect;

        /// <summary>
        /// A room is not explored, it is seen: the moment its terrain bounds are known the whole
        /// of it is revealed. Walking a 14x10 bedroom to clear its fog is busywork, and the
        /// frontier ring drawn inside four walls reads as a fault in the map.
        /// </summary>
        private void RevealInteriorOnce()
        {
            if (!IsInterior || !_manager.FogOfWarEnabled || _baker.PixelsPerUnit <= 0) return;
            var r = _baker.AtlasWorldRect;
            if (r == _revealedInteriorRect) return;
            _revealedInteriorRect = r;
            var centre = new Vector2(r.x + r.z * 0.5f, r.y + r.w * 0.5f);
            float radius = Mathf.Sqrt(r.z * r.z + r.w * r.w) * 0.5f;
            // A rect this big is not a room — the bounds are still the outdoor world's for a
            // frame — and revealing it would clear a town's worth of the interior's fog layer.
            if (radius > 48f) { _revealedInteriorRect = default; return; }
            _manager.Fog.Reveal(centre, radius);
        }

        private string ResolveWorldKey()
        {
            bool interior = WorldTransitionService.IsBaseWorldContentSuspended
                            || (_zoneManager != null && _zoneManager.IsDetectionSuspended);
            if (interior)
            {
                string zone = _zoneManager != null ? _zoneManager.CurrentZone : null;
                return InteriorKeyPrefix + (string.IsNullOrEmpty(zone) ? "room" : zone);
            }
            // Another map lies over the same coordinates as Pepitoria. Under the town's key, walking
            // a generated world explored the town's fog wherever the two overlapped.
            string away = WorldExcursion.Destination;
            return string.IsNullOrEmpty(away) ? MinimapFogMap.WorldKey : MapKeyPrefix + away;
        }

        private static Color ResolveDayTint()
        {
            if (!DayNightCycle.HasInstance) return Color.white;
            var c = DayNightCycle.Instance.CurrentColor;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max < 1e-3f) return new Color(0.62f, 0.66f, 0.8f);
            // Keep the HUE of the light and only some of its darkness: a map that went as dark
            // as the midnight world would be unreadable exactly when the player needs it.
            var hue = new Color(c.r / max, c.g / max, c.b / max);
            float bright = Mathf.Lerp(0.62f, 1f, max);
            return hue * bright;
        }

        private static bool IsNight()
        {
            if (!DayNightCycle.HasInstance) return false;
            var ph = DayNightCycle.Instance.CurrentPhase;
            return ph == DayNightCycle.DayPhase.Night || ph == DayNightCycle.DayPhase.BlueHour;
        }

        private void ResolveWeather(out float desaturate, out float whiten)
        {
            desaturate = 0f;
            whiten = 0f;
            var w = WeatherManager.Instance;
            if (w == null || w.IsIndoors) return;
            desaturate = _style.rainDesaturation * w.DensityOf(WeatherType.Rain);
            whiten = _style.snowWhiten * w.DensityOf(WeatherType.Snow);
        }

        // ── Input ───────────────────────────────────────────────────────────

        private void HandleZoomWheel()
        {
            if (_discInput == null) return;
            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(wheel) < 0.1f) return;
            if (!_discInput.Contains(MouseInputManager.GetScreenMousePosition())) return;
            // Wheel up zooms IN — the convention the Tile and Buildings editors use.
            _manager.AdjustZoom(wheel > 0 ? -1 : 1);
        }

        private static InputActionDescriptor MapDescriptor =>
            InputActionCatalog.Find(InputActionCatalog.MapGameplay, "OpenWorldMap");

        private void HandleMapHotkey()
        {
            if (_worldMap == null) return;
            var action = InputService.Instance?.Gameplay?.OpenWorldMap;
            if (action == null) return;

            // Closing is always allowed from the map itself; opening respects the same
            // suppressions Pause does, and the posture mask the Controls editor edits.
            if (!_worldMap.IsOpen)
            {
                if (InputBlocker.IsGameplayBlocked) return;
                if (GameEditorManager.HasInstance && GameEditorManager.Instance.AnyEditorActive) return;
                var descriptor = MapDescriptor;
                if (descriptor != null && !InputContextPolicy.IsLive(descriptor)) return;
            }
            if (!InputBindingResolver.WasPerformedThisFrame(action)) return;
            ToggleWorldMap();
        }

        /// <summary>Open or close the world map.</summary>
        public void ToggleWorldMap()
        {
            if (_worldMap == null) return;
            if (_worldMap.IsOpen) _worldMap.Close();
            else _worldMap.Open(_view != null ? _view.Centre : Vector2.zero);
        }

        // ── Info plate ──────────────────────────────────────────────────────

        private void UpdateInfoPlate()
        {
            if (_zoneLabel == null) return;
            string zone = _zoneManager != null ? _zoneManager.CurrentZone : null;
            if (zone != _zoneShown)
            {
                bool first = _zoneShown == null;
                _zoneShown = zone;
                _zoneLabel.text = DisplayZoneName(zone);
                if (!first && !string.IsNullOrEmpty(zone))
                {
                    _zoneFlash = 1f;
                    _fx.RimSweep(_view.Radius, _style.ringHighlight);
                }
            }
            if (_coordsLabel != null) _coordsLabel.text = BuildSubtitle(zone);
        }

        private string BuildSubtitle(string zone)
        {
            if (_scene.PlayerIsSpirit) return "<color=#9fd4ff>Forma espiritual · busca un altar</color>";

            bool interior = IsInterior;
            string place;
            if (interior) place = "Interior";
            else if (_zoneManager != null && !string.IsNullOrEmpty(zone) && _manager.FogOfWarEnabled)
            {
                var rect = _zoneManager.GetZoneRect(zone);
                int pct = Mathf.RoundToInt(_manager.Fog.ExploredFraction(rect) * 100f);
                place = "Explorado " + pct + " %";
            }
            else place = string.Empty;

            string weather = WeatherWord();
            if (string.IsNullOrEmpty(weather)) return place;
            return string.IsNullOrEmpty(place) ? weather : place + "  ·  " + weather;
        }

        private static string WeatherWord()
        {
            var w = WeatherManager.Instance;
            if (w == null || w.IsIndoors) return null;
            if (w.DensityOf(WeatherType.Snow) > 0.15f) return "Nieve";
            float rain = w.DensityOf(WeatherType.Rain);
            if (rain > 0.75f) return "Tormenta";
            if (rain > 0.15f) return "Lluvia";
            if (w.DensityOf(WeatherType.Wind) > 0.15f) return "Viento";
            return null;
        }

        /// <summary>
        /// What the plate calls a zone. Generated zones are named after their grid offset
        /// ("zone_0_-50"), which is an identifier and not a place; the plate says so honestly
        /// rather than printing "Zone 0 -50" at the top of the screen.
        /// </summary>
        internal static string DisplayZoneName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            if (raw.StartsWith("zone_", System.StringComparison.OrdinalIgnoreCase)) return "Tierras salvajes";
            // Interiors are named after their overlay FILE ("house_interior_small.overlay").
            int dot = raw.IndexOf('.');
            if (dot > 0) raw = raw.Substring(0, dot);
            return PrettifyZoneName(raw);
        }

        internal static string PrettifyZoneName(string raw)
        {
            // "lobby" → "Lobby"; "zone_100_50" → "Zone 100 50".
            var chars = raw.Replace('_', ' ').ToCharArray();
            bool nextUpper = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') { nextUpper = true; continue; }
                if (nextUpper) { chars[i] = char.ToUpperInvariant(chars[i]); nextUpper = false; }
            }
            return new string(chars);
        }
    }
}
