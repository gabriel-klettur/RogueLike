using NUnit.Framework;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// EditMode coverage for the lines the CHAT ITSELF says — trade refusals, offer
    /// summaries, "there is no one nearby" — as opposed to the lines a character says.
    ///
    /// <para>They used to be Spanish string literals inside <c>ChatTradeBroker</c> and
    /// <c>ChatSystem.Trade</c>, so switching the panel to English produced English chrome
    /// with Spanish machinery in the middle of the conversation. The dialogue itself is a
    /// separate decision and stays Spanish: those lines are authored persona material and
    /// there is no English set to swap to.</para>
    ///
    /// <para><b>PlayerPrefs is MACHINE state.</b> <c>ChatLanguage</c> persists the choice, so
    /// this fixture snapshots whatever the developer had and puts it back — otherwise running
    /// the suite once would leave their game in English, on that machine only, for a reason
    /// nothing in the test name mentions.</para>
    /// </summary>
    [TestFixture]
    public class ChatSystemLinesLanguageTests
    {
        private string _originalLanguage;

        [SetUp]
        public void SetUp() => _originalLanguage = ChatLanguage.Current;

        [TearDown]
        public void TearDown() => ChatLanguage.Set(_originalLanguage);

        private static void InSpanish() => ChatLanguage.Set(ChatLanguage.SPANISH);
        private static void InEnglish() => ChatLanguage.Set(ChatLanguage.ENGLISH);

        // ── Every system line moves with the language ───────────────────────

        [Test]
        public void SystemLines_DifferBetweenLanguages()
        {
            InSpanish();
            string[] es =
            {
                ChatLanguage.NoOneNearby, ChatLanguage.OfferDeclined,
                ChatLanguage.OfferBelongedToSomeoneElse, ChatLanguage.OfferNoLongerPossible,
                ChatLanguage.TradeFailed, ChatLanguage.NoOneToTradeWith,
                ChatLanguage.NotForSaleHere, ChatLanguage.SoldOut, ChatLanguage.InventoryFull,
                ChatLanguage.CarryingNothing, ChatLanguage.NotCarryingThat,
                ChatLanguage.CannotAfford(5, 1), ChatLanguage.OfferBuy("Pan", 3),
                ChatLanguage.OfferSell("Pan", 3), ChatLanguage.OfferPartial(2, "Pan", 6),
                ChatLanguage.TradeDoneBuy("Pan", 3), ChatLanguage.TradeDoneSell("Pan", 3),
            };

            InEnglish();
            string[] en =
            {
                ChatLanguage.NoOneNearby, ChatLanguage.OfferDeclined,
                ChatLanguage.OfferBelongedToSomeoneElse, ChatLanguage.OfferNoLongerPossible,
                ChatLanguage.TradeFailed, ChatLanguage.NoOneToTradeWith,
                ChatLanguage.NotForSaleHere, ChatLanguage.SoldOut, ChatLanguage.InventoryFull,
                ChatLanguage.CarryingNothing, ChatLanguage.NotCarryingThat,
                ChatLanguage.CannotAfford(5, 1), ChatLanguage.OfferBuy("Pan", 3),
                ChatLanguage.OfferSell("Pan", 3), ChatLanguage.OfferPartial(2, "Pan", 6),
                ChatLanguage.TradeDoneBuy("Pan", 3), ChatLanguage.TradeDoneSell("Pan", 3),
            };

            for (int i = 0; i < es.Length; i++)
            {
                Assert.IsNotEmpty(es[i], $"System line {i} is empty in Spanish.");
                Assert.IsNotEmpty(en[i], $"System line {i} is empty in English.");
                Assert.AreNotEqual(es[i], en[i],
                    $"System line {i} reads identically in both languages — a line that was " +
                    "added without a translation is exactly the defect this fixture exists for.");
            }
        }

        [Test]
        public void ParameterisedLines_CarryTheirNumbersAndTheItem()
        {
            InSpanish();
            StringAssert.Contains("7", ChatLanguage.OfferBuy("Pan", 7));
            StringAssert.Contains("Pan", ChatLanguage.OfferBuy("Pan", 7));
            StringAssert.Contains("2", ChatLanguage.OfferPartial(2, "Pan", 6));
            StringAssert.Contains("6", ChatLanguage.OfferPartial(2, "Pan", 6));

            InEnglish();
            StringAssert.Contains("7", ChatLanguage.OfferBuy("Pan", 7));
            StringAssert.Contains("Pan", ChatLanguage.OfferBuy("Pan", 7));
        }

        [Test]
        public void SystemLines_CarryNoCharactersOwnVoice()
        {
            // "Como quieras, tesoro." was said by the blacksmith, the banker and everyone
            // else who ever declined an offer, because a decline is spoken by all seven and
            // Gatita's endearment had been baked into it. Flavour belongs in a persona's
            // authored lines, which is the only place it can be per-character.
            InSpanish();
            StringAssert.DoesNotContain("tesoro", ChatLanguage.OfferDeclined);
            StringAssert.DoesNotContain("cariño", ChatLanguage.OfferDeclined);
        }

        // ── Composition: the broker really goes through it ──────────────────

        [Test]
        public void Broker_RefusesInTheActiveLanguage()
        {
            var proposal = new TradeProposal(TradeIntent.Buy, "bread_01", 1);

            InSpanish();
            TradeQuote spanish = ChatTradeBroker.Quote(proposal, null, null, null);

            InEnglish();
            TradeQuote english = ChatTradeBroker.Quote(proposal, null, null, null);

            Assert.IsFalse(spanish.IsValid, "No vendor means no deal.");
            Assert.AreEqual(ChatLanguage.NoOneToTradeWith, english.Refusal);
            Assert.AreNotEqual(spanish.Refusal, english.Refusal,
                "Asserting the two strings differ is what pins the COMPOSITION: the broker " +
                "asking ChatLanguage rather than holding its own literal. Testing the two " +
                "halves separately would pass with the literals still in the broker.");
        }
    }
}
