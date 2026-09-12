using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The threshold screen: the title gathers, one line invites a key, everything else waits.
    ///
    /// <para><b>What was wrong with it, measured.</b> The blink was
    /// <c>_pressToStartText.enabled = !visible</c> every 0.85 s — not a pulse, a switch: the only
    /// call to action on the title screen was ABSENT for 850 ms at a time, and the first capture
    /// taken for the audit caught it off and showed a title screen with nothing to do. When it
    /// was on, it was bare gold TMP over the carousel's painting and measured <b>2.13 : 1</b>
    /// against the barbarian's skin, which fails every contrast threshold including the one for
    /// large text.</para>
    ///
    /// <para><b>Both halves are fixed by the same change.</b> The line breathes on ALPHA between
    /// 0.45 and 1 instead of switching, so it is never gone, and it sits on a soft dark plate so
    /// it is legible over any frame the carousel can show. Under reduce motion it simply stays
    /// at full.</para>
    ///
    /// <para><b>The footer hints are hidden here.</b> They describe how to navigate a menu the
    /// player cannot see yet, and a screen with instructions at the bottom is not a
    /// threshold.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private const float BREATH_SECONDS = 1.9f;
        private const float BREATH_FLOOR = 0.45f;

        private bool _pressToStartActive = true;
        private GameObject _pressToStartOverlay;
        private TextMeshProUGUI _pressToStartText;
        private Image _pressToStartPlate;
        private float _breathTimer;

        /// <summary>True while the threshold screen is up. Read by the fixtures.</summary>
        private bool PressToStartActive => _pressToStartActive;

        private void BuildPressToStartOverlay(Transform canvas)
        {
            var style = Style;
            _pressToStartOverlay = MenuUIKit.Rect("PressToStart", canvas).gameObject;
            StretchFull(_pressToStartOverlay);

            var plateRt = MenuUIKit.Rect("Plate", _pressToStartOverlay.transform);
            plateRt.anchorMin = plateRt.anchorMax = new Vector2(0.5f, 0.5f);
            plateRt.pivot = new Vector2(0.5f, 0.5f);
            plateRt.anchoredPosition = new Vector2(0f, -92f);
            plateRt.sizeDelta = new Vector2(560f, 120f);
            _pressToStartPlate = plateRt.gameObject.AddComponent<Image>();
            // SoftPlate, not MoteGlow: the atlas is POINT-filtered, so a 9 px radial stretched
            // fifty times came out as a hard grey rectangle — measured on the first capture.
            _pressToStartPlate.sprite = _art.SoftPlate;
            _pressToStartPlate.type = Image.Type.Simple;
            _pressToStartPlate.color = new Color(0f, 0f, 0f, 0.55f);
            _pressToStartPlate.raycastTarget = false;

            var textRt = MenuUIKit.Rect("PressText", _pressToStartOverlay.transform);
            textRt.anchorMin = textRt.anchorMax = new Vector2(0.5f, 0.5f);
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.anchoredPosition = new Vector2(0f, -92f);
            textRt.sizeDelta = new Vector2(820f, 54f);
            _pressToStartText = MenuTypography.Label(textRt.gameObject, style, MenuText.PressToStart,
                                                     style.rowFontSize + 8f, style.Gold,
                                                     TextAlignmentOptions.Center, bold: true);
            _pressToStartText.characterSpacing = 10f;

            _pressToStartActive = true;
            _breathTimer = 0f;

            if (_menuPanelGo != null) _menuPanelGo.SetActive(false);
            if (_footerHint != null) _footerHint.enabled = false;
            if (_footerVersion != null) _footerVersion.enabled = false;
        }

        /// <summary>
        /// Returns true while the threshold is still up, so <c>Update</c> can stop there.
        /// </summary>
        /// <summary>
        /// How long the threshold refuses to be crossed. Not a delay for its own sake: a key
        /// still held from whatever opened the game, or an event the InputSystem delivers on the
        /// first frame, would cross it before the title had drawn once.
        /// </summary>
        private const float THRESHOLD_ARM_SECONDS = 0.35f;

        private bool HandlePressToStart()
        {
            if (!_pressToStartActive) return false;

            _breathTimer += Time.unscaledDeltaTime;
            if (!ReduceMotion)
            {
                float phase = Mathf.Sin(_breathTimer / BREATH_SECONDS * Mathf.PI * 2f) * 0.5f + 0.5f;
                float a = Mathf.Lerp(BREATH_FLOOR, 1f, phase);
                if (_pressToStartText != null)
                {
                    var c = _pressToStartText.color;
                    c.a = a;
                    _pressToStartText.color = c;
                }
                if (_pressToStartPlate != null)
                {
                    var c = _pressToStartPlate.color;
                    c.a = Mathf.Lerp(0.42f, 0.62f, phase);
                    _pressToStartPlate.color = c;
                }
            }

            // Any key or a click. Both helpers fold the OR of the new InputSystem and the legacy
            // backend internally, so the threshold is passable even when one pipeline is dropping
            // events — which is the state a player is in when they most need to get past it.
            bool dismiss = _breathTimer >= THRESHOLD_ARM_SECONDS
                        && (KeyboardInputManager.WasAnyKeyPressedThisFrame()
                            || MouseInputManager.WasLeftMouseButtonPressedThisFrame());

            // TRUE for the frame the threshold is crossed, and that return value is the whole
            // point. Returning false here let Update fall through to the menu's own navigation in
            // the SAME frame, where InputCompat.ConfirmPressed() saw the very key that had just
            // dismissed the screen — so pressing Enter or Space at the title did not open the
            // menu, it chose the first row of it. With "Continue" at the top, that loaded a save
            // and left the player in the world without ever having seen the menu. Caught live:
            // a Play session that should have stopped at MainMenu was in MainGameplay 17.9 s in.
            if (dismiss) { DismissPressToStart(silent: false); return true; }
            return true;
        }

        /// <summary>
        /// Leaves the threshold. <paramref name="silent"/> is for the paths that are not the
        /// player crossing it — a language change rebuilds the canvas and must not put them back
        /// behind a screen they already passed, nor play the sound of passing it again.
        /// </summary>
        private void DismissPressToStart(bool silent)
        {
            _pressToStartActive = false;
            if (_pressToStartOverlay != null) _pressToStartOverlay.SetActive(false);
            if (_menuPanelGo != null) _menuPanelGo.SetActive(_menuScreen == MenuScreen.Main);
            if (_footerVersion != null) _footerVersion.enabled = true;
            ApplyHintVisibility();
            UpdateSelection();

            if (silent) return;
            _sfx?.Confirm();
            // A ring out of the middle of the screen, once. It is the only thing that happens on
            // this screen, so it is allowed to be the loudest beat in the menu.
            if (_fx != null && !ReduceMotion)
            {
                var rect = ((RectTransform)_fx.transform).rect;
                _fx.Burst(new Vector2(rect.width * 0.5f, rect.height * 0.5f - 92f), 22,
                          Style.Gold, 210f, 0.7f, MenuMoteShape.Spark);
            }
            _title?.Sweep();
        }
    }
}
