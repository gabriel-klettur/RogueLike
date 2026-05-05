using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core.Input;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Press-to-start state ─────────────────────────────────────────────
        private bool _pressToStartActive = true;
        private GameObject _pressToStartOverlay;
        private TextMeshProUGUI _pressToStartText;
        private float _blinkTimer;
        private bool _blinkVisible = true;
        private const float BLINK_INTERVAL = 0.85f;
        private const string PRESS_TEXT = "Press to start";

        // ── Build ────────────────────────────────────────────────────────────

        private void BuildPressToStartOverlay(Transform canvas)
        {
            _pressToStartOverlay = CreateUIObject("PressToStart", canvas);
            StretchFull(_pressToStartOverlay);

            var txtGo = CreateUIObject("PressText", _pressToStartOverlay.transform);
            var rt = txtGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -60f);
            rt.sizeDelta = new Vector2(800f, 60f);

            _pressToStartText = txtGo.AddComponent<TextMeshProUGUI>();
            _pressToStartText.text = PRESS_TEXT;
            _pressToStartText.fontSize = 36f;
            _pressToStartText.alignment = TextAlignmentOptions.Center;
            _pressToStartText.color = new Color(1f, 0.86f, 0f, 1f); // gold
            _pressToStartText.fontStyle = FontStyles.Bold;

            _pressToStartActive = true;
            _blinkTimer = 0f;
            _blinkVisible = true;

            // Hide the menu panel until the player acknowledges the press-to-start screen
            if (_menuPanelGo != null) _menuPanelGo.SetActive(false);
        }

        // ── Update (called before menu input) ────────────────────────────────

        private bool HandlePressToStart()
        {
            if (!_pressToStartActive) return false;

            // Blink
            _blinkTimer += Time.unscaledDeltaTime;
            if (_blinkTimer >= BLINK_INTERVAL)
            {
                _blinkTimer = 0f;
                _blinkVisible = !_blinkVisible;
                if (_pressToStartText != null)
                    _pressToStartText.enabled = _blinkVisible;
            }

            // Any key/click dismisses. KeyboardInputManager.WasAnyKeyPressedThisFrame
            // and MouseInputManager.WasLeftMouseButtonPressedThisFrame each fold the
            // OR-of-new-and-legacy fallback internally — single source of truth.
            bool dismiss = KeyboardInputManager.WasAnyKeyPressedThisFrame()
                        || MouseInputManager.WasLeftMouseButtonPressedThisFrame();

            if (dismiss)
            {
                Debug.Log("[PressToStart] Dismiss triggered.");
                _pressToStartActive = false;
                if (_pressToStartOverlay != null)
                    _pressToStartOverlay.SetActive(false);
                if (_menuPanelGo != null)
                {
                    _menuPanelGo.SetActive(true);
                    UpdateSelection();
                }
                return false;
            }

            return true; // Still in press-to-start mode
        }
    }
}
