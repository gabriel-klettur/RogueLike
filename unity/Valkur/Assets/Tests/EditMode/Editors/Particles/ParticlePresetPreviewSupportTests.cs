using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Editor;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Covers the decision half of the Inspector preset preview.
    ///
    /// The rendering half needs a live PreviewRenderUtility and cannot be asserted in
    /// EditMode. The decision half — which presets can be previewed at all, and how the
    /// camera frames them before anything has been simulated — is pure, and it is the
    /// part that goes stale: when the lightning kind stops being coroutine-driven, or a
    /// new kind arrives with its own constraint, this is what must change with it.
    /// </summary>
    [TestFixture]
    public class ParticlePresetPreviewSupportTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ParticlePresetDefinition MakePreset(string kind, System.Action<ParticleVfxParams> tweak = null)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "preview_" + kind;
            def.displayName = kind;
            def.vfx = new ParticleVfxParams { kind = kind };
            tweak?.Invoke(def.vfx);
            return def;
        }

        // ── Previewable ──────────────────────────────────────────────────────────

        [Test]
        public void IsPreviewable_OrdinaryKinds_AreSupported()
        {
            foreach (var kind in new[] { "aura", "explosion", "portal", "smoke", "slash", "water_flow", "falling_leaf" })
                Assert.IsTrue(ParticlePresetPreviewSupport.IsPreviewable(MakePreset(kind)),
                    $"'{kind}' is a plain ParticleSystem preset and must preview in the Inspector.");
        }

        [Test]
        public void IsPreviewable_Lightning_IsNotSupported()
        {
            var preset = MakePreset(ParticlePresetPreviewSupport.LIGHTNING_KIND);

            Assert.IsFalse(ParticlePresetPreviewSupport.IsPreviewable(preset),
                "Lightning is driven by a while(true) coroutine, and coroutines do not advance " +
                "outside Play Mode — it would render frozen, which reads as broken rather than " +
                "unsupported.");
        }

        [Test]
        public void UnsupportedReason_Lightning_TellsTheUserWhereToLookInstead()
        {
            var reason = ParticlePresetPreviewSupport.UnsupportedReason(
                MakePreset(ParticlePresetPreviewSupport.LIGHTNING_KIND));

            Assert.IsNotNull(reason);
            StringAssert.Contains("F1", reason,
                "A blocked preview must point somewhere useful, not just decline.");
        }

        [Test]
        public void UnsupportedReason_SupportedKind_IsNull()
        {
            Assert.IsNull(ParticlePresetPreviewSupport.UnsupportedReason(MakePreset("aura")));
        }

        // ── Degenerate input ─────────────────────────────────────────────────────

        [Test]
        public void UnsupportedReason_NullPreset_DoesNotThrow()
        {
            string reason = null;
            Assert.DoesNotThrow(() => reason = ParticlePresetPreviewSupport.UnsupportedReason(null));
            Assert.IsNotNull(reason, "A null selection must produce a message, not a blank panel.");
        }

        [Test]
        public void UnsupportedReason_PresetWithNoVfx_IsReportedNotCrashed()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "no_vfx";
            def.vfx = null;

            Assert.IsNotNull(ParticlePresetPreviewSupport.UnsupportedReason(def));
        }

        // ── Framing ──────────────────────────────────────────────────────────────

        [Test]
        public void InitialOrthoSize_IsClampedToAUsableRange()
        {
            var tiny = MakePreset("aura", v => { v.radius = 0.01f; v.speed = 0f; v.lifespan = 0.1f; v.sizeMax = 0f; });
            var huge = MakePreset("aura", v => { v.radius = 500f; v.speed = 100f; v.lifespan = 30f; v.sizeMax = 10f; });

            float small = ParticlePresetPreviewSupport.InitialOrthoSize(tiny);
            float large = ParticlePresetPreviewSupport.InitialOrthoSize(huge);

            Assert.GreaterOrEqual(small, 1.5f, "Too small and a single particle fills the whole panel.");
            Assert.LessOrEqual(large, 8f, "Too large and the effect is an invisible speck.");
        }

        [Test]
        public void InitialOrthoSize_GrowsWithHowFarParticlesTravel()
        {
            var slow = MakePreset("aura", v => { v.speed = 1f; v.lifespan = 1f; });
            var fast = MakePreset("aura", v => { v.speed = 4f; v.lifespan = 1f; });

            Assert.Greater(ParticlePresetPreviewSupport.InitialOrthoSize(fast),
                           ParticlePresetPreviewSupport.InitialOrthoSize(slow),
                "A preset whose particles fly further needs a wider frame, or it is cropped on " +
                "the very first repaint — which is when the user forms their impression of it.");
        }

        [Test]
        public void InitialOrthoSize_AccountsForEmissionRadius()
        {
            var tight = MakePreset("portal", v => { v.radius = 0.5f; v.speed = 0f; v.lifespan = 1f; });
            var wide  = MakePreset("portal", v => { v.radius = 4f;   v.speed = 0f; v.lifespan = 1f; });

            Assert.Greater(ParticlePresetPreviewSupport.InitialOrthoSize(wide),
                           ParticlePresetPreviewSupport.InitialOrthoSize(tight),
                "A portal emits from its rim; frame the rim, not the centre.");
        }

        [Test]
        public void InitialOrthoSize_UsesOuterRadiusWhenItIsTheLargerReach()
        {
            var preset = MakePreset("portal", v => { v.radius = 0.5f; v.outerRadius = 5f; v.speed = 0f; v.lifespan = 1f; });

            Assert.Greater(ParticlePresetPreviewSupport.InitialOrthoSize(preset), 1.5f,
                "outerRadius overrides radius for the portal kind, so framing must read it too.");
        }

        [Test]
        public void InitialOrthoSize_NullPreset_ReturnsTheMinimumInsteadOfThrowing()
        {
            Assert.AreEqual(1.5f, ParticlePresetPreviewSupport.InitialOrthoSize(null), 1e-4f);
        }
    }
}
