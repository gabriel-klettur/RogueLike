using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the white hit flash. Two independent faults kept it invisible and
    /// each one alone is enough to break it again:
    ///
    ///   1. Nothing ever attached <see cref="CombatFeedback"/> to a monster, so
    ///      the component that flashes was simply not there.
    ///   2. The flash tinted <c>SpriteRenderer.color</c> white — a multiply.
    ///      <c>EntityAnimationBinder</c> leaves NPC sprites at white, so tinting
    ///      them white changed nothing at all.
    ///
    /// The tests below cover both, plus the shader uniform the fix depends on.
    /// </summary>
    [TestFixture]
    public class CombatFeedbackHitFlashTests
    {
        private GameObject _npc;
        private SpriteRenderer _renderer;
        private CombatFeedback _feedback;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _npc = new GameObject("Npc");
            _renderer = _npc.AddComponent<SpriteRenderer>();
            // Production material first: the component snapshots which renderers
            // can flash through the shader when it initialises.
            EntitySpriteHelper.EnsureUnlitMaterial(_renderer);

            var health = _npc.AddComponent<Health>();
            health.Initialize(100);

            _feedback = _npc.AddComponent<CombatFeedback>();
            // Awake does not fire reliably outside play mode.
            _feedback.EnsureHitFlashReady();
        }

        [TearDown]
        public void TearDown()
        {
            if (_npc != null) Object.DestroyImmediate(_npc);
        }

        private static float ReadFlashUniform(SpriteRenderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetFloat("_FlashAmount");
        }

        // ── The flash itself ────────────────────────────────────────────────

        [Test]
        public void TriggerHitFlash_GoesToFullStrength()
        {
            Assert.AreEqual(0f, _feedback.FlashAmount, "Sanity: resting sprite is not flashed.");

            _feedback.TriggerHitFlash();

            Assert.AreEqual(1f, _feedback.FlashAmount, 0.001f);
            Assert.IsTrue(_feedback.IsFlashing);
        }

        [Test]
        public void TheFlashDrivesTheShaderUniformNotAColourMultiply()
        {
            Assert.IsTrue(_feedback.UsesShaderFlash,
                "The production sprite material must expose _FlashAmount. Without it the " +
                "flash falls back to tinting SpriteRenderer.color, which cannot brighten " +
                "the white-tinted sprites EntityAnimationBinder produces — the exact bug " +
                "that made NPC hits look like nothing happened.");

            _feedback.TriggerHitFlash();

            Assert.AreEqual(1f, ReadFlashUniform(_renderer), 0.001f,
                "A hit must push _FlashAmount to 1 on the renderer.");
            Assert.AreEqual(Color.white, _renderer.color,
                "The shader path must leave SpriteRenderer.color alone so GrayscaleDeath " +
                "and the death fade keep a clean lerp target.");
        }

        [Test]
        public void TheFlashFadesOutAndReleasesTheSprite()
        {
            _feedback.TriggerHitFlash();

            // 0.4 s comfortably outlasts the default 0.14 s flash.
            for (int i = 0; i < 40; i++) _feedback.TickHitFlash(0.01f);

            Assert.AreEqual(0f, _feedback.FlashAmount, 0.001f);
            Assert.IsFalse(_feedback.IsFlashing);
            Assert.AreEqual(0f, ReadFlashUniform(_renderer), 0.001f,
                "A stuck uniform would leave the NPC permanently white.");
        }

        [Test]
        public void TheFlashHoldsBeforeItRamps()
        {
            _feedback.TriggerHitFlash();

            // One short step must still read as a full-strength hit: an immediate
            // ramp makes a fast hit register as a faint shimmer.
            _feedback.TickHitFlash(0.01f);
            Assert.AreEqual(1f, _feedback.FlashAmount, 0.001f);

            // Well into the ramp it must be partway down but not gone.
            for (int i = 0; i < 10; i++) _feedback.TickHitFlash(0.01f);
            Assert.Less(_feedback.FlashAmount, 1f);
            Assert.Greater(_feedback.FlashAmount, 0f);
        }

        [Test]
        public void RetriggeringMidFlashRestartsAtFullStrength()
        {
            _feedback.TriggerHitFlash();
            for (int i = 0; i < 10; i++) _feedback.TickHitFlash(0.01f);
            Assert.Less(_feedback.FlashAmount, 1f, "Sanity: the flash has started ramping.");

            _feedback.TriggerHitFlash();
            Assert.AreEqual(1f, _feedback.FlashAmount, 0.001f,
                "A second hit landing mid-flash must read as a second hit.");

            for (int i = 0; i < 40; i++) _feedback.TickHitFlash(0.01f);
            Assert.AreEqual(Color.white, _renderer.color,
                "Retriggering must not capture the flashed colour as the colour to restore.");
        }

        // ── Fallback for materials without the uniform ──────────────────────

        [Test]
        public void AMaterialWithoutTheUniformFallsBackToTinting()
        {
            var legacy = new GameObject("LegacyNpc");
            try
            {
                var renderer = legacy.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                renderer.color = new Color(0.4f, 0.25f, 0.15f, 1f);   // a tinted sprite

                legacy.AddComponent<Health>().Initialize(50);
                var feedback = legacy.AddComponent<CombatFeedback>();
                feedback.EnsureHitFlashReady();

                Assert.IsFalse(feedback.UsesShaderFlash);

                feedback.TriggerHitFlash();
                Assert.AreEqual(Color.white, renderer.color,
                    "Without the uniform the flash must still do something visible on a " +
                    "tinted sprite rather than silently no-op.");

                for (int i = 0; i < 40; i++) feedback.TickHitFlash(0.01f);
                Assert.AreEqual(0.4f, renderer.color.r, 0.01f,
                    "The fallback must restore the sprite's own tint when the flash ends.");
            }
            finally { Object.DestroyImmediate(legacy); }
        }

        // ── Regressions found on a live monster ─────────────────────────────

        [Test]
        public void TheMaterialCapabilityIsRecheckedNotFrozenAtStartup()
        {
            var late = new GameObject("LateMaterialNpc");
            try
            {
                var renderer = late.AddComponent<SpriteRenderer>();
                renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

                late.AddComponent<Health>().Initialize(50);
                var feedback = late.AddComponent<CombatFeedback>();
                feedback.EnsureHitFlashReady();
                Assert.IsFalse(feedback.UsesShaderFlash, "Sanity: it cannot flash yet.");

                // This is the real spawn order. A monster prefab carries its
                // components, so Awake runs during Instantiate — before
                // EntitySetup.ConfigureMonster swaps in the HDR sprite material.
                EntitySpriteHelper.EnsureUnlitMaterial(renderer);

                Assert.IsTrue(feedback.UsesShaderFlash,
                    "The flash must re-read the material. Deciding once at startup is what " +
                    "left every prefab-spawned NPC stuck on the fallback path, tinting an " +
                    "already-white sprite white and showing nothing.");

                feedback.TriggerHitFlash();
                Assert.AreEqual(1f, ReadFlashUniform(renderer), 0.001f);
            }
            finally { Object.DestroyImmediate(late); }
        }

        [Test]
        public void TheWorldSpaceBarsAreNotFlashed()
        {
            // WorldHealthBar / WorldDashBar / WorldManaBar build their sprites as
            // children of the entity. Flashing every SpriteRenderer in the hierarchy
            // whites out the HP bar on every single hit.
            var barFill = new GameObject("Fill");
            barFill.transform.SetParent(_npc.transform, false);
            var fillRenderer = barFill.AddComponent<SpriteRenderer>();
            var barColor = new Color(0.2f, 0.9f, 0.2f, 1f);
            fillRenderer.color = barColor;

            _feedback.TriggerHitFlash();

            Assert.AreEqual(barColor, fillRenderer.color,
                "The health bar is not part of the creature. It must keep its own colour " +
                "while the body flashes.");
        }

        // ── The component has to actually be on the NPC ─────────────────────

        [Test]
        public void ConfigureMonster_AttachesTheComponentThatFlashes()
        {
            var monster = new GameObject("Barbol");
            var definition = ScriptableObject.CreateInstance<MonsterDefinition>();
            definition.monsterKey = "test_monster";
            definition.displayName = "Test Monster";

            try
            {
                monster.AddComponent<SpriteRenderer>();
                monster.AddComponent<Health>();

                EntitySetup.ConfigureMonster(monster, definition);

                Assert.IsNotNull(monster.GetComponent<CombatFeedback>(),
                    "Monsters must be given CombatFeedback at spawn. For a long time nothing " +
                    "attached it, so NPCs took damage and never flashed.");
            }
            finally
            {
                EntityRegistry.UnregisterMonster(monster);
                Object.DestroyImmediate(monster);
                Object.DestroyImmediate(definition);
            }
        }

        // ── The shader the fix rides on ─────────────────────────────────────

        [Test]
        public void TheSpriteShaderStillDeclaresTheFlashUniforms()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Shaders", "SpriteHDRTint.shader");
            Assert.IsTrue(File.Exists(path), "Valkur/SpriteHDRTint is missing.");

            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("_FlashAmount"),
                "Dropping _FlashAmount silently disables every hit flash in the game.");
            Assert.IsTrue(source.Contains("_FlashColor"));
            Assert.IsTrue(source.Contains("lerp(c.rgb, _FlashColor.rgb, _FlashAmount)"),
                "The flash has to replace the fragment colour. A multiply cannot brighten a " +
                "sprite that is already white.");
        }
    }
}
