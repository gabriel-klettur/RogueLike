using System.IO;
using NUnit.Framework;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// Pins the boot-order invariant that <c>EnsureTileEditor</c> runs BEFORE
    /// <c>LoadWorldProgressively</c> inside <c>GameplaySceneSetup.Start</c>.
    /// If that order ever drifts, <c>TileOverlayPersistence.ApplyAllOverrides</c>
    /// (invoked deep inside <c>LoadWorldProgressively</c>) silently drops the
    /// collision-tag JSON for every zone — its sink is
    /// <c>TileEditorManager.Instance.CollisionTags</c>, and without the manager
    /// alive that sink is null. The downstream M2 baker then reads every cell
    /// as Wildcard "*", stamps them into the WorldAll sub-tilemap, and the
    /// player ends up blocked on every visual layer regardless of the painted
    /// tag — exactly the "Player on L0 still blocked by tag-7 colliders" bug.
    ///
    /// The test reads <c>GameplaySceneSetup.cs</c> as source text and asserts
    /// that the first occurrence of <c>EnsureTileEditor()</c> precedes the
    /// first occurrence of <c>LoadWorldProgressively()</c>. Static analysis,
    /// no Unity scene needed.
    /// </summary>
    [TestFixture]
    public class GameplaySceneSetupBootOrderTests
    {
        [Test]
        public void EnsureTileEditor_Precedes_LoadWorldProgressively_InStart()
        {
            string scriptPath = Path.Combine(UnityEngine.Application.dataPath,
                "_Project/Scripts/Gameplay/Bootstrap/GameplaySceneSetup.cs");
            Assert.IsTrue(File.Exists(scriptPath),
                $"GameplaySceneSetup.cs not found at expected path: {scriptPath}");

            string src = File.ReadAllText(scriptPath);

            int tileEditorIdx = src.IndexOf("EnsureTileEditor()");
            int loadWorldIdx = src.IndexOf("LoadWorldProgressively()");

            Assert.AreNotEqual(-1, tileEditorIdx,
                "Expected an `EnsureTileEditor()` call in GameplaySceneSetup.cs.");
            Assert.AreNotEqual(-1, loadWorldIdx,
                "Expected a `LoadWorldProgressively()` call in GameplaySceneSetup.cs.");

            Assert.Less(tileEditorIdx, loadWorldIdx,
                "EnsureTileEditor() must appear BEFORE LoadWorldProgressively() in " +
                "GameplaySceneSetup.cs — otherwise ApplyAllOverrides runs without " +
                "TileEditorManager alive, the collision-tag JSON is silently dropped, " +
                "and the M2 per-visual-layer filter is bypassed. " +
                $"Got tileEditorIdx={tileEditorIdx}, loadWorldIdx={loadWorldIdx}.");
        }
    }
}
