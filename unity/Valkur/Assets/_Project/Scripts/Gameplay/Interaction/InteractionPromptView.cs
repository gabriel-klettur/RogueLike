using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Interaction
{
    /// <summary>
    /// The floating badge over whatever the player can act on: a key cap, what pressing it
    /// does, and — when it matters — why they cannot, or how long until they can.
    ///
    /// <para>ONE view, reused. The prompt is a property of the PLAYER (only one thing is ever
    /// being offered) and not of the interactable, so a forest of four hundred trees builds
    /// four hundred nothings instead of four hundred badges that are hidden 99.99% of the
    /// time. Same reason <c>BuildingHoverInteractor</c> keeps a single reusable
    /// <c>BuildingSilhouetteOutline</c> and moves it.</para>
    ///
    /// <para>It lives in world space rather than on a Canvas: a screen-space badge would have
    /// to be projected every frame and would not sit behind anything, and the thing it labels
    /// is in the world. Sorting is on <c>UI_World</c>, above the entities and below the screen
    /// HUD, which is what that layer is for.</para>
    ///
    /// <para>The badge is parented to the PLAYER and positioned in world coordinates, never
    /// parented to the target. Parenting to the target would inherit the building scale —
    /// buildings are authored at PPU 32 and freely resized per instance — so the same prompt
    /// would be twice the size over a big rock as over a sapling.</para>
    /// </summary>
    public sealed class InteractionPromptView : MonoBehaviour
    {
        /// <summary>Gap between the top of the target and the bottom of the badge.</summary>
        private const float VERTICAL_CLEARANCE = 0.30f;

        /// <summary>Height of one text row in world units, and of the square key cap.</summary>
        private const float ROW_HEIGHT = 0.34f;
        private const float KEY_SIZE = 0.30f;

        /// <summary>
        /// How much of a row's height the glyphs fill. Each label's transform scale is DERIVED
        /// from this and from the text's own measured height, never authored as a constant: a
        /// fixed scale beside a fixed font size is two numbers that have to agree about a third
        /// one (the font asset's metrics) that neither of them can see.
        ///
        /// <para>They did not agree. The label shipped at fontSize 2.6 with localScale 0.1 and
        /// a comment claiming that kept it "roughly the plate height" — measured live, the
        /// glyphs came out 0.025 world units tall against a 0.42 plate, which is 0.1% of screen
        /// height, or under one pixel on a 768-line window. The badge was drawing a plate with
        /// nothing legible on it, and every part of it reported success.</para>
        /// </summary>
        private const float VERB_HEIGHT_FRACTION = 0.52f;

        /// <summary>The second line is deliberately smaller: it is context, not the action.</summary>
        private const float DETAIL_HEIGHT_FRACTION = 0.38f;

        /// <summary>The glyph inside the key cap, as a fraction of the cap.</summary>
        private const float KEY_GLYPH_FRACTION = 0.52f;

        /// <summary>Padding inside the plate, and the gap between the cap and the verb.</summary>
        private const float PLATE_PADDING = 0.16f;
        private const float KEY_GAP = 0.12f;

        /// <summary>
        /// Seconds the badge takes to fade in or out. Short enough to feel immediate, long
        /// enough that walking along a row of trees does not strobe — an instant cut between
        /// targets is the single thing that makes a prompt read as cheap.
        /// </summary>
        private const float FADE_SECONDS = 0.11f;

        /// <summary>How far the badge rises as it fades in, in world units.</summary>
        private const float RISE_WORLD = 0.09f;

        private const string SORTING_LAYER = "UI_World";
        private const int ORDER_PLATE = 300;
        private const int ORDER_KEYCAP = 301;
        private const int ORDER_TEXT = 302;

        private static readonly Color PlateReady = new Color(0.06f, 0.06f, 0.09f, 0.86f);
        private static readonly Color PlateBlocked = new Color(0.08f, 0.06f, 0.06f, 0.80f);
        private static readonly Color KeyCapFill = new Color(0.97f, 0.83f, 0.28f, 1f);
        private static readonly Color KeyCapGlyph = new Color(0.08f, 0.07f, 0.04f, 1f);
        private static readonly Color VerbReady = new Color(0.97f, 0.97f, 0.98f, 1f);
        private static readonly Color VerbBlocked = new Color(0.70f, 0.66f, 0.66f, 1f);
        private static readonly Color DetailNeutral = new Color(0.72f, 0.75f, 0.82f, 1f);
        private static readonly Color DetailWarn = new Color(0.96f, 0.74f, 0.42f, 1f);

        private Transform _body;
        private SpriteRenderer _plate;
        private SpriteRenderer _keyCap;
        private TextMeshPro _keyGlyph;
        private TextMeshPro _verb;
        private TextMeshPro _detail;

        private string _shownVerb = string.Empty;
        private string _shownDetail = string.Empty;
        private string _shownKey = string.Empty;
        private InteractionAvailability _shownAvailability = InteractionAvailability.Hidden;

        private Vector3 _anchor;
        private float _opacity;
        private bool _wantVisible;

        /// <summary>True while the badge is drawn at all. A test seam.</summary>
        public bool IsVisible => _opacity > 0.001f;

        /// <summary>What the badge currently reads. Empty while hidden. A test seam.</summary>
        public string ShownLabel => IsVisible
            ? (string.IsNullOrEmpty(_shownDetail) ? _shownVerb : _shownVerb + " — " + _shownDetail)
            : string.Empty;

        /// <summary>How the badge is currently styled. A test seam.</summary>
        public InteractionAvailability ShownAvailability => _shownAvailability;

        /// <summary>Build the badge under <paramref name="owner"/>, hidden.</summary>
        public static InteractionPromptView Create(Transform owner)
        {
            var go = new GameObject("InteractionPrompt");
            if (owner != null) go.transform.SetParent(owner, worldPositionStays: false);

            var view = go.AddComponent<InteractionPromptView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        private void Build()
        {
            // A child holds the whole badge so the rise animation can move it without fighting
            // the world position written to the root every frame.
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, worldPositionStays: false);

            _plate = MakeSprite("Plate", PlateReady, ORDER_PLATE);
            _keyCap = MakeSprite("KeyCap", KeyCapFill, ORDER_KEYCAP);

            _keyGlyph = MakeText("KeyGlyph", KeyCapGlyph, TextAlignmentOptions.Center);
            _verb = MakeText("Verb", VerbReady, TextAlignmentOptions.Left);
            _detail = MakeText("Detail", DetailNeutral, TextAlignmentOptions.Left);
        }

        private SpriteRenderer MakeSprite(string name, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_body, worldPositionStays: false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldHealthBar.GetSharedPixelSprite();
            sr.sharedMaterial = WorldHealthBar.GetSharedSpriteMaterial();
            sr.color = color;
            sr.sortingLayerName = SORTING_LAYER;
            sr.sortingOrder = order;
            return sr;
        }

        private TextMeshPro MakeText(string name, Color color, TextAlignmentOptions alignment)
        {
            // Image and TMP on the SAME GameObject throw a NullReferenceException in this
            // project's uGUI paths; the world-space parts are kept split for the same reason
            // and because the plate has to be sized around text it cannot itself measure.
            var go = new GameObject(name);
            go.transform.SetParent(_body, worldPositionStays: false);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize = 3f;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            tmp.richText = false;
            tmp.color = color;

            var mesh = tmp.GetComponent<MeshRenderer>();
            if (mesh != null)
            {
                mesh.sortingLayerName = SORTING_LAYER;
                mesh.sortingOrder = ORDER_TEXT;
            }

            // Left at identity on purpose. Relayout derives the real scale from the measured
            // mesh, so nothing here has to guess at the font's metrics.
            go.transform.localScale = Vector3.one;
            return tmp;
        }

        /// <summary>
        /// Point the badge at a target and say what it should read. Called every frame the
        /// prompt is live, so it re-lays out only when the content actually changed.
        /// </summary>
        public void Show(Bounds targetBounds, InteractionPromptInfo info)
        {
            if (_plate == null) return;
            if (!info.IsVisible) { Hide(); return; }

            _wantVisible = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            string verb = info.Verb ?? string.Empty;
            string detail = info.Detail ?? string.Empty;
            string key = InteractKeyGlyph();

            // The key is re-read every frame because a rebind takes effect immediately, and
            // compared here so a rebind mid-prompt re-lays out rather than lying until the
            // player walks away and back.
            bool changed = _shownAvailability != info.Availability
                           || !string.Equals(_shownVerb, verb, System.StringComparison.Ordinal)
                           || !string.Equals(_shownDetail, detail, System.StringComparison.Ordinal)
                           || !string.Equals(_shownKey, key, System.StringComparison.Ordinal);

            if (changed)
            {
                _shownAvailability = info.Availability;
                _shownVerb = verb;
                _shownDetail = detail;
                _shownKey = key;
                Relayout();
            }

            _anchor = new Vector3(
                targetBounds.center.x,
                targetBounds.max.y + VERTICAL_CLEARANCE,
                0f);
        }

        public void Hide()
        {
            _wantVisible = false;
        }

        private void LateUpdate()
        {
            if (_plate == null) return;

            float target = _wantVisible ? 1f : 0f;

            // Unscaled: the prompt has to fade normally through a hit-stop, and the camera
            // beats this project fires freeze Time.timeScale for a few frames at a time.
            _opacity = FADE_SECONDS <= 0f
                ? target
                : Mathf.MoveTowards(_opacity, target, Time.unscaledDeltaTime / FADE_SECONDS);

            if (_opacity <= 0.001f)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }

            // The badge is parented to the player and therefore inherits the player's scale.
            // Entities are not authored at 1 — characters are normalised to a target body
            // height — so an unneutralised badge is a different size on the dwarf than on the
            // elf, and different again on a resized boss. Neutralising against the LIVE lossy
            // scale rather than caching it also survives anything that rescales the character
            // mid-session.
            var lossy = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(
                Mathf.Approximately(lossy.x, 0f) ? 1f : 1f / lossy.x,
                Mathf.Approximately(lossy.y, 0f) ? 1f : 1f / lossy.y,
                1f);

            transform.position = _anchor;

            // Rises as it appears and settles as it leaves, eased so the motion decelerates
            // into place instead of arriving at full speed.
            float eased = 1f - (1f - _opacity) * (1f - _opacity);
            _body.localPosition = new Vector3(0f, -RISE_WORLD * (1f - eased), 0f);

            ApplyOpacity(eased);
        }

        private void ApplyOpacity(float alpha)
        {
            SetSpriteAlpha(_plate, PlateFor(_shownAvailability), alpha);
            SetSpriteAlpha(_keyCap, KeyCapFill, alpha);

            if (_keyGlyph != null) _keyGlyph.alpha = alpha;
            if (_verb != null) _verb.alpha = alpha;
            if (_detail != null) _detail.alpha = alpha;
        }

        /// <summary>
        /// Fade against the layout colour rather than the renderer's CURRENT colour. Reading
        /// back what was written last frame and multiplying again is how a fade ends up
        /// exponential — it dims to nothing over a few frames and never recovers.
        /// </summary>
        private static void SetSpriteAlpha(SpriteRenderer sr, Color layoutColor, float alpha)
        {
            if (sr == null) return;
            sr.color = new Color(layoutColor.r, layoutColor.g, layoutColor.b,
                layoutColor.a * alpha);
        }

        private static Color PlateFor(InteractionAvailability availability) =>
            availability == InteractionAvailability.Blocked ? PlateBlocked : PlateReady;

        // Layout -------------------------------------------------------------------------

        /// <summary>
        /// Size and place the parts for the current content. Runs only when the content
        /// changed, because it forces a TMP mesh update per label to measure it.
        ///
        /// <para>Each label is scaled to a known fraction of a ROW and the plate is then
        /// widened to fit whichever line is longer. The order matters: widths are measured from
        /// the mesh AFTER it has been scaled, so a long verb widens the badge instead of
        /// spilling off it.</para>
        /// </summary>
        private void Relayout()
        {
            bool showKey = _shownAvailability == InteractionAvailability.Ready
                           || _shownAvailability == InteractionAvailability.Busy;
            bool showDetail = !string.IsNullOrEmpty(_shownDetail);

            _keyCap.enabled = showKey;
            _keyGlyph.enabled = showKey;
            _detail.enabled = showDetail;

            _keyGlyph.text = showKey ? _shownKey : string.Empty;
            _verb.text = _shownVerb;
            _detail.text = _shownDetail;

            float verbWidth = FitToHeight(_verb, ROW_HEIGHT * VERB_HEIGHT_FRACTION);
            float detailWidth = showDetail
                ? FitToHeight(_detail, ROW_HEIGHT * DETAIL_HEIGHT_FRACTION)
                : 0f;
            if (showKey) FitToHeight(_keyGlyph, KEY_SIZE * KEY_GLYPH_FRACTION);

            float keyBlock = showKey ? KEY_SIZE + KEY_GAP : 0f;
            float textWidth = Mathf.Max(verbWidth, detailWidth);
            float width = keyBlock + textWidth + PLATE_PADDING * 2f;
            float height = ROW_HEIGHT * (showDetail ? 1.75f : 1f);

            _plate.transform.localScale = new Vector3(width, height, 1f);
            _plate.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            float left = -width * 0.5f + PLATE_PADDING;
            float verbY = height * 0.5f + (showDetail ? ROW_HEIGHT * 0.36f : 0f);

            if (showKey)
            {
                float capX = left + KEY_SIZE * 0.5f;
                _keyCap.transform.localScale = new Vector3(KEY_SIZE, KEY_SIZE, 1f);
                _keyCap.transform.localPosition = new Vector3(capX, verbY, 0f);
                _keyGlyph.transform.localPosition = new Vector3(capX, verbY, 0f);
            }

            float textLeft = left + keyBlock;

            _verb.color = _shownAvailability == InteractionAvailability.Blocked
                ? VerbBlocked
                : VerbReady;
            _verb.transform.localPosition = new Vector3(textLeft, verbY, 0f);

            if (showDetail)
            {
                // A warning on a READY prompt is the interesting case: the player can act, and
                // needs to be told it will be miserable. On a blocked one the detail is just
                // the reason, so it stays neutral and the plate carries the refusal.
                _detail.color = _shownAvailability == InteractionAvailability.Blocked
                    ? DetailNeutral
                    : DetailWarn;
                _detail.transform.localPosition =
                    new Vector3(textLeft, verbY - ROW_HEIGHT * 0.72f, 0f);
            }

            ApplyOpacity(_opacity);
        }

        /// <summary>
        /// Scale one label so its line box is <paramref name="worldHeight"/> tall, and return
        /// how wide it ended up in world units.
        ///
        /// <para><c>renderedWidth</c> and <c>renderedHeight</c> are in TMP's OWN units, which
        /// depend on the font asset's metrics — so the only honest way to hit a world-space
        /// size is to measure and divide. <c>renderedHeight</c> is the line box, which is why
        /// the fractions are around half rather than near 1: the glyphs sit inside it with
        /// ascender and descender room either side.</para>
        /// </summary>
        private static float FitToHeight(TextMeshPro tmp, float worldHeight)
        {
            tmp.ForceMeshUpdate();

            float measured = tmp.renderedHeight;
            float scale = measured > 0.0001f ? worldHeight / measured : 1f;
            tmp.transform.localScale = new Vector3(scale, scale, 1f);

            // Anchored on the left edge so two stacked rows share a margin. Set after the
            // scale, because the pivot is applied to the already-scaled rect.
            tmp.rectTransform.pivot = tmp.alignment == TextAlignmentOptions.Center
                ? new Vector2(0.5f, 0.5f)
                : new Vector2(0f, 0.5f);

            return tmp.renderedWidth * scale;
        }

        /// <summary>
        /// The key the player would actually press, read from the live binding.
        ///
        /// <para>Hard-coding "E" would be a lie the moment anyone rebinds, and a prompt that
        /// names the wrong key is worse than no prompt: the player presses it, nothing happens,
        /// and they conclude the mine is broken rather than that the badge is.</para>
        /// </summary>
        private static string InteractKeyGlyph()
        {
            var action = InputService.Instance?.Gameplay?.Interact;
            if (action == null) return "E";

            string display = action.GetBindingDisplayString();
            if (string.IsNullOrEmpty(display)) return "E";

            // Several bindings render as "E | Gamepad West"; the cap has room for one key, so
            // take the first.
            int bar = display.IndexOf('|');
            if (bar > 0) display = display.Substring(0, bar);

            display = display.Trim();
            return display.Length == 0 ? "E" : display.ToUpperInvariant();
        }
    }
}
