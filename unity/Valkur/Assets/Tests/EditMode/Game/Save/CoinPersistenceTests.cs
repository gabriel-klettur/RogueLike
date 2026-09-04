using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;

namespace Valkur.Tests.EditMode.Game.Save
{
    /// <summary>
    /// Coverage for the coin balance surviving a save and load.
    ///
    /// <para>It did not. Nothing in the entire save pipeline mentioned
    /// <see cref="CurrencyWallet"/> — grep the folder and the word does not appear — while
    /// its <c>Awake</c> resets the balance to <c>startingCoins</c> on every session. So
    /// coins collected from pickups and earned by selling to a vendor were gone by the next
    /// launch, silently and completely.</para>
    ///
    /// <para>Nothing failed to make that visible: the wallet worked, the shop charged
    /// correctly, the HUD showed the right number. The only place the game disagreed with
    /// itself was across a restart — the same shape as the spawner coordinate drift, and
    /// the reason these tests assert on the ROUND TRIP rather than on either half.</para>
    /// </summary>
    [TestFixture]
    public class CoinPersistenceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// A player carrying a wallet. <c>Awake</c> does not run on a component added in
        /// Edit Mode, so the balance is set explicitly rather than assumed.
        /// </summary>
        private CurrencyWallet MakeWallet(int coins)
        {
            var go = new GameObject("player");
            _scene.Add(go);
            var wallet = go.AddComponent<CurrencyWallet>();
            wallet.SetBalance(coins);
            return wallet;
        }

        private static void Restore(GameObject player, PlayerSaveData psd)
        {
            var mi = typeof(GameStateRestorer).GetMethod("RestoreCoins",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(mi, "GameStateRestorer.RestoreCoins was renamed or removed.");
            try { mi.Invoke(null, new object[] { player, psd }); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        // ── The field exists and defaults to "unknown" ──────────────────────

        [Test]
        public void PlayerSaveData_DefaultsCoinsToTheAbsentSentinel()
        {
            Assert.AreEqual(-1, new PlayerSaveData().coins,
                "-1 is what tells a save written before this field existed apart from a " +
                "player who is genuinely broke. A default of 0 would make every legacy save " +
                "restore as 'you have no money', which is indistinguishable from the bug.");
        }

        [Test]
        public void LegacyJson_WithNoCoinsField_ArrivesAsTheSentinel()
        {
            // Exactly the shape of a save written before coins were persisted.
            var legacy = JsonUtility.FromJson<PlayerSaveData>(
                "{\"playerClass\":\"dwarf\",\"hp\":50,\"maxHp\":50,\"experience\":120,\"level\":3}");

            Assert.AreEqual(-1, legacy.coins,
                "JsonUtility runs field initialisers before overwriting what the JSON " +
                "carries, which is the whole mechanism this migration rests on.");
        }

        // ── Restore ─────────────────────────────────────────────────────────

        [Test]
        public void Restore_WritesTheSavedBalance()
        {
            var wallet = MakeWallet(0);
            Restore(wallet.gameObject, new PlayerSaveData { coins = 250 });

            Assert.AreEqual(250, wallet.Coins);
        }

        [Test]
        public void Restore_ZeroIsARealBalance_AndIsApplied()
        {
            var wallet = MakeWallet(90);
            Restore(wallet.gameObject, new PlayerSaveData { coins = 0 });

            Assert.AreEqual(0, wallet.Coins,
                "A player who spent everything must load broke. Zero is data, not absence — " +
                "that is what the -1 sentinel buys.");
        }

        [Test]
        public void Restore_LegacySave_LeavesTheWalletAlone()
        {
            var wallet = MakeWallet(90);
            Restore(wallet.gameObject, new PlayerSaveData()); // coins == -1

            Assert.AreEqual(90, wallet.Coins,
                "A save that says nothing about money must not be read as saying zero.");
        }

        [Test]
        public void Restore_PlayerWithoutAWallet_DoesNotThrow()
        {
            var go = new GameObject("walletless");
            _scene.Add(go);

            Assert.DoesNotThrow(() => Restore(go, new PlayerSaveData { coins = 10 }),
                "EntitySetup adds the wallet, but a restore must not be the thing that " +
                "breaks when it has not run yet.");
        }

        // ── The round trip, which is what actually broke ────────────────────

        [Test]
        public void SaveThenLoad_KeepsTheBalance()
        {
            var earned = MakeWallet(0);
            earned.Add(35);                 // a coin pickup
            earned.Add(45);                 // selling to a vendor
            Assert.AreEqual(80, earned.Coins, "Pre-condition: the wallet itself works.");

            // Serialise the way SaveFileManager does, and read it back the same way, so the
            // test exercises the format rather than an in-memory struct copy.
            var saved = new PlayerSaveData { coins = earned.Coins };
            string json = JsonUtility.ToJson(saved);
            var loaded = JsonUtility.FromJson<PlayerSaveData>(json);

            var fresh = MakeWallet(0);      // a new session: Awake would reset to startingCoins
            Restore(fresh.gameObject, loaded);

            Assert.AreEqual(80, fresh.Coins,
                "This is the whole bug: eighty coins earned in one session, zero in the next.");
        }

        [Test]
        public void SavedJson_CarriesTheCoinsField()
        {
            string json = JsonUtility.ToJson(new PlayerSaveData { coins = 7 });

            StringAssert.Contains("\"coins\":7", json,
                "If the field is not in the written JSON there is nothing to load back, " +
                "however well the restorer behaves.");
        }
    }
}
