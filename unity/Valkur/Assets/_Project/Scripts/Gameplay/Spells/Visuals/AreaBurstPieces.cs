using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Everything an area burst throws off: the spikes and thorns the wave trips, the opaque
    /// chips that say the ground was disturbed, the dust, and the wash over a caster the spell
    /// went off on.
    ///
    /// <para>Nothing here is pooled. A burst is a rare event — the cheapest of these five
    /// spells is on an 5 s cooldown — so pooling would buy a lifetime and cost the correctness
    /// that comes free from a one-shot object destroying itself.</para>
    /// </summary>
    internal static class AreaBurstPieces
    {
        /// <summary>Chip and dust speeds, world units per second.</summary>
        private const float CHIP_SPEED_MIN = 1.4f;
        private const float CHIP_SPEED_MAX = 3.6f;

        /// <summary>How many gold motes a radiant burst throws. Its opaque layer is zero by
        /// declaration, so this is the only thing it scatters.</summary>
        private const int RADIANCE_MOTES = 20;

        /// <summary>Crystal dust on the rim of a frost nova, drifting up and outward.</summary>
        private const int RIME_MOTES = 16;

        // ── the event layer ──────────────────────────────────────────────────────────

        /// <summary>
        /// A crystal standing up out of the floor, assembled the way <c>IceWallVisual</c>
        /// assembles one: an OPAQUE body carrying the silhouette with an additive facet and rim
        /// inside it. The facet alone — which is what a "spike of ice" naively reaches for — is
        /// documented as a specular highlight, so a spike made of it would be a glow on the
        /// floor rather than a piece of ice.
        /// </summary>
        internal static void IceSpike(Vector3 world, AreaBurstProfile profile, int order, int index)
        {
            IceSprites.EnsureAll();
            int variant = index;

            var go = new GameObject("IceSpike");
            go.transform.position = world;

            float height = profile.Radius * Random.Range(0.16f, 0.30f);
            float width = height * Random.Range(0.34f, 0.52f);

            Color crystal = Color.Lerp(profile.Palette.core, Color.white, 0.25f);
            crystal.a = 1f;   // opaque: this is the silhouette, not a glow over one
            var body = Make(go.transform, "Body", IceSprites.Body(variant), crystal, order, false);
            var facet = Make(go.transform, "Facet", IceSprites.Facet(variant),
                             Overdrive(profile.Palette.hotCore, profile.Gain, 0.55f), order + 1, true);
            var rim = Make(go.transform, "Rim", IceSprites.Rim(variant),
                           Overdrive(profile.Palette.accent, profile.Gain, 0.45f), order + 2, true);

            go.AddComponent<AreaBurstShard>().Begin(
                new[] { body, facet, rim }, width, height,
                IceSprites.ShardUnitHeight, centrePivot: false,
                lean: Random.Range(-14f, 14f),
                rise: 0.09f, hold: 0.34f, fall: 0.34f);

            Pop(world, ElementalSprites.HotCore,
                Overdrive(profile.Palette.hotCore, profile.Gain, 0.8f),
                width * 2.2f, 0.16f, order + 3);
        }

        /// <summary>
        /// A thorn. THE MAIN LAYER IS OPAQUE and that inversion is the whole point of this
        /// silhouette (Law L3 applied to the subject rather than to a debris layer): this is
        /// matter coming through the floor, not light being drawn over it, so only the pop at
        /// the base is additive.
        ///
        /// <para>The lighter tip is a SECOND, taller, narrower tendril sorted BEHIND the dark
        /// one, so the only part of it that shows is the point sticking out above. Drawing a
        /// highlight on top instead needs a sprite two or three pixels tall at 16 PPU, which is
        /// not a tip, it is a speck.</para>
        /// </summary>
        internal static void Thorn(Vector3 world, AreaBurstProfile profile, int order, bool throwsClod)
        {
            RootSprites.EnsureAll();
            var soil = RootPalette.From(profile.Swatch);

            var go = new GameObject("Thorn");
            go.transform.position = world;

            float height = profile.Radius * Random.Range(0.22f, 0.42f);
            float width = RootSprites.TendrilWorldWidth * Random.Range(0.75f, 1.25f);
            float mirror = Random.value < 0.5f ? -1f : 1f;

            var tip = Make(go.transform, "Tip", RootSprites.Tendril, soil.Leaf, order, false);
            tip.transform.localScale = new Vector3(0.6f * mirror, 1.10f, 1f);

            var stem = Make(go.transform, "Stem", RootSprites.Tendril,
                            Color.Lerp(soil.Bark, soil.Soil, Random.Range(0f, 0.35f)), order + 1, false);
            stem.transform.localScale = new Vector3(mirror, 1f, 1f);

            go.AddComponent<AreaBurstShard>().Begin(
                new[] { tip, stem }, width, height, unitHeight: 1f, centrePivot: false,
                lean: Random.Range(-18f, 18f),
                // Punches up fast and retracts over half a second, which is the beat the spell
                // is named for — the retraction is what leaves the clods behind.
                rise: 0.11f, hold: 0.42f, fall: 0.50f);

            // Sized off the thorn's HEIGHT, not its width: the pop marks the hole the thorn
            // came out of, and a thorn half a unit wide still opens a hole you can see.
            Pop(world, RootSprites.Burst, Overdrive(soil.Sap, profile.Gain, 0.65f),
                height * 0.9f, 0.20f, order + 2);

            if (throwsClod)
                Chip(world, RootSprites.Clod, soil.Soil,
                     new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(1.8f, 3.4f)),
                     Random.Range(0.09f, 0.16f), Random.Range(0.5f, 0.9f), 2.6f, order + 3);
        }

        /// <summary>
        /// A fork on the rim, present for about six frames. It is the event layer for the
        /// shock silhouette and its whole value is that it appears and is GONE — a bolt that
        /// hangs around becomes another continuous layer and the ring already is one.
        /// </summary>
        internal static void BoltFork(Vector3 world, AreaBurstProfile profile, int order, float bearing)
        {
            var go = new GameObject("BoltFork");
            go.transform.position = world;

            float height = profile.Radius * Random.Range(0.30f, 0.52f);
            var sr = Make(go.transform, "Bolt", ElementalSprites.Bolt,
                          Overdrive(profile.Palette.hotCore, profile.Gain, 0.9f), order, true);

            go.AddComponent<AreaBurstShard>().Begin(
                new[] { sr }, height * 0.55f, height, unitHeight: 1f, centrePivot: true,
                // Leaned OFF the rim's tangent, so a ring of forks splays outward instead of
                // standing in a picket fence.
                lean: Mathf.Repeat(bearing * Mathf.Rad2Deg, 360f) * 0.06f + Random.Range(-22f, 22f),
                rise: 0.03f, hold: 0.05f, fall: 0.08f);
        }

        /// <summary>The generic event: a pop, for any silhouette with nothing more specific.</summary>
        internal static void Spark(Vector3 world, AreaBurstProfile profile, int order)
            => Pop(world, ElementalSprites.Sparkle,
                   Overdrive(profile.Palette.accent, profile.Gain, 0.85f),
                   profile.Radius * 0.22f, 0.20f, order);

        // ── the scattered layers ─────────────────────────────────────────────────────

        /// <summary>
        /// Law L3 for the frost nova: dark blue-grey chips of BROKEN GROUND, opaque, thrown LOW
        /// and outward. They are the only layer in the rig that is not light, and without them
        /// the nova is something shining on the floor rather than something that hit it.
        /// </summary>
        internal static void IceChips(Vector3 center, AreaBurstProfile profile, int order)
        {
            // Alpha forced to 1: this is the L3 layer, and several element palettes author a
            // translucent core. A half-transparent chip on the alpha material is a chip that
            // reads as light again, which is the whole thing the layer exists to contradict.
            Color chip = Color.Lerp(profile.Palette.core, Color.black, 0.55f);
            chip.a = 1f;
            for (int i = 0; i < profile.GritCount; i++)
            {
                Vector2 outward = Random.insideUnitCircle.normalized;
                Chip(center, IceSprites.Debris, chip,
                     outward * Random.Range(CHIP_SPEED_MIN, CHIP_SPEED_MAX) + Vector2.up * Random.Range(0.2f, 1.1f),
                     Random.Range(0.07f, 0.14f), Random.Range(0.35f, 0.65f), 2.2f, order);
            }
        }

        internal static void CrystalDust(Vector3 center, AreaBurstProfile profile, int order)
            => Motes(center, profile, ElementalSprites.Snowflake, profile.Palette.accent,
                     RIME_MOTES, order);

        internal static void GoldMotes(Vector3 center, AreaBurstProfile profile, int order)
            => Motes(center, profile, ElementalSprites.Sparkle, profile.Palette.hotCore,
                     RADIANCE_MOTES, order);

        /// <summary>Opaque grit and small stones, jumping STRAIGHT UP and falling back.</summary>
        internal static void Grit(Vector3 center, AreaBurstProfile profile, int order)
        {
            Color stone = Color.Lerp(profile.Palette.halo, Color.black, 0.6f);
            stone.a = 1f;   // opaque, for the reason IceChips records
            for (int i = 0; i < profile.GritCount; i++)
            {
                Vector2 at = Random.insideUnitCircle * profile.Radius * 0.9f;
                var world = center + new Vector3(at.x, at.y * 0.34f, 0f);
                Chip(world, KiSprites.Pebble, stone,
                     new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(2.2f, 4.4f)),
                     Random.Range(0.06f, 0.12f), Random.Range(0.35f, 0.60f), 4.2f, order);
            }
        }

        /// <summary>
        /// One scrap of dead plant, dropped where a snare stem finally let go. It is the last
        /// frame of the only feedback a zero-damage control spell produces: without it the
        /// stems simply stop existing, and "the root ended" looks exactly like "the root was
        /// never drawn".
        /// </summary>
        internal static void WitheredScrap(Vector3 world, Color soil, int order)
            => Chip(world, RootSprites.Clod, soil,
                    new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.4f, 1.2f)),
                    Random.Range(0.06f, 0.11f), Random.Range(0.45f, 0.75f), 3.0f, order);

        /// <summary>The wash over a caster the burst went off on. See <see cref="AreaBurstBloom"/>.</summary>
        internal static void CasterBloom(Transform caster, AreaBurstProfile profile)
        {
            var go = new GameObject("AreaBurstCasterBloom");
            go.transform.position = caster.position;

            var glow = Make(go.transform, "Glow", ElementalSprites.Glow,
                            profile.CasterTint, ORDER_BLOOM, true);
            var hot = Make(go.transform, "Hot", ElementalSprites.HotCore,
                           profile.CasterTint, ORDER_BLOOM + 1, true);

            go.AddComponent<AreaBurstBloom>().Begin(caster, glow, hot, profile.CasterTint,
                                                    size: 2.2f, life: 0.28f, gain: profile.Gain);
        }

        // ── plumbing ─────────────────────────────────────────────────────────────────

        private const int ORDER_BLOOM = 40;

        private static void Motes(Vector3 center, AreaBurstProfile profile, Sprite sprite,
                                  Color color, int count, int order)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 outward = Random.insideUnitCircle.normalized;
                // Up AND outward. A mote that only rises reads as smoke off a fire; the
                // outward component is what ties the dust to the wave that made it.
                Chip(center, sprite, Overdrive(color, profile.Gain, 0.9f),
                     outward * Random.Range(0.9f, 1.6f) + Vector2.up * Random.Range(0.6f, 1.6f),
                     Random.Range(0.06f, 0.13f), Random.Range(0.40f, 0.85f),
                     gravity: -0.5f, order: order, additive: true);
            }
        }

        /// <summary>
        /// One thrown piece. Reuses <c>SpellProjectileDebris</c> rather than growing a second
        /// implementation of "a chip with drag, gravity, spin and a fade" — it is not projectile
        /// specific and duplicating it is how two debris layers end up on different curves.
        /// </summary>
        private static void Chip(Vector3 world, Sprite sprite, Color color, Vector2 velocity,
                                 float size, float life, float gravity, int order,
                                 bool additive = false)
        {
            var go = new GameObject("AreaBurstChip");
            go.transform.position = world;
            go.transform.localScale = Vector3.one * size;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;

            go.AddComponent<SpellProjectileDebris>().Begin(sr, velocity, size, life, color, gravity);
        }

        /// <summary>An expanding additive flash. Same reuse argument as <see cref="Chip"/>.</summary>
        private static void Pop(Vector3 world, Sprite sprite, Color color,
                                float scale, float life, int order)
        {
            var go = new GameObject("AreaBurstPop");
            go.transform.position = world;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;

            go.AddComponent<SpellProjectileFlash>().Begin(sr, color, scale, color.a, life, contract: false);
        }

        private static SpriteRenderer Make(Transform parent, string name, Sprite sprite,
                                           Color color, int order, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            // Law L6: LAYER_VFX with a SMALL order. Z_SKY is a Z depth, not a sorting order.
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Law L2, restated once here so every piece takes the same route to it.</summary>
        private static Color Overdrive(Color c, float gain, float alpha)
            => new Color(c.r * gain, c.g * gain, c.b * gain, Mathf.Clamp01(alpha));
    }
}
