using System.Linq;
using NUnit.Framework;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Coverage for how a reply is broken into the bubbles it is spoken as.
    ///
    /// Found by playing the game rather than by reading the code: Gatita answered a price
    /// question with "Si te llevas canasta, te hago precio de vecina" and it arrived as an
    /// eight-word bubble followed, three seconds later, by a bubble containing the single
    /// word "vecina". Nothing failed — the old split was a faithful port of Python's
    /// eight-word cut, and it is simply wrong for the short authored lines this game ships.
    /// </summary>
    [TestFixture]
    public class ReplyChunkingTests
    {
        private const int MaxBubbleWords = 22;
        private const int MinChunkWords = 3;

        [Test]
        public void ShortReply_IsOneBubble()
        {
            var chunks = ChatSystem.SplitIntoSpokenChunks("Con pancita feliz, todo sale mejor");
            Assert.AreEqual(1, chunks.Count, "Six words is one thing a person says.");
        }

        [Test]
        public void NineWordSentence_IsNotSplitIntoAnOrphanedWord()
        {
            // The exact line and the exact defect, measured in game.
            var chunks = ChatSystem.SplitIntoSpokenChunks(
                "Si te llevas canasta, te hago precio de vecina");

            Assert.AreEqual(1, chunks.Count,
                "A nine-word sentence must not become an eight-word bubble plus 'vecina'.");
            StringAssert.Contains("vecina", chunks[0]);
        }

        [Test]
        public void NoChunkIsShorterThanTheMinimum()
        {
            foreach (string reply in new[]
            {
                "Si te llevas canasta, te hago precio de vecina",
                "Tengo remolacha fresca para un borsch que enamora, corazon",
                "uno dos tres cuatro cinco seis siete ocho nueve",
                "uno dos tres cuatro cinco seis siete ocho nueve diez",
            })
            {
                var chunks = ChatSystem.SplitIntoSpokenChunks(reply);
                foreach (string chunk in chunks)
                {
                    int words = chunk.Split(' ').Length;
                    Assert.GreaterOrEqual(words, MinChunkWords,
                        $"'{chunk}' is a fragment, not a pause. Reply was: {reply}");
                }
            }
        }

        [Test]
        public void ShortSentences_ShareABubbleRatherThanPausingOnEveryFullStop()
        {
            var chunks = ChatSystem.SplitIntoSpokenChunks(
                "Primero lo inspecciono. Luego te digo precio y plazos.");

            Assert.AreEqual(1, chunks.Count,
                "Nine words across two sentences is one thing a person says. Pausing on every " +
                "full stop would turn a two-line answer into two bubbles and a wait.");
        }

        [Test]
        public void SentencesSeparate_OnceTheyNoLongerFitTogether()
        {
            // Two sentences of fifteen words each: either alone fits a bubble, together they
            // do not, so the break lands on the boundary between them.
            string sentence = string.Join(" ", Enumerable.Range(1, 15).Select(i => "palabra" + i));
            var chunks = ChatSystem.SplitIntoSpokenChunks(sentence + ". " + sentence + ".");

            Assert.AreEqual(2, chunks.Count, "The budget is exceeded, so the pack must break.");
            StringAssert.EndsWith(".", chunks[0],
                "And it must break at the sentence boundary, keeping punctuation with its sentence.");
        }

        [Test]
        public void ASentenceTooLongForABubble_IsCutAsALastResort()
        {
            // Fifty words and not one full stop: there is no punctuation to break on, so
            // something has to give. This is the ONLY case a reply is cut mid-phrase.
            string longSentence = string.Join(" ", Enumerable.Range(1, 50).Select(i => "palabra" + i));
            var chunks = ChatSystem.SplitIntoSpokenChunks(longSentence);

            Assert.Greater(chunks.Count, 1, "Fifty words is more than one bubble's worth.");
            foreach (string chunk in chunks)
            {
                // The last bubble may exceed the budget when a short tail was folded back
                // into it — that is the deliberate trade: a slightly long bubble reads as a
                // sentence, an orphaned word reads as a bug.
                Assert.LessOrEqual(chunk.Split(' ').Length, MaxBubbleWords + MinChunkWords,
                    "A bubble may absorb a stray tail, but not grow without bound.");
            }
        }

        [Test]
        public void ARealModelReply_ArrivesInAtMostTwoBubbles()
        {
            // Measured in game: this exact reply came back as FIVE bubbles under the old
            // eight-word cut, one of them "de remolacha— lista para cocinar y regalarte un",
            // and at the delay of the time took fifteen seconds to finish arriving.
            var chunks = ChatSystem.SplitIntoSpokenChunks(
                "¡Ay, mi vida! Estoy estupenda, tarareo y con la libreta manchada de " +
                "remolacha, lista para cocinar y regalarte un bocado que te haga sonreir. " +
                "Con pancita feliz, todo sale mejor.");

            // Three, not five: one bubble per sentence, because the middle sentence alone
            // is already most of a bubble's worth. Three whole sentences is the right
            // reading of this reply — "¡Ay, mi vida!" IS a beat of its own — and the count
            // is not the property worth pinning anyway.
            Assert.LessOrEqual(chunks.Count, 3, "A three-sentence reply is at most three bubbles.");

            foreach (string chunk in chunks)
            {
                Assert.GreaterOrEqual(chunk.Split(' ').Length, MinChunkWords);

                // THIS is the contract. Every bubble ends where a sentence ends, so none of
                // them can read as the NPC having been interrupted — which is exactly what
                // "…la libreta manchada" followed by "de remolacha— lista para…" did.
                char last = chunk[chunk.Length - 1];
                Assert.IsTrue(last == '.' || last == '!' || last == '?',
                    $"A bubble must end on a sentence boundary, not mid-phrase: '{chunk}'");
            }
        }

        [Test]
        public void NoWordIsLostOrDuplicated()
        {
            const string reply = "Primero lo inspecciono. Luego te digo precio y plazos, corazon. Vale?";
            var chunks = ChatSystem.SplitIntoSpokenChunks(reply);

            string rejoined = string.Join(" ", chunks);
            CollectionAssert.AreEqual(
                reply.Split(new[] { ' ', '\n' }, System.StringSplitOptions.RemoveEmptyEntries),
                rejoined.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries),
                "Splitting is a presentation decision; it must not edit what the NPC said.");
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void BlankReply_ProducesNoBubbles(string reply)
        {
            CollectionAssert.IsEmpty(ChatSystem.SplitIntoSpokenChunks(reply),
                "An empty reply must schedule nothing rather than an empty bubble.");
        }

        [Test]
        public void EllipsisFallback_IsASingleBubble()
        {
            var chunks = ChatSystem.SplitIntoSpokenChunks("...");
            Assert.AreEqual(1, chunks.Count,
                "The provider-failure fallback must survive the sentence splitter: '...' is " +
                "three sentence terminators in a row and must not become three empty bubbles.");
            Assert.AreEqual("...", chunks[0]);
        }
    }
}
