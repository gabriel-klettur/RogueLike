using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Generation;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// "Partida con semilla" in the main menu: a new run that walks out of Pepitoria into a
    /// generated world, offered only while the Seed World lab is on (project decision, 2026-09-14).
    ///
    /// <para>Pinned here: the row exists only with the lab on and sits next to the ordinary new
    /// game; choosing it arms nothing until the new game really starts; and every other way into
    /// the game withdraws the request, because a request waits for the NEXT boot and one left armed
    /// by a selector the player backed out of would turn a Continue into a trip.</para>
    /// </summary>
    public class MainMenuSeededNewGameTests
    {
        private GameObject _go;
        private MainMenuUI _menu;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            SeedWorldNewGame.Cancel();
            SeedWorldLab.SetOverrideForTests(null);
        }

        private void BuildMenu(bool lab)
        {
            SeedWorldLab.SetOverrideForTests(lab);
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            _go = new GameObject("SeededNewGameMenu");
            _menu = _go.AddComponent<MainMenuUI>();
            Invoke("Start");
        }

        [Test]
        public void WithTheLabOff_TheMenuOffersNoSeededGame()
        {
            BuildMenu(lab: false);

            CollectionAssert.DoesNotContain(MenuItems(), "SeededNewGame");
            CollectionAssert.Contains(MenuItems(), "NewGame", "Sanity: the ordinary new game is still there.");
        }

        [Test]
        public void WithTheLabOn_TheSeededGameSitsRightAfterTheNewGame_WithItsOwnLabel()
        {
            BuildMenu(lab: true);

            var items = MenuItems();
            int newGame = items.IndexOf("NewGame");
            Assert.AreEqual(newGame + 1, items.IndexOf("SeededNewGame"), "Both new games belong together in the list.");

            var labels = (string[])Field("_menuOptions").GetValue(_menu);
            Assert.AreEqual(items.Count, labels.Length);
            Assert.IsFalse(string.IsNullOrWhiteSpace(labels[newGame + 1]));
            Assert.AreNotEqual(labels[newGame], labels[newGame + 1], "Two rows spelled alike cannot be told apart.");
        }

        [Test]
        public void ChoosingTheSeededGame_ArmsTheWorldOnlyWhenTheNewGameStarts()
        {
            BuildMenu(lab: true);

            Invoke("ChooseNewGame", true);
            Assert.IsFalse(SeedWorldNewGame.IsPending, "Opening the class selector commits to nothing.");

            Invoke("ArmOrWithdrawSeededWorld");
            Assert.IsTrue(SeedWorldNewGame.IsPending, "Starting the new game arms the generated world for the boot.");
        }

        [Test]
        public void AnOrdinaryNewGame_WithdrawsARequestLeftFromTheSeededRow()
        {
            BuildMenu(lab: true);
            Invoke("ChooseNewGame", true);
            Invoke("ArmOrWithdrawSeededWorld");
            Assert.IsTrue(SeedWorldNewGame.IsPending);

            Invoke("ChooseNewGame", false);
            Invoke("ArmOrWithdrawSeededWorld");

            Assert.IsFalse(SeedWorldNewGame.IsPending, "A plain new game must not walk into a generated world.");
        }

        [Test]
        public void TurningTheLabOffBeforeConfirming_StartsAPlainNewGame()
        {
            BuildMenu(lab: true);
            Invoke("ChooseNewGame", true);

            SeedWorldLab.SetOverrideForTests(false);
            Invoke("ArmOrWithdrawSeededWorld");

            Assert.IsFalse(SeedWorldNewGame.IsPending);
        }

        /// <summary>
        /// Continue and Load start a scene load, which a fixture cannot do, so their half is read off
        /// the source: each must withdraw the request BEFORE it hands over to the loading screen.
        /// </summary>
        [Test]
        public void ContinueAndLoad_WithdrawTheRequestBeforeTheyLeaveTheMenu()
        {
            string dir = Path.Combine(Application.dataPath, "_Project", "Scripts", "UI", "MainMenu");
            AssertWithdrawsBefore(Path.Combine(dir, "MainMenuUI.InputHandling.cs"), "ContinueMostRecentRun", "BeginTransitionToGame()");
            AssertWithdrawsBefore(Path.Combine(dir, "MainMenuUI.LoadPanel.Actions.cs"), "MMLoadSelectedSave", "LoadingScreenController.Show(");
        }

        private static void AssertWithdrawsBefore(string file, string method, string handOver)
        {
            string source = Regex.Replace(File.ReadAllText(file), @"//.*$", string.Empty, RegexOptions.Multiline);
            int start = source.IndexOf("void " + method + "(", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(start, 0, $"{method} not found in {Path.GetFileName(file)}.");
            int withdraw = source.IndexOf("WithdrawSeededWorld()", start, System.StringComparison.Ordinal);
            int leave = source.IndexOf(handOver, start, System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(leave, 0, $"{method} no longer hands over through {handOver}; re-point this test.");
            Assert.That(withdraw, Is.GreaterThanOrEqualTo(0).And.LessThan(leave),
                $"{method} must call WithdrawSeededWorld() before {handOver}.");
        }

        // ── Reflection helpers ─────────────────────────────────────────────────

        private static FieldInfo Field(string name)
        {
            var f = typeof(MainMenuUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"MainMenuUI.{name} must exist");
            return f;
        }

        private void Invoke(string method, params object[] args)
        {
            var m = typeof(MainMenuUI).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, $"MainMenuUI.{method} must exist");
            m.Invoke(_menu, args.Length == 0 ? null : args);
        }

        private List<string> MenuItems()
        {
            var names = new List<string>();
            foreach (var item in (System.Collections.IEnumerable)Field("_menuItems").GetValue(_menu))
                names.Add(item.ToString());
            return names;
        }
    }
}
