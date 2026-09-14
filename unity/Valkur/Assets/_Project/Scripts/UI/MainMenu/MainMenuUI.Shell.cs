using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.MainMenu.Kit;
using Valkur.UI.MainMenu.Title;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// The frame every pre-game screen is drawn inside: the background carousel, the one veil
    /// over it, the vignette, the particle title, the mote layer and the footer — plus the tick
    /// that drives all of them.
    ///
    /// <para><b>Why it is one file.</b> These pieces were spread over three builders and a
    /// coroutine, and each owned its own colours and its own pacing. The carousel faded every
    /// 2.0 s (1.4 s of stillness per portrait), the veil was applied twice whenever a sub-screen
    /// was open, and the title was a PNG of the wrong game's name.</para>
    /// </summary>
    public partial class MainMenuUI
    {
        private MenuStyle _style;
        private MenuArt _art;
        private Material _additive;
        private MenuFxLayer _fx;
        private MenuTitle _title;
        private MenuSfx _sfx;

        private Image _scrim;
        private Image _vignette;
        private RectTransform _bgContainer;
        private readonly RectTransform[] _bgRects = new RectTransform[2];
        private float _scrimAlpha;
        private float _scrimTarget;
        private float _carouselZoom = 1f;

        /// <summary>The live style, so the partials do not each resolve it.</summary>
        private MenuStyle Style => _style != null ? _style : (_style = MenuStyle.Active);

        /// <summary>The player's accessibility choice, read once per build.</summary>
        private bool ReduceMotion => GameSettings.Instance != null
            ? GameSettings.Instance.reduceMotion
            : Style.reduceMotionDefault;

        // ── Build ────────────────────────────────────────────────────────────

        private void BuildShell(Transform canvas)
        {
            _style = MenuStyle.Active;
            _art = MenuArt.Get(_style);
            _additive = BuildAdditiveMaterial(_style);

            BuildBackground(canvas);
            BuildScrim(canvas);

            // The mote layer sits BETWEEN the background and the panels, so a spark thrown off
            // the title crosses the art and passes behind an open panel. Above everything it
            // would draw over the rows; below the veil it would be invisible.
            _fx = MenuFxLayer.Create(canvas, _art, Style.moteCapacity, _additive);

            _title = MenuTitle.Create(canvas, _art, _style, _additive, _fx,
                                      MenuText.GameTitle, ReduceMotion);
            // The brand plane already gathered the word a couple of seconds ago. Doing it again
            // here would read as a loop; inheriting it makes the two one introduction.
            if (Title.BrandSplash.Consumed) _title.SnapSettled();
            // The other half of the pair above: whichever of the two builders runs second is the
            // one that seats the plate against the frame the player opens on.
            if (_bgImages != null && _bgImages.Length > 0 && _bgImages[0] != null)
                _title.SetBackdrop(_bgImages[0].sprite);

            BuildVignette(canvas);
            _sfx = new MenuSfx(_style);
        }

        /// <summary>
        /// The additive material for the title and the motes. On an additive surface alpha is
        /// COVERAGE and colour is brightness, so an ember over the dark panel brightens it
        /// instead of punching a hole in it. The shader is carried by the style so it survives
        /// a build: one found only by <c>Shader.Find</c> is stripped.
        /// </summary>
        private static Material BuildAdditiveMaterial(MenuStyle style)
        {
            var shader = style != null && style.hudFxShader != null
                ? style.hudFxShader
                : Shader.Find("Valkur/UI/HudFx");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "MenuFxAdditive", hideFlags = HideFlags.DontSave };
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }

        private void BuildBackground(Transform canvas)
        {
            _bgContainer = MenuUIKit.Stretch("BG_Container", canvas);
            _bgContainer.gameObject.AddComponent<RectMask2D>();

            for (int i = 0; i < 2; i++)
            {
                var rt = MenuUIKit.Rect($"BG_{i}", _bgContainer);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var img = rt.gameObject.AddComponent<Image>();
                img.color = Color.clear;
                img.raycastTarget = false;
                _bgImages[i] = img;
                _bgRects[i] = rt;

                var fitter = rt.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = 1.5f;
            }

            var first = LoadBackground(0);
            if (first != null)
            {
                _bgImages[0].sprite = first;
                _bgImages[0].color = Color.white;
                ApplyAspect(0, first);
                // Null-safe on purpose: the title and the carousel are built by two methods whose
                // order is the caller's business, so BOTH push the first frame and whichever runs
                // second is the one that lands. A plate that is only correct from the second
                // carousel change onward is wrong for the seven seconds the player is looking.
                _title?.SetBackdrop(first);
            }
            _bgIndex = 0;
            _carouselSlot = 0;
            ApplyCarouselFraming(0f);
        }

        /// <summary>
        /// ONE veil. The shipped menu built a 55 % black behind the whole menu AND a second one
        /// inside every sub-screen's overlay, so an open panel sat on an 80 % dark painting — the
        /// art it exists to show was almost gone exactly when the player was reading over it.
        /// </summary>
        private void BuildScrim(Transform canvas)
        {
            var rt = MenuUIKit.Stretch("Scrim", canvas);
            _scrim = rt.gameObject.AddComponent<Image>();
            _scrim.color = new Color(0f, 0f, 0f, Style.scrimBase);
            _scrim.raycastTarget = false;
            _scrimAlpha = _scrimTarget = Style.scrimBase;
        }

        private void BuildVignette(Transform canvas)
        {
            var rt = MenuUIKit.Stretch("Vignette", canvas);
            _vignette = rt.gameObject.AddComponent<Image>();
            _vignette.sprite = _art.Vignette;
            _vignette.type = Image.Type.Simple;
            _vignette.color = new Color(0f, 0f, 0f, Style.vignetteStrength);
            _vignette.raycastTarget = false;

            // A cubic ramp along the bottom so the hint line and the version have a floor to sit
            // on whatever the carousel is showing. The shipped footer was grey text straight on
            // the painting, so whether it could be read depended on which portrait was up.
            var scrimRt = MenuUIKit.Rect("BottomScrim", canvas);
            scrimRt.anchorMin = new Vector2(0f, 0f);
            scrimRt.anchorMax = new Vector2(1f, 0f);
            scrimRt.pivot = new Vector2(0.5f, 0f);
            scrimRt.sizeDelta = new Vector2(0f, 128f);
            var img = scrimRt.gameObject.AddComponent<Image>();
            img.sprite = _art.BottomScrim;
            img.type = Image.Type.Simple;
            img.color = new Color(0f, 0f, 0f, 0.85f);
            img.raycastTarget = false;
        }

        // ── Carousel ─────────────────────────────────────────────────────────

        /// <summary>
        /// Loads a background as a SPRITE. The asset already has one — every file under
        /// <c>Resources/UI/</c> imports with <c>spriteMode: 1</c> — so the shipped
        /// <c>Sprite.Create</c> was building, in 21 ms, an object the AssetDatabase already had.
        /// Measured: 22.41 ms against 0.029 ms for the same call with <c>FullRect</c>, and the
        /// carousel leaked one of them every 2.6 s for as long as the menu was open.
        /// </summary>
        private static Sprite LoadBackground(int index)
        {
            if (index < 0 || index >= BgPaths.Length) return null;
            var sprite = Resources.Load<Sprite>(BgPaths[index]);
            if (sprite != null) return sprite;

            var tex = Resources.Load<Texture2D>(BgPaths[index]);
            if (tex == null) return null;
            // FullRect, always: Tight traces the alpha outline of the whole 1536x1024 region.
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private void ApplyAspect(int slot, Sprite sprite)
        {
            var fitter = _bgRects[slot].GetComponent<AspectRatioFitter>();
            if (fitter == null || sprite == null) return;
            var tex = sprite.texture;
            fitter.aspectRatio = tex != null
                ? tex.width / Mathf.Max(1f, tex.height)
                : sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        }

        /// <summary>
        /// The art is 3:2 and the window is 2:1, so a third of the height is cropped. Centred,
        /// that crop takes the characters' heads off — measured on the shipped barbarian, whose
        /// face is above the frame and whose torso is what the logo used to sit on. The bias
        /// pushes the visible window DOWN the image, and the zoom is a slow Ken Burns push so a
        /// held portrait is not a still.
        /// </summary>
        private void ApplyCarouselFraming(float heldSeconds)
        {
            float hold = Mathf.Max(0.01f, Style.carouselHold);
            float t = ReduceMotion ? 0f : Mathf.Clamp01(heldSeconds / hold);
            _carouselZoom = Mathf.Lerp(1f, Mathf.Max(1f, Style.carouselZoom), t);

            for (int i = 0; i < 2; i++)
            {
                if (_bgRects[i] == null) continue;
                _bgRects[i].localScale = new Vector3(_carouselZoom, _carouselZoom, 1f);
                float bias = Style.carouselVerticalBias * _bgRects[i].rect.height;
                _bgRects[i].anchoredPosition = new Vector2(0f, bias);
            }
        }

        private IEnumerator RunCarousel()
        {
            if (BgPaths.Length < 2) yield break;
            while (true)
            {
                float held = 0f;
                float hold = Mathf.Max(0.5f, Style.carouselHold);
                while (held < hold)
                {
                    held += Time.unscaledDeltaTime;
                    ApplyCarouselFraming(held);
                    yield return null;
                }

                int nextBg = (_bgIndex + 1) % BgPaths.Length;
                int nextSlot = 1 - _carouselSlot;

                var sprite = LoadBackground(nextBg);
                if (sprite == null) { _bgIndex = nextBg; continue; }

                _bgImages[nextSlot].sprite = sprite;
                _bgImages[nextSlot].color = Color.clear;
                ApplyAspect(nextSlot, sprite);

                // The title's plate is SOLVED against whatever is about to be behind it, and it
                // is told at the start of the crossfade so the plate arrives with the image
                // rather than a fade later. Measured on the shipped art, the same word read at
                // 7.5:1 over the dark arch and 4.2:1 over the bright barbarian — a constant plate
                // cannot serve both, and which one the player saw was decided by a timer.
                _title?.SetBackdrop(sprite);

                float fade = Mathf.Max(0.05f, Style.carouselFade);
                float elapsed = 0f;
                while (elapsed < fade)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fade));
                    _bgImages[nextSlot].color = new Color(1f, 1f, 1f, k);
                    _bgImages[_carouselSlot].color = new Color(1f, 1f, 1f, 1f - k);
                    // A few motes drift the way the image is moving, so a crossfade reads as
                    // weather over the art rather than as one picture replacing another.
                    if (_fx != null && !ReduceMotion && Random.value < 0.25f)
                        EmitDrift();
                    yield return null;
                }

                _bgImages[nextSlot].color = Color.white;
                _bgImages[_carouselSlot].color = Color.clear;
                _carouselSlot = nextSlot;
                _bgIndex = nextBg;
            }
        }

        private void EmitDrift()
        {
            if (_fx == null) return;
            var rect = ((RectTransform)_fx.transform).rect;
            _fx.Emit(new Vector2(Random.Range(0f, rect.width), Random.Range(0f, rect.height * 0.9f)),
                     new Vector2(Random.Range(-14f, 14f), Random.Range(6f, 20f)),
                     new Color(1f, 0.92f, 0.75f, 0.35f), Random.Range(1.4f, 2.8f),
                     MenuMoteShape.Dot, gravity: -2f, drag: 0.35f,
                     size: Random.Range(0.6f, 1.1f), twinkle: true);
        }

        // ── Tick ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Advances everything that moves. One place, called once per frame, so nothing in the
        /// menu has its own <c>Update</c> and a test can step the whole screen.
        /// </summary>
        private void TickShell(float dt)
        {
            _title?.Tick(dt);
            _fx?.Tick(dt);
            _menuList?.Tick(dt);
            TickPanels(dt);

            if (!Mathf.Approximately(_scrimAlpha, _scrimTarget) && _scrim != null)
            {
                _scrimAlpha = Mathf.MoveTowards(_scrimAlpha, _scrimTarget, dt / 0.22f);
                _scrim.color = new Color(0f, 0f, 0f, _scrimAlpha);
            }
        }

        /// <summary>Deepens the veil while a sub-screen is open, and lifts it when one closes.</summary>
        private void SetScrimForScreen(bool subScreenOpen)
        {
            _scrimTarget = subScreenOpen ? Style.scrimPanel : Style.scrimBase;
            if (_title != null) _title.Alpha = subScreenOpen ? 0.45f : 1f;
        }

        /// <summary>A burst of motes at a point of the mote layer, in canvas space.</summary>
        private void BurstAtCanvas(Vector2 canvasPoint, int count, Color colour, float speed,
                                   float life, MenuMoteShape shape = MenuMoteShape.Dot,
                                   float spread = 360f, float direction = 90f)
        {
            if (_fx == null || ReduceMotion) return;
            var rt = (RectTransform)_fx.transform;
            var local = canvasPoint - rt.rect.min;
            _fx.Burst(local, count, colour, speed, life, shape, spread, direction);
        }

        /// <summary>The centre of a RectTransform, expressed in the mote layer's own space.</summary>
        private Vector2 MoteSpaceOf(RectTransform rt)
        {
            if (_fx == null || rt == null) return Vector2.zero;
            var world = rt.TransformPoint(rt.rect.center);
            var fxRt = (RectTransform)_fx.transform;
            return (Vector2)fxRt.InverseTransformPoint(world) - fxRt.rect.min;
        }
    }
}
