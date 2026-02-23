using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Unified debug overlay combining gameplay state and performance metrics.
    /// Toggle with F1. Professional layout with sectioned panels, color-coded
    /// indicators, and compact information density.
    /// </summary>
    public class DebugHUD : MonoBehaviour
    {
        // --- Color Palette ---
        private static readonly Color COL_BG         = new Color(0.08f, 0.08f, 0.12f, 0.88f);
        private static readonly Color COL_HEADER     = new Color(0.65f, 0.78f, 1.0f, 1f);
        private static readonly Color COL_LABEL      = new Color(0.55f, 0.58f, 0.65f, 1f);
        private static readonly Color COL_VALUE      = new Color(0.90f, 0.92f, 0.96f, 1f);
        private static readonly Color COL_SEPARATOR  = new Color(0.30f, 0.32f, 0.40f, 1f);
        private static readonly Color COL_HP         = new Color(0.30f, 0.90f, 0.40f, 1f);
        private static readonly Color COL_HP_LOW     = new Color(0.95f, 0.30f, 0.25f, 1f);
        private static readonly Color COL_MP         = new Color(0.35f, 0.60f, 1.0f, 1f);
        private static readonly Color COL_READY      = new Color(0.30f, 0.90f, 0.40f, 1f);
        private static readonly Color COL_COOLDOWN   = new Color(0.95f, 0.65f, 0.20f, 1f);
        private static readonly Color COL_FPS_GOOD   = new Color(0.30f, 0.90f, 0.40f, 1f);
        private static readonly Color COL_FPS_WARN   = new Color(0.95f, 0.85f, 0.20f, 1f);
        private static readonly Color COL_FPS_BAD    = new Color(0.95f, 0.30f, 0.25f, 1f);
        private static readonly Color COL_MONSTER    = new Color(0.90f, 0.55f, 0.55f, 1f);
        private static readonly Color COL_DIM        = new Color(0.45f, 0.47f, 0.52f, 1f);

        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private UnityEngine.UI.Image _bg;
        private bool _visible = true;

        private GameObject _player;
        private Health _health;
        private Mana _mana;
        private MeleeCombat _melee;
        private DashAbility _dash;
        private SpellCaster _spellCaster;
        private Rigidbody2D _rb;

        private readonly StringBuilder _sb = new StringBuilder(1024);

        // FPS tracking (local, fast)
        private float _fpsTimer;
        private int _fpsFrameCount;
        private float _currentFps;
        private float _currentMs;

        private InputAction _toggleAction;

        private void Start()
        {
            _toggleAction = new InputAction("ToggleDebugHUD", InputActionType.Button, "<Keyboard>/f1");
            _toggleAction.Enable();
            CreateOverlay();
        }

        private void Update()
        {
            if (_toggleAction != null && _toggleAction.WasPerformedThisFrame())
            {
                _visible = !_visible;
                if (_canvas != null)
                    _canvas.gameObject.SetActive(_visible);
            }

            if (!_visible) return;

            TrackFps();
            TryFindPlayer();
            BuildText();
        }

        // ─────────────────────────────────────────────
        //  FPS
        // ─────────────────────────────────────────────

        private void TrackFps()
        {
            _fpsFrameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _currentFps = _fpsFrameCount / _fpsTimer;
                _currentMs = _fpsTimer / _fpsFrameCount * 1000f;
                _fpsFrameCount = 0;
                _fpsTimer = 0f;
            }
        }

        // ─────────────────────────────────────────────
        //  Player cache
        // ─────────────────────────────────────────────

        private void TryFindPlayer()
        {
            if (_player != null) return;

            _player = EntityRegistry.Player;
            if (_player == null) return;

            _health = _player.GetComponent<Health>();
            _mana = _player.GetComponent<Mana>();
            _melee = _player.GetComponent<MeleeCombat>();
            _dash = _player.GetComponent<DashAbility>();
            _spellCaster = _player.GetComponent<SpellCaster>();
            _rb = _player.GetComponent<Rigidbody2D>();
        }

        // ─────────────────────────────────────────────
        //  Text builder
        // ─────────────────────────────────────────────

        private void BuildText()
        {
            _sb.Clear();

            // ── Performance Section ──
            AppendHeader("PERFORMANCE");

            // FPS with color coding
            string fpsCol = FpsColorHex(_currentFps);
            _sb.Append(Label("FPS "));
            _sb.Append($"<color={fpsCol}><b>{_currentFps:F0}</b></color>");
            _sb.Append(Dim($"  ({_currentMs:F1}ms)"));

            // Perf monitor stats (if available)
            var perf = PerformanceMonitor.Instance;
            if (perf != null)
            {
                _sb.Append(Label("   p95 "));
                _sb.Append(Val($"{perf.P95FrameTimeMs:F1}ms"));
                _sb.Append(Label("  p99 "));
                _sb.Append(Val($"{perf.P99FrameTimeMs:F1}ms"));
            }
            _sb.AppendLine();

            int entityCount = EntityRegistry.MonsterCount + (EntityRegistry.HasPlayer ? 1 : 0);
            _sb.Append(Label("Entities "));
            _sb.Append(Val($"{entityCount}"));
            _sb.Append(Label("   Time "));
            _sb.Append(Val($"{Time.time:F0}s"));
            if (perf != null)
            {
                _sb.Append(Label("   GC "));
                _sb.Append(Val($"{System.GC.CollectionCount(0)}"));
            }
            _sb.AppendLine();

            AppendSeparator();

            // ── Player Section ──
            if (_player == null)
            {
                AppendHeader("PLAYER");
                _sb.AppendLine(Dim("  Waiting for player..."));
            }
            else
            {
                AppendHeader("PLAYER");

                // Position & Velocity (single line)
                Vector3 pos = _player.transform.position;
                Vector2 vel = _rb != null ? _rb.velocity : Vector2.zero;
                _sb.Append(Label("Pos "));
                _sb.Append(Val($"{pos.x:F1}, {pos.y:F1}"));
                _sb.Append(Label("   Vel "));
                _sb.Append(Val($"{vel.magnitude:F1}"));
                _sb.Append(Dim($" ({vel.x:F1}, {vel.y:F1})"));
                _sb.AppendLine();

                _sb.Append(Label("Class "));
                _sb.Append(Val(PlayerSelectionState.SelectedPlayerKey));
                _sb.AppendLine();

                // HP bar
                if (_health != null)
                {
                    float hpPct = _health.NormalizedHp;
                    string hpHex = hpPct > 0.3f ? ColorHex(COL_HP) : ColorHex(COL_HP_LOW);
                    _sb.Append(Label("HP  "));
                    _sb.Append($"<color={hpHex}>{ProgressBar(hpPct, 12)} {_health.CurrentHp}/{_health.MaxHp}</color>");
                    _sb.AppendLine();
                }

                // MP bar
                if (_mana != null)
                {
                    float mpPct = _mana.NormalizedMana;
                    string mpHex = ColorHex(COL_MP);
                    _sb.Append(Label("MP  "));
                    _sb.Append($"<color={mpHex}>{ProgressBar(mpPct, 12)} {_mana.CurrentMana}/{_mana.MaxMana}</color>");
                    _sb.AppendLine();
                }

                AppendSeparator();

                // ── Combat Section ──
                AppendHeader("COMBAT");

                if (_melee != null)
                {
                    bool ready = _melee.CanAttack;
                    string stateCol = ready ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                    string stateTag = ready ? "RDY" : $"{_melee.CooldownRemaining:F1}s";
                    _sb.Append(Label("Melee "));
                    _sb.Append(Val($"dmg={_melee.Damage}"));
                    _sb.Append($"  <color={stateCol}>{stateTag}</color>");
                    _sb.AppendLine();
                }

                if (_dash != null)
                {
                    bool ready = _dash.CanDash;
                    string stateCol = ready ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                    string stateTag = _dash.IsDashing ? "DASH" : ready ? "RDY" : $"{_dash.CooldownRemaining:F1}s";
                    _sb.Append(Label("Dash  "));
                    _sb.Append($"<color={stateCol}>{stateTag}</color>");
                    _sb.AppendLine();
                }

                // Spells
                if (_spellCaster != null)
                {
                    for (int i = 0; i < _spellCaster.SlotCount; i++)
                    {
                        float cd = _spellCaster.GetCooldownRemaining(i);
                        string name = _spellCaster.GetSlotName(i);
                        bool rdy = cd <= 0.01f;
                        string stateCol = rdy ? ColorHex(COL_READY) : ColorHex(COL_COOLDOWN);
                        string stateTag = rdy ? "RDY" : $"{cd:F1}s";
                        _sb.Append(Label($"  [{i + 1}] "));
                        _sb.Append(Val($"{name,-14}"));
                        _sb.Append($"<color={stateCol}>{stateTag}</color>");
                        _sb.AppendLine();
                    }
                }

                AppendSeparator();

                // ── Nearby Monsters ──
                AppendHeader("NEARBY");

                var monsters = EntityRegistry.Monsters;
                int shown = 0;
                for (int i = 0; i < monsters.Count; i++)
                {
                    var m = monsters[i];
                    if (m == null) continue;
                    if (shown >= 5) { _sb.AppendLine(Dim("  ...")); break; }

                    float dist = Vector2.Distance(_player.transform.position, m.transform.position);
                    if (dist > 15f) continue;

                    var mHealth = m.GetComponent<Health>();
                    var mBrain = m.GetComponent<FSMMonsterBrain>();

                    string hpStr = mHealth != null ? $"{mHealth.CurrentHp}/{mHealth.MaxHp}" : "?";
                    string stateStr = mBrain != null ? mBrain.CurrentStateName : "?";

                    string monHex = ColorHex(COL_MONSTER);
                    _sb.Append($"  <color={monHex}>{m.name,-14}</color>");
                    _sb.Append(Label(" HP "));
                    _sb.Append(Val($"{hpStr,-7}"));
                    _sb.Append(Label(" "));
                    _sb.Append(Dim($"{stateStr,-8}"));
                    _sb.Append(Label(" d="));
                    _sb.Append(Val($"{dist:F0}"));
                    _sb.AppendLine();
                    shown++;
                }
                if (shown == 0)
                    _sb.AppendLine(Dim("  No enemies nearby"));
            }

            if (_text != null)
                _text.text = _sb.ToString();
        }

        // ─────────────────────────────────────────────
        //  Formatting helpers
        // ─────────────────────────────────────────────

        private static string ColorHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }

        private static string FpsColorHex(float fps)
        {
            if (fps >= 55f) return ColorHex(COL_FPS_GOOD);
            if (fps >= 30f) return ColorHex(COL_FPS_WARN);
            return ColorHex(COL_FPS_BAD);
        }

        private static string Label(string s) => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_LABEL)}>{s}</color>";
        private static string Val(string s)   => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_VALUE)}>{s}</color>";
        private static string Dim(string s)   => $"<color=#{ColorUtility.ToHtmlStringRGB(COL_DIM)}>{s}</color>";

        private void AppendHeader(string title)
        {
            string hex = ColorHex(COL_HEADER);
            _sb.AppendLine($"<color={hex}><b>{title}</b></color>");
        }

        private void AppendSeparator()
        {
            string hex = ColorHex(COL_SEPARATOR);
            _sb.AppendLine($"<color={hex}>────────────────────────────────</color>");
        }

        private static string ProgressBar(float ratio, int width)
        {
            ratio = Mathf.Clamp01(ratio);
            int filled = Mathf.RoundToInt(ratio * width);
            int empty = width - filled;
            return "\u2588" + new string('\u2588', Mathf.Max(0, filled - 1))
                 + new string('\u2591', empty);
        }

        // ─────────────────────────────────────────────
        //  UI construction
        // ─────────────────────────────────────────────

        private void CreateOverlay()
        {
            var canvasGo = new GameObject("DebugHUDCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;

            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Panel (top-right, auto-height via content size fitter)
            var panelGo = new GameObject("DebugPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-12f, -12f);
            panelRect.sizeDelta = new Vector2(380f, 0f);

            _bg = panelGo.AddComponent<UnityEngine.UI.Image>();
            _bg.color = COL_BG;

            var csf = panelGo.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            // Text
            var textGo = new GameObject("DebugText", typeof(RectTransform));
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 13;
            _text.color = COL_VALUE;
            _text.font = TMP_Settings.defaultFontAsset;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.richText = true;
            _text.lineSpacing = -8f;
        }

        private void OnDisable()
        {
            _toggleAction?.Disable();
        }

        private void OnEnable()
        {
            _toggleAction?.Enable();
        }

        private void OnDestroy()
        {
            _toggleAction?.Disable();
            _toggleAction?.Dispose();
        }
    }
}
