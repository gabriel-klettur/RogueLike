using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The War / Peace chip, top-left, directly under <see cref="DayNightClockHUD"/> and above
    /// the spell-cooldown stack. It reports <see cref="PlayerStance"/> and flips it on click.
    ///
    /// <para>IT IS THE INDICATOR, NOT THE CONTROL. Nothing auto-switches, so being jumped in
    /// Peace means the player cannot answer until the stance flips — and a mouse trip up here
    /// mid-fight is a death. Tab is the control (<c>PlayerStanceToggle</c>); this exists so the
    /// stance is never a mode the player is in without being told, which is the failure every
    /// modal control has. The click is a convenience for calm moments.</para>
    ///
    /// <para>It lives in the top-left COLUMN rather than over the target panel, which is
    /// anchored at y = -15 with nothing above it and an alpha of 0 whenever there is no target
    /// — a chip pinned to it would spend most of the session floating over empty screen. Here
    /// it sits with the other permanent read-outs, always visible, and
    /// <c>HUDVisibilityController</c> hides it with the rest when an editor opens.</para>
    ///
    /// <para>Unlike the clock this canvas DOES carry a <see cref="GraphicRaycaster"/>, and that
    /// is also what stops the click reaching the world: <c>PollCombatActions</c> opens with
    /// <c>IsPointerOverInteractiveUI()</c>, so a raycastable graphic here swallows the press
    /// instead of casting the primary spell through it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StanceHUD : MonoBehaviour
    {
        // Geometry — the left column, sized off DayNightClockHUD so the two edges line up.
        // The clock occupies y from -24 to -178 (MARGIN_TOP 24 + WIDGET_TOTAL_H 154); the
        // cooldown stack was moved down to make this gap.
        private const float MARGIN_LEFT = 24f;
        private const float TOP         = -186f;
        private const float WIDTH       = 110f;
        private const float HEIGHT      = 26f;
        private const float ACCENT_W    = 4f;

        private static readonly Color BG         = new Color(0.04f, 0.05f, 0.08f, 0.65f);
        private static readonly Color WAR_TINT   = new Color(0.95f, 0.42f, 0.32f, 1f);
        private static readonly Color PEACE_TINT = new Color(0.45f, 0.85f, 0.55f, 1f);
        private static readonly Color HINT_TINT  = new Color(0.72f, 0.76f, 0.84f, 0.65f);

        private Image _accent;
        private TextMeshProUGUI _label;

        private void Start()
        {
            BuildUI();
            Refresh(PlayerStance.Current);
        }

        private void OnEnable()  => PlayerStance.OnChanged += Refresh;
        private void OnDisable() => PlayerStance.OnChanged -= Refresh;

        /// <summary>
        /// Subscribed in <see cref="OnEnable"/>, which runs BEFORE <see cref="Start"/>, so the
        /// handles can still be null on the first call. Guarded rather than reordered: a
        /// stance flipped between enable and the first frame is a real sequence, and the
        /// <see cref="Start"/> call above catches up with whatever it landed on.
        /// </summary>
        private void Refresh(Stance stance)
        {
            bool peace = stance == Stance.Peace;
            var tint = peace ? PEACE_TINT : WAR_TINT;

            if (_accent != null) _accent.color = tint;
            if (_label != null)
            {
                _label.text  = peace ? "PAZ" : "GUERRA";
                _label.color = tint;
            }
        }

        // ── UI build ─────────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("StanceCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 105;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var root = NewUI("Root", canvasGo.transform);
            root.anchorMin        = new Vector2(0f, 1f);
            root.anchorMax        = new Vector2(0f, 1f);
            root.pivot            = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(MARGIN_LEFT, TOP);
            root.sizeDelta        = new Vector2(WIDTH, HEIGHT);

            var bg = AddImage(root, "Bg");
            Stretch(bg.rectTransform);
            bg.color = BG;
            bg.raycastTarget = true;

            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(PlayerStance.Toggle);

            _accent = AddImage(root, "Accent");
            var accentRt = _accent.rectTransform;
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot     = new Vector2(0f, 0.5f);
            accentRt.offsetMin = Vector2.zero;
            accentRt.offsetMax = new Vector2(ACCENT_W, 0f);

            _label = AddText(root, "Label", 14f, TextAlignmentOptions.Left);
            Stretch(_label.rectTransform);
            _label.rectTransform.offsetMin = new Vector2(ACCENT_W + 6f, 0f);
            _label.rectTransform.offsetMax = new Vector2(-34f, 0f);

            var hint = AddText(root, "KeyHint", 11f, TextAlignmentOptions.Right);
            Stretch(hint.rectTransform);
            hint.rectTransform.offsetMax = new Vector2(-7f, 0f);
            hint.text  = "TAB";
            hint.color = HINT_TINT;
        }

        private static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(Transform parent, string name)
        {
            var img = NewUI(name, parent).gameObject.AddComponent<Image>();
            img.sprite = WhitePixel();
            img.type   = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI AddText(Transform parent, string name, float size,
                                               TextAlignmentOptions align)
        {
            var tmp = NewUI(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.fontSize      = size;
            tmp.alignment     = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return tmp;
        }

        // ── Shared sprite ────────────────────────────────────────────────────

        private static Sprite _whitePixel;

        /// <summary>
        /// Domain Reload is off, so a cached sprite outlives its own Texture2D and comes back
        /// next session pointing at a destroyed object. Assigned with a plain <c>null</c>
        /// because that is the only form the static-reset ratchet can see in IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => _whitePixel = null;

        private static Sprite WhitePixel()
        {
            if (_whitePixel != null) return _whitePixel;
            var tex = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            _whitePixel = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _whitePixel;
        }
    }
}
