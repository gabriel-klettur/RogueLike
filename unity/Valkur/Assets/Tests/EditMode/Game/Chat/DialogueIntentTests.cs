using NUnit.Framework;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Coverage for <see cref="DialogueIntentClassifier"/>.
    ///
    /// The classifier decides which of a persona's authored pools answers a player line, so
    /// a wrong verdict does not crash anything — it just makes an NPC answer a haggle with
    /// small talk, which reads as the character not listening. The two properties worth
    /// pinning are that it matches WHOLE WORDS (a substring match fires "hi" inside "this")
    /// and that it ignores accents, because a player types "cuanto" as often as "cuánto".
    /// </summary>
    [TestFixture]
    public class DialogueIntentTests
    {
        [TestCase("¿Cuánto vale esto?", DialogueIntent.Trade)]
        [TestCase("cuanto cuesta", DialogueIntent.Trade)]
        [TestCase("me haces descuento?", DialogueIntent.Trade)]
        [TestCase("how much does it cost", DialogueIntent.Trade)]
        [TestCase("Hola, buenas", DialogueIntent.Greeting)]
        [TestCase("hey", DialogueIntent.Greeting)]
        [TestCase("adios", DialogueIntent.Farewell)]
        [TestCase("me voy, nos vemos", DialogueIntent.Farewell)]
        [TestCase("cuentame un chiste", DialogueIntent.Joke)]
        [TestCase("quien eres", DialogueIntent.SmallTalk)]
        [TestCase("tell me your story", DialogueIntent.SmallTalk)]
        public void Classify_RecognisesTheIntent(string text, DialogueIntent expected)
        {
            Assert.AreEqual(expected, DialogueIntentClassifier.Classify(text));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        [TestCase("asdkjh qwe")]
        public void Classify_UnrecognisedOrBlank_IsUnknown(string text)
        {
            Assert.AreEqual(DialogueIntent.Unknown, DialogueIntentClassifier.Classify(text),
                "Unknown is the honest majority verdict and routes to the ordinary repertoire.");
        }

        [TestCase("this machine is broken")]
        [TestCase("architecture")]
        [TestCase("Valentina")]
        [TestCase("goodbyes are things")]
        public void Classify_DoesNotMatchAKeywordBuriedInsideAWord(string text)
        {
            // "hi" lives inside "this" and "machine"; "hi" again in "architecture";
            // "vale" inside "Valentina"; "bye" inside "goodbyes". Substring matching made
            // all four fire, so an NPC answered a complaint about scenery with a greeting.
            Assert.AreEqual(DialogueIntent.Unknown, DialogueIntentClassifier.Classify(text),
                $"'{text}' contains a keyword only as a fragment of a longer word.");
        }

        [Test]
        public void Classify_AccentedAndUnaccented_AgreeWithEachOther()
        {
            Assert.AreEqual(
                DialogueIntentClassifier.Classify("¿cuánto?"),
                DialogueIntentClassifier.Classify("cuanto"),
                "A person spells these the same word; an NPC that answers only one reads as broken.");
        }

        [Test]
        public void Classify_TradeBeatsGreeting_WhenALineIsBoth()
        {
            Assert.AreEqual(DialogueIntent.Trade,
                DialogueIntentClassifier.Classify("hola, ¿cuánto vale la poción?"),
                "Someone opening with a price question is a customer. Answering that with " +
                "small talk is the more annoying of the two failures, so Trade is tested first.");
        }
    }
}
