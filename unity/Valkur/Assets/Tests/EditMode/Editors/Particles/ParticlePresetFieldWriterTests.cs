using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// The write-back behind the F1 Properties form. Pure logic on a throwaway
    /// ScriptableObject instance — no panel, no scene, and critically no real asset:
    /// these run in EditMode, where dirtying a catalog preset would reach disk.
    /// </summary>
    [TestFixture]
    public class ParticlePresetFieldWriterTests
    {
        private ParticlePresetDefinition _def;

        [SetUp]
        public void SetUp()
        {
            _def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _def.id = "__writer_probe";
            _def.displayName = "before";
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_def);

        // ── Key resolution ──────────────────────────────────────────────────

        /// <summary>
        /// The reason this class exists: GetField does not traverse into member objects,
        /// so the Spells editor's flat lookup would report every vfx field as missing.
        /// </summary>
        [Test]
        public void VfxPrefixedKey_ReachesTheNestedParams()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.emitRate", 42f, out var err);

            Assert.IsTrue(ok, err);
            Assert.AreEqual(42f, _def.vfx.emitRate);
        }

        [Test]
        public void UnprefixedKey_ReachesTheDefinitionItself()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "displayName", "after", out var err);

            Assert.IsTrue(ok, err);
            Assert.AreEqual("after", _def.displayName);
        }

        [Test]
        public void UnknownField_FailsWithAMessage_InsteadOfThrowing()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.nonsense", 1f, out var err);

            Assert.IsFalse(ok);
            StringAssert.Contains("nonsense", err,
                "The status line must name the field so a typo in a row key is findable.");
        }

        [Test]
        public void NullTarget_FailsGracefully()
        {
            // Both overloads: the writer now also targets a bare block, which is what a placed
            // instance's own configuration is.
            Assert.IsFalse(ParticlePresetFieldWriter.TrySetField(
                (ParticlePresetDefinition)null, "vfx.emitRate", 1f, out _));
            Assert.IsFalse(ParticlePresetFieldWriter.TrySetField(
                (ParticleVfxParams)null, "vfx.emitRate", 1f, out _));
        }

        [Test]
        public void BlockOverload_WritesTheInstancesOwnConfiguration_AndRefusesAssetFields()
        {
            var block = new ParticleVfxParams { emitRate = 5f };

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(block, "vfx.emitRate", 12f, out _));
            Assert.AreEqual(12f, block.emitRate, 1e-4f);

            // displayName names the ASSET. One placement renaming it would rename it for every
            // other placement, which is the coupling copy-on-place exists to remove.
            Assert.IsFalse(ParticlePresetFieldWriter.TrySetField(block, "displayName", "x", out string err));
            StringAssert.Contains("preset asset", err);
        }

        // ── Type conversion (what the form's rows actually emit) ────────────

        [Test]
        public void FloatField_AcceptsInt_AndString()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.speed", 3, out _));
            Assert.AreEqual(3f, _def.vfx.speed);

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.speed", "4.5", out _));
            Assert.AreEqual(4.5f, _def.vfx.speed, 0.0001f);
        }

        [Test]
        public void IntField_AcceptsFloat_ByRounding()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.count", 7.6f, out _));
            Assert.AreEqual(8, _def.vfx.count);
        }

        [Test]
        public void BoolField_AcceptsBool()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.additive", true, out _));
            Assert.IsTrue(_def.vfx.additive);
        }

        [Test]
        public void UnparseableString_FailsWithAMessage()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.speed", "fast", out var err);

            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        // ── Enums come from a dropdown as an index ──────────────────────────

        [Test]
        public void EnumField_AcceptsDropdownIndex()
        {
            int smokeIndex = System.Array.IndexOf(
                System.Enum.GetValues(typeof(ParticleTextureShape)), ParticleTextureShape.Smoke);

            bool ok = ParticlePresetFieldWriter.TrySetField(
                _def, "vfx.textureShape", smokeIndex, out var err);

            Assert.IsTrue(ok, err);
            Assert.AreEqual(ParticleTextureShape.Smoke, _def.vfx.textureShape);
        }

        [Test]
        public void EnumField_RejectsAnOutOfRangeIndex()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.textureShape", 999, out var err);

            Assert.IsFalse(ok);
            Assert.IsNotNull(err);
        }

        // ── [Range] is honoured ─────────────────────────────────────────────

        /// <summary>
        /// AddFloat happily accepts 9999. The Inspector would have clamped; the form must
        /// too, or a typo quietly breaks the preset's own invariants — drag above 0.98
        /// freezes particles solid.
        /// </summary>
        [Test]
        public void RangedFloat_IsClampedToItsAttribute()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.drag", 9999f, out var err);

            Assert.IsTrue(ok, err);
            Assert.AreEqual(0.98f, _def.vfx.drag, 0.0001f);

            ParticlePresetFieldWriter.TrySetField(_def, "vfx.noiseVerticalScale", -5f, out _);
            Assert.AreEqual(0f, _def.vfx.noiseVerticalScale);
        }

        // ── The spawn-area and direction fields ride the same path ──────────

        [Test]
        public void SpawnAreaAndDirection_AreWritable_LikeAnyOtherScalar()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.spawnWidth", 4f, out _));
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.spawnHeight", "1.5", out _));
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.directionDegrees", 270, out _));
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.directionSpreadDegrees", 30f, out _));

            Assert.AreEqual(4f, _def.vfx.spawnWidth);
            Assert.AreEqual(1.5f, _def.vfx.spawnHeight, 0.0001f);
            Assert.AreEqual(270f, _def.vfx.directionDegrees);
            Assert.AreEqual(30f, _def.vfx.directionSpreadDegrees);
        }

        /// <summary>
        /// Both default OFF: 0 area keeps the kind's built-in shape, -1 heading keeps its
        /// built-in behaviour. All ~128 existing presets must deserialize to exactly the
        /// emission they had before these fields existed.
        /// </summary>
        [Test]
        public void SpawnAreaAndDirection_DefaultToOff()
        {
            Assert.AreEqual(0f, _def.vfx.spawnWidth);
            Assert.AreEqual(0f, _def.vfx.spawnHeight);
            Assert.AreEqual(-1f, _def.vfx.directionDegrees);
        }

        // ── Colours: base, variation pair, intensity, gradient ──────────────

        [Test]
        public void ColorField_AcceptsHex_WithOrWithoutHash()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.color", "#FF0000", out _));
            Assert.AreEqual(Color.red, _def.vfx.color);

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.color", "00FF00", out _));
            Assert.AreEqual(Color.green, _def.vfx.color);
        }

        [Test]
        public void InvalidHex_FailsWithAMessage()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.color", "notacolour", out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("RRGGBB", err, "The message must teach the accepted format.");
        }

        /// <summary>
        /// BuildColorParameter randomises between cols[0] and cols[last] only, so A and B
        /// are the whole authorable surface. Editing one end of an empty array must grow it
        /// to two, seeded from the base colour so the untouched end keeps the preset's look.
        /// </summary>
        [Test]
        public void VariationPair_GrowsFromEmpty_SeededWithTheBaseColour()
        {
            _def.vfx.color = Color.cyan;
            _def.vfx.colors = null;

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.colors.a", "#FF0000", out _));

            Assert.AreEqual(2, _def.vfx.colors.Length);
            Assert.AreEqual(Color.red, _def.vfx.colors[0]);
            Assert.AreEqual(Color.cyan, _def.vfx.colors[1], "The B end must inherit the base.");
        }

        [Test]
        public void VariationPair_BSetsTheLastEntry()
        {
            _def.vfx.colors = new[] { Color.white, Color.grey, Color.black };

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.colors.b", "#0000FF", out _));

            Assert.AreEqual(Color.blue, _def.vfx.colors[2],
                "B is cols[last] — the entry the engine actually reads.");
            Assert.AreEqual(Color.white, _def.vfx.colors[0], "A untouched.");
        }

        /// <summary>
        /// colourOverLife is IGNORED by the engine unless alphaOverLife is authored. Editing
        /// a gradient stop on a preset without one must seed the exact legacy fade
        /// (1 -> 0.5 at 0.6 -> 0), or the user edits a colour, sees nothing change, and
        /// reasonably files it as a bug.
        /// </summary>
        [Test]
        public void GradientStop_OnEmptyPreset_BuildsThreeKeys_AndSeedsTheLegacyFade()
        {
            _def.vfx.colorOverLife = null;
            _def.vfx.alphaOverLife = null;

            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(
                _def, "vfx.colorOverLife.mid", "#FF0000", out var err), err);

            Assert.AreEqual(3, _def.vfx.colorOverLife.Length);
            Assert.AreEqual(0f, _def.vfx.colorOverLife[0].time);
            Assert.AreEqual(0.5f, _def.vfx.colorOverLife[1].time);
            Assert.AreEqual(1f, _def.vfx.colorOverLife[2].time);
            Assert.AreEqual(Color.red, _def.vfx.colorOverLife[1].color);

            Assert.AreEqual(3, _def.vfx.alphaOverLife.Length, "Legacy fade must be seeded.");
            Assert.AreEqual(0.6f, _def.vfx.alphaOverLife[1].time, 0.0001f);
            Assert.AreEqual(0.5f, _def.vfx.alphaOverLife[1].value, 0.0001f);
        }

        [Test]
        public void GradientStop_PreservesTheOtherStops()
        {
            ParticlePresetFieldWriter.TrySetField(_def, "vfx.colorOverLife.start", "#FF0000", out _);
            ParticlePresetFieldWriter.TrySetField(_def, "vfx.colorOverLife.end", "#0000FF", out _);
            ParticlePresetFieldWriter.TrySetField(_def, "vfx.colorOverLife.mid", "#00FF00", out _);

            Assert.AreEqual(Color.red, _def.vfx.colorOverLife[0].color);
            Assert.AreEqual(Color.green, _def.vfx.colorOverLife[1].color);
            Assert.AreEqual(Color.blue, _def.vfx.colorOverLife[2].color);
        }

        [Test]
        public void GradientStop_UnknownName_FailsWithGuidance()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(
                _def, "vfx.colorOverLife.banana", "#FF0000", out var err);
            Assert.IsFalse(ok);
            StringAssert.Contains("start", err);
        }

        [Test]
        public void ColorIntensity_IsAPlainFloatField()
        {
            Assert.IsTrue(ParticlePresetFieldWriter.TrySetField(_def, "vfx.colorIntensity", 1.8f, out _));
            Assert.AreEqual(1.8f, _def.vfx.colorIntensity, 0.0001f);
        }

        // ── Fields that need widgets are refused, not mangled ───────────────

        [Test]
        public void ArrayField_IsRefused_UntilItsWidgetExists()
        {
            bool ok = ParticlePresetFieldWriter.TrySetField(_def, "vfx.colors", "red", out var err);

            Assert.IsFalse(ok);
            StringAssert.Contains("widget", err,
                "The refusal must say WHY, so the gap reads as a stated limit, not a bug.");
        }

        [Test]
        public void SpriteField_IsRefused_UntilItsWidgetExists()
        {
            Assert.IsFalse(ParticlePresetFieldWriter.TrySetField(
                _def, "vfx.customSprite", "anything", out _));
        }
    }
}
