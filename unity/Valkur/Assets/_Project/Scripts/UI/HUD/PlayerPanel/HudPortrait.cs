using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The portrait window: a warm backdrop, the character's head and shoulders baked from their
    /// own idle frame, and a bevelled gold frame over both.
    ///
    /// <para><b>A portrait, not a body.</b> The old slot took the first <c>SpriteRenderer.sprite</c>
    /// it found and fit the WHOLE figure into 108 px, so the dwarf was a 60-pixel doll in a box of
    /// empty space — and whatever frame the animator happened to be on, facing whichever way.
    /// This bakes the EAST idle frame (facing into the panel, towards the bars) through
    /// <see cref="HudTextureBaker.Portrait"/>, cropped to the head.</para>
    ///
    /// <para><b>It follows the character.</b> The idle set is re-read twice a second, so a
    /// loadout swap (the dwarf drawing his sword replaces the idle set) re-bakes the portrait; the
    /// old one was captured once at boot and stayed stale forever.</para>
    ///
    /// <para><b>It reports the fight</b> without a readout of its own: colour drains out of it as
    /// health falls under the low threshold, it goes fully grey while the player is a spirit, and
    /// it flashes on a blow, a heal and a level. Those three are pushed in by
    /// <see cref="PlayerHUD"/>; the portrait never subscribes to anything itself.</para>
    /// </summary>
    public sealed class HudPortrait
    {
        private const int FrameInset = 3;
        private const float PollSeconds = 0.5f;

        public RectTransform Root { get; }

        private readonly RawImage _backdrop;
        private readonly RawImage _face;
        private readonly Image _fallback;
        private readonly Material _material;
        private readonly int _innerW, _innerH;
        private readonly PlayerHudStyle _style;

        private GameObject _player;
        private Sprite _source;
        private Texture2D _faceTex;
        private float _poll;
        private float _saturation = 1f;
        private float _flashLeft, _flashSeconds = 0.1f;
        private Color _flashColour;

        /// <summary>True once a baked head is showing (false on a null graphics device).</summary>
        public bool HasBakedFace => _faceTex != null;

        /// <summary>The sprite the current portrait was baked from.</summary>
        public Sprite Source => _source;

        /// <summary>The saturation the portrait is drawn at right now.</summary>
        public float Saturation => _saturation;

        /// <summary>What the last bake found. For the tests and the probe.</summary>
        public HudTextureBaker.PortraitInfo LastBake { get; private set; }

        public HudPortrait(Transform parent, HudArt art, PlayerHudStyle style, int x, int y, int size, Shader fx)
        {
            _style = style;
            Root = HudRect.Make("Portrait", parent, x, y, size, size);
            _innerW = _innerH = size - FrameInset * 2;

            var backdropTex = HudArt.BakeBackdrop(_innerW, _innerH, style.backdropCentre, style.backdropEdge);
            _backdrop = MakeRaw("Backdrop", Root, FrameInset, FrameInset, _innerW, _innerH);
            _backdrop.texture = backdropTex;

            _face = MakeRaw("Face", Root, FrameInset, FrameInset, _innerW, _innerH);
            _face.enabled = false;
            if (fx != null)
            {
                _material = new Material(fx) { name = "HudPortraitFx", hideFlags = HideFlags.DontSave };
                _face.material = _material;
            }

            // Shown only when no bake is possible: the raw sprite, aspect-kept, as the old slot did.
            _fallback = HudRect.MakeImage("Fallback", Root, null, FrameInset, FrameInset, _innerW, _innerH);
            _fallback.preserveAspect = true;
            _fallback.enabled = false;

            HudRect.MakeImage("Frame", Root, art.PortraitFrame, 0, 0, size, size, Image.Type.Sliced);
        }

        private static RawImage MakeRaw(string name, Transform parent, int x, int y, int w, int h)
        {
            var rt = HudRect.Make(name, parent, x, y, w, h);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        public void Bind(GameObject player)
        {
            _player = player;
            _source = null;
            _poll = 0f;
            Refresh();
        }

        /// <summary>Flashes the head in <paramref name="colour"/> (its alpha is the strength).</summary>
        public void Flash(Color colour, float seconds)
        {
            if (seconds <= 0f) return;
            _flashColour = colour;
            _flashSeconds = seconds;
            _flashLeft = seconds;
        }

        public void Tick(float dt, float healthRatio, bool dead)
        {
            _poll -= dt;
            if (_poll <= 0f)
            {
                _poll = PollSeconds;
                Refresh();
            }

            // Colour drains from the low threshold down to the floor at zero; a spirit has none.
            float goal = 1f;
            if (dead) goal = 0f;
            else if (healthRatio < _style.lowThreshold)
                goal = Mathf.Lerp(_style.portraitSaturationAtZero, 1f,
                                  Mathf.Clamp01(healthRatio / Mathf.Max(0.01f, _style.lowThreshold)));
            _saturation = Mathf.MoveTowards(_saturation, goal, dt * 2.5f);

            if (_flashLeft > 0f) _flashLeft = Mathf.Max(0f, _flashLeft - dt);

            if (_material != null)
            {
                _material.SetFloat("_Saturation", _saturation);
                var f = _flashColour;
                f.a *= _flashLeft > 0f ? Mathf.Clamp01(_flashLeft / _flashSeconds) : 0f;
                _material.SetColor("_FlashColor", f);
            }
            else if (_fallback.enabled || _face.enabled)
            {
                var g = Color.Lerp(new Color(0.55f, 0.55f, 0.55f, 1f), Color.white, _saturation);
                _face.color = g;
                _fallback.color = g;
            }
        }

        /// <summary>Re-reads the idle frame and re-bakes when it changed.</summary>
        public void Refresh()
        {
            var sprite = ResolveSourceSprite(_player);
            if (sprite == _source && (_faceTex != null || _fallback.enabled)) return;
            _source = sprite;

            if (_faceTex != null)
            {
                HudLifetime.Release(_faceTex);
                _faceTex = null;
            }

            if (sprite == null)
            {
                _face.enabled = false;
                _fallback.enabled = false;
                return;
            }

            var tex = HudTextureBaker.Portrait(sprite, _innerW, _innerH, _style.portraitBodyTexels,
                                               _style.outline, out var info);
            LastBake = info;
            if (tex != null)
            {
                _faceTex = tex;
                _face.texture = tex;
                _face.enabled = true;
                _fallback.enabled = false;
            }
            else
            {
                _face.enabled = false;
                _fallback.sprite = sprite;
                _fallback.enabled = true;
            }
        }

        /// <summary>
        /// The frame to paint the portrait from: the first EAST idle frame when the animator has
        /// one — facing into the panel — else whatever the body renderer is drawing.
        /// </summary>
        public static Sprite ResolveSourceSprite(GameObject player)
        {
            if (player == null) return null;
            var anim = player.GetComponentInChildren<DirectionalAnimator>();
            if (anim != null)
            {
                var frames = anim.IdleSprites.GetFrames(DirectionalAnimator.Direction.East);
                if (frames != null)
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null) return frames[i];
            }
            var sr = player.GetComponent<SpriteRenderer>();
            if (sr == null) sr = player.GetComponentInChildren<SpriteRenderer>();
            return sr != null ? sr.sprite : null;
        }

        public void Dispose()
        {
            HudLifetime.Release(_faceTex);
            if (_backdrop != null) HudLifetime.Release(_backdrop.texture);
            HudLifetime.Release(_material);
        }
    }
}
