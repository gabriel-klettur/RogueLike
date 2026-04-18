using UnityEngine;
using UnityEngine.UI;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Radial cooldown overlay that shrinks clockwise from full to empty.
    /// Uses a built-in UI sprite with Image.type = Filled, fillMethod = Radial360.
    /// Matches Python's MagicSpellBarRenderSystem pie-chart cooldown indicator.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CooldownRing : MonoBehaviour
    {
        private Image _image;
        private float _progress; // 0..1 where 1 = full ring (on cooldown)

        /// <summary>Color used when the ability is on cooldown.</summary>
        public Color CooldownColor { get; set; } = new Color(0f, 0f, 0f, 0.65f);

        /// <summary>Color flashed briefly when cooldown completes.</summary>
        public Color ReadyFlashColor { get; set; } = new Color(1f, 0.9f, 0.2f, 0.55f);

        private float _flashTimer;

        private void Awake()
        {
            EnsureImage();
        }

        private void EnsureImage()
        {
            if (_image != null) return;
            _image = GetComponent<Image>();
            if (_image == null) _image = gameObject.AddComponent<Image>();
            if (_image.sprite == null)
            {
                // Use the built-in UI mask sprite (white circle) if available.
                var tex = Texture2D.whiteTexture;
                if (tex != null)
                {
                    _image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    _image.preserveAspect = true;
                }
            }
            _image.type = Image.Type.Filled;
            _image.fillMethod = Image.FillMethod.Radial360;
            _image.fillOrigin = (int)Image.Origin360.Top;
            _image.fillClockwise = false;
            _image.color = CooldownColor;
            _image.raycastTarget = false;
            _image.fillAmount = 0f;
        }

        private void Update()
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(_flashTimer / 0.35f);
                _image.color = Color.Lerp(CooldownColor, ReadyFlashColor, t);
                if (_flashTimer <= 0f) _image.color = CooldownColor;
            }
        }

        /// <summary>
        /// Sets the cooldown progress. <paramref name="normalized"/> is the remaining fraction (1 = full cooldown, 0 = ready).
        /// </summary>
        public void SetProgress(float normalized)
        {
            EnsureImage();
            normalized = Mathf.Clamp01(normalized);
            if (_progress > 0.02f && normalized <= 0.001f)
                _flashTimer = 0.35f; // just became ready
            _progress = normalized;
            if (_image != null)
            {
                _image.fillAmount = normalized;
                _image.enabled = normalized > 0.001f || _flashTimer > 0f;
            }
        }

        /// <summary>Convenience: attach a CooldownRing filling its parent rect.</summary>
        public static CooldownRing AddToParent(Transform parent)
        {
            var go = new GameObject("CooldownRing", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go.AddComponent<CooldownRing>();
        }
    }
}
