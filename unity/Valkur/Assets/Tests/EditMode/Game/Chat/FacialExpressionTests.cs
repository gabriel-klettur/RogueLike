using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// The vocabulary, the fallback chain and the persona-side resolution that make a face
    /// reachable.
    ///
    /// <para>What these exist to stop is the failure this project has hit eleven times and
    /// written down each time: an authored layer that reaches no pixel. A face has three
    /// independent ways to be silently unreachable — the enum grows a value the chain does
    /// not answer for, the chain stops somewhere the character has no art, or the art is on
    /// disk and never wired to the asset — and none of them produces an error at runtime.
    /// Each gets a test here.</para>
    /// </summary>
    public class FacialExpressionTests
    {
        private static FacialExpression[] All => FacialExpressionFallback.All;

        // ── Vocabulary ──────────────────────────────────────────────────────

        [Test]
        public void Neutral_IsZero_SoDefaultIsAlwaysShowable()
        {
            Assert.AreEqual(0, (int)FacialExpression.Neutral,
                "Neutral must be 0. It is the value a ChatReply carries when no provider " +
                "said anything about the face, and default(FacialExpression) has to land on " +
                "the one expression every character is guaranteed to have.");
        }

        [Test]
        public void EveryExpression_HasAChainThatEndsAtNeutral()
        {
            foreach (FacialExpression e in All)
            {
                var chain = FacialExpressionFallback.Chain(e);

                Assert.IsNotNull(chain, $"{e} has a null chain.");
                Assert.Greater(chain.Count, 0, $"{e} has an empty chain.");
                Assert.AreEqual(e, chain[0],
                    $"{e}'s chain must START with itself, so a caller can walk one list " +
                    "instead of special-casing the exact match.");
                Assert.AreEqual(FacialExpression.Neutral, chain[chain.Count - 1],
                    $"{e}'s chain must END at Neutral. A chain that stops anywhere else can " +
                    "resolve to nothing on a character that did not draw its last link, and " +
                    "a blank portrait reads as a bug rather than as missing art.");
            }
        }

        [Test]
        public void ChainsHaveNoRepeats()
        {
            foreach (FacialExpression e in All)
            {
                var chain = FacialExpressionFallback.Chain(e);
                CollectionAssert.AllItemsAreUnique(chain.ToArray(),
                    $"{e}'s chain visits the same expression twice, which is a lookup done " +
                    "for nothing on every portrait change.");
            }
        }

        [Test]
        public void Angry_NeverFallsThroughSad()
        {
            CollectionAssert.DoesNotContain(
                FacialExpressionFallback.Chain(FacialExpression.Angry).ToArray(),
                FacialExpression.Sad,
                "A smaller version of cross is blank-faced, not sad. Sad is a different " +
                "claim about the character and the player reads it as one.");
        }

        [Test]
        public void AnUndeclaredValue_StillResolves()
        {
            // Simulates the enum growing without its chain being declared: the guard inside
            // Chain must degrade to [itself, Neutral] rather than throwing or returning null.
            var chain = FacialExpressionFallback.Chain((FacialExpression)9999);

            Assert.AreEqual(2, chain.Count);
            Assert.AreEqual(FacialExpression.Neutral, chain[chain.Count - 1],
                "Adding a value to the enum must degrade to Neutral, never throw inside the " +
                "panel's crossfade.");
        }

        // ── Parsing ─────────────────────────────────────────────────────────

        [TestCase("happy", FacialExpression.Happy)]
        [TestCase("HAPPY", FacialExpression.Happy)]
        [TestCase("  Wink  ", FacialExpression.Wink)]
        [TestCase("neutral", FacialExpression.Neutral)]
        public void TryParse_AcceptsNames_CaseAndSpaceInsensitively(string token, FacialExpression expected)
        {
            Assert.IsTrue(FacialExpressionFallback.TryParse(token, out FacialExpression parsed));
            Assert.AreEqual(expected, parsed);
        }

        [TestCase("3")]
        [TestCase("0")]
        [TestCase("9999")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("risas")]
        public void TryParse_RefusesAnythingThatIsNotAName(string token)
        {
            Assert.IsFalse(FacialExpressionFallback.TryParse(token, out _),
                $"'{token}' is not an expression NAME. Enum.TryParse alone would accept any " +
                "integer, so a model answering \"[3]\" or a console typo would be taken as a " +
                "real face instead of refused.");
        }

        // ── Persona resolution ──────────────────────────────────────────────

        [Test]
        public void ACharacterWithTwoDrawings_ResolvesEveryExpression()
        {
            var persona = BuildPersona(FacialExpression.Neutral, FacialExpression.Happy);
            try
            {
                foreach (FacialExpression e in All)
                {
                    Assert.IsNotNull(persona.ResolveFace(e),
                        $"{e} resolved to nothing on a character that has Neutral. The chain " +
                        "is the whole reason a small set of drawings can answer the whole " +
                        "vocabulary.");
                }

                Assert.AreEqual("Happy", persona.ResolveFace(FacialExpression.Laugh).name,
                    "Laugh should borrow Happy, not Neutral — the nearest thing that exists.");
                Assert.AreEqual("Neutral", persona.ResolveFace(FacialExpression.Angry).name,
                    "Angry has no warm neighbour and must land on Neutral.");
            }
            finally { Cleanup(persona); }
        }

        [Test]
        public void HasOwnFace_SeparatesDrawnFromBorrowed()
        {
            var persona = BuildPersona(FacialExpression.Neutral, FacialExpression.Happy);
            try
            {
                Assert.IsTrue(persona.HasOwnFace(FacialExpression.Happy));
                Assert.IsFalse(persona.HasOwnFace(FacialExpression.Laugh),
                    "Laugh RESOLVES on this character but is not DRAWN. The two have to be " +
                    "distinguishable or the 'faces' command cannot tell an author that an " +
                    "import half worked.");
            }
            finally { Cleanup(persona); }
        }

        [Test]
        public void ACharacterWithNoArt_HasNoFacesAndResolvesNull()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            try
            {
                Assert.IsFalse(persona.HasFaces,
                    "HasFaces is what makes the panel skip the portrait gutter. Reserving it " +
                    "anyway puts an empty rectangle beside five of the six conversations in " +
                    "the game, which reads as a portrait that failed to load.");
                Assert.IsNull(persona.ResolveFace(FacialExpression.Happy));
            }
            finally { Cleanup(persona); }
        }

        [Test]
        public void ALoneFallbackPortrait_CountsAsAFace()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            persona.portrait = MakeSprite("OnlyPortrait");
            try
            {
                Assert.IsTrue(persona.HasFaces);
                foreach (FacialExpression e in All)
                    Assert.AreEqual("OnlyPortrait", persona.ResolveFace(e).name,
                        "A character with one drawing shows it for everything. It is a face; " +
                        "it just never changes.");
            }
            finally { Cleanup(persona); }
        }

        // ── The prompt instruction ──────────────────────────────────────────

        [Test]
        public void Instruction_ListsOnlyTheExpressionsThatAreDrawn()
        {
            var persona = BuildPersona(FacialExpression.Neutral, FacialExpression.Happy, FacialExpression.Angry);
            try
            {
                string rule = ExpressionTag.BuildInstruction(persona);

                StringAssert.Contains("neutral", rule);
                StringAssert.Contains("happy", rule);
                StringAssert.Contains("angry", rule);
                StringAssert.DoesNotContain("laugh", rule,
                    "Offering a face the art cannot show spends tokens teaching a model a " +
                    "distinction the player will never see, and invites a laugh that renders " +
                    "identically to every happy.");
            }
            finally { Cleanup(persona); }
        }

        [Test]
        public void Instruction_IsNullWhenNothingIsDistinguishable()
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            persona.portrait = MakeSprite("OnlyPortrait");
            try
            {
                Assert.IsNull(ExpressionTag.BuildInstruction(persona),
                    "A persona whose only art is the single fallback portrait must emit no " +
                    "rule at all. An empty list reads to a model as 'choose from nothing'.");
            }
            finally { Cleanup(persona); }
        }

        // ── Fixtures ────────────────────────────────────────────────────────

        private static readonly List<Texture2D> Textures = new List<Texture2D>();
        private static readonly List<Sprite> Sprites = new List<Sprite>();

        private static NPCPersonaDefinition BuildPersona(params FacialExpression[] drawn)
        {
            var persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            persona.faces = new List<NPCPersonaDefinition.FacialSprite>();
            foreach (FacialExpression e in drawn)
            {
                persona.faces.Add(new NPCPersonaDefinition.FacialSprite
                {
                    expression = e,
                    sprite = MakeSprite(e.ToString()),
                });
            }
            return persona;
        }

        private static Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            Textures.Add(tex);
            Sprites.Add(sprite);
            return sprite;
        }

        private static void Cleanup(ScriptableObject persona)
        {
            if (persona != null) UnityEngine.Object.DestroyImmediate(persona);
        }

        [TearDown]
        public void DestroyFixtureAssets()
        {
            foreach (var s in Sprites) if (s != null) UnityEngine.Object.DestroyImmediate(s);
            foreach (var t in Textures) if (t != null) UnityEngine.Object.DestroyImmediate(t);
            Sprites.Clear();
            Textures.Clear();
        }
    }
}
