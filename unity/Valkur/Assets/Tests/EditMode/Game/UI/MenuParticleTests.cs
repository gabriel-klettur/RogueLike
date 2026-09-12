using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.UI.MainMenu;
using Valkur.UI.MainMenu.Title;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The menu's particles: the title made of them, and the mote layer that answers events.
    ///
    /// <para>The audit's flattest number was <b>zero</b> — no <c>ParticleSystem</c>, no
    /// <c>ParticleEmitter</c> and no sound anywhere in <c>UI/</c>. What replaced it has one rule
    /// worth defending in a test, because it is the rule every mote layer in this project lives
    /// by and the one that is easiest to break by accident: <b>motes answer an EVENT, never a
    /// clock.</b> A menu that sparkles at rest is a menu whose sparkle means nothing when it
    /// matters.</para>
    /// </summary>
    public class MenuParticleTests
    {
        private GameObject _root;
        private MenuArt _art;
        private MenuStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = MenuStyle.Active;
            _art = MenuArt.Get(_style);
            _root = new GameObject("MenuParticleTestRoot", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)_root.transform;
            rt.sizeDelta = new Vector2(1600f, 800f);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate: Object.Destroy is an outright ERROR in Edit Mode.
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── The mote layer ───────────────────────────────────────────────────

        [Test]
        public void ANewLayer_IsEmpty_AndTickingItChangesNothing()
        {
            var fx = MenuFxLayer.Create(_root.transform, _art, 32, null);
            Assert.AreEqual(0, fx.Alive);
            for (int i = 0; i < 100; i++) fx.Tick(0.05f);
            Assert.AreEqual(0, fx.Alive, "the layer emitted with nothing having happened");
        }

        [Test]
        public void Emit_AddsOne_AndTheMoteExpiresOnItsOwnClock()
        {
            var fx = MenuFxLayer.Create(_root.transform, _art, 32, null);
            fx.Emit(Vector2.zero, Vector2.up * 10f, Color.white, 0.5f, MenuMoteShape.Dot);
            Assert.AreEqual(1, fx.Alive);

            fx.Tick(0.3f);
            Assert.AreEqual(1, fx.Alive, "it died early");
            fx.Tick(0.3f);
            Assert.AreEqual(0, fx.Alive, "it outlived its own lifetime");
        }

        [Test]
        public void ThePoolIsNeverGrown_AnEmitPastItIsDropped()
        {
            var fx = MenuFxLayer.Create(_root.transform, _art, 4, null);
            for (int i = 0; i < 20; i++)
                fx.Emit(Vector2.zero, Vector2.up, Color.white, 1f, MenuMoteShape.Dot);
            Assert.AreEqual(4, fx.Alive, "the capacity is a budget, not a suggestion");
            Assert.AreEqual(4, fx.Capacity);
        }

        [Test]
        public void Burst_ThrowsTheRequestedCount_AndClearRemovesThemAll()
        {
            var fx = MenuFxLayer.Create(_root.transform, _art, 64, null);
            fx.Burst(new Vector2(100f, 100f), 12, Color.white, 80f, 0.6f);
            Assert.AreEqual(12, fx.Alive);
            fx.Clear();
            Assert.AreEqual(0, fx.Alive);
        }

        [Test]
        public void AZeroLifeMote_IsRefused()
        {
            var fx = MenuFxLayer.Create(_root.transform, _art, 8, null);
            Assert.IsFalse(fx.Emit(Vector2.zero, Vector2.up, Color.white, 0f, MenuMoteShape.Dot));
            Assert.AreEqual(0, fx.Alive);
        }

        // ── The title ────────────────────────────────────────────────────────

        private TitleParticleField BuildTitle(bool reduceMotion = false)
        {
            var field = TitleParticleField.Create(_root.transform, _art, _style, null);
            field.SetText(MenuText.GameTitle, reduceMotion);
            return field;
        }

        [Test]
        public void TheTitle_IsMadeOfMotes_WithinItsBudget()
        {
            var field = BuildTitle();
            Assert.Greater(field.MoteCount, 200, "the word is too sparse to read");
            Assert.LessOrEqual(field.MoteCount, _style.titleMaxPoints,
                "the budget is a ceiling, and it was exceeded");
            Assert.Greater(field.TitleSize.x, field.TitleSize.y,
                "VALKUR is a word, so it must be wider than it is tall");
        }

        [Test]
        public void TheTitle_StartsScattered_AndSettlesExactlyOnce()
        {
            var field = BuildTitle();
            Assert.AreEqual(0f, field.Assembly, 0.001f, "it must begin scattered");

            float span = _style.titleAssembleSeconds;
            for (int i = 0; i < 400 && field.Assembly < 1f; i++) field.Tick(span / 40f);
            Assert.AreEqual(1f, field.Assembly, 0.001f, "it never finished assembling");

            for (int i = 0; i < 50; i++) field.Tick(0.05f);
            Assert.AreEqual(1f, field.Assembly, 0.001f, "it must stay settled, not loop");
        }

        [Test]
        public void UnderReduceMotion_TheWordIsSimplyThere()
        {
            var field = BuildTitle(reduceMotion: true);
            Assert.AreEqual(1f, field.Assembly, 0.001f,
                "reduce motion must remove the assembly, not merely shorten it");
        }

        [Test]
        public void SnapToSettled_ArrivesWithoutTouchingReduceMotion()
        {
            // The brand plane already gathered the word; the menu inherits that state. Forcing
            // reduce motion to get there would also kill the shimmer and the sweep.
            var field = BuildTitle();
            field.SnapToSettled();
            Assert.AreEqual(1f, field.Assembly, 0.001f);
            field.Sweep();
            Assert.IsTrue(field.Sweeping, "the sweep must still be available after a snap");
        }

        [Test]
        public void Replay_PutsTheWordBackOnTheFarSideOfItsAssembly()
        {
            var field = BuildTitle();
            for (int i = 0; i < 400 && field.Assembly < 1f; i++) field.Tick(0.05f);
            Assert.AreEqual(1f, field.Assembly, 0.001f);
            field.Replay();
            Assert.AreEqual(0f, field.Assembly, 0.001f);
        }

        [Test]
        public void UnderReduceMotion_TheSweepNeverRuns()
        {
            var field = BuildTitle(reduceMotion: true);
            field.Sweep();
            Assert.IsFalse(field.Sweeping);
        }

        [Test]
        public void AnEmberSource_IsOfferedOnlyOnceTheWordHasSettled()
        {
            var field = BuildTitle();
            Assert.IsFalse(field.TryPickEmberSource(out _, out _),
                "a word still flying in has no settled point to throw an ember off");

            field.SnapToSettled();
            Assert.IsTrue(field.TryPickEmberSource(out var where, out var colour));
            Assert.Greater(colour.a, 0f);
            Assert.IsFalse(float.IsNaN(where.x));
        }

        [Test]
        public void ATitleWithNoDrawableCharacters_IsEmptyRatherThanBroken()
        {
            var field = TitleParticleField.Create(_root.transform, _art, _style, null);
            field.SetText("!!!unknownÑÑÑ", false);
            // Punctuation IS drawable, so this is not zero; what matters is that nothing threw
            // and the size stayed sane.
            Assert.GreaterOrEqual(field.MoteCount, 0);
            Assert.Greater(field.TitleSize.x, 0f);
        }

        // ── The rule, read off the source ────────────────────────────────────

        /// <summary>
        /// A source scan, because the rule is about what the code DOES NOT do and no runtime
        /// assertion can see an emitter that was never written. The same shape as
        /// <c>FacingIndicatorRigTests.Source_HasOneJob</c>.
        /// </summary>
        [Test]
        public void NothingInTheMenu_UsesAParticleSystem()
        {
            string root = Path.Combine(Application.dataPath, "_Project/Scripts/UI/MainMenu");
            Assert.IsTrue(Directory.Exists(root), root);

            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string text = StripComments(File.ReadAllText(file));
                Assert.IsFalse(Regex.IsMatch(text, @"\bParticleSystem\b"),
                    Path.GetFileName(file) + " uses a ParticleSystem. It is a WORLD renderer and " +
                    "does not sort against the Graphics of a Screen Space Overlay canvas, so it " +
                    "would sit in front of every panel or behind the art — never between them.");
            }
        }

        /// <summary>
        /// Strips line and block comments so a rule NAMED in a doc comment does not fail the scan
        /// that enforces it — the trap the boot-sequence fixtures hit when a file's own comment
        /// mentioned the thing being grepped for.
        /// </summary>
        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            source = Regex.Replace(source, @"//.*?$", string.Empty, RegexOptions.Multiline);
            return source;
        }
    }
}
