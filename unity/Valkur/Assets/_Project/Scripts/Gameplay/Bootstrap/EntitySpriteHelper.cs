using UnityEngine;
using Valkur.Core.Rendering;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Shared helpers for entity sprite setup: placeholder sprites and the entity material.
    /// Extracted from EntitySetup to isolate rendering concerns.
    /// </summary>
    public static class EntitySpriteHelper
    {
        private static Sprite _playerSprite;
        private static Sprite _monsterSprite;
        private static Material _entityMaterial;

        /// <summary>
        /// Domain Reload is OFF. These natives are destroyed with the Play session that made
        /// them, so a cached handle would resurface as a MissingReferenceException on the
        /// second Play — the exact failure mode CLAUDE.md warns about for static mutable state.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _playerSprite   = null;
            _monsterSprite  = null;
            _entityMaterial = null;
        }

        public static void EnsurePlayerSprite(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite != null) return;
            if (_playerSprite == null)
                _playerSprite = CreatePlaceholderSprite(new Color(0.2f, 0.47f, 0.86f));
            if (_playerSprite != null)
                sr.sprite = _playerSprite;
            EnsureEntityMaterial(sr);
        }

        public static void EnsureMonsterSprite(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite != null) return;
            if (_monsterSprite == null)
                _monsterSprite = CreatePlaceholderSprite(new Color(0.78f, 0.2f, 0.2f));
            if (_monsterSprite != null)
                sr.sprite = _monsterSprite;
            EnsureEntityMaterial(sr);
        }

        /// <summary>
        /// Gives an entity renderer the HDR-tint sprite material — the LIT variant when the
        /// scene has a Global Light2D, so the player and the monsters darken with the world
        /// instead of floating at noon brightness over a night-blue town, and the UNLIT one
        /// when there is no light to receive (a lit sprite with no light renders black).
        ///
        /// Both variants honour the same contract: an HDR <c>_Color</c> pushed through a
        /// MaterialPropertyBlock (SpriteRenderer.color's Color32 route would crush a 2.5x
        /// monster-variant tint to 1.0) plus the <c>_FlashAmount</c> hit flash.
        /// </summary>
        public static void EnsureEntityMaterial(SpriteRenderer sr)
        {
            if (sr == null) return;
            if (_entityMaterial == null)
            {
                bool lit = WorldSpriteMaterials.AmbientLightingAvailable;
                // Fallbacks cover shader stripping in builds and the pre-first-import editor.
                var shader = (lit ? Shader.Find("Valkur/SpriteHDRTintLit") : null)
                          ?? Shader.Find("Valkur/SpriteHDRTint")
                          ?? Shader.Find(lit ? "Universal Render Pipeline/2D/Sprite-Lit-Default"
                                             : "Universal Render Pipeline/2D/Sprite-Unlit-Default")
                          ?? Shader.Find("Sprites/Default");
                _entityMaterial = new Material(shader);
            }
            sr.sharedMaterial = _entityMaterial;
        }

        /// <summary>Obsolete name kept so existing call sites and tests keep compiling.</summary>
        public static void EnsureUnlitMaterial(SpriteRenderer sr) => EnsureEntityMaterial(sr);

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
