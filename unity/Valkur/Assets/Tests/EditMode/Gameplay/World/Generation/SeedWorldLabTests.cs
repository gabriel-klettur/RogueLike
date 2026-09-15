using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data.WorldGen;
using Valkur.Gameplay;
using Valkur.Gameplay.World.Generation;

namespace Valkur.Tests.EditMode.Gameplay.World.Generation
{
    /// <summary>
    /// Seed World is kept apart from the game until it is refined (project decision, 2026-09-14).
    ///
    /// <para>What "apart" has to mean to be true rather than a convention: with the lab off nothing
    /// builds or enters a generated world from ANY of its doors (the launcher both the editor and
    /// the console go through, and the console command itself), and the boot installs nothing of it
    /// — the streamer used to be a boot step in every build.</para>
    /// </summary>
    [TestFixture]
    public class SeedWorldLabTests
    {
        [TearDown]
        public void TearDown()
        {
            SeedWorldNewGame.Cancel();
            SeedWorldLab.SetOverrideForTests(null);
        }

        [Test]
        public void WithTheLabOff_TheMenuCannotArmASeededNewGame()
        {
            SeedWorldLab.SetOverrideForTests(false);

            Assert.IsFalse(SeedWorldNewGame.Available, "The menu must not even offer it.");
            Assert.IsFalse(SeedWorldNewGame.Request(1337), "And a request that slipped through must be refused.");
            Assert.IsFalse(SeedWorldNewGame.IsPending, "Nothing may be waiting for the next boot.");
        }

        [Test]
        public void WithTheLabOn_ASeededNewGameWaitsForTheBoot_AndCanBeWithdrawn()
        {
            SeedWorldLab.SetOverrideForTests(true);

            Assert.IsTrue(SeedWorldNewGame.Request(1337));
            Assert.IsTrue(SeedWorldNewGame.IsPending, "The request is carried to the next gameplay boot.");
            Assert.AreEqual("partida_1337", SeedWorldNewGame.SlotFor(1337), "One slot per seed, the console's naming.");

            SeedWorldNewGame.Cancel();
            Assert.IsFalse(SeedWorldNewGame.IsPending);
        }

        [Test]
        public void ASeededNewGame_SurvivesTheLoadingScreenTakingItsSignal()
        {
            SeedWorldLab.SetOverrideForTests(true);
            Assert.IsTrue(SeedWorldNewGame.Request(1337));

            // What LoadingScreenController does between the menu and the boot: it ASSIGNS the ready
            // delegate in Start and clears it on teardown. A request subscribed to that delegate was
            // erased right here, and the seeded new game never started.
            System.Action screen = () => { };
            try
            {
                LoadingReporter.OnGameplayReady = screen;
                LoadingReporter.Clear();
                LoadingReporter.OnGameplayReady = screen;
                Assert.IsTrue(SeedWorldNewGame.IsPending);

                // In Edit Mode the launcher stops at its Play Mode gate, which is the proof the ready
                // signal reached the request.
                LogAssert.Expect(LogType.Warning, new Regex("La nueva partida con semilla no pudo empezar"));
                LoadingReporter.ReportGameplayReady();

                Assert.IsFalse(SeedWorldNewGame.IsPending, "The ready signal spends the request; it must not fire twice.");
            }
            finally
            {
                LoadingReporter.Clear();
            }
        }

        [Test]
        public void DuringATestRun_TheLabReadsOff_UnlessAFixturePinsIt()
        {
            SeedWorldLab.SetOverrideForTests(null);
            Assert.IsFalse(SeedWorldLab.Enabled,
                "The switch is this machine's PlayerPrefs. A fixture that does not pin it must read it OFF, or the " +
                "menu fixtures are red only on a machine where somebody once turned the lab on.");
        }

        [Test]
        public void WithTheLabOff_TheLauncherRefuses_BeforeAnythingElse()
        {
            SeedWorldLab.SetOverrideForTests(false);

            var outcome = SeedWorldLauncher.BuildAndLoad(new WorldGenSettings { seed = 1337 }, "zzz_lab_off", live: true);

            Assert.IsFalse(outcome.Succeeded);
            Assert.AreEqual(SeedWorldLab.OffMessage, outcome.Error,
                "The lab switch is the FIRST gate: a build refused for any later reason would still have been attempted.");
            Assert.IsNull(outcome.Result, "Nothing may have been baked.");
        }

        [Test]
        public void WithTheLabOn_TheLauncherReachesItsNextGate()
        {
            SeedWorldLab.SetOverrideForTests(true);

            var outcome = SeedWorldLauncher.BuildAndLoad(new WorldGenSettings { seed = 1337 }, "zzz_lab_on", live: true);

            Assert.AreNotEqual(SeedWorldLab.OffMessage, outcome.Error,
                "Switched on, the lab must not be what refuses (here it is Play Mode, which EditMode is not).");
        }

        [Test]
        public void WithTheLabOff_TheConsoleDoesNotStartANewWorld()
        {
            SeedWorldLab.SetOverrideForTests(false);
            var go = new GameObject("LabConsole");
            try
            {
                var console = go.AddComponent<DevConsole>();
                var cmd = typeof(DevConsole).GetMethod("CmdSeedWorld", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(cmd, "Sanity: the seedworld handler exists.");

                string answer = (string)cmd.Invoke(console, new object[] { new[] { "seedworld", "nueva", "1337" } });

                Assert.AreEqual(SeedWorldLab.OffMessage, answer);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheBootSequence_InstallsNothingOfSeedWorld_ButItsEditor()
        {
            // The authoring editor is the one legitimate reference: the lab switch lives inside it,
            // and an editor with no General Editor entry cannot be opened at all. Everything else
            // (the streamer, the launcher, a seeded new game) must be absent, so the guard strips the
            // editor's own names and comments and then refuses any "SeedWorld" left over — a future
            // boot step re-adding the streamer fails here whatever it is called.
            string bootstrap = Path.Combine(Application.dataPath, "_Project", "Scripts", "Gameplay", "Bootstrap");
            string[] files = Directory.GetFiles(bootstrap, "GameplaySceneSetup*.cs");
            Assert.That(files.Length, Is.GreaterThan(0), "Sanity: the boot sources were found.");

            bool editorSeen = false;
            foreach (string file in files)
            {
                string source = Regex.Replace(File.ReadAllText(file), @"//.*$", string.Empty, RegexOptions.Multiline);
                if (source.Contains("EnsureSeedWorldEditor")) editorSeen = true;
                string rest = source
                    .Replace("Valkur.Gameplay.Editors.SeedWorld.SeedWorldRuntimeEditor", string.Empty)
                    .Replace("EnsureSeedWorldEditor", string.Empty)
                    .Replace("\"SeedWorldEditor\"", string.Empty)
                    .Replace("SeedWorldEditor created", string.Empty);
                Assert.That(rest, Does.Not.Contain("SeedWorld"),
                    $"{Path.GetFileName(file)} references Seed World beyond its authoring editor. The boot must not " +
                    "depend on it while it is a lab: the streamer is created when the lab walks a player into a " +
                    "generated world.");
            }
            Assert.IsTrue(editorSeen, "The Seed World editor must still be installed (the lab switch lives in it).");
        }
    }
}
