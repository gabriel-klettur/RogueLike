using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Tests.EditMode.Game.World.Weather
{
    /// <summary>
    /// Snow LYING on the world: the accumulation scalar and the shared materials that read it.
    ///
    /// The properties worth pinning are the ones that would each produce a specific, familiar
    /// kind of wrong:
    ///
    ///   • Cover never exceeds the density that made it, so a light flurry leaves a dusting.
    ///     Without that, every snowfall eventually ends in the same white world and the levels
    ///     stop meaning anything.
    ///   • The global is actually published. Everything downstream is a shader read, so a
    ///     value that never leaves C# is a feature that silently does nothing.
    ///   • Ground and silhouettes get DIFFERENT materials. One role for everything is the
    ///     failure that looks worst on screen: a blanket role on a wall paints its whole face
    ///     white, which reads as a missing texture rather than as snow.
    /// </summary>
    [TestFixture]
    public class SnowAccumulationTests
    {
        private static readonly int AmountId = Shader.PropertyToID("_ValkurSnowAmount");

        [SetUp]
        public void Reset() => SnowAccumulation.SetAmount(0f);

        [TearDown]
        public void Restore() => SnowAccumulation.SetAmount(0f);

        /// <summary>Advance the accumulation by <paramref name="seconds"/> at a fixed step.</summary>
        private static void Simulate(float seconds, float density, bool enabled = true)
        {
            const float step = 0.1f;
            int ticks = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < ticks; i++)
                SnowAccumulation.Tick(step, density, enabled);
        }

        // ── accumulation ─────────────────────────────────────────────────────────────

        [Test]
        public void AWorldWithNoSnow_StaysBare()
        {
            Simulate(60f, density: 0f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0f));
        }

        [Test]
        public void HeavySnow_EventuallyCoversTheWorld()
        {
            Simulate(150f, density: 1f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void Accumulation_IsSlowEnoughThatTheWorldIsNotSeenTurningWhite()
        {
            // Ten seconds of the heaviest snow in the game must still read as a dusting.
            Simulate(10f, density: 1f);
            Assert.That(SnowAccumulation.Amount, Is.LessThan(0.2f));
        }

        [Test]
        public void LightSnow_LeavesADusting_NotAFullCover()
        {
            // Light is 0.30 on the density scale; the cover must converge there and stop.
            Simulate(400f, density: 0.30f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0.30f).Within(0.01f));
        }

        [Test]
        public void RaisingTheLevel_DeepensAnExistingCover()
        {
            Simulate(400f, density: 0.30f);
            float dusting = SnowAccumulation.Amount;

            Simulate(400f, density: 1f);
            Assert.That(SnowAccumulation.Amount, Is.GreaterThan(dusting));
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(1f).Within(0.01f));
        }

        // ── melt ─────────────────────────────────────────────────────────────────────

        [Test]
        public void WhenTheSnowStops_TheWorldMeltsBackToBare()
        {
            SnowAccumulation.SetAmount(1f);
            Simulate(400f, density: 0f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0f));
        }

        [Test]
        public void MeltIsSlowerThanAccumulation_SoASnowfallOutlivesItself()
        {
            SnowAccumulation.SetAmount(1f);
            Simulate(60f, density: 0f);
            float afterMelt = 1f - SnowAccumulation.Amount;

            SnowAccumulation.SetAmount(0f);
            Simulate(60f, density: 1f);
            float afterFall = SnowAccumulation.Amount;

            Assert.That(afterMelt, Is.LessThan(afterFall),
                "snow that melts as fast as it falls never accumulates at all");
        }

        [Test]
        public void DisablingAccumulation_MeltsTheWorld_EvenWhileItIsSnowing()
        {
            // Freezing the drift where it stood would leave a half-covered world with the
            // feature switched off, which is worse than either end of the range.
            SnowAccumulation.SetAmount(1f);
            Simulate(400f, density: 1f, enabled: false);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0f));
        }

        // ── publication ──────────────────────────────────────────────────────────────

        [Test]
        public void TheCover_IsPublishedToTheShaders()
        {
            // Everything downstream is a shader read; a value that never leaves C# is a
            // feature that silently does nothing.
            SnowAccumulation.SetAmount(0.42f);
            Assert.That(Shader.GetGlobalFloat(AmountId), Is.EqualTo(0.42f).Within(1e-4f));

            SnowAccumulation.SetAmount(0f);
            Assert.That(Shader.GetGlobalFloat(AmountId), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void SetAmount_ClampsToTheLegalRange()
        {
            SnowAccumulation.SetAmount(4f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(1f));

            SnowAccumulation.SetAmount(-3f);
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0f));
        }

        // ── materials ────────────────────────────────────────────────────────────────

        [Test]
        public void EachSnowRole_GetsItsOwnSharedMaterial()
        {
            var cap     = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Cap);
            var blanket = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Blanket);
            var none    = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.None);

            Assert.That(cap,     Is.Not.Null);
            Assert.That(blanket, Is.Not.Null);
            Assert.That(none,    Is.Not.Null);

            Assert.That(cap, Is.Not.SameAs(blanket),
                "one role for everything paints wall faces white — the roles must not collapse");
            Assert.That(cap, Is.Not.SameAs(none));
        }

        [Test]
        public void SnowMaterials_AreShared_NotClonedPerCaller()
        {
            // Every tilemap layer and every placed building asks for one of these; a fresh
            // material per call would break batching across the entire world.
            var first  = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Cap);
            var second = WorldSpriteMaterials.WorldWithSnow(WorldSpriteMaterials.SnowRole.Cap);
            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void SetAmount_AlsoFillsTheAccumulationBuffer()
        {
            // The shader multiplies the global scalar by the local map value, so raising the
            // scalar over an empty buffer changes nothing on screen. `snow 1` has to mean
            // "pretend it has been snowing", in both halves, or the command silently no-ops.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device — the accumulation buffer cannot be exercised.");

            var go = new GameObject("Test_SnowSplatMap_ForSetAmount");
            try
            {
                var map = go.AddComponent<SnowSplatMap>();
                map.EnsureBuilt();
                map.Stamp(Vector2.zero);

                SnowAccumulation.SetAmount(0.5f);

                // Fill rewrote the buffer, which drops anything queued against the old state.
                Assert.That(map.PendingCount, Is.EqualTo(0));
                Assert.That(SnowAccumulation.Amount, Is.EqualTo(0.5f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetAmount_WorksWithNoAccumulationBufferInTheScene()
        {
            // The buffer is created by WeatherManager; every other entry point has to survive
            // its absence, because the console and the tests reach the scalar directly.
            //
            // Compared with Unity's == rather than NUnit's Is.Null: a destroyed UnityEngine
            // .Object is not C# null, and SetAmount's own guard is the same overloaded
            // operator — so this asserts exactly the condition the production code tests.
            if (SnowSplatMap.Instance != null)
                Object.DestroyImmediate(SnowSplatMap.Instance.gameObject);

            Assert.That(SnowSplatMap.Instance == null, Is.True, "a buffer survived into this test");
            Assert.DoesNotThrow(() => SnowAccumulation.SetAmount(0.3f));
            Assert.That(SnowAccumulation.Amount, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void SnowMaterials_CarryTheirRole()
        {
            foreach (WorldSpriteMaterials.SnowRole role in
                     System.Enum.GetValues(typeof(WorldSpriteMaterials.SnowRole)))
            {
                var mat = WorldSpriteMaterials.WorldWithSnow(role);
                Assert.That(mat.HasProperty("_SnowRole"), Is.True,
                    $"{role}: the material is not on a snow-capable shader");
                Assert.That(mat.GetFloat("_SnowRole"), Is.EqualTo((float)(int)role).Within(1e-4f), role.ToString());
            }
        }
    }
}
