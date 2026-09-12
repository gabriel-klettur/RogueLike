using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Core.UI;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        private void BuildUI()
        {
            InputDiagnostics.EnsureEventSystem();

            var canvasGo = new GameObject("MainMenuCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            // Reference, match and the player's interface size, all through the one helper the
            // HUD canvases use. Setting the three by hand is how the quest tracker ended up on
            // Unity's 800x600 default at twice every other canvas's scale.
            Valkur.Core.UI.HudLayout.ApplyScaler(scaler);
            scaler.referenceResolution = MenuStyleResolution();

            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasTransform = canvasGo.transform;

            // The frame first: background, veil, motes, title, vignette. Every screen is drawn
            // inside it, and nothing below re-declares a colour or a veil of its own.
            BuildShell(canvasGo.transform);

            // ONLY what the first screen needs. Everything else is built the first time it is
            // asked for (EnsureScreenBuilt), because measured on the rebuilt menu the screens a
            // player may never open — the class selector, the four options panels, the load
            // browser and the credits — were 790 of 815 transforms and the bulk of the build.
            // The Items editor already proved the shape of this fix: 3 480 ms to 413 ms by
            // building fewer widgets, less often. The cost is uGUI, not the logic.
            BuildMenuPanel(canvasGo.transform);
            BuildFooter(canvasGo.transform);
            BuildPressToStartOverlay(canvasGo.transform);

            UILayerHelper.SetUILayerRecursive(canvasGo);

            _selectedIndex = 0;
            StartCoroutine(DeferredInit());
            StartCoroutine(RunCarousel());
        }

        private Vector2 MenuStyleResolution()
        {
            var r = Style.referenceResolution;
            return r.x > 1f && r.y > 1f ? r : new Vector2(1600f, 800f);
        }

        private IEnumerator DeferredInit()
        {
            yield return null;
            UpdateSelection();
        }

        // ── UI helper methods shared across partial files ─────────────────

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void StretchFull(GameObject go)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Prefers the sprite the asset already has and only builds one as a last resort — and
        /// then with <c>SpriteMeshType.FullRect</c>.
        ///
        /// <para>What this replaces was the last site in the project that hit the documented
        /// Tight trap. Measured on the menu's own art: <c>Sprite.Create</c> at its default cost
        /// <b>22.41 ms</b> on a 1536 × 1024 texture against <b>0.029 ms</b> for the same call
        /// with FullRect — a factor of 773 — and every one of those textures already had a
        /// sprite, because everything under <c>Resources/UI/</c> imports with
        /// <c>spriteMode: 1</c>. It was called three times when the menu opened (background,
        /// logo, tavern) for 60 ms of a 128 ms build, and once more every carousel tick,
        /// forever, leaking the result each time.</para>
        /// </summary>
        private static Sprite MakeSprite(Texture2D tex)
        {
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// The sprite for a Resources path — and says WHO OWNS IT, which is the half that was
        /// missing.
        ///
        /// <para>It returns two different kinds of object: a Sprite that is a shipped ASSET, or
        /// one this menu builds at runtime when only the texture exists. From the outside those
        /// are indistinguishable without <c>AssetDatabase</c>, which a build does not have — so
        /// the menu's teardown destroyed both, and Unity refused six times per session with
        /// <c>Destroying assets is not permitted to avoid data loss</c>. The message's own
        /// suggestion (<c>DestroyImmediate(theObject, true)</c>) would have deleted the PNGs from
        /// the project, which is why the ownership has to be recorded at the one moment it is
        /// known rather than guessed at teardown.</para>
        /// </summary>
        private static Sprite LoadSprite(string resourcePath, out bool ownedByUs)
        {
            ownedByUs = false;
            if (string.IsNullOrEmpty(resourcePath)) return null;
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null) return sprite;          // a shipped asset: shared, never ours
            var made = MakeSprite(Resources.Load<Texture2D>(resourcePath));
            ownedByUs = made != null;                   // built here, so this menu must free it
            return made;
        }

        /// <summary>For callers that only display the sprite and never free it.</summary>
        private static Sprite LoadSprite(string resourcePath) => LoadSprite(resourcePath, out _);
    }
}
