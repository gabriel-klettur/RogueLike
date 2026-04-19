using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Spells;
using Valkur.UI.HUD;

namespace Valkur.UI
{
    /// <summary>
    /// Spell bar HUD showing equipped spells with cooldown overlays.
    /// Maps to Python's MagicSpellBarSystem + MagicSpellBarRenderSystem.
    /// Positioned bottom-center of screen.
    /// </summary>
    public class SpellBarHUD : MonoBehaviour
    {
        [SerializeField, Tooltip("Max spell slots to display.")]
        private int maxSlots = 6;

        [SerializeField, Tooltip("Slot size in pixels.")]
        private float slotSize = 48f;

        [SerializeField, Tooltip("Gap between slots in pixels.")]
        private float slotGap = 4f;

        private Canvas _canvas;
        private RectTransform _barPanel;
        private SpellSlotUI[] _slots;
        private SpellCaster _playerCaster;

        private static readonly string[] _keyLabels = { "1", "2", "3", "4", "5", "6" };

        private void Start()
        {
            BuildUI();
        }

        private void Update()
        {
            if (_playerCaster == null)
            {
                var player = EntityRegistry.Player;
                if (player != null) _playerCaster = player.GetComponent<SpellCaster>();
                if (_playerCaster == null) return;
            }

            UpdateSlots();
        }

        private void BuildUI()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                var canvasGo = new GameObject("SpellBarCanvas");
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasGo.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasGo.transform, false);
            }

            // Bar panel at bottom center
            var panelGo = new GameObject("SpellBarPanel");
            panelGo.transform.SetParent(transform, false);
            _barPanel = panelGo.AddComponent<RectTransform>();
            _barPanel.anchorMin = new Vector2(0.5f, 0f);
            _barPanel.anchorMax = new Vector2(0.5f, 0f);
            _barPanel.pivot = new Vector2(0.5f, 0f);

            float totalWidth = maxSlots * slotSize + (maxSlots - 1) * slotGap;
            _barPanel.sizeDelta = new Vector2(totalWidth + 16, slotSize + 16);
            _barPanel.anchoredPosition = new Vector2(0, 8);

            var panelImg = panelGo.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.6f);

            // Create slots
            _slots = new SpellSlotUI[maxSlots];
            for (int i = 0; i < maxSlots; i++)
            {
                _slots[i] = CreateSlot(i);
            }

            UILayerHelper.SetUILayerRecursive(_canvas.gameObject);
        }

        private SpellSlotUI CreateSlot(int index)
        {
            var slotGo = new GameObject($"SpellSlot_{index}");
            slotGo.transform.SetParent(_barPanel, false);

            var rt = slotGo.AddComponent<RectTransform>();
            float totalWidth = maxSlots * slotSize + (maxSlots - 1) * slotGap;
            float startX = -totalWidth / 2f + slotSize / 2f;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(slotSize, slotSize);
            rt.anchoredPosition = new Vector2(startX + index * (slotSize + slotGap), 0);

            // Background
            var bg = slotGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            // Spell icon
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(slotGo.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.sizeDelta = new Vector2(-4, -4);
            iconRt.anchoredPosition = Vector2.zero;
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            // Radial cooldown ring (replaces the old bottom-up fill)
            var ring = CooldownRing.AddToParent(slotGo.transform);

            // Key label
            var keyGo = new GameObject("KeyLabel");
            keyGo.transform.SetParent(slotGo.transform, false);
            var keyRt = keyGo.AddComponent<RectTransform>();
            keyRt.anchorMin = new Vector2(0, 1);
            keyRt.anchorMax = new Vector2(0, 1);
            keyRt.pivot = new Vector2(0, 1);
            keyRt.sizeDelta = new Vector2(16, 16);
            keyRt.anchoredPosition = new Vector2(2, -2);
            var keyText = keyGo.AddComponent<TextMeshProUGUI>();
            keyText.text = index < _keyLabels.Length ? _keyLabels[index] : "";
            keyText.fontSize = 11;
            keyText.color = new Color(0.8f, 0.8f, 0.8f, 0.8f);
            keyText.alignment = TextAlignmentOptions.TopLeft;

            return new SpellSlotUI
            {
                root = rt,
                icon = iconImg,
                cooldownRing = ring,
                keyLabel = keyText
            };
        }

        private void UpdateSlots()
        {
            for (int i = 0; i < maxSlots; i++)
            {
                var slot = _slots[i];
                var spell = _playerCaster.GetSpellAtSlot(i);
                if (spell != null)
                {
                    slot.icon.color = Color.white;
                    if (spell.sprite != null) slot.icon.sprite = spell.sprite;
                    float cdNorm = _playerCaster.GetCooldownNormalized(i);
                    slot.cooldownRing?.SetProgress(cdNorm);
                }
                else
                {
                    slot.icon.color = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                    slot.cooldownRing?.SetProgress(0f);
                }
            }
        }

        private class SpellSlotUI
        {
            public RectTransform root;
            public Image icon;
            public CooldownRing cooldownRing;
            public TextMeshProUGUI keyLabel;
        }
    }
}
