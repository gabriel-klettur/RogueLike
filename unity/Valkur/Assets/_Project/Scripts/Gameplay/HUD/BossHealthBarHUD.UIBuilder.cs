using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Procedural hierarchy for the boss bar. Built into the HUD canvas when the
    /// bar is hosted by <c>HUDManager</c>, or into a canvas of its own when the
    /// component stands alone (scripted encounters, tests).
    ///
    /// The rect deliberately matches the <c>TargetHUD</c> slot — top-centre,
    /// same offset from the screen edge — because the two are mutually
    /// exclusive: the boss owns that space whenever it is on screen.
    /// </summary>
    public sealed partial class BossHealthBarHUD : SingletonMonoBehaviour<BossHealthBarHUD>
    {
        // ── Slot shared with TargetHUD ────────────────────────────────────
        private const float PanelWidth  = 460f;
        private const float PanelHeight = 68f;
        private const float PanelTopGap = 15f;

        private const float PadX          = 12f;
        private const float PadY          = 8f;
        private const float HeaderHeight  = 22f;
        private const float HeaderBarGap  = 6f;
        private const float HpBarHeight   = 24f;
        private const float PipSize       = 8f;
        private const float PipGap        = 4f;
        private const float EdgeOutset    = 2f;

        private static readonly Color PanelColor  = new Color(0.05f, 0.04f, 0.05f, 0.86f);
        private static readonly Color EdgeColor   = new Color(0.62f, 0.13f, 0.15f, 0.55f);
        private static readonly Color TrackColor  = new Color(0.14f, 0.10f, 0.11f, 0.95f);
        private static readonly Color GhostColor  = new Color(1f, 0.86f, 0.80f, 0.45f);
        private static readonly Color FillColor   = new Color(0.85f, 0.16f, 0.18f, 1f);
        private static readonly Color PhaseColor  = new Color(1f, 0.78f, 0.45f, 1f);
        private static readonly Color PipOnColor  = new Color(1f, 0.80f, 0.42f, 0.95f);
        private static readonly Color PipOffColor = new Color(1f, 1f, 1f, 0.18f);

        private GameObject      _root;
        private Image           _fillImage;
        private Image           _ghostImage;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _phaseText;
        private TextMeshProUGUI _hpText;
        private RectTransform   _pipsRoot;
        private Image[]         _pipImages;

        private static Sprite _whiteSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpriteCacheOnPlayModeEnter() => _whiteSprite = null;

        /// <summary>
        /// Build the hierarchy once. Idempotent — a second call is a no-op, and
        /// it never adds a second Canvas.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_root != null) return;

            _root = NewChild("BossHealthBarHUD_Root", transform);
            var rootRt = _root.GetComponent<RectTransform>();

            // Hosted inside an existing HUD canvas when there is one; otherwise
            // the bar carries its own so it works as a standalone object too.
            if (GetComponentInParent<Canvas>() == null)
            {
                Stretch(rootRt, 0f);
                var canvas = _root.AddComponent<Canvas>();
                canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
                // Above the HUD canvas (100): the boss bar outranks everything
                // sharing the top-centre slot.
                canvas.sortingOrder = 110;
                _root.AddComponent<CanvasScaler>().uiScaleMode =
                    CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _root.AddComponent<GraphicRaycaster>();
            }
            else
            {
                Stretch(rootRt, 0f);
            }

            BuildPanel(rootRt);
            _root.SetActive(false);
        }

        private void BuildPanel(RectTransform rootRt)
        {
            var edge = NewChild("Edge", rootRt);
            var edgeRt = edge.GetComponent<RectTransform>();
            PlaceInSlot(edgeRt, EdgeOutset);
            var edgeImg = edge.AddComponent<Image>();
            edgeImg.sprite        = WhiteSprite();
            edgeImg.color         = EdgeColor;
            edgeImg.raycastTarget = false;

            var panel = NewChild("Panel", rootRt);
            var panelRt = panel.GetComponent<RectTransform>();
            PlaceInSlot(panelRt, 0f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.sprite        = WhiteSprite();
            panelImg.color         = PanelColor;
            panelImg.raycastTarget = false;

            BuildHeader(panelRt);
            BuildHpBar(panelRt);
        }

        // Top-centre slot, identical to the one TargetHUD occupies.
        private static void PlaceInSlot(RectTransform rt, float outset)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(PanelWidth + outset * 2f, PanelHeight + outset * 2f);
            rt.anchoredPosition = new Vector2(0f, -PanelTopGap + outset);
        }

        private void BuildHeader(RectTransform panelRt)
        {
            // Phase label (left).
            var phaseGo = NewChild("Phase", panelRt);
            var phaseRt = phaseGo.GetComponent<RectTransform>();
            phaseRt.anchorMin = new Vector2(0f, 1f);
            phaseRt.anchorMax = new Vector2(0f, 1f);
            phaseRt.pivot     = new Vector2(0f, 1f);
            phaseRt.sizeDelta = new Vector2(130f, HeaderHeight);
            phaseRt.anchoredPosition = new Vector2(PadX, -PadY);

            _phaseText = phaseGo.AddComponent<TextMeshProUGUI>();
            _phaseText.fontSize         = 13f;
            _phaseText.fontStyle        = FontStyles.Bold;
            _phaseText.characterSpacing = 6f;
            _phaseText.alignment        = TextAlignmentOptions.MidlineLeft;
            _phaseText.enableWordWrapping = false;
            _phaseText.overflowMode     = TextOverflowModes.Ellipsis;
            _phaseText.color            = PhaseColor;
            _phaseText.raycastTarget    = false;
            _phaseText.text             = string.Empty;

            // Boss name (centre).
            var nameGo = NewChild("Name", panelRt);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot     = new Vector2(0.5f, 1f);
            nameRt.offsetMin = new Vector2(PadX + 130f, -(PadY + HeaderHeight));
            nameRt.offsetMax = new Vector2(-(PadX + 130f), -PadY);

            _nameText = nameGo.AddComponent<TextMeshProUGUI>();
            _nameText.fontSize         = 20f;
            _nameText.fontStyle        = FontStyles.Bold;
            _nameText.alignment        = TextAlignmentOptions.Midline;
            _nameText.enableWordWrapping = false;
            _nameText.overflowMode     = TextOverflowModes.Ellipsis;
            _nameText.color            = Color.white;
            _nameText.raycastTarget    = false;
            _nameText.text             = string.Empty;

            // Phase pips (right).
            var pipsGo = NewChild("Pips", panelRt);
            _pipsRoot = pipsGo.GetComponent<RectTransform>();
            _pipsRoot.anchorMin = new Vector2(1f, 1f);
            _pipsRoot.anchorMax = new Vector2(1f, 1f);
            _pipsRoot.pivot     = new Vector2(1f, 0.5f);
            _pipsRoot.sizeDelta = new Vector2(0f, PipSize);
            _pipsRoot.anchoredPosition = new Vector2(-PadX, -(PadY + HeaderHeight * 0.5f));
        }

        private void BuildHpBar(RectTransform panelRt)
        {
            var trackGo = NewChild("HpTrack", panelRt);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 1f);
            trackRt.anchorMax = new Vector2(1f, 1f);
            trackRt.pivot     = new Vector2(0.5f, 1f);
            trackRt.offsetMin = new Vector2(PadX, -(PadY + HeaderHeight + HeaderBarGap + HpBarHeight));
            trackRt.offsetMax = new Vector2(-PadX, -(PadY + HeaderHeight + HeaderBarGap));

            var track = trackGo.AddComponent<Image>();
            track.sprite        = WhiteSprite();
            track.color         = TrackColor;
            track.raycastTarget = false;

            _ghostImage = AddFill(trackRt, "HpGhost", GhostColor);
            _fillImage  = AddFill(trackRt, "HpFill", FillColor);

            // HP readout overlays the bar on its own GameObject — TMP and Image
            // must never share one (NRE in MaskableGraphic).
            var textGo = NewChild("HpText", trackRt);
            var textRt = textGo.GetComponent<RectTransform>();
            Stretch(textRt, 0f);

            _hpText = textGo.AddComponent<TextMeshProUGUI>();
            _hpText.fontSize      = 14f;
            _hpText.fontStyle     = FontStyles.Bold;
            _hpText.alignment     = TextAlignmentOptions.Midline;
            _hpText.color         = new Color(1f, 1f, 1f, 0.95f);
            _hpText.raycastTarget = false;
            _hpText.text          = string.Empty;
        }

        private static Image AddFill(RectTransform parent, string name, Color color)
        {
            var go = NewChild(name, parent);
            Stretch(go.GetComponent<RectTransform>(), 0f);

            var img = go.AddComponent<Image>();
            img.sprite        = WhiteSprite();
            img.color         = color;
            img.type          = Image.Type.Filled;
            img.fillMethod    = Image.FillMethod.Horizontal;
            img.fillOrigin    = (int)Image.OriginHorizontal.Left;
            img.fillAmount    = 1f;
            img.raycastTarget = false;
            return img;
        }

        // ── Painting ──────────────────────────────────────────────────────

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
            IsShowing = visible;
        }

        private void ApplyVisualState(BossPhaseController boss, Health health, float fill)
        {
            if (_fillImage != null) _fillImage.fillAmount = fill;

            if (_nameText != null)
                _nameText.text = boss.gameObject.name.Replace("(Clone)", string.Empty).Trim();

            if (_phaseText != null)
            {
                string label = boss.CurrentLabel;
                _phaseText.text = string.IsNullOrEmpty(label) ? string.Empty : label.ToUpperInvariant();
            }

            if (_hpText != null)
                _hpText.text = $"{Mathf.Max(0, health.CurrentHp)} / {health.MaxHp}";

            UpdatePhasePips(boss.CurrentPhase);
        }

        /// <summary>One pip per authored phase, rebuilt whenever a new boss binds.</summary>
        private void RebuildPhasePips()
        {
            if (_pipsRoot == null || _boundBoss == null) return;

            int wanted = Mathf.Max(0, _boundBoss.PhaseCount);

            if (_pipImages != null)
            {
                for (int i = 0; i < _pipImages.Length; i++)
                    if (_pipImages[i] != null) DestroySafely(_pipImages[i].gameObject);
            }

            _pipImages = new Image[wanted];
            float width = wanted <= 0 ? 0f : wanted * PipSize + (wanted - 1) * PipGap;
            _pipsRoot.sizeDelta = new Vector2(width, PipSize);

            for (int i = 0; i < wanted; i++)
            {
                var go = NewChild("Pip_" + i, _pipsRoot);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot     = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(PipSize, PipSize);
                rt.anchoredPosition = new Vector2(-(wanted - 1 - i) * (PipSize + PipGap), 0f);

                var img = go.AddComponent<Image>();
                img.sprite        = WhiteSprite();
                img.color         = PipOffColor;
                img.raycastTarget = false;
                _pipImages[i] = img;
            }
        }

        private void UpdatePhasePips(int currentPhase)
        {
            if (_pipImages == null) return;
            for (int i = 0; i < _pipImages.Length; i++)
            {
                if (_pipImages[i] == null) continue;
                _pipImages[i].color = i <= currentPhase ? PipOnColor : PipOffColor;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────


        private static void DestroySafely(Object obj)
        {
            if (obj == null) return;
            // EditMode tests rebuild these rows outside play mode, where
            // Object.Destroy is illegal.
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Stretch(RectTransform rt, float outset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-outset, -outset);
            rt.offsetMax = new Vector2(outset, outset);
        }

        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();

            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            _whiteSprite.name = "BossHealthBar_White";
            return _whiteSprite;
        }
    }
}
