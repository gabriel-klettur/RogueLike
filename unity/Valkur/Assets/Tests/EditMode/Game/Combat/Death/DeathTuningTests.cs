using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// The tuning asset: its resolution contract, its altar list, and the invariant that keeps its
    /// undo honest.
    /// </summary>
    public class DeathTuningTests
    {
        private DeathTuning _scratch;

        [SetUp]
        public void SetUp()
        {
            _scratch = ScriptableObject.CreateInstance<DeathTuning>();
            _scratch.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            if (_scratch != null) Object.DestroyImmediate(_scratch);
            DeathTuning.InvalidateCache();
        }

        /// <summary>
        /// <c>Active</c> may never be null. Every reader in the death flow calls it without a null
        /// check on purpose — a tuning layer that changes behaviour by being ABSENT is worse than
        /// no tuning layer, because the failure only appears in the builds that stripped it.
        /// </summary>
        [Test]
        public void Active_IsNeverNull_EvenWithNoAsset()
        {
            DeathTuning.InvalidateCache();
            Assert.That(DeathTuning.Active, Is.Not.Null);
        }

        [Test]
        public void IsAltarTemplate_AnswersForEveryListedId_AndRefusesOthers()
        {
            _scratch.altarTemplateIds = new[] { 197, 249 };

            Assert.That(_scratch.IsAltarTemplate(197), Is.True);
            Assert.That(_scratch.IsAltarTemplate(249), Is.True);
            Assert.That(_scratch.IsAltarTemplate(248), Is.False);
            Assert.That(_scratch.IsAltarTemplate(0), Is.False);
        }

        [Test]
        public void IsAltarTemplate_IsFalse_WhenTheListIsNull()
        {
            _scratch.altarTemplateIds = null;
            Assert.That(_scratch.IsAltarTemplate(197), Is.False);
        }

        [Test]
        public void AddAltarTemplate_IsIdempotent()
        {
            _scratch.altarTemplateIds = new[] { 197 };

            Assert.That(_scratch.AddAltarTemplate(300), Is.True);
            Assert.That(_scratch.AddAltarTemplate(300), Is.False, "a second add must report that nothing changed");
            Assert.That(_scratch.altarTemplateIds, Is.EquivalentTo(new[] { 197, 300 }));
        }

        /// <summary>
        /// A duplicated id must not leave a trailing zero behind, because <b>0 is a template id</b>
        /// as far as <c>IsAltarTemplate</c> is concerned — and a stray 0 would silently make every
        /// building whose template failed to resolve into a resurrection altar.
        /// </summary>
        [Test]
        public void RemoveAltarTemplate_LeavesNoPhantomZero_EvenWithDuplicates()
        {
            _scratch.altarTemplateIds = new[] { 197, 249, 197 };

            Assert.That(_scratch.RemoveAltarTemplate(197), Is.True);
            Assert.That(_scratch.altarTemplateIds, Is.EqualTo(new[] { 249 }));
            Assert.That(_scratch.IsAltarTemplate(0), Is.False);
        }

        [Test]
        public void RemoveAltarTemplate_ReportsFalse_ForAnIdThatWasNotListed()
        {
            _scratch.altarTemplateIds = new[] { 197 };
            Assert.That(_scratch.RemoveAltarTemplate(999), Is.False);
        }

        /// <summary>
        /// The defaults must describe a WORKING flow, not a disabled one.
        ///
        /// <para>Each of these is a value whose "safe-looking" alternative is what shipped: a
        /// rescue mode of None, an empty altar list, a spirit limit of 0 meaning forever. Every one
        /// of them reads as a conservative default and every one of them recreates the dead end
        /// this whole subsystem was rebuilt to make impossible.</para>
        /// </summary>
        [Test]
        public void Defaults_DescribeAFlowWithAnExit()
        {
            Assert.That(_scratch.altarTemplateIds, Is.Not.Empty,
                "with no altar template nothing in the world can ever be an altar");
            Assert.That(_scratch.rescueMode, Is.Not.EqualTo(DeathRescueMode.None),
                "the safety net is the only thing that makes a dead end impossible");
            Assert.That(_scratch.spiritTimeLimitSeconds, Is.GreaterThan(0f),
                "a limit of 0 means the rescue's long clock never fires");
            Assert.That(_scratch.rescueDelayWithoutAltar, Is.GreaterThan(0f));
            Assert.That(_scratch.rescueHpFraction, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(_scratch.persistDeathState, Is.True,
                "without persistence, dying costs nothing to anyone willing to reload");
        }

        /// <summary>
        /// The rescue must be strictly worse than reaching an altar, or the altar stops being worth
        /// walking to and the safety net quietly becomes the intended path.
        /// </summary>
        [Test]
        public void Defaults_MakeTheAltarBetterThanTheRescue()
        {
            Assert.That(_scratch.rescueHpFraction, Is.LessThan(1f));
        }

        /// <summary>
        /// The spirit's solidity and the trail's shape are ONE decision in two fields, and the
        /// shipped combination was the incoherent one: a straight line through walls the ghost
        /// bounced off. Either the spirit passes through walls, or the path is routed (or off).
        /// </summary>
        [Test]
        public void Defaults_KeepTheSpiritAndItsPathCoherent()
        {
            bool coherent = _scratch.spiritPassesThroughWalls
                            || _scratch.pathMode != SpiritPathMode.StraightLine;

            Assert.That(coherent, Is.True,
                "a straight-line compass through walls a solid spirit cannot cross points down a " +
                "route that does not exist");
        }

        /// <summary>
        /// Every public serialized field must be copied by the editor's <c>CopyTuning</c>.
        ///
        /// <para>That method backs undo and "restore defaults". A field added later and not listed
        /// there produces an undo that restores MOST of a reset — which is the worst possible
        /// outcome, because it looks like it worked. The check reads the editor's source rather
        /// than calling it, so it needs no editor instance and no scene.</para>
        /// </summary>
        [Test]
        public void EveryPublicField_IsCopiedByTheEditorsCopyTuning()
        {
            string source = ReadEditorSource();
            Assert.That(source, Is.Not.Null.And.Not.Empty,
                "DeathRuntimeEditor.cs must be readable for this guard to mean anything");

            int start = source.IndexOf("private static void CopyTuning", System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThan(-1), "CopyTuning must exist");
            string body = source.Substring(start);

            var missing = new List<string>();
            foreach (var field in typeof(DeathTuning).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsNotSerialized) continue;
                if (!body.Contains("to." + field.Name + " =")) missing.Add(field.Name);
            }

            Assert.That(missing, Is.Empty,
                "these DeathTuning fields are not copied by DeathRuntimeEditor.CopyTuning, so undo " +
                "and 'restore defaults' would silently leave them behind: " + string.Join(", ", missing));
        }

        private static string ReadEditorSource()
        {
            string path = System.IO.Path.Combine(
                Application.dataPath, "_Project", "Scripts", "Gameplay", "Editors", "Death",
                "DeathRuntimeEditor.cs");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }
    }
}
