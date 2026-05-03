using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;

namespace Valkur.UIKit
{
    /// <summary>
    /// Persistent bottom-right HUD icon bar. Owns its own ScreenSpaceOverlay
    /// canvas at sortingOrder 250 (above every other HUD canvas) so the icons
    /// are ALWAYS visible, regardless of which HUD window is open or expanded.
    ///
    /// Public API (scalable for future buttons):
    ///   <code>
    ///   HUDIconBar.Instance.Register("inventory", sprite, ToggleInventory);
    ///   HUDIconBar.Instance.SetEnabled("inventory", canOpen);
    ///   HUDIconBar.Instance.SetBadge("inventory", unreadCount);
    ///   HUDIconBar.Instance.Unregister("inventory");
    ///   </code>
    ///
    /// Lives in <c>Valkur.UIKit</c> so every assembly (Gameplay, UI, Editor)
    /// can reference it without creating circular dependencies.
    /// </summary>
    public class HUDIconBar : SingletonMonoBehaviour<HUDIconBar>
    {
        // ── Visual constants ────────────────────────────────────────────────
        public  const float BUTTON_SIZE  = 80f;
        private const float BUTTON_GAP   = 6f;
        private const float EDGE_INSET   = 16f;
        private const int   CANVAS_SORT  = 250;
        private const float BADGE_SIZE   = 20f;

        // ── Refs ────────────────────────────────────────────────────────────
        private Canvas        _canvas;
        private RectTransform _containerRt;

        // ── Entries ─────────────────────────────────────────────────────────
        private class Entry
        {
            public string          Id;
            public Sprite          Sprite;
            public Action          OnClick;
            public int             Order;
            public GameObject      Go;
            public Image           Image;
            public Button          Button;
            public CanvasGroup     Cg;
            public TextMeshProUGUI BadgeText;
            public GameObject      BadgeGo;
            public bool            Enabled;
            public int             BadgeCount;
        }
        private readonly List<Entry>               _order   = new List<Entry>();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        protected override void OnSingletonAwake()
        {
            BuildCanvas();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("HUDIconBarCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CANVAS_SORT;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 800);
            scaler.matchWidthOrHeight  = 0.5f;

            // Explicit raycaster config: blockingObjects=None means only the
            // bar's own Graphic components (the 36×36 button images) intercept
            // raycasts. Clicks anywhere else on the screen fall through to the
            // canvas below (music panel at sortingOrder 150, etc.). This is the
            // default in Unity 2022 but we set it explicitly so future Unity
            // upgrades or auto-fixes can't break the contract.
            var raycaster = canvasGo.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            // Container — anchored to bottom-right, no background image.
            var go = new GameObject("HUDIconBarContainer", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            _containerRt = (RectTransform)go.transform;
            _containerRt.anchorMin = new Vector2(1f, 0f);
            _containerRt.anchorMax = new Vector2(1f, 0f);
            _containerRt.pivot     = new Vector2(1f, 0f);
            _containerRt.anchoredPosition = new Vector2(-EDGE_INSET, EDGE_INSET);

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding                = new RectOffset(0, 0, 0, 0);
            hlg.spacing                = BUTTON_GAP;
            hlg.childAlignment         = TextAnchor.LowerRight;
            hlg.childControlWidth      = false;
            hlg.childControlHeight     = false;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Register (or update) a persistent toolbar button. Buttons stay
        /// pinned for the rest of the session; clicks invoke
        /// <paramref name="onClick"/>.
        /// </summary>
        /// <param name="order">Lower = further left. Defaults to insertion order.</param>
        public void Register(string id, Sprite sprite, Action onClick, int order = 0)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureContainer();

            if (_entries.TryGetValue(id, out var existing))
            {
                existing.Sprite  = sprite;
                existing.OnClick = onClick;
                existing.Order   = order;
                if (existing.Image  != null) existing.Image.sprite = sprite;
                if (existing.Button != null)
                {
                    existing.Button.onClick.RemoveAllListeners();
                    var cb = onClick;
                    existing.Button.onClick.AddListener(() => cb?.Invoke());
                }
                ReorderSiblings();
                return;
            }

            var entry = new Entry
            {
                Id      = id,
                Sprite  = sprite,
                OnClick = onClick,
                Order   = order,
                Enabled = true,
            };
            _order.Add(entry);
            _entries[id] = entry;
            BuildButton(entry);
            ReorderSiblings();
        }

        public void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_entries.TryGetValue(id, out var entry)) return;
            if (entry.Go != null) SafeDestroy.Of(entry.Go);
            _entries.Remove(id);
            _order.Remove(entry);
        }

        public bool IsRegistered(string id)
            => !string.IsNullOrEmpty(id) && _entries.ContainsKey(id);

