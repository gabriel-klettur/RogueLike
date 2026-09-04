using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;
using Valkur.Gameplay.NPC;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Coverage for the two shop facts that reach a language model, and for the memory
    /// Reset control.
    ///
    /// <para>Why the purse is worth sending at all: without it an NPC upsells a player who
    /// is carrying nothing, which is the single most obvious way a generated line betrays
    /// that the character cannot see the game. Why only TWO facts: a model handed a stock
    /// list writes prices for it, and <c>VendorEconomyService</c> is the only thing that
    /// knows what an item costs.</para>
    /// </summary>
    [TestFixture]
    public class ChatTradeContextTests
    {
        private readonly List<Object> _scene = new List<Object>();
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _testRoot = Path.Combine(Path.GetTempPath(), "ValkurChatTrade_" + System.Guid.NewGuid().ToString("N"));
            ChatPersistencePaths.OverrideRoot = _testRoot;
            ServiceLocator.Clear();
            EntityRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _scene) if (o != null) Object.DestroyImmediate(o);
            _scene.Clear();

            ChatSessionLogger.CloseSession();
            ChatPersistencePaths.OverrideRoot = null;
            if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true);

            ServiceLocator.Clear();
            EntityRegistry.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private T Track<T>(T o) where T : Object { _scene.Add(o); return o; }

        private ItemDefinition MakeItem(string id, int buyPrice)
        {
            var item = Track(ScriptableObject.CreateInstance<ItemDefinition>());
            item.itemId = id;
            item.displayName = id;
            item.buyPrice = buyPrice;
            return item;
        }

        private NPCPersonaDefinition MakePersona()
        {
            var p = Track(ScriptableObject.CreateInstance<NPCPersonaDefinition>());
            p.personaId = "p-trade";
            p.displayName = "Tendera";
            p.chatRange = 5f;
            p.dialogueLines = new List<string> { "uno", "dos" };
            return p;
        }

        /// <summary>A vendor with the given prices, built the way EntitySetup builds one.</summary>
        private VendorNPC MakeVendor(NPCPersonaDefinition persona, params int[] prices)
        {
            var go = Track(new GameObject("vendor"));
            go.AddComponent<NPCInteractable>().Configure(persona.displayName, persona.chatRange);

            var config = Track(ScriptableObject.CreateInstance<VendorConfigDefinition>());
            config.vendorKey = persona.personaId;
            config.persona = persona;
            for (int i = 0; i < prices.Length; i++)
            {
                config.inventorySeed.Add(new VendorConfigDefinition.SeedSlot
                {
                    item = MakeItem("item" + i, prices[i]),
                    quantity = 3,
                });
            }

            var vendor = go.AddComponent<VendorNPC>();
            vendor.Configure(config);
            return vendor;
        }

        private CurrencyWallet MakeWallet(int coins)
        {
            var go = Track(new GameObject("purse"));
            var wallet = go.AddComponent<CurrencyWallet>();
            wallet.SetBalance(coins);
            return wallet;
        }

        // ── Trade context ───────────────────────────────────────────────────

        [Test]
        public void FromLive_NoVendor_IsNotATradingContext()
        {
            var context = ChatTradeContext.FromLive(null, MakeWallet(50));

            Assert.IsFalse(context.IsVendor,
                "A character who sells nothing must produce no shop talk at all — the prompt " +
                "skips the whole section on this flag.");
            Assert.AreEqual(50, context.PlayerCoins, "The purse is still read; only the shop is absent.");
        }

        [Test]
        public void FromLive_NoWallet_ReportsNoCoinsRatherThanThrowing()
        {
            var context = ChatTradeContext.FromLive(MakeVendor(MakePersona(), 10), null);

            Assert.AreEqual(0, context.PlayerCoins,
                "A player with no CurrencyWallet is broke, not a crash — the component is " +
                "added at spawn and a conversation can outlive a respawn.");
        }

        [Test]
        public void FromLive_BrokePlayer_CanAffordNothing()
        {
            var context = ChatTradeContext.FromLive(MakeVendor(MakePersona(), 10, 30, 15), MakeWallet(0));

            Assert.IsTrue(context.IsVendor);
            Assert.AreEqual(3, context.StockCount);
            Assert.AreEqual(0, context.AffordableCount);
            Assert.AreEqual(10, context.CheapestPrice,
                "The cheapest price is what lets the NPC decline with a real number instead of " +
                "inventing one.");
        }

        [Test]
        public void FromLive_PartlyAffordable_CountsOnlyWhatIsWithinReach()
        {
            var context = ChatTradeContext.FromLive(MakeVendor(MakePersona(), 10, 30, 15), MakeWallet(15));

            Assert.AreEqual(2, context.AffordableCount,
                "15 coins reaches the 10 and the 15, not the 30. Boundary included: a price " +
                "exactly equal to the purse IS affordable.");
            Assert.AreEqual(3, context.StockCount);
        }

        [Test]
        public void FromLive_RichPlayer_CanAffordEverything()
        {
            var context = ChatTradeContext.FromLive(MakeVendor(MakePersona(), 10, 30, 15), MakeWallet(500));
            Assert.AreEqual(context.StockCount, context.AffordableCount);
        }

        // ── What the model is actually told ─────────────────────────────────

        [Test]
        public void SystemPrompt_BrokePlayer_TellsTheNpcNotToOffer()
        {
            var persona = MakePersona();
            var context = ChatTradeContext.FromLive(MakeVendor(persona, 10, 30), MakeWallet(0));

            string prompt = PersonaPromptBuilder.BuildSystemPrompt(persona, null, context, "", "es");

            StringAssert.Contains("no lleva ni una moneda", prompt);
            StringAssert.Contains("10", prompt, "The cheapest real price must be stated.");
        }

        [Test]
        public void SystemPrompt_RichPlayer_SaysWhatTheyCanAfford()
        {
            var persona = MakePersona();
            var context = ChatTradeContext.FromLive(MakeVendor(persona, 10, 30), MakeWallet(500));

            string prompt = PersonaPromptBuilder.BuildSystemPrompt(persona, null, context, "", "es");

            StringAssert.Contains("500 monedas", prompt);
            StringAssert.Contains("cualquier cosa", prompt);
        }

        [Test]
        public void SystemPrompt_NonVendor_SaysNothingAboutMoney()
        {
            var persona = MakePersona();
            string prompt = PersonaPromptBuilder.BuildSystemPrompt(persona, null, default, "", "es");

            StringAssert.DoesNotContain("monedas", prompt,
                "Felipondor is a tree. Handing every NPC a purse report would have all of them " +
                "commenting on the player's finances.");
        }

        [Test]
        public void SystemPrompt_ListsTheRealCounter()
        {
            var persona = MakePersona();
            var vendor = MakeVendor(persona, 10, 30);
            var context = ChatTradeContext.FromLive(vendor, MakeWallet(50));

            string prompt = PersonaPromptBuilder.BuildSystemPrompt(persona, null, context, "", "es");

            // Withholding this was a deliberate mistake, on the reasoning that "a model handed
            // an inventory writes prices for it". Half true, and it produced something far
            // worse: with NO inventory the model invents one. Measured in a shipped
            // conversation, Gatita offered apples, pears, plums, blueberries and blackberries,
            // none of which exist in this game, and deflected four straight requests for a
            // price because she had none she was permitted to say.
            foreach (var entry in vendor.ShopInventory)
            {
                StringAssert.Contains(entry.item.displayName, prompt,
                    "Every item on the counter must be named, or the model fills the gap itself.");
                StringAssert.Contains(entry.item.itemId, prompt,
                    "The id is what a proposed purchase is matched against.");
                StringAssert.Contains(vendor.GetBuyPrice(entry.item).ToString(), prompt,
                    "Prices come from the same GetBuyPrice the counter charges, so the " +
                    "character and the shop cannot disagree.");
            }
        }

        [Test]
        public void SystemPrompt_MarksTheCounterAsExhaustive()
        {
            var persona = MakePersona();
            var context = ChatTradeContext.FromLive(MakeVendor(persona, 10), MakeWallet(50));

            string prompt = PersonaPromptBuilder.BuildSystemPrompt(persona, null, context, "", "es");

            StringAssert.Contains("SOLO esto", prompt,
                "A list the model reads as a sample rather than as the whole counter is a " +
                "list it will happily extend.");
        }

        [Test]
        public void SystemPrompt_NonVendor_GetsNoCounterAtAll()
        {
            string prompt = PersonaPromptBuilder.BuildSystemPrompt(MakePersona(), null, default, "", "es");
            StringAssert.DoesNotContain("SOLO esto", prompt,
                "Felipondor is a tree. A counter section for someone who sells nothing invites " +
                "them to talk shop.");
        }

        [Test]
        public void TradeContext_CarriesEveryStockedRow()
        {
            var context = ChatTradeContext.FromLive(MakeVendor(MakePersona(), 10, 30, 15), MakeWallet(50));

            Assert.IsNotNull(context.Stock);
            Assert.AreEqual(3, context.Stock.Count);
            CollectionAssert.AreEquivalent(
                new[] { 10, 30, 15 }, context.Stock.Select(l => l.Price).ToArray(),
                "The prices carried must be the ones GetBuyPrice returns, not the raw buyPrice.");
        }
    }
}
