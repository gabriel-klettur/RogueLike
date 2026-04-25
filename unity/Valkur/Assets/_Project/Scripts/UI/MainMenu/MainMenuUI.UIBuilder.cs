using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Save;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        private void BuildUI()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            var canvasGo = new GameObject("MainMenuCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _canvasTransform = canvasGo.transform;

            BuildBackground(canvasGo.transform);
            BuildOverlay(canvasGo.transform);
            BuildLogo(canvasGo.transform);
            BuildMenuPanel(canvasGo.transform);
            BuildClassSelectorPanel(canvasGo.transform);
            BuildFooter(canvasGo.transform);
            BuildOptionsSubmenu(canvasGo.transform);
            BuildLoadGameSubmenu(canvasGo.transform);
            BuildPressToStartOverlay(canvasGo.transform);

            UILayerHelper.SetUILayerRecursive(canvasGo);

            _selectedIndex = 0;
            StartCoroutine(DeferredInit());
            StartCoroutine(RunCarousel());
        }

        private void BuildBackground(Transform canvas)
        {
            // Container masks overflow so "cover" scaling crops edges
            var container = CreateUIObject("BG_Container", canvas);
            StretchFull(container);
            container.AddComponent<RectMask2D>();

            for (int i = 0; i < 2; i++)
            {
                var go = CreateUIObject($"BG_{i}", container.transform);
                StretchFull(go);
                _bgImages[i] = go.AddComponent<Image>();
                _bgImages[i].preserveAspect = true;
                _bgImages[i].color = Color.clear;

                // EnvelopeParent = "cover" mode: fill parent, crop overflow
                var fitter = go.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = 1.5f; // default; updated per image in carousel
            }
            var firstTex = Resources.Load<Texture2D>(BgPaths[0]);
            if (firstTex != null)
            {
                _bgImages[0].sprite = MakeSprite(firstTex);
                _bgImages[0].color  = Color.white;
                _bgImages[0].GetComponent<AspectRatioFitter>().aspectRatio =
                    (float)firstTex.width / firstTex.height;
            }
            _bgIndex = 0;
            _carouselSlot = 0;
        }

        private void BuildOverlay(Transform canvas)
        {
            var go = CreateUIObject("Overlay", canvas);
            StretchFull(go);
            go.AddComponent<Image>().color = OverlayColor;
        }

        private void BuildLogo(Transform canvas)
        {
            var go   = CreateUIObject("Logo", canvas);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin        = new Vector2(0.5f, 1f);
            rect.anchorMax        = new Vector2(0.5f, 1f);
            rect.pivot            = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta        = new Vector2(560f, 240f);
            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            var tex = Resources.Load<Texture2D>("UI/Intro/game_name");
            if (tex != null)
                img.sprite = MakeSprite(tex);
            else
                img.color = Color.clear;
        }
    }
}