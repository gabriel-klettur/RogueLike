using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Shared identity for Ice Lance. The projectile itself is procedural, so its HUD art
    /// must come from the same vocabulary instead of falling back to the generic projectile
    /// dot used by the runtime editor.
    /// </summary>
    public static class IceLanceArt
    {
        public const string SpellKey = "ice_lance";

        private static Sprite _icon;

        public static bool Matches(SpellDefinition spell)
            => spell != null && spell.spellKey == SpellKey;

        /// <summary>Normal authored-icon chain, with a purpose-built procedural fallback.</summary>
        public static Sprite ResolveIcon(SpellDefinition spell)
        {
            if (spell == null) return null;
            if (spell.iconSprite != null) return spell.iconSprite;
            if (spell.sprite != null) return spell.sprite;
            return Matches(spell) ? Icon : null;
        }

        public static Sprite Icon
        {
            get
            {
                if (_icon == null) _icon = BuildIcon();
                return _icon;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _icon = null;

        private static Sprite BuildIcon()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "IceLance_Icon_Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[size * size];

            Vector2 a = new Vector2(12f, 13f);
            Vector2 b = new Vector2(52f, 51f);
            Vector2 axis = (b - a).normalized;
            Vector2 normal = new Vector2(-axis.y, axis.x);
            float length = Vector2.Distance(a, b);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float along = Vector2.Dot(p - a, axis);
                    float across = Vector2.Dot(p - a, normal);

                    // Quiet cyan halo: enough separation on a black slot, never a blue box.
                    float tipDist = Vector2.Distance(p, b);
                    float shaftDist = Mathf.Abs(across);
                    float halo = Mathf.Clamp01(1f - shaftDist / 10f)
                               * Mathf.Clamp01(along / 8f)
                               * Mathf.Clamp01((length + 8f - along) / 14f);
                    if (tipDist < 13f) halo = Mathf.Max(halo, Mathf.Clamp01(1f - tipDist / 13f));
                    Color c = new Color(0.12f, 0.56f, 1f, halo * 0.16f);

                    if (along >= -3f && along <= length + 3f)
                    {
                        float t = Mathf.Clamp01(along / length);
                        float halfWidth = t < 0.16f
                            ? Mathf.Lerp(4.7f, 2.8f, t / 0.16f)
                            : Mathf.Lerp(2.8f, 0.05f, Mathf.Pow((t - 0.16f) / 0.84f, 1.35f));
                        float edge = Mathf.Abs(across);
                        if (edge <= halfWidth + 1.25f)
                        {
                            bool outline = edge > halfWidth || along < 0f || along > length;
                            if (outline)
                            {
                                c = new Color(0.03f, 0.18f, 0.42f, 0.96f);
                            }
                            else
                            {
                                Color deep = new Color(0.10f, 0.38f, 0.82f, 1f);
                                Color mid  = new Color(0.32f, 0.78f, 1f, 1f);
                                Color tip  = new Color(0.91f, 0.99f, 1f, 1f);
                                c = Color.Lerp(deep, mid, Mathf.SmoothStep(0f, 0.72f, t));
                                c = Color.Lerp(c, tip, Mathf.SmoothStep(0.62f, 1f, t));
                                if (across > -0.4f && across < 1.25f)
                                    c = Color.Lerp(c, Color.white, 0.62f);
                            }
                        }
                    }

                    // Four-point glint at the tip and two detached crystal chips.
                    float star = Mathf.Max(
                        Mathf.Clamp01(1f - (Mathf.Abs(p.x - b.x) + Mathf.Abs(p.y - b.y) * 0.22f) / 7f),
                        Mathf.Clamp01(1f - (Mathf.Abs(p.y - b.y) + Mathf.Abs(p.x - b.x) * 0.22f) / 7f));
                    if (star > 0.12f)
                        c = Color.Lerp(c, new Color(0.88f, 0.99f, 1f, 1f), star);

                    if (Vector2.Distance(p, new Vector2(16f, 30f)) < 2.2f ||
                        Vector2.Distance(p, new Vector2(30f, 13f)) < 1.7f)
                        c = new Color(0.48f, 0.90f, 1f, 0.92f);

                    px[y * size + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, true);
            _icon = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _icon.name = "IceLance_Icon_Procedural";
            _icon.hideFlags = HideFlags.HideAndDontSave;
            return _icon;
        }
    }
}
