using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Where a face comes from: the tag a language model writes, the words underneath when it
    /// does not, and the art actually shipped for the one character that has any.
    ///
    /// <para>The two sources are deliberately tested TOGETHER, because the contract that
    /// matters is that every reply gets a face by one route or the other. A tag test alone
    /// would pass on a build where the offline path answers Neutral for everything, and the
    /// default provider in this project IS the offline one.</para>
    /// </summary>
    public class ExpressionSourceTests
    {
        // ── The tag ─────────────────────────────────────────────────────────

        [TestCase("[happy] Bienvenida, vecina.", FacialExpression.Happy, "Bienvenida, vecina.")]
        [TestCase("[ANGRY]  Fuera de aqui.", FacialExpression.Angry, "Fuera de aqui.")]
        [TestCase("[wink]: Entre tu y yo.", FacialExpression.Wink, "Entre tu y yo.")]
        [TestCase("  [sad] Lo siento.", FacialExpression.Sad, "Lo siento.")]
        public void TryStrip_TakesTheTagOffAndLeavesTheWords(
            string raw, FacialExpression expected, string expectedText)
        {
            Assert.IsTrue(ExpressionTag.TryStrip(raw, out FacialExpression face, out string text));
            Assert.AreEqual(expected, face);
            Assert.AreEqual(expectedText, text,
                "The tag has to be gone before the reply leaves the provider. ChatSystem " +
                "records what comes back to memory and to the session log verbatim, so a " +
                "surviving tag becomes part of what the character is remembered to have said.");
        }

        [TestCase("Sin etiqueta, solo texto.")]
        [TestCase("[risas] Una etiqueta que no es una cara.")]
        [TestCase("[] vacia")]
        [TestCase("")]
        [TestCase(null)]
        public void TryStrip_RefusesAnythingThatIsNotAKnownFace(string raw)
        {
            Assert.IsFalse(ExpressionTag.TryStrip(raw, out FacialExpression face, out _));
            Assert.AreEqual(FacialExpression.Neutral, face);
        }

        [Test]
        public void TryStrip_LeavesAnUnknownBracketedWordInThePlayersView()
        {
            ExpressionTag.TryStrip("[risas] Se rie de ti.", out _, out string text);

            Assert.AreEqual("[risas] Se rie de ti.", text,
                "A character is allowed to open with a bracketed aside. Swallowing every " +
                "bracket would silently eat words a human authored, so only a token naming a " +
                "real expression is treated as a tag.");
        }

        [Test]
        public void TryStrip_ATagOnlyTurnLeavesNothingToSay()
        {
            Assert.IsTrue(ExpressionTag.TryStrip("[tired]", out FacialExpression face, out string text));
            Assert.AreEqual(FacialExpression.Tired, face);
            Assert.IsEmpty(text,
                "A turn that is nothing but a tag has said nothing, and the provider's " +
                "emptiness check runs on the STRIPPED text for exactly that reason.");
        }

        // ── The classifier ──────────────────────────────────────────────────

        [TestCase("Jajaja, que cosas dices.", FacialExpression.Laugh)]
        [TestCase("Bienvenida a mi puesto, que gusto verte.", FacialExpression.Happy)]
        [TestCase("Entre tu y yo, te hago precio de vecina.", FacialExpression.Wink)]
        [TestCase("Basta ya, largo de aqui.", FacialExpression.Angry)]
        [TestCase("Lo siento, no me queda borsch.", FacialExpression.Sad)]
        [TestCase("Dejame ver que tengo por aqui...", FacialExpression.Thinking)]
        [TestCase("Llevo cansada desde el amanecer.", FacialExpression.Tired)]
        [TestCase("El cielo esta despejado hoy.", FacialExpression.Neutral)]
        public void Classify_ReadsSpanish(string line, FacialExpression expected)
        {
            Assert.AreEqual(expected, ExpressionClassifier.Classify(line));
        }

        [TestCase("Hahaha, good one.", FacialExpression.Laugh)]
        [TestCase("Welcome, traveller.", FacialExpression.Happy)]
        [TestCase("Between you and me, I can do better.", FacialExpression.Wink)]
        [TestCase("Enough. Get out.", FacialExpression.Angry)]
        [TestCase("Let me think about it.", FacialExpression.Thinking)]
        public void Classify_ReadsEnglishToo(string line, FacialExpression expected)
        {
            Assert.AreEqual(expected, ExpressionClassifier.Classify(line),
                "The language toggle is per NPC and a player may type in either regardless " +
                "of it, so a reply can come back in either — the same reason " +
                "DialogueIntentClassifier carries both keyword sets.");
        }

        [Test]
        public void Classify_IsAccentInsensitive()
        {
            Assert.AreEqual(
                ExpressionClassifier.Classify("Dejame ver que tengo."),
                ExpressionClassifier.Classify("Déjame ver qué tengo."),
                "The two spellings are the same words to a person, and an NPC whose face " +
                "changes depending on whether the model typed an accent is broken.");
        }

        [Test]
        public void Classify_PrefersWarmthOverAQuestionMark()
        {
            Assert.AreEqual(FacialExpression.Happy,
                ExpressionClassifier.Classify("Hola, vecina. Que te trae por aqui?"),
                "A question mark is the commonest character in friendly dialogue. Testing " +
                "the pensive set first would make almost every line pensive, which is why " +
                "the ORDER inside the classifier is the design and not an accident.");
        }

        [Test]
        public void Classify_UsesThePlayerIntentOnlyWhenTheWordsSaidNothing()
        {
            Assert.AreEqual(FacialExpression.Laugh,
                ExpressionClassifier.Classify("El barril pesa lo mismo que el burro.", DialogueIntent.Joke),
                "A punchline with no keyword in it is still a joke being told.");

            Assert.AreEqual(FacialExpression.Angry,
                ExpressionClassifier.Classify("Basta ya.", DialogueIntent.Greeting),
                "What the character actually said is better evidence than what it was asked, " +
                "so the prior must never override a signal found in the text.");
        }

        [Test]
        public void Classify_ReadsEmojiWhateverTheLanguage()
        {
            Assert.AreEqual(FacialExpression.Laugh, ExpressionClassifier.Classify("\U0001F602"));
            Assert.AreEqual(FacialExpression.Wink, ExpressionClassifier.Classify("\U0001F609"));
            Assert.AreEqual(FacialExpression.Angry, ExpressionClassifier.Classify("\U0001F621"));
        }

        [Test]
        public void EmojiSets_DoNotCrossMatch()
        {
            // ContainsAnyChar compares surrogate pairs on their LOW half alone, which is exact
            // for these sets and only for these sets. Two emoji sharing a low surrogate would
            // make one set answer for another, silently and in exactly one direction.
            var probes = new Dictionary<FacialExpression, string>
            {
                { FacialExpression.Laugh, "\U0001F602\U0001F923\U0001F606\U0001F605" },
                { FacialExpression.Angry, "\U0001F620\U0001F621\U0001F624\U0001F92C" },
                { FacialExpression.Sad, "\U0001F622\U0001F62D\U0001F614\U0001F61E" },
                { FacialExpression.Tired, "\U0001F634\U0001F62A\U0001F971" },
                { FacialExpression.Wink, "\U0001F609" },
                { FacialExpression.Playful, "\U0001F61C\U0001F61B\U0001F61D\U0001F92A" },
                { FacialExpression.Thinking, "\U0001F914\U0001F928" },
                { FacialExpression.Happy, "\U0001F60A\U0001F642\U0001F60D\U0001F495\U0001F338" },
            };

            foreach (var pair in probes)
            {
                // Walk by CODE POINT, not by char: every entry here is a surrogate pair, and
                // indexing by char would test half an emoji.
                for (int i = 0; i < pair.Value.Length; i += char.IsSurrogatePair(pair.Value, i) ? 2 : 1)
                {
                    string single = char.ConvertFromUtf32(char.ConvertToUtf32(pair.Value, i));

                    Assert.AreEqual(pair.Key, ExpressionClassifier.Classify(single),
                        $"An emoji from the {pair.Key} set was read as something else. Its " +
                        "low surrogate collides with another set's, which makes one set " +
                        "answer for another silently and in exactly one direction.");
                }
            }
        }

        // ── The shipped art ─────────────────────────────────────────────────

        [Test]
        public void Gatita_ShipsEveryExpressionWithItsOwnDrawing()
        {
            NPCPersonaDefinition gatita = LoadPersona("vendor_cheff_gatita");
            Assert.IsNotNull(gatita, "Gatita's persona asset is missing.");

            var missing = new List<string>();
            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                if (!gatita.HasOwnFace(e)) missing.Add(e.ToString());
            }

            CollectionAssert.IsEmpty(missing,
                "Gatita is the character the whole vocabulary was read off, so every value " +
                "must have her own drawing behind it. A missing one means the import half " +
                "worked — the face still RESOLVES through the fallback chain, so nothing " +
                "fails at runtime and the drawing simply never appears. Missing: " +
                string.Join(", ", missing) + ". Re-run " +
                "'Valkur > Chat > Import Facial Expressions'.");
        }

        [Test]
        public void Gatita_FaceSpritesAllLoad()
        {
            NPCPersonaDefinition gatita = LoadPersona("vendor_cheff_gatita");
            Assert.IsNotNull(gatita);

            foreach (var entry in gatita.faces)
            {
                Assert.IsNotNull(entry.sprite,
                    $"{entry.expression} is listed with a null sprite, which resolves as if " +
                    "the expression were not drawn at all.");
            }
        }

        [Test]
        public void EveryPersonaWithFaces_HasNeutral()
        {
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:NPCPersonaDefinition", new[] { "Assets/_Project/Data/ChatPersonas" }))
            {
                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (persona == null || persona.faces == null || persona.faces.Count == 0) continue;

                Assert.IsNotNull(persona.ResolveFace(FacialExpression.Neutral),
                    $"{persona.displayName} has face art but nothing resolves for Neutral. " +
                    "Every chain ends there, so this character shows a blank portrait for " +
                    "every expression it did not draw.");
            }
        }

        private static NPCPersonaDefinition LoadPersona(string personaId)
        {
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:NPCPersonaDefinition", new[] { "Assets/_Project/Data/ChatPersonas" }))
            {
                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (persona != null && persona.personaId == personaId) return persona;
            }
            return null;
        }
    }
}
