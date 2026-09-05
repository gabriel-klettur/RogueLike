using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Creates a persistent ground field that damages whatever stands in it and applies the
    /// spell's authored status effects.
    ///
    /// <para><b>radius is WORLD UNITS.</b> It used to be divided by 16 here — the Python
    /// pixel scale this game was ported from, the same one that shipped <c>wallWidth</c>,
    /// the totem radius, the vortex radius, <c>range</c> on three executors and
    /// <c>coneLength</c>. The tell was always the same: the fallback for an unauthored field
    /// (4 WORLD units) was sixteen times anything the asset could produce.</para>
    ///
    /// <para><b>WHICH RIG A FIELD GETS IS DERIVED, NOT BRANCHED ON HERE.</b>
    /// <see cref="GroundFieldProfile"/> owns that decision, for the reason
    /// <see cref="ProjectileVisualProfile"/> owns the projectile one: the shape has to follow
    /// the verb or the two drift. Before it, <c>PuddleController</c> built
    /// <c>AreaPalette.LavaPuddle()</c> unconditionally, so <c>blizzard</c> — an Ice spell
    /// authoring <c>(0.72, 0.90, 1.00)</c> — was ORANGE and pixel-identical to
    /// <c>cinder_trail</c>.</para>
    /// </summary>
    public class PuddleExecutor : ISpellExecutor
    {
        /// <summary>How long each cinder patch burns when the spell authors no <c>ttl</c>.</summary>
        private const float DEFAULT_PATCH_TTL = 3.5f;

        public void Execute(SpellContext ctx)
        {
            float radius = ctx.Spell.radius > 0 ? ctx.Spell.radius : 4f;
            float duration = ctx.Spell.duration > 0 ? ctx.Spell.duration : 6f;
            float tickPeriod = ctx.Spell.tickPeriod > 0 ? ctx.Spell.tickPeriod : 0.25f;
            float damagePerTick = SpellPower.Scale(ctx.Spell.damagePerTick, ctx.Caster);
            float dist = ctx.Spell.distance > 0 ? ctx.Spell.distance : 2f;

            var profile = GroundFieldProfile.Resolve(ctx.Spell);

            if (profile.Shape == GroundFieldShape.Trail)
            {
                ExecuteTrail(ctx, radius, duration, tickPeriod, damagePerTick, profile);
                return;
            }

            Vector2 spawnPos = SpellTargeting.ResolveGroundTarget(ctx, 5f, dist);

            var puddleGo = new GameObject(NameFor(profile.Shape));
            puddleGo.transform.position = (Vector3)spawnPos;

            IGroundFieldVisual ownVisual = BuildVisual(profile, puddleGo.transform, radius, spawnPos);

            // A rig that brought its own visual keeps the legacy SpriteRenderer off entirely.
            // The old code attached one and then wrote localScale = radius * 0.5 over the root,
            // which is the pair of lines that sizes an owned rig's children twice and renders
            // its Light2D at `authored x lossyScale`.
            if (ownVisual == null) BuildLegacyPuddleSprite(ctx, puddleGo, radius);

            var controller = puddleGo.AddComponent<PuddleController>();
            controller.Initialize(duration, radius, Mathf.RoundToInt(damagePerTick), tickPeriod,
                ctx.TargetLayers, ctx.Spell.element, ctx.Caster != null ? ctx.Caster.gameObject : null,
                ProjectileExecutor.ResolveElement(ctx.Spell), ctx.Spell.statusApplications,
                ownVisual);

            // Every owned rig draws its own ground ring, pinned to the damage radius. A second
            // halo over it would be a second, contradictory promise about the same circle.
            if (ownVisual == null && VFXManager.Instance != null)
            {
                Color col = ctx.Spell.particleColor != Color.clear
                    ? ctx.Spell.particleColor
                    : new Color(1f, 0.47f, 0.24f, 0.6f);
                VFXManager.Instance.SpawnAreaIndicator((Vector3)spawnPos, col, radius, 0.4f);
            }

            // Free-standing world object: nothing else can end it. The registry enforces
            // maxInstances and clears it on a zone change.
            SpellEffectRegistry.Track(puddleGo, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        private static string NameFor(GroundFieldShape shape) => shape switch
        {
            GroundFieldShape.Roots => "SpellRootField",
            GroundFieldShape.Storm => "SpellBlizzard",
            _ => "SpellPuddle",
        };

        private static IGroundFieldVisual BuildVisual(GroundFieldProfile profile, Transform root,
            float radius, Vector2 spawnPos)
        {
            switch (profile.Shape)
            {
                case GroundFieldShape.Roots:
                    // Its own rig, in Spells/Visuals/ with every other one. It used to be a
                    // nested class in THIS file emitting a stretched-billboard particle at zero
                    // velocity, which makes the stretch axis undefined — nothing rose out of
                    // the ground.
                    var roots = RootWhipFX.Attach(root, radius, profile.Swatch);
                    RootWhipAudio.PlaySproutAt(spawnPos);
                    return roots;

                case GroundFieldShape.Storm:
                    return BlizzardFieldFX.Attach(root, radius, profile.Palette);

                default:
                    return null;
            }
        }

        /// <summary>
        /// The historical flat splat, kept for <c>puddle_lava</c> — the one field this executor
        /// drives that really is a pool lying on the floor, and the only spell
        /// <see cref="AreaFXRig"/>'s concentric discs were ever right for.
        /// </summary>
        private static void BuildLegacyPuddleSprite(SpellContext ctx, GameObject puddleGo, float radius)
        {
            var sr = puddleGo.AddComponent<SpriteRenderer>();
            if (ctx.Spell.sprite != null)
            {
                sr.sprite = ctx.Spell.sprite;
            }
            else
            {
                sr.sprite = CreatePuddleSprite();
                Color puddleColor = !string.IsNullOrEmpty(ctx.Spell.element) && ctx.Spell.element == "lava"
                    ? new Color(1f, 0.47f, 0.24f, 0.6f)
                    : new Color(0.4f, 0.8f, 0.3f, 0.6f);
                sr.color = puddleColor;
            }
            sr.sortingLayerName = "FloorDecals";
            sr.sortingOrder = 5;
            puddleGo.transform.localScale = Vector3.one * (radius * 0.5f);
        }

        /// <summary>
        /// A trail is not a puddle that moves — it is many small independent fires dropped
        /// behind a walking caster, which is a different TOPOLOGY and needs its own controller.
        /// <c>followCaster</c> and <c>ttl</c> were both authored on <c>cinder_trail</c> and read
        /// by nothing on this path, so the spell was a single static disc at the cursor.
        /// </summary>
        private static void ExecuteTrail(SpellContext ctx, float patchRadius, float duration,
            float tickPeriod, float damagePerTick, GroundFieldProfile profile)
        {
            var go = new GameObject("SpellCinderTrail");
            // The root sits where the caster started; every patch is placed in WORLD space as
            // the caster walks, so the root's own position is only an anchor for the hierarchy.
            go.transform.position = ctx.Caster != null
                ? ctx.Caster.position
                : (Vector3)ProjectileExecutor.ResolveCastStart(ctx.Caster, ctx.Direction, ctx.Spell);

            var controller = go.AddComponent<CinderTrailController>();
            controller.Initialize(
                caster: ctx.Caster,
                duration: duration,
                patchRadius: patchRadius,
                patchTtl: ctx.Spell.ttl > 0f ? ctx.Spell.ttl : DEFAULT_PATCH_TTL,
                damagePerTick: Mathf.RoundToInt(damagePerTick),
                tickPeriod: tickPeriod,
                targetLayers: ctx.TargetLayers,
                damageElement: ProjectileExecutor.ResolveElement(ctx.Spell),
                statusApplications: ctx.Spell.statusApplications,
                palette: profile.Palette);

            SpellEffectRegistry.Track(go, ctx.Spell, ctx.Caster != null ? ctx.Caster.gameObject : null);
        }

        /// <summary>
        /// One shared sprite for every legacy puddle. Generated once: it used to be built per
        /// cast and never released, leaking a 48x48 texture on each one.
        /// </summary>
        private static Sprite _puddleSprite;

        /// <summary>
        /// Domain Reload is OFF, so the managed handle survives a recompile while the native
        /// texture does not. A plain field assignment is the only reset shape
        /// <c>DomainReloadStaticResetTests</c> reads off the IL.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _puddleSprite = null;
        }

        private static Sprite CreatePuddleSprite()
        {
            if (_puddleSprite != null) return _puddleSprite;

            int size = 48;
            var tex = new Texture2D(size, size);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            float center = size / 2f;
            float rSq = center * center;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    float dSq = dx * dx + dy * dy;
                    pixels[y * size + x] = dSq <= rSq ? Color.white : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            _puddleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            return _puddleSprite;
        }
    }
}
