using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The tint arbiter, and the bug it exists to remove.
    ///
    /// Nine systems wrote an entity body sprite's <c>color</c>, each with its own
    /// cache-the-original-and-restore-it dance. Any two overlapping produced a sprite stuck
    /// in a colour no live effect was still asking for — most often a monster left orange
    /// after being hit while burning.
    /// </summary>
    [TestFixture]
    public class SpriteTintStackTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private SpriteRenderer MakeBody(Color resting)
        {
            var go = new GameObject("Body");
            _spawned.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = resting;
            return sr;
        }

        private static void AssertColor(Color expected, Color actual, string because)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, because + " (r)");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, because + " (g)");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, because + " (b)");
            Assert.AreEqual(expected.a, actual.a, 1e-4f, because + " (a)");
        }

        // ── Base and identity ─────────────────────────────────────────────────

        [Test]
        public void AttachCapturesTheRestingColourAndChangesNothing()
        {
            var resting = new Color(0.8f, 0.7f, 0.6f, 1f);
            var sr = MakeBody(resting);

            var stack = SpriteTintStack.Attach(sr.gameObject);

            Assert.IsNotNull(stack);
            AssertColor(resting, stack.BaseColor, "The resting colour is the base.");
            AssertColor(resting, sr.color, "Attaching must not repaint anything.");
        }

        [Test]
        public void AttachIsIdempotent()
        {
            var sr = MakeBody(Color.white);
            var first = SpriteTintStack.Attach(sr.gameObject);
            var second = SpriteTintStack.Attach(sr.gameObject);

            Assert.AreSame(first, second,
                "Two stacks on one entity would each own a different base and fight exactly " +
                "like the systems they replaced.");
            Assert.AreEqual(1, sr.GetComponents<SpriteTintStack>().Length);
        }

        [Test]
        public void AttachReturnsNullWithoutABodySprite()
        {
            var go = new GameObject("NoSprite");
            _spawned.Add(go);

            Assert.IsNull(SpriteTintStack.Attach(go),
                "Spawners, triggers and test doubles all reach the status-effect code " +
                "without a renderer. Callers null-check; this must give them something to " +
                "null-check rather than an inert component.");
            Assert.IsNull(SpriteTintStack.Attach((GameObject)null));
        }

        [Test]
        public void TheBodySpriteIsThisRendererOrTheFirstBelowIt()
        {
            var parent = new GameObject("Entity");
            _spawned.Add(parent);
            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(parent.transform);
            var barSr = bar.AddComponent<SpriteRenderer>();

            // No renderer on the entity itself: the child stands in.
            Assert.AreSame(barSr, SpriteTintStack.ResolveBodyRenderer(parent));

            var own = parent.AddComponent<SpriteRenderer>();
            Assert.AreSame(own, SpriteTintStack.ResolveBodyRenderer(parent),
                "The entity's own renderer wins, or tinting a monster would whiten its " +
                "world-space HP bar on every hit.");
        }

        // ── Composition ───────────────────────────────────────────────────────

        [Test]
        public void OneLayerTintsAndClearingItRestoresTheBase()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);
            var orange = new Color(1f, 0.4f, 0.1f, 1f);

            stack.Set(TintLayer.Burn, orange);
            AssertColor(orange, sr.color, "A single layer on a white base is that layer.");
            Assert.IsTrue(stack.IsActive(TintLayer.Burn));

            stack.Clear(TintLayer.Burn);
            AssertColor(Color.white, sr.color, "Clearing the only layer restores the base.");
            Assert.IsFalse(stack.IsActive(TintLayer.Burn));
        }

        [Test]
        public void LayersMultiplyRatherThanReplace()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.Set(TintLayer.Burn, new Color(1f, 0.5f, 0.5f, 1f));
            stack.Set(TintLayer.Poison, new Color(0.5f, 1f, 0.5f, 1f));

            AssertColor(new Color(0.5f, 0.5f, 0.25f, 1f), sr.color,
                "Burning AND poisoned must read as both. Replacement would make whichever " +
                "was applied second the only one you can see.");
        }

        [Test]
        public void ClearingOneLayerLeavesTheOthersIntact()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);
            var poison = new Color(0.5f, 1f, 0.5f, 1f);

            stack.Set(TintLayer.Burn, new Color(1f, 0.5f, 0.5f, 1f));
            stack.Set(TintLayer.Poison, poison);
            stack.Clear(TintLayer.Burn);

            AssertColor(poison, sr.color,
                "This is the whole point: a system leaving removes its own colour and " +
                "nobody else's.");
        }

        [Test]
        public void TheBaseIsRespectedByEveryLayer()
        {
            var resting = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            var sr = MakeBody(resting);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.Set(TintLayer.Slow, new Color(0.5f, 1f, 1f, 1f));
            AssertColor(new Color(0.25f, 0.5f, 0.5f, 0.8f), sr.color,
                "A tinted or faded sprite keeps its identity under a debuff.");
        }

        // ── Flash ─────────────────────────────────────────────────────────────

        [Test]
        public void FlashOverridesTheTintInsteadOfMultiplyingIntoIt()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.Set(TintLayer.Burn, new Color(1f, 0.4f, 0.1f, 1f));
            stack.SetFlash(Color.white, 1f);

            AssertColor(Color.white, sr.color,
                "A hit reads as a hit whatever the victim is tinted. Multiplying white into " +
                "an orange sprite produces orange, which is to say nothing at all.");
        }

        [Test]
        public void FlashPreservesAlpha()
        {
            var sr = MakeBody(new Color(1f, 1f, 1f, 0.25f));
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.SetFlash(Color.white, 1f);
            Assert.AreEqual(0.25f, sr.color.a, 1e-4f,
                "A body mid-teleport is being faded out by its alpha. A flash must not " +
                "snap it back to solid.");
        }

        [Test]
        public void ClearingTheFlashReturnsToTheLayersUnderneath()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);
            var orange = new Color(1f, 0.4f, 0.1f, 1f);

            stack.Set(TintLayer.Burn, orange);
            stack.SetFlash(Color.white, 1f);
            stack.ClearFlash();

            AssertColor(orange, sr.color, "The burn was still running underneath the flash.");
        }

        // ── The regression ────────────────────────────────────────────────────

        [Test]
        public void AHitDuringABurnDoesNotLeaveTheSpriteOrangeForever()
        {
            // The shipped bug, played back in order:
            //   burn tints orange -> hit lands and captures orange as "the original"
            //   -> burn ends and restores white -> flash ends and restores its capture.
            // The sprite then sat orange with nothing tinting it, for the rest of the run.
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.Set(TintLayer.Burn, new Color(1f, 0.4f, 0.1f, 1f));
            stack.SetFlash(Color.white, 1f);
            stack.Clear(TintLayer.Burn);
            stack.ClearFlash();

            AssertColor(Color.white, sr.color,
                "With nothing left tinting it, the sprite must be exactly its resting colour.");
        }

        [Test]
        public void ResetAllReturnsAPooledEntityToItsRestingColour()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            stack.Set(TintLayer.Burn, Color.red);
            stack.Set(TintLayer.Death, Color.gray);
            stack.SetFlash(Color.white, 0.5f);
            stack.ResetAll();

            AssertColor(Color.white, sr.color,
                "A monster coming back out of the pool must not wear the tint of whatever " +
                "killed it last time.");
            Assert.AreEqual(0f, stack.FlashAmount);
        }

        [Test]
        public void EveryLayerHasItsOwnSlot()
        {
            var sr = MakeBody(Color.white);
            var stack = SpriteTintStack.Attach(sr.gameObject);

            var layers = System.Enum.GetValues(typeof(TintLayer)).Cast<TintLayer>().ToList();
            foreach (var layer in layers) stack.Set(layer, Color.white);
            foreach (var layer in layers)
                Assert.IsTrue(stack.IsActive(layer),
                    $"{layer} was overwritten by another layer — the slot mask is too small.");
        }

        // ── Source guard ──────────────────────────────────────────────────────

        [Test]
        public void TheMigratedSystemsNoLongerWriteTheBodyColourThemselves()
        {
            string scripts = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var migrated = new[]
            {
                "Gameplay/Combat/StatusEffects/BurnEffect.cs",
                "Gameplay/Combat/StatusEffects/PoisonEffect.cs",
                "Gameplay/Combat/StatusEffects/FreezeEffect.cs",
                "Gameplay/Combat/StatusEffects/SlowEffect.cs",
                "Gameplay/Combat/StatusEffects/StunEffect.cs",
                "Gameplay/Combat/Damage/GrayscaleDeath.cs",
            };

            var offenders = new List<string>();
            foreach (string rel in migrated)
            {
                string path = Path.Combine(scripts, rel.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(path), $"Migrated file missing: {rel}");

                string body = Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", "",
                                            RegexOptions.Singleline);
                body = string.Join("\n", body.Split('\n').Select(l =>
                {
                    int i = l.IndexOf("//", System.StringComparison.Ordinal);
                    return i < 0 ? l : l.Substring(0, i);
                }));

                if (Regex.IsMatch(body, @"\.color\s*="))
                    offenders.Add($"{Path.GetFileName(path)}: writes .color directly");
                if (body.Contains("SpriteRenderer"))
                    offenders.Add($"{Path.GetFileName(path)}: still resolves its own renderer");
            }

            Assert.IsEmpty(offenders,
                "These systems must go through SpriteTintStack. A direct write reintroduces " +
                "the stuck-tint bug and, worse, corrupts the stack's idea of the base " +
                "colour for every other system on the entity.\n\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
