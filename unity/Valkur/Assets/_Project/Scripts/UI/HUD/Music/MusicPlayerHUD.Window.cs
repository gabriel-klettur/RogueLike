using UnityEngine;
using Valkur.Gameplay;
using Valkur.UIKit;

namespace Valkur.UI.HUD
{
    public sealed partial class MusicPlayerHUD
    {
        // Per-machine window state, written at the END of a gesture — a write per drag frame is a
        // file write per frame. The editor workspace layer cannot hold it: every entry point
        // there is keyed on an editor name, which a HUD panel does not have.
        private const string PrefHidden = "valkur.musichud.hidden";
        private const string PrefExpanded = "valkur.musichud.expanded";
        private const string PrefDockRight = "valkur.musichud.dock.right";
        private const string PrefDockBottom = "valkur.musichud.dock.bottom";

        // Keys the old panel wrote and nothing reads any more. A constant, not a static array:
        // a static array is mutable state the Domain-Reload ratchet refuses.
        private const string ObsoletePrefs =
            "valkur.musichud.simple.width|valkur.musichud.simple.height|" +
            "valkur.musichud.expanded.width|valkur.musichud.expanded.height|" +
            "valkur.musichud.amplitude|valkur.musichud.volume";

        private const string TrayButtonId = "music";

        private bool _hidden;
        private bool _expanded;
        private float _dockRight, _dockBottom;
        private bool _trayRegistered;

        private Health _playerHealth;
        private float _playerSearchT;
        private float _combatT;
        private bool _yieldedToFight;
        private float _spiritT;

        // -- Preferences -----------------------------------------------------------

        private void RestorePreferences()
        {
            // Visible on a fresh install: a player who never learns the panel exists has no music
            // controls at all. Once they close it, it stays closed until they open it again.
            _hidden = PlayerPrefs.GetInt(PrefHidden, 0) != 0;
            _expanded = PlayerPrefs.GetInt(PrefExpanded, 0) != 0;
            _dockRight = PlayerPrefs.GetFloat(PrefDockRight, _style.dockRight);
            _dockBottom = PlayerPrefs.GetFloat(PrefDockBottom, _style.dockBottom);
            bool any = false;
            foreach (var key in ObsoletePrefs.Split('|'))
            {
                if (!PlayerPrefs.HasKey(key)) continue;
                PlayerPrefs.DeleteKey(key);
                any = true;
            }
            if (any) PlayerPrefs.Save();
        }

        // -- Visibility --------------------------------------------------------------

        /// <summary>Opens or closes the panel, and remembers it.</summary>
        public void SetHidden(bool hidden)
        {
            if (_hidden == hidden) return;
            _hidden = hidden;
            PlayerPrefs.SetInt(PrefHidden, hidden ? 1 : 0);
            PlayerPrefs.Save();
            if (hidden) { _tooltip?.Hide(); _motes?.Clear(); }
            _yieldedToFight = false;
            _group.blocksRaycasts = !hidden;
            _group.interactable = !hidden;
        }

        /// <summary>What the tray button does.</summary>
        public void Toggle() => SetHidden(!_hidden);

        /// <summary>Opens or closes the resonance, and remembers it.</summary>
        public void SetExpanded(bool expanded)
        {
            if (_expanded == expanded) return;
            _expanded = expanded;
            PlayerPrefs.SetInt(PrefExpanded, expanded ? 1 : 0);
            PlayerPrefs.Save();
            _tooltip?.Hide();
            ApplyExpanded();
        }

        private float AlphaGoal()
        {
            if (_hidden) return 0f;
            // The song yields to the fight: while the player is being hit the panel steps back,
            // unless the pointer is on it — then the player is using it.
            bool hovered = _hoveringPanel;
            return _combatT > 0f && !hovered ? _style.combatAlpha : 1f;
        }

        private void ApplyVisibilityImmediately()
        {
            _group.alpha = AlphaGoal();
            _group.blocksRaycasts = !_hidden;
            _group.interactable = !_hidden;
        }

        private void TickVisibility(float dt)
        {
            float goal = AlphaGoal();
            float speed = 1f / Mathf.Max(0.01f, _style.fadeSeconds);
            if (_combatT > 0f && !_hidden) _yieldedToFight = true;
            // Coming back after a fight is slower than stepping back: a panel snapping to full
            // brightness the instant the last blow lands pulls the eye at exactly the wrong time.
            // Only that return is slow — opening the panel is always quick.
            if (_yieldedToFight && goal > _group.alpha && !_hidden) speed *= 0.25f;
            float a = Mathf.MoveTowards(_group.alpha, goal, dt * speed);
            if (!Mathf.Approximately(a, _group.alpha)) _group.alpha = a;
            if (Mathf.Approximately(a, goal) && _combatT <= 0f) _yieldedToFight = false;
        }

