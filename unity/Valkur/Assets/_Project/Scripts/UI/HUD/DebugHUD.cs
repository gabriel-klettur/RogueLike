using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Unified debug overlay combining gameplay state and performance metrics.
    /// Toggle with F9. Professional layout with sectioned panels, color-coded
    /// indicators, and compact information density.
    /// </summary>
    public partial class DebugHUD : MonoBehaviour
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
        private bool _visible = false;

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
        private bool _ownsToggleAction;

        // Throttle the (very expensive) text rebuild. TMP mesh rebuild + dozens of
        // string allocations per frame was costing ~30-50 ms/frame and ~450 KB/s GC.
        // 5 Hz is plenty for a debug overlay.
        private const float TEXT_REBUILD_INTERVAL = 0.2f;
        private float _nextTextRebuildTime;

        private void Start()
        {
            _toggleAction = EditorHotkeyBindings.Resolve(
                EditorHotkeyBindings.Hotkey.ToggleDebugHUD, out _ownsToggleAction);
            CreateOverlay();
        }

        private void OnDestroy()
        {
            if (_ownsToggleAction)
            {
                _toggleAction?.Disable();
                _toggleAction?.Dispose();
            }
        }

        private void Update()
        {
            if (EditorHotkeyBindings.WasPerformedThisFrame(EditorHotkeyBindings.Hotkey.ToggleDebugHUD))
            {
                _visible = !_visible;
                if (_canvas != null)
                    _canvas.gameObject.SetActive(_visible);
            }

            if (!_visible) return;

            TrackFps();
            TryFindPlayer();

            if (Time.unscaledTime >= _nextTextRebuildTime)
            {
                _nextTextRebuildTime = Time.unscaledTime + TEXT_REBUILD_INTERVAL;
                BuildText();
            }
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

        private partial void BuildText();
    }
}
