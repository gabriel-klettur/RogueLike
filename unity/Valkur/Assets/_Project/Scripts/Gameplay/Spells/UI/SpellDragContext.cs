using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Spells.UI
{
    public enum SpellDragOrigin
    {
        None = 0,
        Picker = 1,
        HudSlot = 2,
    }

    /// <summary>
    /// Shared state for spell drag-and-drop across the runtime editor and HUD.
    /// Keeps the source item stationary and renders a yellow ghost preview instead.
    /// </summary>
    public static class SpellDragContext
    {
        private static GameObject _ghostRoot;
        private static RectTransform _ghostRect;
        private static Image _ghostImage;
        private static CanvasGroup _ghostCanvasGroup;

        public static SpellDefinition DraggedSpell { get; private set; }
        public static SpellDragOrigin Origin { get; private set; }
        public static int SourceSlotIndex { get; private set; } = -1;
        public static bool IsDragging => DraggedSpell != null;
        public static GameObject GhostObject => _ghostRoot;

        public static void Begin(
            SpellDefinition spell,
            Sprite previewSprite,
            SpellDragOrigin origin,
            int sourceSlotIndex,
            Canvas canvas,
            Vector2 screenPosition)
        {
            if (spell == null || canvas == null)
                return;

            EnsureGhost(canvas.transform as RectTransform);

            DraggedSpell = spell;
            Origin = origin;
            SourceSlotIndex = sourceSlotIndex;

            _ghostRoot.SetActive(true);
            // Prefer the explicit preview > the dedicated HUD icon > the in-world sprite (legacy).
            Sprite ghost = previewSprite;
            if (ghost == null) ghost = spell.iconSprite;
            if (ghost == null) ghost = spell.sprite;
            _ghostImage.sprite = ghost;
            _ghostImage.enabled = _ghostImage.sprite != null;
            UpdatePosition(screenPosition, canvas);
        }

        public static void UpdatePosition(Vector2 screenPosition, Canvas canvas)
        {
            if (!IsDragging || _ghostRect == null || canvas == null)
                return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null)
                return;

            var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition,
                eventCamera,
                out var localPoint))
            {
                _ghostRect.anchoredPosition = localPoint;
            }
        }

        public static void End()
        {
            DraggedSpell = null;
            Origin = SpellDragOrigin.None;
            SourceSlotIndex = -1;

            if (_ghostRoot != null)
                _ghostRoot.SetActive(false);
        }

        private static void EnsureGhost(RectTransform canvasRect)
        {
            if (_ghostRoot != null)
            {
                if (_ghostRoot.transform.parent != canvasRect)
                    _ghostRoot.transform.SetParent(canvasRect, false);
                return;
            }

            _ghostRoot = new GameObject("SpellDragGhost", typeof(RectTransform));
            _ghostRoot.transform.SetParent(canvasRect, false);
            _ghostRect = _ghostRoot.GetComponent<RectTransform>();
            _ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
            _ghostRect.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRect.pivot = new Vector2(0.5f, 0.5f);
            _ghostRect.sizeDelta = new Vector2(52f, 52f);

            var bg = _ghostRoot.AddComponent<Image>();
            bg.color = new Color(1f, 0.92f, 0.35f, 0.16f);
            bg.raycastTarget = false;

            var outline = _ghostRoot.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.86f, 0.18f, 0.95f);
            outline.effectDistance = new Vector2(2f, 2f);

            var shadow = _ghostRoot.AddComponent<Shadow>();
            shadow.effectColor = new Color(1f, 0.82f, 0.05f, 0.75f);
            shadow.effectDistance = new Vector2(0f, -1f);

            _ghostCanvasGroup = _ghostRoot.AddComponent<CanvasGroup>();
            _ghostCanvasGroup.alpha = 0.92f;
            _ghostCanvasGroup.blocksRaycasts = false;
            _ghostCanvasGroup.interactable = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(_ghostRoot.transform, false);
            var iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(4f, 4f);
            iconRect.offsetMax = new Vector2(-4f, -4f);

            _ghostImage = iconGo.AddComponent<Image>();
            _ghostImage.preserveAspect = true;
            _ghostImage.raycastTarget = false;

            _ghostRoot.SetActive(false);
        }
    }
}
