using System.Collections;
using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.UI.MainMenu.Kit;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options → Video.
    ///
    /// <para><b>What the shipped panel got right and is kept.</b> Changes are STAGED and only
    /// committed on Apply, the resolution list is curated to exactly 2:1 because
    /// <c>SnapOrthoSize</c> only guarantees whole screen pixels per texel when the viewport is
    /// exactly that ratio, and the status line reports the LIVE viewport instead of echoing back
    /// the setting the player just picked. All three are unusual and all three are right.</para>
    ///
    /// <para><b>What it was missing.</b> Three rows: no vertical sync, no frame cap, no
    /// interface size — and, worse, no way back. Applying a display mode the monitor cannot show
    /// left the player looking at nothing with no way to undo it. The revert countdown is the
    /// standard answer and the one this panel most needed.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private enum VideoRow { Resolution, DisplayMode, VSync, FrameCap, UiScale, Apply }

        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly VideoRow[] VideoRows =
        {
            VideoRow.Resolution, VideoRow.DisplayMode, VideoRow.VSync,
            VideoRow.FrameCap, VideoRow.UiScale, VideoRow.Apply,
        };

        /// <summary>0 means no cap, and it is first because it is the default.</summary>
        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly int[] FrameCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };

        /// <summary>0 means "follow the resolution".</summary>
        [SelfHealingStatic("Immutable table built once from literals. Nothing writes to it after the static initialiser, it holds no Unity object and no subscription, so it cannot carry a destroyed reference or a session decision across Play.")]
        private static readonly float[] UiScales = { 0f, 0.85f, 1f, 1.15f, 1.35f, 1.6f };

        private MenuPanelView _videoPanel;
        private MenuList _videoList;
        private TextMeshProUGUI _videoStatus;
        private TextMeshProUGUI _videoCountdown;

        private int _videoResIndex;
        private int _videoModeIndex;
        private int _videoVSync;
        private int _videoFrameCapIndex;
        private int _videoUiScaleIndex;

        private Coroutine _videoRevert;
        private GameSettings _videoRollback;

        private void BuildVideoPanel(Transform canvas)
        {
            var style = Style;
            _videoPanel = new MenuPanelView(canvas, _art, style, MenuText.VideoTitle,
                                            style.panelWidth, ReduceMotion);
            _videoList = new MenuList(_videoPanel.Body, _art, style, ReduceMotion);

            for (int i = 0; i < VideoRows.Length; i++)
            {
                var row = _videoList.Add(_art, VideoRowLabel(VideoRows[i]));
                row.Value.text = string.Empty;
                if (VideoRows[i] != VideoRow.Apply)
                {
                    int captured = i;
                    // Arrows, so the mouse can do exactly what the keyboard can. A row that can
                    // only be changed with the arrow keys is a row half the players cannot use.
                    AddVideoArrow(row, left: true, () => ChangeVideo(captured, -1));
                    AddVideoArrow(row, left: false, () => ChangeVideo(captured, +1));
                }
            }

            _videoList.Changed += _ => _sfx?.Move();
            _videoList.Chosen += OnVideoRowChosen;

            float extra = 64f;
            _videoPanel.FitToContent(_videoList.ContentHeight, extra);
            _videoList.SetViewport(_videoPanel.BodyHeight);

            var statusRt = MenuUIKit.Rect("Status", _videoPanel.Root);
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(1f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.anchoredPosition = new Vector2(0f, style.hintBarHeight + 2f);
            statusRt.sizeDelta = new Vector2(-24f, 40f);
            _videoStatus = MenuTypography.Label(statusRt.gameObject, style, string.Empty,
                                                style.detailFontSize, style.TextMuted,
                                                TextAlignmentOptions.Center);
            _videoStatus.enableWordWrapping = true;

            var cdRt = MenuUIKit.Rect("Countdown", _videoPanel.Root);
            cdRt.anchorMin = new Vector2(0f, 0f);
            cdRt.anchorMax = new Vector2(1f, 0f);
            cdRt.pivot = new Vector2(0.5f, 0f);
            cdRt.anchoredPosition = new Vector2(0f, style.hintBarHeight + 44f);
            cdRt.sizeDelta = new Vector2(-24f, 26f);
            _videoCountdown = MenuTypography.Label(cdRt.gameObject, style, string.Empty,
                                                   style.detailFontSize + 1f, style.Gold,
                                                   TextAlignmentOptions.Center, bold: true);
            _videoCountdown.enabled = false;

            _videoPanel.SetHint(MenuText.VideoHint);
            _videoPanel.Close();

            LoadVideoFromSettings();
            RefreshVideoRows();
        }

        private void AddVideoArrow(MenuRow row, bool left, UnityEngine.Events.UnityAction onClick)
        {
            var sprite = left ? _art.ArrowLeft : _art.ArrowRight;
            var img = MenuUIKit.Sprite(left ? "Left" : "Right", row.Content, sprite,
                                       Style.TextDim, UnityEngine.UI.Image.Type.Simple, raycast: true);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(left ? 0.06f : 0.94f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(18f, 20f);
            rt.anchoredPosition = Vector2.zero;
            var btn = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colours = btn.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
            colours.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = colours;
            btn.onClick.AddListener(onClick);
        }

        private static string VideoRowLabel(VideoRow row)
        {
            switch (row)
            {
                case VideoRow.Resolution: return MenuText.VideoResolution;
                case VideoRow.DisplayMode: return MenuText.VideoDisplayMode;
                case VideoRow.VSync: return MenuText.VideoVSync;
                case VideoRow.FrameCap: return MenuText.VideoFrameCap;
                case VideoRow.UiScale: return MenuText.VideoUiScale;
                default: return MenuText.VideoApply;
            }
        }

        private void LoadVideoFromSettings()
        {
            var gs = GameSettings.Instance;
            if (gs == null) return;
            // Reached from ShowMenuScreen before the panel may exist; the staged indices are
            // plain ints and are perfectly happy to be loaded early.
            _videoResIndex = DisplaySettings.ClampIndex(
                DisplaySettings.IndexOf(gs.resolutionWidth, gs.resolutionHeight));
            _videoModeIndex = Mathf.Clamp((int)gs.windowMode, 0,
                                          DisplaySettings.WindowModeLabels.Length - 1);
            _videoVSync = Mathf.Clamp(gs.vSyncCount, 0, 2);
            _videoFrameCapIndex = Mathf.Max(0, System.Array.IndexOf(FrameCaps, gs.frameRateCap));
            _videoUiScaleIndex = Mathf.Max(0, ClosestUiScaleIndex(gs.uiScale));
        }

        private static int ClosestUiScaleIndex(float value)
        {
            int best = 0;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < UiScales.Length; i++)
            {
                float d = Mathf.Abs(UiScales[i] - value);
                if (d < bestDelta) { bestDelta = d; best = i; }
            }
            return best;
        }

        private void ChangeVideo(int rowIndex, int dir)
        {
            if (rowIndex < 0 || rowIndex >= VideoRows.Length) return;
            switch (VideoRows[rowIndex])
            {
                case VideoRow.Resolution:
                    _videoResIndex = Wrap(_videoResIndex + dir, DisplaySettings.Presets.Length);
                    break;
                case VideoRow.DisplayMode:
                    _videoModeIndex = Wrap(_videoModeIndex + dir, DisplaySettings.WindowModeLabels.Length);
                    break;
                case VideoRow.VSync:
                    _videoVSync = Wrap(_videoVSync + dir, 3);
                    break;
                case VideoRow.FrameCap:
                    _videoFrameCapIndex = Wrap(_videoFrameCapIndex + dir, FrameCaps.Length);
                    break;
                case VideoRow.UiScale:
                    _videoUiScaleIndex = Wrap(_videoUiScaleIndex + dir, UiScales.Length);
                    break;
                default:
                    return;
            }
            _videoList.Index = rowIndex;
            _sfx?.Move();
            RefreshVideoRows();
        }

        private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;

        private void OnVideoRowChosen(int index)
        {
            if (index >= 0 && index < VideoRows.Length && VideoRows[index] == VideoRow.Apply)
            {
                ApplyVideo();
                return;
            }
            ChangeVideo(index, +1);
        }

        private void RefreshVideoRows()
        {
            if (_videoList == null) return;
            var rows = _videoList.Rows;
            for (int i = 0; i < VideoRows.Length && i < rows.Count; i++)
            {
                string text;
                switch (VideoRows[i])
                {
                    case VideoRow.Resolution:
                        text = DisplaySettings.Presets[DisplaySettings.ClampIndex(_videoResIndex)].Label;
                        break;
                    case VideoRow.DisplayMode:
                        text = DisplaySettings.WindowModeLabel((WindowMode)_videoModeIndex);
                        break;
                    case VideoRow.VSync:
                        text = _videoVSync == 0 ? MenuText.VideoOff
                             : _videoVSync == 1 ? MenuText.VideoOn : MenuText.VideoOn + " (1/2)";
                        break;
                    case VideoRow.FrameCap:
                        text = FrameCaps[_videoFrameCapIndex] == 0
                            ? MenuText.VideoUnlimited
                            : FrameCaps[_videoFrameCapIndex] + " fps";
                        break;
                    case VideoRow.UiScale:
                        text = UiScales[_videoUiScaleIndex] <= 0f
                            ? MenuText.VideoUiScaleAuto
                            : Mathf.RoundToInt(UiScales[_videoUiScaleIndex] * 100f) + " %";
                        break;
                    default:
                        text = MenuText.VideoApply;
                        break;
                }
                rows[i].Value.text = text;
            }

            // The frame cap does nothing while vSync is on — Unity ignores targetFrameRate
            // entirely — so the row says so instead of offering a control that is inert.
            int capRow = System.Array.IndexOf(VideoRows, VideoRow.FrameCap);
            if (capRow >= 0 && capRow < rows.Count)
                rows[capRow].Interactable = _videoVSync == 0;

            RefreshVideoStatus();
        }

        private void RefreshVideoStatus()
        {
            if (_videoStatus == null) return;
            var cam = Camera.main;
            int vw = cam != null ? cam.pixelWidth : Screen.width;
            int vh = cam != null ? cam.pixelHeight : Screen.height;
            bool exact = vh > 0 && vw == Mathf.RoundToInt(vh * DisplaySettings.TargetAspect);
            string text = MenuText.VideoViewport(Screen.width, Screen.height, vw, vh, exact);
            if (Application.isEditor) text += "\n" + MenuText.VideoEditorNote;
            _videoStatus.text = text;
        }

        private void ApplyVideo()
        {
            var gs = GameSettings.Instance;
            if (gs == null) return;

            // Everything that can strand the player is captured BEFORE it is changed. A display
            // mode a monitor cannot show leaves nothing on screen to press.
            _videoRollback = new GameSettings
            {
                resolutionWidth = gs.resolutionWidth,
                resolutionHeight = gs.resolutionHeight,
                windowMode = gs.windowMode,
                vSyncCount = gs.vSyncCount,
                frameRateCap = gs.frameRateCap,
                uiScale = gs.uiScale,
            };

            var preset = DisplaySettings.Presets[DisplaySettings.ClampIndex(_videoResIndex)];
            gs.resolutionWidth = preset.Width;
            gs.resolutionHeight = preset.Height;
            gs.windowMode = (WindowMode)_videoModeIndex;
            gs.vSyncCount = _videoVSync;
            gs.frameRateCap = FrameCaps[_videoFrameCapIndex];
            gs.uiScale = UiScales[_videoUiScaleIndex];
            gs.Save();
            DisplaySettings.Apply(gs);
            gs.ApplyVideoSettings();
            RefreshVideoRows();

            if (_videoRevert != null) StopCoroutine(_videoRevert);
            _videoRevert = StartCoroutine(VideoRevertCountdown());
        }

        /// <summary>
        /// Fifteen seconds to confirm, then everything goes back. Unscaled time, because the
        /// menu does not run on the game clock and a paused one would hold the countdown at the
        /// moment the player most needs it to move.
        /// </summary>
        private IEnumerator VideoRevertCountdown()
        {
            const float window = 15f;
            float left = window;
            _videoCountdown.enabled = true;
            while (left > 0f)
            {
                _videoCountdown.text = MenuText.VideoKeepQuestion(Mathf.CeilToInt(left));
                if (InputCompat.ConfirmPressed())
                {
                    _videoCountdown.enabled = false;
                    _videoRevert = null;
                    _videoRollback = null;
                    _sfx?.Confirm();
                    yield break;
                }
                left -= Time.unscaledDeltaTime;
                yield return null;
            }

            RevertVideo();
        }

        private void RevertVideo()
        {
            var gs = GameSettings.Instance;
            if (gs != null && _videoRollback != null)
            {
                gs.resolutionWidth = _videoRollback.resolutionWidth;
                gs.resolutionHeight = _videoRollback.resolutionHeight;
                gs.windowMode = _videoRollback.windowMode;
                gs.vSyncCount = _videoRollback.vSyncCount;
                gs.frameRateCap = _videoRollback.frameRateCap;
                gs.uiScale = _videoRollback.uiScale;
                gs.Save();
                DisplaySettings.Apply(gs);
                gs.ApplyVideoSettings();
            }
            _videoRollback = null;
            _videoRevert = null;
            LoadVideoFromSettings();
            RefreshVideoRows();
            if (_videoCountdown != null)
            {
                _videoCountdown.text = MenuText.VideoReverted;
                _videoCountdown.enabled = true;
            }
            _sfx?.Cancel();
        }

        private void HandleVideoInput()
        {
            // While the countdown is up, Enter means KEEP and is consumed there; Esc reverts at
            // once rather than waiting the window out.
            if (_videoRevert != null && InputCompat.CancelPressed())
            {
                StopCoroutine(_videoRevert);
                RevertVideo();
                return;
            }
            if (InputCompat.NavLeftPressed()) { ChangeVideo(_videoList.Index, -1); return; }
            if (InputCompat.NavRightPressed()) { ChangeVideo(_videoList.Index, +1); return; }
            if (_videoRevert != null && InputCompat.ConfirmPressed()) return;
            HandleListInput(_videoList, OptionsGoBack);
        }
    }
}
