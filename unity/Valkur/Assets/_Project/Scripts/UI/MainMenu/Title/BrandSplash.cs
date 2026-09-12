using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.UI.MainMenu.Title
{
    /// <summary>
    /// The first thing the game shows: the name gathering out of embers on black.
    ///
    /// <para><b>What this replaces is nothing at all.</b> The Bootstrap scene held a camera, an
    /// EventSystem and two MonoBehaviours; <c>GameBootstrap.Start</c> loaded the menu on the
    /// first frame, so the game appeared out of a black frame with no mark of its own. There was
    /// no studio plate, no title, no fade — and the logo that did exist, on the menu behind it,
    /// said <c>ROGUELIKE 1.0</c>.</para>
    ///
    /// <para><b>It is the SAME title the menu draws, and that continuity is the point.</b> The
    /// word assembles here, once, and the menu then finds it already assembled
    /// (<see cref="Consumed"/>) instead of scattering and gathering it a second time three
    /// seconds later. Two assemblies in a row would read as a loop rather than as an
    /// introduction.</para>
    ///
    /// <para><b>Always skippable, and skippable from the first frame.</b> A brand plane that
    /// holds a player who has seen it two hundred times is a tax. The one thing it must not do
    /// is be un-skippable while a key is still being read by whatever came before, which is why
    /// it takes the press through <c>KeyboardInputManager</c> — both backends OR-ed — rather
    /// than the raw device.</para>
    /// </summary>
    public sealed class BrandSplash : MonoBehaviour
    {
        private const float FadeInSeconds = 0.45f;
        private const float HoldSeconds = 0.9f;
        private const float FadeOutSeconds = 0.55f;

        /// <summary>
        /// True once the splash has shown the title in this session, so the menu can open with
        /// the word already settled. Reset with the domain, like every static here.
        /// </summary>
        public static bool Consumed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Consumed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // BeforeSceneLoad, for the reason SceneLoadRouter gives: the call this hooks is
            // GameBootstrap.Start, which runs in the FIRST scene, and a hook installed after
            // that scene loads would miss it.
            GameBootstrap.SplashHandler = Offer;
        }

        private static bool Offer(Action continuation)
        {
            if (!Application.isPlaying || continuation == null) return false;

            // A static delegate with Domain Reload off means the handler an earlier Play session
            // installed is still here; showing the plate twice in one session would be worse
            // than not showing it at all.
            if (Consumed) return false;

            var go = new GameObject("[BrandSplash]");
            DontDestroyOnLoad(go);
            var splash = go.AddComponent<BrandSplash>();
            splash._continuation = continuation;
            return true;
        }

        private Action _continuation;
        private CanvasGroup _group;
        private MenuTitle _title;
        private MenuFxLayer _fx;
        private bool _finished;

        private void Start()
        {
            Consumed = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            var style = MenuStyle.Active;
            var art = MenuArt.Get(style);
            bool reduceMotion = GameSettings.Instance != null
                ? GameSettings.Instance.reduceMotion
                : style.reduceMotionDefault;

            var canvasGo = new GameObject("SplashCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the loading screen's 9999: the splash is the very first thing and must not
            // be drawn under a screen that has not been asked for yet.
            canvas.sortingOrder = 10050;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            Valkur.Core.UI.HudLayout.ApplyScaler(scaler);
            scaler.referenceResolution = style.referenceResolution;
            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var bg = new GameObject("Black", typeof(RectTransform)).GetComponent<RectTransform>();
            bg.SetParent(canvasGo.transform, false);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = Color.black;
            bgImg.raycastTarget = false;

            var material = BuildAdditive(style);
            _fx = MenuFxLayer.Create(canvasGo.transform, art, 48, material);

            // Centred rather than at the menu's own titleTopOffset: on a black plate the word is
            // the whole composition, and repeating the menu's layout here would read as the menu
            // with everything else missing.
            _title = MenuTitle.Create(canvasGo.transform, art, style, material, _fx,
                                      MenuText.GameTitle, MenuText.TitleTagline, reduceMotion);
            var titleRt = (RectTransform)_title.transform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0f, style.titleCapHeight * 0.55f);

            UILayerHelper.SetUILayerRecursive(canvasGo);

            float t = 0f;
            while (t < FadeInSeconds && !Skipped())
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Clamp01(t / FadeInSeconds);
                Step(Time.unscaledDeltaTime);
                yield return null;
            }
            _group.alpha = 1f;

            // Wait for the word itself, not for a clock: on a slow machine a fixed hold ends
            // while the motes are still arriving, and the one thing the plate exists to show
            // would be the thing it cut off.
            float guard = 0f;
            while (_title.Assembly < 1f && guard < 6f && !Skipped())
            {
                guard += Time.unscaledDeltaTime;
                Step(Time.unscaledDeltaTime);
                yield return null;
            }

            float hold = 0f;
            while (hold < HoldSeconds && !Skipped())
            {
                hold += Time.unscaledDeltaTime;
                Step(Time.unscaledDeltaTime);
                yield return null;
            }

            float f = 0f;
            while (f < FadeOutSeconds)
            {
                f += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(f / FadeOutSeconds);
                Step(Time.unscaledDeltaTime);
                yield return null;
            }

            Finish();
        }

        private void Step(float dt)
        {
            _title?.Tick(dt);
            _fx?.Tick(dt);
        }

        /// <summary>
        /// Any key or a click. Through the centralized helpers so both input backends are read:
        /// this is the very first screen of the session, which is exactly when the 2022.3
        /// event-drop bug is most likely to have the new InputSystem silent.
        /// </summary>
        private bool Skipped()
            => KeyboardInputManager.WasAnyKeyPressedThisFrame()
            || MouseInputManager.WasLeftMouseButtonPressedThisFrame();

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            var go = _continuation;
            _continuation = null;
            // Destroyed BEFORE the continuation runs: the menu loads a scene, and a canvas at
            // sorting order 10050 that outlived the load would sit on top of it.
            Destroy(gameObject);
            go?.Invoke();
        }

        private static Material BuildAdditive(MenuStyle style)
        {
            var shader = style != null && style.hudFxShader != null
                ? style.hudFxShader
                : Shader.Find("Valkur/UI/HudFx");
            if (shader == null) return null;
            var mat = new Material(shader) { name = "SplashFxAdditive", hideFlags = HideFlags.DontSave };
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            return mat;
        }
    }
}
