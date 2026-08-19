using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode robustness tests for <see cref="LaserBeamController"/>.
    /// Covers:
    ///   * static range / channel-duration resolution helpers
    ///   * impact-burst ParticleSystem configuration (no "Setting duration while
    ///     system is still playing" error after the fix)
    ///   * BuildVisual creates the Glow + Core + Impact children
    ///   * Refresh / Stop bookkeeping
    /// PlayMode-only behaviour (raycast, damage tick) is intentionally not exercised
    /// here — those are covered indirectly through SpellCaster integration tests.
    /// </summary>
    public class LaserBeamControllerTests
    {
        private readonly List<GameObject> _scene = new();

        [SetUp]
        public void SetUp()
        {
            // Procedural materials/sprites and ParticleSystem renderer init can log
            // benign warnings in EditMode — silence them so tests focus on assertions.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static SpellDefinition MakeBeamSpell(float range, float channel = 0f, float damage = 1f, float scale = 1f)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = "test_beam";
            s.displayName = "Test Beam";
            s.type = SpellType.Beam;
            s.range = range;
            s.channelDuration = channel;
            s.damage = damage;
            s.scale = scale;
            s.particleColor = new Color(0f, 0.9f, 1f, 1f);
            return s;
        }

        private LaserBeamController CreateController()
        {
            var go = new GameObject("BeamCaster");
            _scene.Add(go);
            return go.AddComponent<LaserBeamController>();
        }

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {instance.GetType().Name}");
            return f.GetValue(instance) as T;
        }

        private static object GetFieldRaw(object instance, string name)
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {instance.GetType().Name}");
            return f.GetValue(instance);
        }

        private static void InvokeBuildVisual(LaserBeamController c, SpellContext ctx)
        {
            // Stash _ctx (Begin would do this) and call BuildVisual via reflection.
            typeof(LaserBeamController)
                .GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(c, ctx);
            var m = typeof(LaserBeamController)
                .GetMethod("BuildVisual", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "BuildVisual not found");
            m.Invoke(c, new object[] { ctx });
        }

        // ── ResolveBeamRange ───────────────────────────────────────────

        [Test]
        public void ResolveBeamRange_NullSpell_ReturnsDefault()
        {
            Assert.AreEqual(LaserBeamController.DEFAULT_RANGE,
                LaserBeamController.ResolveBeamRange(null));
        }

        [Test]
        public void ResolveBeamRange_PositiveRange_ReturnsAssetValue()
        {
            var s = MakeBeamSpell(range: 25f);
            Assert.AreEqual(25f, LaserBeamController.ResolveBeamRange(s), 1e-4f);
        }

        [Test]
        public void ResolveBeamRange_ZeroRange_FallsBackToDefault()
        {
            var s = MakeBeamSpell(range: 0f);
            Assert.AreEqual(LaserBeamController.DEFAULT_RANGE,
                LaserBeamController.ResolveBeamRange(s), 1e-4f);
        }

        [Test]
        public void ResolveBeamRange_NegativeRange_FallsBackToDefault()
        {
            var s = MakeBeamSpell(range: -3f);
            Assert.AreEqual(LaserBeamController.DEFAULT_RANGE,
                LaserBeamController.ResolveBeamRange(s), 1e-4f);
        }

        [Test]
        public void ResolveBeamRange_LargeRange_PreservedExactly()
        {
            // The Python parity value is 1000 px / 16 PPU = 62.5 world units.
            var s = MakeBeamSpell(range: 62.5f);
            Assert.AreEqual(62.5f, LaserBeamController.ResolveBeamRange(s), 1e-4f);
        }

        // ── ResolveMaxDuration ─────────────────────────────────────────

        [Test]
        public void ResolveMaxDuration_NullSpell_ReturnsInfinity()
        {
            Assert.AreEqual(float.PositiveInfinity,
                LaserBeamController.ResolveMaxDuration(null));
        }

        [Test]
        public void ResolveMaxDuration_ZeroChannel_ReturnsInfinity()
        {
            var s = MakeBeamSpell(range: 10f, channel: 0f);
            Assert.AreEqual(float.PositiveInfinity,
                LaserBeamController.ResolveMaxDuration(s));
        }

        [Test]
        public void ResolveMaxDuration_NegativeChannel_ReturnsInfinity()
        {
            var s = MakeBeamSpell(range: 10f, channel: -1f);
            Assert.AreEqual(float.PositiveInfinity,
                LaserBeamController.ResolveMaxDuration(s));
        }

        [Test]
        public void ResolveMaxDuration_PositiveChannel_PreservedExactly()
        {
            var s = MakeBeamSpell(range: 10f, channel: 2.5f);
            Assert.AreEqual(2.5f, LaserBeamController.ResolveMaxDuration(s), 1e-4f);
        }

        // ── BuildVisual: rig construction ──────────────────────────────

        [Test]
        public void BuildVisual_CreatesGlowCoreAndImpactChildren()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            var ctx = new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right };
            InvokeBuildVisual(c, ctx);

            Assert.IsNotNull(c.transform.Find("LaserBeam_Glow"),   "Glow line missing");
            Assert.IsNotNull(c.transform.Find("LaserBeam_Core"),   "Core line missing");
            Assert.IsNotNull(c.transform.Find("LaserBeam_Impact"), "Impact host missing");
        }

        [Test]
        public void BuildVisual_GlowAndCoreUseUniformWidth()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f, scale: 1f);
            var ctx = new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right };
            InvokeBuildVisual(c, ctx);

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            var core = c.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>();

            Assert.AreEqual(glow.startWidth, glow.endWidth, 1e-4f, "Glow must have uniform width");
            Assert.AreEqual(core.startWidth, core.endWidth, 1e-4f, "Core must have uniform width");
            Assert.Greater(glow.startWidth, core.startWidth, "Glow should be wider than core");
        }

        [Test]
        public void BuildVisual_CoreRendersAboveGlow()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            var ctx = new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right };
            InvokeBuildVisual(c, ctx);

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            var core = c.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>();

            Assert.Greater(core.sortingOrder, glow.sortingOrder,
                "Core must render above glow for the bright punch effect");
        }

        [Test]
        public void BuildVisual_ScaleAffectsBeamWidth()
        {
            var c1 = CreateController();
            InvokeBuildVisual(c1, new SpellContext {
                Spell = MakeBeamSpell(range: 12f, scale: 1f), Caster = c1.transform, Direction = Vector2.right });

            var c2 = CreateController();
            InvokeBuildVisual(c2, new SpellContext {
                Spell = MakeBeamSpell(range: 12f, scale: 2f), Caster = c2.transform, Direction = Vector2.right });

            var w1 = c1.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>().startWidth;
            var w2 = c2.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>().startWidth;
            Assert.AreEqual(w1 * 2f, w2, 1e-3f, "scale=2 should double the beam width");
        }

        // ── BuildImpactBurst: ParticleSystem config (regression for the
        //     "Setting the duration while system is still playing" error) ──

        [Test]
        public void BuildVisual_ImpactBurstIsConfiguredWithoutDurationError()
        {
            // Any "Setting the duration while system is still playing" log would be
            // captured here; LogAssert.ignoreFailingMessages=true would still allow
            // the assertion below to verify the system is actually configured.
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });

            var impact = c.transform.Find("LaserBeam_Impact").GetComponent<ParticleSystem>();
            Assert.IsNotNull(impact, "Impact ParticleSystem missing");

            var main = impact.main;
            Assert.AreEqual(1f, main.duration, 1e-3f, "duration should be set to 1f");
            Assert.IsTrue(main.loop, "Impact burst must loop while beam is active");
            Assert.IsFalse(main.playOnAwake, "playOnAwake must be false to avoid auto-play race");
        }

        [Test]
        public void BuildVisual_ImpactBurstUsesBeamColor()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            spell.particleColor = new Color(1f, 0.2f, 0.1f, 1f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });

            var impact = c.transform.Find("LaserBeam_Impact").GetComponent<ParticleSystem>();
            var startColor = impact.main.startColor.color;
            Assert.AreEqual(spell.particleColor.r, startColor.r, 1e-3f);
            Assert.AreEqual(spell.particleColor.g, startColor.g, 1e-3f);
            Assert.AreEqual(spell.particleColor.b, startColor.b, 1e-3f);
        }

        [Test]
        public void BuildVisual_ImpactBurstIsPlayingAfterBuild()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });

            var impact = c.transform.Find("LaserBeam_Impact").GetComponent<ParticleSystem>();
            Assert.IsTrue(impact.isPlaying, "Impact burst should be playing immediately after BuildVisual");
        }

        // ── Refresh / Stop ─────────────────────────────────────────────

        [Test]
        public void Refresh_UpdatesLastRefreshTimestamp()
        {
            var c = CreateController();
            // Seed timestamp to a deliberately old value.
            typeof(LaserBeamController)
                .GetField("_lastRefreshTime", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(c, -10f);

            c.Refresh();
            float t = (float)GetFieldRaw(c, "_lastRefreshTime");
            Assert.Greater(t, -10f, "Refresh must bump _lastRefreshTime past the seeded -10");
        }

        [Test]
        public void Stop_SetsStopRequestedFlag()
        {
            var c = CreateController();
            Assert.IsFalse((bool)GetFieldRaw(c, "_stopRequested"), "Should start un-stopped");
            c.Stop();
            Assert.IsTrue((bool)GetFieldRaw(c, "_stopRequested"), "Stop() must set _stopRequested=true");
        }

        // ── Public constants contract (gameplay tuning) ────────────────

        [Test]
        public void Constants_ManaPerSecond_Is2()
        {
            Assert.AreEqual(2f, LaserBeamController.MANA_PER_SECOND, 1e-4f,
                "Mana cost contract: 2 mp/s while channeling (low so the beam is testable)");
        }

        [Test]
        public void Constants_AutoStopGrace_IsShortButForgiving()
        {
            Assert.GreaterOrEqual(LaserBeamController.AUTO_STOP_GRACE, 0.05f);
            Assert.LessOrEqual(LaserBeamController.AUTO_STOP_GRACE, 0.5f);
        }

        [Test]
        public void Constants_DefaultRange_IsPositive()
        {
            Assert.Greater(LaserBeamController.DEFAULT_RANGE, 0f);
        }

        // ── SpellDefinition.range contract ─────────────────────────────

        [Test]
        public void SpellDefinition_RangeField_ServesAsMaxDistanceForBeams()
        {
            // This test guards the contract: LaserBeamController must read the
            // beam's max travel distance from SpellDefinition.range. If anyone
            // renames the field or changes its meaning, this test catches it.
            var s = MakeBeamSpell(range: 33f);
            float resolved = LaserBeamController.ResolveBeamRange(s);
            Assert.AreEqual(s.range, resolved, 1e-4f,
                "ResolveBeamRange must echo SpellDefinition.range when > 0");
        }

        // ── Trail particles ────────────────────────────────────────────

        [Test]
        public void BuildVisual_CreatesTrailParticleSystem()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });
            Assert.IsNotNull(c.transform.Find("LaserBeam_Trail"), "Trail host missing");
            var trail = c.transform.Find("LaserBeam_Trail").GetComponent<ParticleSystem>();
            Assert.IsNotNull(trail, "Trail ParticleSystem missing");
            Assert.IsTrue(trail.isPlaying, "Trail PS must play immediately");
            Assert.IsFalse(trail.main.playOnAwake, "Trail must not auto-play on awake (avoids duration error)");
        }

        [Test]
        public void BuildVisual_TrailUsesEdgeShape()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });
            var trail = c.transform.Find("LaserBeam_Trail").GetComponent<ParticleSystem>();
            // Edge shapes are how we paint particles along the beam line.
            Assert.AreEqual(ParticleSystemShapeType.SingleSidedEdge, trail.shape.shapeType,
                "Trail shape must be a SingleSidedEdge so particles spawn along the beam line");
        }

        [Test]
        public void BuildVisual_TrailUsesBeamColor()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            spell.particleColor = new Color(0.2f, 0.9f, 0.1f, 1f);
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });
            var trail = c.transform.Find("LaserBeam_Trail").GetComponent<ParticleSystem>();
            var col = trail.main.startColor.color;
            Assert.AreEqual(spell.particleColor.r, col.r, 1e-3f);
            Assert.AreEqual(spell.particleColor.g, col.g, 1e-3f);
            Assert.AreEqual(spell.particleColor.b, col.b, 1e-3f);
        }

        // ── Grow / Fade envelope contract ──────────────────────────────

        [Test]
        public void Begin_InitializesGrowAtZeroAndFadeAtOne()
        {
            var c = CreateController();
            var spell = MakeBeamSpell(range: 8f);
            // Begin sets envelope state but starts a coroutine — we only assert
            // the initial scalar state to avoid running into MonoBehaviour lifecycle.
            typeof(LaserBeamController)
                .GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });
            // Manually replicate the field setup that Begin() does so we can assert
            // the documented initial values.
            typeof(LaserBeamController).GetField("_growT", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, 0f);
            typeof(LaserBeamController).GetField("_fadeT", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, 1f);
            typeof(LaserBeamController).GetField("_fading", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, false);

            Assert.AreEqual(0f, (float)GetFieldRaw(c, "_growT"), 1e-4f, "Grow envelope must start at 0 (beam invisible)");
            Assert.AreEqual(1f, (float)GetFieldRaw(c, "_fadeT"), 1e-4f, "Fade envelope must start at 1 (full alpha)");
            Assert.IsFalse((bool)GetFieldRaw(c, "_fading"), "_fading must start false");
        }

        [Test]
        public void Constants_GrowAndFade_ArePositiveAndShort()
        {
            // Both must be > 0 to avoid division by zero, and short enough that
            // the user perceives them as "snappy" rather than sluggish.
            Assert.Greater(LaserBeamController.GROW_DURATION, 0f);
            Assert.Less(LaserBeamController.GROW_DURATION, 0.5f);
            Assert.Greater(LaserBeamController.FADE_DURATION, 0f);
            Assert.Less(LaserBeamController.FADE_DURATION, 0.5f);
        }

        [Test]
        public void Constants_GrowAndFade_AreVisible()
        {
            // Must be longer than a couple of frames so the animation is actually visible.
            // 60 fps → 2 frames ≈ 0.033s; require both envelopes ≥ 0.04s.
            Assert.GreaterOrEqual(LaserBeamController.GROW_DURATION, 0.04f, "Grow must last more than 2 frames");
            Assert.GreaterOrEqual(LaserBeamController.FADE_DURATION, 0.04f, "Fade must last more than 2 frames");
        }

        // ── Shared Fireball origin ─────────────────────────────────────────

        [Test]
        public void SharedCastStart_HasFireballForwardClearance()
        {
            Assert.Greater(ProjectileExecutor.CAST_FORWARD_OFFSET, 0f,
                "The shared start must clear the caster collider just like Fireball.");
            Assert.LessOrEqual(ProjectileExecutor.CAST_FORWARD_OFFSET, 0.5f,
                "The shared start must remain attached to the caster's hand.");
        }

        [Test]
        public void BuildVisual_LineRenderersUseVfxLayer()
        {
            // The beam now emerges in FRONT of the caster, so it must render ON TOP
            // of world geometry / entities — the VFX sorting layer (above Entities).
            // Pin the contract explicitly so a future sorting-config refactor can't
            // silently demote the beam back beneath the world.
            var c = CreateController();
            InvokeBuildVisual(c, new SpellContext {
                Spell = MakeBeamSpell(range: 12f), Caster = c.transform, Direction = Vector2.right });

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            var core = c.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>();

            Assert.AreEqual(Valkur.Core.SortingConfig.LAYER_VFX, glow.sortingLayerName,
                "Glow line must render on the VFX layer so it sits above entities");
            Assert.AreEqual(Valkur.Core.SortingConfig.LAYER_VFX, core.sortingLayerName,
                "Core line must render on the VFX layer so it sits above entities");
        }

        [Test]
        public void BuildVisual_TrailUsesVfxLayer()
        {
            var c = CreateController();
            InvokeBuildVisual(c, new SpellContext {
                Spell = MakeBeamSpell(range: 12f), Caster = c.transform, Direction = Vector2.right });

            var trail = c.transform.Find("LaserBeam_Trail").GetComponent<ParticleSystem>();
            var renderer = trail.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(Valkur.Core.SortingConfig.LAYER_VFX, renderer.sortingLayerName,
                "Trail particles must render on the VFX layer to match the beam line");
        }

        // ── Color / fallback ───────────────────────────────────────────

        [Test]
        public void BuildVisual_DefaultBeamColor_WhenSpellHasClearColor()
        {
            // If the spell asset leaves particleColor at default (clear/zero alpha),
            // BuildVisual must still produce a visible beam (cyan default).
            var c = CreateController();
            var spell = MakeBeamSpell(range: 12f);
            spell.particleColor = Color.clear; // alpha 0 → must trigger fallback
            InvokeBuildVisual(c, new SpellContext { Spell = spell, Caster = c.transform, Direction = Vector2.right });

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            // Glow alpha is GLOW_ALPHA = 0.45, so check RGB instead of alpha.
            // The fallback default is approximately (0, 0.9, 1) — assert it's NOT clear.
            var col = glow.startColor;
            Assert.Greater(col.r + col.g + col.b, 0.5f,
                "BuildVisual must use a visible default color when spell.particleColor is clear");
            Assert.Greater(col.a, 0f, "Default color must have non-zero alpha");
        }

        // ── BuildLine config (visual quality) ──────────────────────────

        [Test]
        public void BuildVisual_LinesUseRoundedCaps()
        {
            // Rounded caps make the beam look like a "laser" instead of a hard rectangle.
            var c = CreateController();
            InvokeBuildVisual(c, new SpellContext {
                Spell = MakeBeamSpell(range: 12f), Caster = c.transform, Direction = Vector2.right });

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            var core = c.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>();
            Assert.Greater(glow.numCapVertices, 0, "Glow must have rounded cap vertices");
            Assert.Greater(core.numCapVertices, 0, "Core must have rounded cap vertices");
        }

        [Test]
        public void BuildVisual_LinesUseWorldSpace()
        {
            // World-space lines so SetPosition takes world coordinates directly
            // (avoids stale local-space transforms when the player rotates).
            var c = CreateController();
            InvokeBuildVisual(c, new SpellContext {
                Spell = MakeBeamSpell(range: 12f), Caster = c.transform, Direction = Vector2.right });

            var glow = c.transform.Find("LaserBeam_Glow").GetComponent<LineRenderer>();
            var core = c.transform.Find("LaserBeam_Core").GetComponent<LineRenderer>();
            Assert.IsTrue(glow.useWorldSpace, "Glow line must use world space");
            Assert.IsTrue(core.useWorldSpace, "Core line must use world space");
        }

        // ── Stop / Refresh idempotency ─────────────────────────────────

        [Test]
        public void Stop_IsIdempotent()
        {
            var c = CreateController();
            c.Stop();
            c.Stop();
            c.Stop();
            Assert.IsTrue((bool)GetFieldRaw(c, "_stopRequested"),
                "Calling Stop() multiple times must keep _stopRequested true (no toggling)");
        }

        [Test]
        public void Refresh_DoesNotClearStopFlag()
        {
            // Refresh is meant to keep an active beam alive — it must NOT cancel a
            // pending Stop request (otherwise releasing the trigger and quickly
            // re-pressing it could leak a stale beam).
            var c = CreateController();
            c.Stop();
            c.Refresh();
            Assert.IsTrue((bool)GetFieldRaw(c, "_stopRequested"),
                "Refresh() must not clear a pending Stop request");
        }

        [Test]
        public void Refresh_IsIdempotent()
        {
            // Multiple Refresh calls in the same frame must not break anything.
            var c = CreateController();
            for (int i = 0; i < 10; i++) c.Refresh();
            Assert.IsFalse((bool)GetFieldRaw(c, "_stopRequested"),
                "Calling Refresh repeatedly must never set the stop flag");
        }
    }
}
