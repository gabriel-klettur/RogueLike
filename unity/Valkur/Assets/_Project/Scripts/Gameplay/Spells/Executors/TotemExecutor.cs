using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Places a healing totem at the target position that periodically heals nearby allies.
    /// Mirrors Python's TotemResolver (healing_totem: kind=heal).
    /// </summary>
    public class TotemExecutor : ISpellExecutor
    {
        /// <summary>
        /// Tint used when a totem leaves <c>particleColor</c> unset. The gold this shipped as
        /// before the colour became authorable, so an untouched totem looks exactly as it did.
        /// </summary>
        private static readonly Color DefaultTint = new Color(1f, 0.9f, 0.3f, 1f);

        /// <summary>
        /// One shared sprite for every totem ever placed. It used to be generated per cast and
        /// never released — a 24x32 texture leaked on each one, the same defect the magic
        /// shield carried.
        /// </summary>
        private static Sprite _totemSprite;

        /// <summary>
        /// Domain Reload is OFF, so the managed handle survives a recompile while the native
        /// texture does not. Nulling the field is a plain <c>stsfld</c>, the only shape
        /// <c>DomainReloadStaticResetTests</c> reads as a reset.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _totemSprite = null;
        }

        /// <summary>
        /// The colour this totem draws with — its body, its area indicator, and the cast gather
        /// that precedes it. Same shape as <see cref="SlashExecutor.ResolveTint"/>, and for the
        /// same reason: the swatch reaches the flourish now, so a totem that ignored it would
        /// be announced in one colour and arrive in another.
        /// </summary>
        public static Color ResolveTint(SpellDefinition spell)
        {
            if (spell == null || KiPalette.IsUnauthored(spell.particleColor)) return DefaultTint;
            return spell.particleColor;
        }

        public void Execute(SpellContext ctx)
        {
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 10f;
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius / 16f : 13.75f;
            float healPerTick = ctx.Spell.healPerTick > 0 ? ctx.Spell.healPerTick : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.5f;
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 3f;

            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(ctx, 5f, dist);

            var totemGo = new GameObject("SpellTotem");
            totemGo.transform.position = (Vector3)spawnPos;

            // Totem visual: triangle/pillar shape
            Color tint = ResolveTint(ctx.Spell);

            var sr = totemGo.AddComponent<SpriteRenderer>();
            sr.sprite = TotemSprite();
            // Alpha is tuning and stays put; only the hue is authorable.
            sr.color = new Color(tint.r, tint.g, tint.b, 0.9f);
            sr.sortingLayerName = "Entities";
            sr.sortingOrder = 8;
            totemGo.transform.localScale = Vector3.one * 0.8f;

            // Collision
            var col = totemGo.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.7f);

            var controller = totemGo.AddComponent<TotemController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(healPerTick), tickPeriod, ctx.Caster);

            if (VFXManager.Instance != null)
            {
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos,
                    new Color(tint.r, tint.g, tint.b, 0.4f), radius, 0.5f);
            }

        
            // Free-standing world object: nothing else can end it. The registry
            // enforces maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(totemGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
}

        private static Sprite TotemSprite()
        {
            if (_totemSprite != null) return _totemSprite;

            int w = 24, h = 32;
            var tex = new Texture2D(w, h);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[w * h];
            // Triangle shape
            for (int y = 0; y < h; y++)
            {
                float halfWidth = (float)(h - y) / h * (w / 2f);
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Abs(x - w / 2f);
                    pixels[y * w + x] = dx <= halfWidth ? Color.white : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _totemSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f);
            return _totemSprite;
        }
    }
}