        // -- Dock and drag ----------------------------------------------------------------

        private void MoveBy(Vector2 screenDelta)
        {
            float root = _rootScale > 0f ? _rootScale : 1f;
            _dockRight -= screenDelta.x / root;
            _dockBottom += screenDelta.y / root;
            ApplyDock();
        }

        private void SaveDock()
        {
            PlayerPrefs.SetFloat(PrefDockRight, _dockRight);
            PlayerPrefs.SetFloat(PrefDockBottom, _dockBottom);
            PlayerPrefs.Save();
        }

        /// <summary>Moves the panel back to its default corner. For the console and the tests.</summary>
        public void ResetDock()
        {
            _dockRight = _style.dockRight;
            _dockBottom = _style.dockBottom;
            ApplyDock();
            SaveDock();
        }

        /// <summary>
        /// Places the window from its dock offsets: clamped inside the canvas, then rounded so
        /// its corner lands on a whole SCREEN pixel — every texel inside follows from there.
        /// </summary>
        private void ApplyDock()
        {
            if (_window == null || _canvas == null) return;
            float root = _rootScale > 0f ? _rootScale : 1f;
            var canvasRt = (RectTransform)_canvas.transform;
            float cw = canvasRt.rect.width > 0f ? canvasRt.rect.width : Screen.width / root;
            float ch = canvasRt.rect.height > 0f ? canvasRt.rect.height : Screen.height / root;
            float ww = _window.sizeDelta.x, wh = _window.sizeDelta.y;
            _dockRight = Mathf.Clamp(_dockRight, 0f, Mathf.Max(0f, cw - ww));
            _dockBottom = Mathf.Clamp(_dockBottom, 0f, Mathf.Max(0f, ch - wh));
            float right = Mathf.Round(_dockRight * root) / root;
            float bottom = Mathf.Round(_dockBottom * root) / root;
            _window.anchoredPosition = new Vector2(-right, bottom);
        }

        // -- Tray --------------------------------------------------------------------------

        private void RegisterTrayButton()
        {
            var bar = HUDIconBar.Instance != null ? HUDIconBar.Instance : FindObjectOfType<HUDIconBar>();
            if (bar == null || _style == null) return;
            // order 2 keeps inventory (0), spells (1), music (2) left to right.
            bar.Register(TrayButtonId, _style.trayIcon, Toggle, order: 2);
            _trayRegistered = true;
        }

        private void UnregisterTrayButton()
        {
            var bar = HUDIconBar.Instance != null ? HUDIconBar.Instance : FindObjectOfType<HUDIconBar>();
            if (bar != null) bar.Unregister(TrayButtonId);
            _trayRegistered = false;
        }

        // -- The player: yielding to a fight, going grey with the spirit --------------------

        private void TickPlayer(float dt)
        {
            if (_playerHealth == null)
            {
                _playerSearchT -= dt;
                if (_playerSearchT <= 0f)
                {
                    _playerSearchT = 1f;
                    var player = GameObject.FindWithTag("Player");
                    if (player != null)
                    {
                        _playerHealth = player.GetComponent<Health>();
                        if (_playerHealth != null) _playerHealth.OnDamaged += OnPlayerDamaged;
                    }
                }
            }
            if (_combatT > 0f) _combatT -= dt;

            // A spirit's panel goes cold with the world, exactly like the player panel.
            bool dead = _playerHealth != null && _playerHealth.IsDead;
            float spirit = Mathf.MoveTowards(_spiritT, dead ? 1f : 0f, dt * 2f);
            if (!Mathf.Approximately(spirit, _spiritT))
            {
                _spiritT = spirit;
                var c = Color.Lerp(Color.white, _theme.spiritStone, _spiritT);
                if (_stone != null && _stone.color != c) _stone.color = c;
            }
        }

        private void OnPlayerDamaged(int amount)
        {
            if (amount <= 0) return;
            _combatT = _style.combatHoldSeconds;
        }

        private void UnbindPlayer()
        {
            if (_playerHealth != null) _playerHealth.OnDamaged -= OnPlayerDamaged;
            _playerHealth = null;
        }

        /// <summary>Marks the player as being hit, as a blow would. For the tests.</summary>
        public void NotePlayerHit() => _combatT = _style.combatHoldSeconds;

        /// <summary>How far the panel has gone grey, 0..1. For the tests.</summary>
        public float SpiritAmount => _spiritT;
    }
}
