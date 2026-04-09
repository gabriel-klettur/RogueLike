using UnityEngine;
using TMPro;

namespace Valkur.UI
{
    /// <summary>
    /// Floating name plate above NPC heads.
    /// Mirrors Python's NamePlateSystem + NamePlateRenderSystem.
    /// Color is faction-based: GOOD=blue(90,160,255), EVIL=red(255,80,80), NEUTRAL=white(245,245,245).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class NamePlate : MonoBehaviour
    {
        [SerializeField, Tooltip("Vertical offset above sprite top, in world units.")]
        private float yOffset = 0.35f;

        [SerializeField, Tooltip("Font size of the name plate text.")]
        private float fontSize = 3f;

        private TextMeshPro _tmp;
        private SpriteRenderer _sr;

        // Python faction colors
        private static readonly Color ColorGood    = new Color(90f/255f, 160f/255f, 255f/255f, 1f);
        private static readonly Color ColorEvil    = new Color(255f/255f, 80f/255f, 80f/255f, 1f);
        private static readonly Color ColorNeutral = new Color(245f/255f, 245f/255f, 245f/255f, 1f);

        /// <summary>Call once after spawn to configure the plate.</summary>
        public void Initialize(string displayName, string faction)
        {
            if (string.IsNullOrEmpty(displayName)) displayName = gameObject.name;
            EnsureTMP();
            _tmp.text = displayName;
            _tmp.color = FactionColor(faction);
        }

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            EnsureTMP();
        }

        private void LateUpdate()
        {
            if (_tmp == null) return;
            float spriteTop = _sr != null && _sr.sprite != null
                ? _sr.bounds.extents.y
                : 0.5f;
            _tmp.transform.localPosition = new Vector3(0, spriteTop + yOffset, 0);
        }

        private void EnsureTMP()
        {
            if (_tmp != null) return;
            var go = new GameObject("NameText");
            go.transform.SetParent(transform, false);
            _tmp = go.AddComponent<TextMeshPro>();
            _tmp.fontSize = fontSize;
            _tmp.alignment = TextAlignmentOptions.Bottom;
            _tmp.sortingOrder = 100;
            _tmp.rectTransform.sizeDelta = new Vector2(4, 1);
            _tmp.enableWordWrapping = false;

            // Outline for readability (Python outline_w=2)
            _tmp.outlineWidth = 0.15f;
            _tmp.outlineColor = new Color32(0, 0, 0, 200);
        }

        private static Color FactionColor(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return ColorNeutral;
            switch (faction.ToUpperInvariant())
            {
                case "GOOD":
                case "FRIENDLY":
                    return ColorGood;
                case "EVIL":
                case "HOSTILE":
                    return ColorEvil;
                default:
                    return ColorNeutral;
            }
        }
    }
}