        public int Count => _order.Count;

        /// <summary>
        /// Toggles the button's interactability and dims it when disabled.
        /// Use for context-sensitive buttons (e.g. "shop" only when in town).
        /// </summary>
        public void SetEnabled(string id, bool enabled)
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            entry.Enabled = enabled;
            if (entry.Button != null) entry.Button.interactable = enabled;
            if (entry.Cg     != null) entry.Cg.alpha = enabled ? 1f : 0.45f;
        }

        /// <summary>
        /// Sets the numeric badge in the top-right corner of the button.
        /// Pass 0 (or negative) to hide the badge.
        /// </summary>
        public void SetBadge(string id, int count)
        {
            if (!_entries.TryGetValue(id, out var entry)) return;
            entry.BadgeCount = count;
            if (entry.BadgeGo == null && count > 0) BuildBadge(entry);
            if (entry.BadgeGo == null) return;
            entry.BadgeGo.SetActive(count > 0);
            if (entry.BadgeText != null)
                entry.BadgeText.text = count > 99 ? "99+" : count.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────────────────────────────

        private void EnsureContainer()
        {
            if (_containerRt == null) BuildCanvas();
        }

        private void ReorderSiblings()
        {
            // Stable sort by Order ascending (lower = further left). Ties
            // preserve insertion order from _order.
            _order.Sort((a, b) => a.Order.CompareTo(b.Order));
            for (int i = 0; i < _order.Count; i++)
            {
                var e = _order[i];
                if (e.Go != null) e.Go.transform.SetSiblingIndex(i);
            }
        }

        private void BuildButton(Entry entry)
        {
            var go = new GameObject($"HUDIconBtn_{entry.Id}", typeof(RectTransform));
            go.transform.SetParent(_containerRt, false);
            entry.Go = go;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(BUTTON_SIZE, BUTTON_SIZE);

            // The sprite IS the button — no inner frame, no inner icon.
            entry.Image = go.AddComponent<Image>();
            entry.Image.sprite         = entry.Sprite;
            entry.Image.preserveAspect = true;
            entry.Image.color          = Color.white;
            if (entry.Image.sprite == null)
                entry.Image.color = new Color(0.18f, 0.18f, 0.22f, 1f); // visible fallback

            entry.Button = go.AddComponent<Button>();
            entry.Button.targetGraphic = entry.Image;
            var c = entry.Button.colors;
            c.normalColor      = Color.white;
            c.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            c.pressedColor     = new Color(0.78f, 0.78f, 0.78f, 1f);
            c.selectedColor    = Color.white;
            c.disabledColor    = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            entry.Button.colors = c;
            entry.Button.interactable = entry.Enabled;

            entry.Cg = go.AddComponent<CanvasGroup>();
            entry.Cg.alpha = entry.Enabled ? 1f : 0.45f;

            var cb = entry.OnClick;
            entry.Button.onClick.AddListener(() => cb?.Invoke());

            // Fallback letter for headless / asset-missing scenarios.
            if (entry.Sprite == null)
            {
                var lbl = new GameObject("Label", typeof(RectTransform));
                lbl.transform.SetParent(rt, false);
                var lrt = (RectTransform)lbl.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                var tmp = lbl.AddComponent<TextMeshProUGUI>();
                tmp.text          = (entry.Id ?? "?").Substring(0, 1).ToUpperInvariant();
                tmp.fontSize      = 24f;
                tmp.fontStyle     = FontStyles.Bold;
                tmp.alignment     = TextAlignmentOptions.Center;
                tmp.color         = new Color(0.90f, 0.76f, 0.38f, 1f);
                tmp.raycastTarget = false;
            }
        }

        private void BuildBadge(Entry entry)
        {
            if (entry.Go == null) return;

            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            badgeGo.transform.SetParent(entry.Go.transform, false);
            entry.BadgeGo = badgeGo;

            var brt = (RectTransform)badgeGo.transform;
            brt.anchorMin = new Vector2(1f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot     = new Vector2(1f, 1f);
            brt.anchoredPosition = new Vector2(2f, 2f);
            brt.sizeDelta = new Vector2(BADGE_SIZE, BADGE_SIZE);

            var bg = badgeGo.AddComponent<Image>();
            bg.color = new Color(0.85f, 0.20f, 0.20f, 1f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(badgeGo.transform, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            entry.BadgeText = txtGo.AddComponent<TextMeshProUGUI>();
            entry.BadgeText.text          = "0";
            entry.BadgeText.fontSize      = 12f;
            entry.BadgeText.fontStyle     = FontStyles.Bold;
            entry.BadgeText.alignment     = TextAlignmentOptions.Center;
            entry.BadgeText.color         = Color.white;
            entry.BadgeText.raycastTarget = false;
        }

    }
}
