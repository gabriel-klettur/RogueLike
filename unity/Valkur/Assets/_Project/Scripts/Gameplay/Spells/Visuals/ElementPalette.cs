using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Element-specific palette + behaviour. Returned by <see cref="For"/>.
    /// Sprites resolve lazily via <see cref="ElementalSprites"/>.
    /// </summary>
    internal struct ElementPalette
    {
        public SpellElement element;
        public Color hotCore, core, glow, halo, accent, lightColor;
        public float coreScale, glowScale, haloScale, hotCoreScale, accentScale;
        public int ghostCount;
        public float ghostSpacing;
        public float emberInterval, emberLifetime, emberJitter, emberDrag, emberBuoyancy;
        public float flickerRate;
        public float stretch;
        public float lightIntensity, lightOuter, lightInner;
        public float accentSpinSpeed;
        public Sprite hotCoreSprite, coreSprite, glowSprite, haloSprite, emberSprite, accentSprite, ringSprite;
        public string impactSfxId;

        public static ElementPalette For(SpellElement e)
        {
            ElementalSprites.EnsureAll();
            switch (e)
            {
                case SpellElement.Ice:        return Ice();
                case SpellElement.Light:      return Light();
                case SpellElement.Lightning:  return Lightning();
                case SpellElement.Boomerang:  return Boomerang();
                case SpellElement.Arcane:     return Arcane();
                case SpellElement.Fire:       return Fire();
                case SpellElement.Dark:
                default:                      return Dark();
            }
        }

        /// <summary>
        /// Return this palette wearing the spell's own authored hue, keeping every behaviour
        /// field and every field's own BRIGHTNESS. Returns the palette untouched when the
        /// swatch is unauthored.
        ///
        /// <para>WHY THIS IS A RETINT AND NOT A REPLACEMENT. Two different things live in these
        /// colour fields at once. The HUE says what element this is; the per-field VALUE is
        /// tuning — <c>hotCore</c> is near-white and <c>halo</c> is dim, and that spread is what
        /// makes a flourish read as a hot centre inside a soft bloom rather than as six sprites
        /// of one colour. Swapping the fields wholesale for a derived palette throws the second
        /// away, and it fails hardest exactly where it is least recoverable: an authored swatch
        /// like <c>hostile_slash_dark</c>'s 0.04 grey would drive every layer to near-black, and
        /// on an ADDITIVE material near-black adds nothing — the flourish would not dim, it
        /// would disappear. Keeping V per field means a dark spell simply gets a desaturated
        /// flourish, which is what it should look like.</para>
        ///
        /// <para>This exists because 39 of the 74 shipped spells author a
        /// <c>particleColor</c> and the flourish ignored every one of them, so a green laser
        /// fired with an arcane-violet gather.</para>
        /// </summary>
        public ElementPalette RecolouredTo(Color authored)
        {
            // Same sentinel as the ki palette, and deliberately the same owner: opaque white is
            // what the field holds when nobody has touched it.
            if (KiPalette.IsUnauthored(authored)) return this;

            Color.RGBToHSV(authored, out float hue, out float saturation, out float value);
            // A pure grey authored swatch has no hue to give, only an absence of colour — which
            // is a real request, so it desaturates rather than being ignored.
            if (value <= 0.001f) saturation = 0f;

            var tinted = this;
            tinted.hotCore    = Retint(hotCore, hue, saturation);
            tinted.core       = Retint(core, hue, saturation);
            tinted.glow       = Retint(glow, hue, saturation);
            tinted.halo       = Retint(halo, hue, saturation);
            tinted.accent     = Retint(accent, hue, saturation);
            tinted.lightColor = Retint(lightColor, hue, saturation);
            return tinted;
        }

        /// <summary>
        /// Move one colour onto <paramref name="hue"/>, keeping its own value and alpha.
        /// Saturation is blended rather than replaced so a field authored near-white — the hot
        /// core — picks up a tint without becoming a flat block of the spell's colour.
        /// </summary>
        private static Color Retint(Color original, float hue, float saturation)
        {
            Color.RGBToHSV(original, out float _, out float s, out float v);

            // An ACHROMATIC swatch has no hue to give — RGBToHSV reports 0 for grey, which is
            // red — so blending toward it the normal way lights a grey spell with a pale pink
            // gather. Measured on hostile_slash_gray: a 0.59 grey blade against a
            // (1.00, 0.84, 0.84) core. Grey is a real request, and what it asks for is the
            // ABSENCE of colour, so it goes fully neutral at the field's own brightness.
            if (saturation <= 0.02f) return new Color(v, v, v, original.a);

            var result = Color.HSVToRGB(hue, Mathf.Lerp(s, saturation, 0.7f), v);
            result.a = original.a;
            return result;
        }

        // Dark: deep purple/black void with violet halo, slow swirling wisps.
        private static ElementPalette Dark() => new ElementPalette
        {
            element = SpellElement.Dark,
            hotCore = new Color(0.85f, 0.55f, 1.00f, 1f),
            core    = new Color(0.55f, 0.20f, 0.85f, 1f),
            glow    = new Color(0.30f, 0.05f, 0.55f, 0.70f),
            halo    = new Color(0.10f, 0.00f, 0.25f, 0.30f),
            accent  = new Color(0.65f, 0.30f, 1.00f, 0.55f),
            lightColor = new Color(0.55f, 0.20f, 1.00f, 1f),
            coreScale = 0.42f, glowScale = 1.05f, haloScale = 1.85f, hotCoreScale = 0.22f, accentScale = 0.95f,
            ghostCount = 6, ghostSpacing = 0.11f,
            emberInterval = 0.04f, emberLifetime = 0.65f, emberJitter = 0.9f, emberDrag = 1.4f, emberBuoyancy = -0.4f,
            flickerRate = 12f, stretch = 0.45f,
            lightIntensity = 1.6f, lightOuter = 2.4f, lightInner = 0.3f,
            accentSpinSpeed = -65f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Wisp,
            accentSprite  = ElementalSprites.Wisp,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_dark_impact",
        };

        // Ice: cyan/white frost with snowflake accent + sharp shards.
        private static ElementPalette Ice() => new ElementPalette
        {
            element = SpellElement.Ice,
            hotCore = new Color(0.92f, 0.99f, 1.00f, 1f),
            core    = new Color(0.65f, 0.92f, 1.00f, 1f),
            glow    = new Color(0.35f, 0.75f, 1.00f, 0.65f),
            halo    = new Color(0.20f, 0.55f, 1.00f, 0.25f),
            accent  = new Color(0.85f, 0.98f, 1.00f, 0.85f),
            lightColor = new Color(0.55f, 0.85f, 1.00f, 1f),
            coreScale = 0.40f, glowScale = 0.95f, haloScale = 1.65f, hotCoreScale = 0.20f, accentScale = 0.85f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.03f, emberLifetime = 0.55f, emberJitter = 0.6f, emberDrag = 0.6f, emberBuoyancy = -1.2f,
            flickerRate = 8f, stretch = 0.30f,
            lightIntensity = 1.5f, lightOuter = 2.2f, lightInner = 0.4f,
            accentSpinSpeed = 40f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Snowflake,
            accentSprite  = ElementalSprites.Snowflake,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_ice_impact",
        };

        // Light/Holy: warm white-yellow with sparkle starburst accent.
        private static ElementPalette Light() => new ElementPalette
        {
            element = SpellElement.Light,
            hotCore = new Color(1.00f, 1.00f, 0.95f, 1f),
            core    = new Color(1.00f, 0.95f, 0.65f, 1f),
            glow    = new Color(1.00f, 0.85f, 0.40f, 0.65f),
            halo    = new Color(1.00f, 0.95f, 0.65f, 0.30f),
            accent  = new Color(1.00f, 1.00f, 0.85f, 0.85f),
            lightColor = new Color(1.00f, 0.90f, 0.55f, 1f),
            coreScale = 0.42f, glowScale = 1.00f, haloScale = 1.75f, hotCoreScale = 0.22f, accentScale = 1.10f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.025f, emberLifetime = 0.50f, emberJitter = 0.8f, emberDrag = 1.0f, emberBuoyancy = 0.6f,
            flickerRate = 20f, stretch = 0.40f,
            lightIntensity = 2.4f, lightOuter = 2.8f, lightInner = 0.5f,
            accentSpinSpeed = 90f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.SparkleStar,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_light_impact",
        };

        // Lightning: blue-white plasma with crackling bolt accent + fast flicker.
        private static ElementPalette Lightning() => new ElementPalette
        {
            element = SpellElement.Lightning,
            hotCore = new Color(1.00f, 1.00f, 1.00f, 1f),
            core    = new Color(0.75f, 0.95f, 1.00f, 1f),
            glow    = new Color(0.40f, 0.75f, 1.00f, 0.75f),
            halo    = new Color(0.25f, 0.55f, 1.00f, 0.30f),
            accent  = new Color(1.00f, 1.00f, 1.00f, 0.95f),
            lightColor = new Color(0.65f, 0.85f, 1.00f, 1f),
            coreScale = 0.35f, glowScale = 0.85f, haloScale = 1.50f, hotCoreScale = 0.18f, accentScale = 1.10f,
            ghostCount = 4, ghostSpacing = 0.12f,
            emberInterval = 0.02f, emberLifetime = 0.30f, emberJitter = 1.4f, emberDrag = 3.0f, emberBuoyancy = 0f,
            flickerRate = 70f, stretch = 0.55f,
            lightIntensity = 2.2f, lightOuter = 2.4f, lightInner = 0.3f,
            accentSpinSpeed = 0f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Bolt,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_lightning_impact",
        };

        // Boomerang: green/wood spinning blade with leaf trail.
        private static ElementPalette Boomerang() => new ElementPalette
        {
            element = SpellElement.Boomerang,
            hotCore = new Color(0.95f, 1.00f, 0.65f, 1f),
            core    = new Color(0.55f, 0.85f, 0.30f, 1f),
            glow    = new Color(0.40f, 0.65f, 0.20f, 0.55f),
            halo    = new Color(0.25f, 0.45f, 0.10f, 0.20f),
            accent  = new Color(0.85f, 0.75f, 0.40f, 1f),
            lightColor = new Color(0.65f, 0.95f, 0.45f, 1f),
            coreScale = 0.30f, glowScale = 0.75f, haloScale = 1.20f, hotCoreScale = 0.15f, accentScale = 0.80f,
            ghostCount = 3, ghostSpacing = 0.13f,
            emberInterval = 0.05f, emberLifetime = 0.45f, emberJitter = 0.5f, emberDrag = 1.6f, emberBuoyancy = -0.3f,
            flickerRate = 6f, stretch = 0.20f,
            lightIntensity = 0.9f, lightOuter = 1.4f, lightInner = 0.2f,
            accentSpinSpeed = 720f,                   // very fast spin
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Blade,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_boomerang_impact",
        };

        // Arcane: bright magenta/cyan dual-tone with star accent.
        private static ElementPalette Arcane() => new ElementPalette
        {
            element = SpellElement.Arcane,
            hotCore = new Color(1.00f, 0.95f, 1.00f, 1f),
            core    = new Color(0.95f, 0.45f, 1.00f, 1f),
            glow    = new Color(0.75f, 0.30f, 1.00f, 0.65f),
            halo    = new Color(0.45f, 0.20f, 0.85f, 0.30f),
            accent  = new Color(0.95f, 0.85f, 1.00f, 0.85f),
            lightColor = new Color(0.85f, 0.45f, 1.00f, 1f),
            coreScale = 0.40f, glowScale = 0.95f, haloScale = 1.65f, hotCoreScale = 0.20f, accentScale = 1.00f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.025f, emberLifetime = 0.55f, emberJitter = 0.9f, emberDrag = 1.0f, emberBuoyancy = 0.3f,
            flickerRate = 18f, stretch = 0.40f,
            lightIntensity = 2.0f, lightOuter = 2.5f, lightInner = 0.4f,
            accentSpinSpeed = 120f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.SparkleStar,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_arcane_impact",
        };

        // Fire: orange/red flame with hot yellow core, rising embers, deep orange light.
        private static ElementPalette Fire() => new ElementPalette
        {
            element = SpellElement.Fire,
            hotCore = new Color(1.00f, 0.95f, 0.55f, 1f),
            core    = new Color(1.00f, 0.55f, 0.10f, 1f),
            glow    = new Color(1.00f, 0.30f, 0.05f, 0.75f),
            halo    = new Color(0.65f, 0.10f, 0.00f, 0.30f),
            accent  = new Color(1.00f, 0.80f, 0.30f, 0.85f),
            lightColor = new Color(1.00f, 0.55f, 0.20f, 1f),
            coreScale = 0.42f, glowScale = 1.05f, haloScale = 1.80f, hotCoreScale = 0.22f, accentScale = 0.95f,
            ghostCount = 5, ghostSpacing = 0.10f,
            emberInterval = 0.02f, emberLifetime = 0.60f, emberJitter = 1.0f, emberDrag = 1.2f, emberBuoyancy = 1.4f,
            flickerRate = 22f, stretch = 0.45f,
            lightIntensity = 2.4f, lightOuter = 2.8f, lightInner = 0.4f,
            accentSpinSpeed = 60f,
            hotCoreSprite = ElementalSprites.HotCore,
            coreSprite    = ElementalSprites.Core,
            glowSprite    = ElementalSprites.Glow,
            haloSprite    = ElementalSprites.Halo,
            emberSprite   = ElementalSprites.Sparkle,
            accentSprite  = ElementalSprites.Sparkle,
            ringSprite    = ElementalSprites.Ring,
            impactSfxId   = "spell_fire_impact",
        };
    }
}
