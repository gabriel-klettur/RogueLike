using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Shared helpers for entity sprite setup: placeholder sprites and unlit material.
    /// Extracted from EntitySetup to isolate rendering concerns.
    /// </summary>
    public static class EntitySpriteHelper
    {
        private static Sprite _playerSprite;
        private static Sprite _monsterSprite;
        private static Material _unlitSpriteMaterial;

        public static void EnsurePlayerSprite(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite != null) return;
            if (_playerSprite == null)
                _playerSprite = CreatePlaceholderSprite(new Color(0.2f, 0.47f, 0.86f));
            if (_playerSprite != null)
                sr.sprite = _playerSprite;
            EnsureUnlitMaterial(sr);
        }

        public static void EnsureMonsterSprite(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite != null) return;
            if (_monsterSprite == null)
                _monsterSprite = CreatePlaceholderSprite(new Color(0.78f, 0.2f, 0.2f));
            if (_monsterSprite != null)
                sr.sprite = _monsterSprite;
            EnsureUnlitMaterial(sr);
        }

        public static void EnsureUnlitMaterial(SpriteRenderer sr)
        {
            if (sr == null) return;
            if (_unlitSpriteMaterial == null)
            {
                // Prefer Valkur's HDR-tint sprite shader so EntityAnimationBinder can
                // push >1 channel values via MaterialPropertyBlock without the vertex
                // color clamping that flattens monster variant tints. Fall back to
                // URP 2D unlit (or legacy Sprites/Default) when the HDR shader is
                // missing — shader stripping in builds, or before first asset import.
                var shader = Shader.Find("Valkur/SpriteHDRTint")
                          ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                          ?? Shader.Find("Sprites/Default");
                _unlitSpriteMaterial = new Material(shader);
            }
            sr.sharedMaterial = _unlitSpriteMaterial;
        }

        private static Sprite CreatePlaceholderSprite(Color color)
        {
            var tex = new Texture2D(32, 32);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[32 * 32];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        }
    }
}
