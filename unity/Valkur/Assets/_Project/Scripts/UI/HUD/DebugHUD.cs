using System.Text;
using UnityEngine;
using TMPro;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Minimalist debug overlay that dumps all gameplay data as monospace text.
    /// Mirrors the Python roguelike HUD data: HP, MP, position, combat, spells, dash, FSM, FPS.
    /// Toggle with F1. Attach to a scene GameObject or let HUDBootstrap create it.
    /// </summary>
    public class DebugHUD : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private Color textColor = new Color(0.0f, 1f, 0.0f, 0.9f);
        [SerializeField] private Color bgColor = new Color(0f, 0f, 0f, 0.65f);

        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private UnityEngine.UI.Image _bg;
        private bool _visible = true;

        private GameObject _player;
        private Health _health;
        private MeleeCombat _melee;
        private DashAbility _dash;
        private SpellCaster _spellCaster;
        private Rigidbody2D _rb;

        private readonly StringBuilder _sb = new StringBuilder(512);

        // FPS tracking
        private float _fpsTimer;
        private int _fpsFrameCount;
        private float _currentFps;

        private void Start()
        {
            CreateOverlay();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
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

        private void TrackFps()
        {
            _fpsFrameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _currentFps = _fpsFrameCount / _fpsTimer;
                _fpsFrameCount = 0;
                _fpsTimer = 0f;
            }
        }

        private void TryFindPlayer()
        {
            if (_player != null) return;

            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) return;

            _health = _player.GetComponent<Health>();
            _melee = _player.GetComponent<MeleeCombat>();
            _dash = _player.GetComponent<DashAbility>();
            _spellCaster = _player.GetComponent<SpellCaster>();
            _rb = _player.GetComponent<Rigidbody2D>();
        }

        private void BuildText()
        {
            _sb.Clear();

            // Header
            _sb.AppendLine("<b>--- DEBUG HUD (F1 toggle) ---</b>");
            _sb.AppendLine();

            // FPS & Time
            _sb.AppendLine($"FPS: {_currentFps:F0}  dt: {Time.deltaTime * 1000f:F1}ms  Time: {Time.time:F1}s");
            _sb.AppendLine();

            if (_player == null)
            {
                _sb.AppendLine("Player: NOT FOUND");
            }
            else
            {
                // Position & Velocity
                Vector3 pos = _player.transform.position;
                Vector2 vel = _rb != null ? _rb.velocity : Vector2.zero;
                _sb.AppendLine($"Pos:  ({pos.x:F2}, {pos.y:F2})");
                _sb.AppendLine($"Vel:  ({vel.x:F2}, {vel.y:F2})  |{vel.magnitude:F2}|");
                _sb.AppendLine();

                // Health
                if (_health != null)
                {
                    float pct = _health.NormalizedHp * 100f;
                    _sb.AppendLine($"HP:   {_health.CurrentHp}/{_health.MaxHp}  ({pct:F0}%)  Dead={_health.IsDead}");
                }
                else
                {
                    _sb.AppendLine("HP:   (no Health)");
                }

                // Mana placeholder
                _sb.AppendLine($"MP:   100/100  (100%)");
                _sb.AppendLine();

                // Melee Combat
                if (_melee != null)
                {
                    string cdBar = CooldownBar(_melee.CooldownRemaining, _melee.CooldownTotal);
                    _sb.AppendLine($"Melee: dmg={_melee.Damage}  cd={_melee.CooldownRemaining:F2}s  {cdBar}  ready={_melee.CanAttack}");
                }

                // Dash
                if (_dash != null)
                {
                    string cdBar = CooldownBar(_dash.CooldownRemaining, _dash.CooldownTotal);
                    _sb.AppendLine($"Dash:  cd={_dash.CooldownRemaining:F2}s  {cdBar}  dashing={_dash.IsDashing}  ready={_dash.CanDash}");
                }
                _sb.AppendLine();

                // Spells
                if (_spellCaster != null)
                {
                    _sb.AppendLine($"Cast:  phase={_spellCaster.CurrentPhase}  timer={_spellCaster.PhaseTimer:F2}s  slot={_spellCaster.ActiveSlot}");
                    for (int i = 0; i < _spellCaster.SlotCount; i++)
                    {
                        float cd = _spellCaster.GetCooldownRemaining(i);
                        string name = _spellCaster.GetSlotName(i);
                        string status = cd > 0.01f ? $"cd={cd:F1}s" : "READY";
                        _sb.AppendLine($"  [{i + 1}] {name,-16} {status}");
                    }
                }
                _sb.AppendLine();

                // Nearby monsters
                _sb.AppendLine("<b>--- NEARBY MONSTERS ---</b>");
                var monsters = GameObject.FindGameObjectsWithTag("Monster");
                int shown = 0;
                foreach (var m in monsters)
                {
                    if (shown >= 5) { _sb.AppendLine("  ..."); break; }
                    float dist = Vector2.Distance(_player.transform.position, m.transform.position);
                    if (dist > 15f) continue;

                    var mHealth = m.GetComponent<Health>();
                    var mBrain = m.GetComponent<FSMMonsterBrain>();

                    string hpStr = mHealth != null ? $"{mHealth.CurrentHp}/{mHealth.MaxHp}" : "?";
                    string stateStr = mBrain != null ? mBrain.CurrentStateName : "?";
                    _sb.AppendLine($"  {m.name,-18} HP={hpStr,-8} State={stateStr,-10} d={dist:F1}");
                    shown++;
                }
                if (shown == 0)
                    _sb.AppendLine("  (none nearby)");
            }

            // Entity count
            _sb.AppendLine();
            int totalGo = FindObjectsOfType<Transform>().Length;
            _sb.AppendLine($"GameObjects: {totalGo}");

            if (_text != null)
                _text.text = _sb.ToString();
        }

        private static string CooldownBar(float remaining, float total)
        {
            if (total <= 0f) return "[=====]";
            float ratio = 1f - Mathf.Clamp01(remaining / total);
            int filled = Mathf.RoundToInt(ratio * 5);
            return "[" + new string('=', filled) + new string('.', 5 - filled) + "]";
        }

        private void CreateOverlay()
        {
            // Canvas
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

            // Panel (top-right)
            var panelGo = new GameObject("DebugPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-10f, -10f);
            panelRect.sizeDelta = new Vector2(420f, 520f);

            _bg = panelGo.AddComponent<UnityEngine.UI.Image>();
            _bg.color = bgColor;

            // Text
            var textGo = new GameObject("DebugText", typeof(RectTransform));
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 8f);
            textRect.offsetMax = new Vector2(-8f, -8f);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = fontSize;
            _text.color = textColor;
            _text.font = TMP_Settings.defaultFontAsset;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = false;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.richText = true;
        }
    }
}
