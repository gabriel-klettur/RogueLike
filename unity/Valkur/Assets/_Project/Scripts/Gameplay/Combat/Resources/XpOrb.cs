using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// XP Orb that gets attracted toward and absorbed by the player.
    /// Maps to Python's OrbAttractionSystem + ExperienceSystem.
    /// Constants: attract_radius=6.25 world units (100px/16), speed=0.3125 world units/frame (5px/16).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class XpOrb : MonoBehaviour
    {
        [SerializeField, Tooltip("XP value of this orb.")]
        private int xpValue = 1;

        [SerializeField, Tooltip("Attraction radius in world units (Python: 100px / 16 PPU).")]
        private float attractRadius = 6.25f;

        [SerializeField, Tooltip("Movement speed toward player in world units/sec (Python: 5px/frame * 60fps / 16 PPU).")]
        private float attractSpeed = 18.75f;

        [SerializeField, Tooltip("Distance at which the orb is absorbed.")]
        private float absorbDistance = 0.5f;

        private Transform _playerTransform;
        private bool _absorbed;

        public int XpValue => xpValue;

        public void Initialize(int xp, Vector3 position)
        {
            xpValue = xp;
            transform.position = position;
            _absorbed = false;
        }

        private void Update()
        {
            if (_absorbed) return;

            if (_playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _playerTransform = player.transform;
                else return;
            }

            Vector3 toPlayer = _playerTransform.position - transform.position;
            float dist = toPlayer.magnitude;

            if (dist <= absorbDistance)
            {
                Absorb();
                return;
            }

            if (dist <= attractRadius)
            {
                Vector3 dir = toPlayer.normalized;
                float step = attractSpeed * Time.deltaTime;
                if (step >= dist) step = dist;
                transform.position += dir * step;
            }
        }

        private void Absorb()
        {
            if (_absorbed) return;
            _absorbed = true;

            if (_playerTransform != null)
            {
                var xp = _playerTransform.GetComponent<Experience>();
                if (xp != null)
                {
                    xp.AddXp(xpValue);
                    GameEvents.FireXpGained(_playerTransform.gameObject, xpValue);
                }
            }

            Destroy(gameObject);
        }

        private static Sprite _cachedOrbSprite;

        /// <summary>Returns a shared circular sprite for XP orbs.</summary>
        public static Sprite GetOrbSprite()
        {
            if (_cachedOrbSprite != null) return _cachedOrbSprite;

            int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            float center = size / 2f;
            float radius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    if (dist <= radius)
                    {
                        float alpha = 1f - (dist / radius);
                        tex.SetPixel(x, y, new Color(0.3f, 1f, 0.5f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();

            _cachedOrbSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            _cachedOrbSprite.name = "XpOrbSprite";
            return _cachedOrbSprite;
        }
    }
}
