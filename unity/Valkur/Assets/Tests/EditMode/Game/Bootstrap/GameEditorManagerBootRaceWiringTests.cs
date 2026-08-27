using System.IO;
using NUnit.Framework;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Pins the fix for the "opens without closing whatever else is open" race: every
    /// runtime editor's OnEnable does <c>if (GameEditorManager.HasInstance) …Register(this)</c>
    /// with NO retry — only Tile/General/DungeonNodeGraph self-heal via
    /// <c>GameEditorManager.EnsureInstance()</c>. If the manager doesn't exist yet when one
    /// of the OTHER ten editors (Spawners, Buildings, FSM, Items, Spells, Entities, Boss,
    /// Inventory, Particles, Lighting) runs its OnEnable, Register is silently skipped
    /// FOREVER — that editor never joins the exclusivity group.
    ///
    /// Before this fix the protection was incidental: <c>EnsureTileEditor()</c> happened to
    /// run before any of the non-retrying editors and self-created the manager as a side
    /// effect. <c>GameplaySceneSetup.Start</c> now creates it explicitly, first, so the
    /// guarantee no longer depends on Ensure*Editor call ORDER. Static source-text checks,
    /// no Unity scene needed — mirrors <see cref="GameplaySceneSetupBootOrderTests"/>.
    /// </summary>
    [TestFixture]
    public class GameEditorManagerBootRaceWiringTests
    {
        private static string ReadScript(string relativePath)
        {
            string path = Path.Combine(UnityEngine.Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"Script not found at expected path: {path}");
            return File.ReadAllText(path);
        }

        /// <summary>
        /// Same source with every comment line blanked out.
        ///
        /// The guard checks below assert something about CODE — "is this call inside an
        /// open <c>#if UNITY_EDITOR</c>" — by scanning raw text, and raw text cannot tell a
        /// directive from prose. A doc comment that merely mentions
        /// <c>#if UNITY_EDITOR</c> (explaining why the call is deliberately NOT inside one)
        /// read as an open guard and failed the test that comment was written to describe.
        /// Blanking rather than deleting the lines keeps every index meaningful, so a
        /// failure message still points at a real offset.
        /// </summary>
        private static string StripComments(string src)
        {
            var lines = src.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                    lines[i] = new string(' ', lines[i].Length);
            }
            return string.Join("\n", lines);
        }

        [Test]
        public void GameEditorManager_EnsureInstance_IsCalled_InGameplaySceneSetupStart()
        {
            string src = ReadScript("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.cs");

            Assert.IsTrue(src.Contains("GameEditorManager.EnsureInstance()"),
                "GameplaySceneSetup.Start must explicitly create the GameEditorManager " +
                "singleton instead of relying on some other Ensure*Editor call to do it " +
                "as a side effect.");
        }

        [Test]
        public void GameEditorManager_EnsureInstance_Precedes_EnsureTileEditor_InStart()
        {
            string src = ReadScript("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.cs");

            int ensureManagerIdx = src.IndexOf("GameEditorManager.EnsureInstance()");
            int ensureTileIdx = src.IndexOf("EnsureTileEditor()");

            Assert.AreNotEqual(-1, ensureManagerIdx, "Expected a GameEditorManager.EnsureInstance() call.");
            Assert.AreNotEqual(-1, ensureTileIdx, "Expected an EnsureTileEditor() call.");

            Assert.Less(ensureManagerIdx, ensureTileIdx,
                "GameEditorManager.EnsureInstance() must run before EnsureTileEditor() — " +
                "and therefore before every other Ensure*Editor call in Start — so the " +
                "exclusivity manager's existence stops being an accident of call order.");
        }

        [Test]
        public void MonsterCatalog_ServiceLocatorRegistration_IsNotGuardedByUnityEditor()
        {
            string src = StripComments(ReadScript("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.Systems2.Editors.cs"));

            int registerIdx = src.IndexOf("ServiceLocator.Register<MonsterCatalog>");
            Assert.AreNotEqual(-1, registerIdx, "Expected a ServiceLocator.Register<MonsterCatalog> call.");

            // The call sits inside an #if UNITY_EDITOR guard iff the LAST "#if
            // UNITY_EDITOR" before it opens later than the LAST "#endif" before it closes
            // (i.e. that #if is still open at registerIdx).
            int precedingGuardIdx = src.LastIndexOf("#if UNITY_EDITOR", registerIdx);
            int precedingEndifIdx = src.LastIndexOf("#endif", registerIdx);
            bool openGuardBeforeCall = precedingGuardIdx != -1 &&
                (precedingEndifIdx == -1 || precedingEndifIdx < precedingGuardIdx);

            Assert.IsFalse(openGuardBeforeCall,
                "ServiceLocator.Register<MonsterCatalog> must run unconditionally (no " +
                "#if UNITY_EDITOR) so a BUILT player also publishes the catalog — this is " +
                "the whole point of the fallback, unlike the SerializedObject injection " +
                "next to it which is Editor-only by necessity.");
        }

        [Test]
        public void SpawnerTemplateCatalog_ServiceLocatorRegistration_IsNotGuardedByUnityEditor()
        {
            string src = StripComments(ReadScript("_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.Systems2.World.cs"));

            int registerIdx = src.IndexOf("ServiceLocator.Register<SpawnerTemplateCatalog>");
            Assert.AreNotEqual(-1, registerIdx, "Expected a ServiceLocator.Register<SpawnerTemplateCatalog> call.");

            int precedingGuardIdx = src.LastIndexOf("#if UNITY_EDITOR", registerIdx);
            int precedingEndifIdx = src.LastIndexOf("#endif", registerIdx);
            bool openGuardBeforeCall = precedingGuardIdx != -1 &&
                (precedingEndifIdx == -1 || precedingEndifIdx < precedingGuardIdx);

            Assert.IsFalse(openGuardBeforeCall,
                "ServiceLocator.Register<SpawnerTemplateCatalog> must run unconditionally.");
        }
    }
}
