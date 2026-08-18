using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Keeps sprite atlases from quietly swallowing vendor demo content.
    ///
    /// A SpriteAtlas packable can be a folder, and a folder packable pulls in
    /// everything underneath it — including whatever a third-party pack ships in its
    /// Demo directory. <c>vfx.spriteatlas</c> packed all of <c>_Project/Art/VFX</c>,
    /// so the SlashVFX demo mannequin's 224 KB diffuse texture and the rest of its
    /// demo art were being built into the game's VFX atlas. 518 packed sprites where
    /// 162 were actually used.
    ///
    /// Nothing about that was visible: the atlas file lists one folder GUID, and the
    /// bloat only shows up in the built texture. Hence this test — the cost of a
    /// folder packable is invisible at the declaration and obvious only here.
    /// </summary>
    [TestFixture]
    public class SpriteAtlasPackablesTests
    {
        private const string ATLAS_FOLDER = "Assets/_Project/SpriteAtlases";

        /// <summary>Path fragments that mark content shipped for demonstration, never for the game.</summary>
        private static readonly string[] DemoMarkers = { "/Demo/", "/Demos/", "/Example/", "/Examples/", "/Sample/", "/Samples/" };

        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr" };

        private static IEnumerable<SpriteAtlas> AllAtlases()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SpriteAtlas", new[] { ATLAS_FOLDER }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas != null) yield return atlas;
            }
        }

        /// <summary>Every texture a packable drags in — resolving folder packables the way Unity does.</summary>
        private static IEnumerable<string> TexturesReachedBy(Object packable)
        {
            var path = AssetDatabase.GetAssetPath(packable);
            if (string.IsNullOrEmpty(path)) yield break;

            if (!AssetDatabase.IsValidFolder(path))
            {
                yield return path;
                yield break;
            }

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var p = file.Replace('\\', '/');
                if (TextureExtensions.Any(e => p.EndsWith(e, System.StringComparison.OrdinalIgnoreCase)))
                    yield return p;
            }
        }

        private static bool IsDemoContent(string path)
            => DemoMarkers.Any(m => path.Replace('\\', '/').Contains(m));

        [Test]
        public void AtlasFolderExists()
        {
            Assert.IsTrue(AssetDatabase.IsValidFolder(ATLAS_FOLDER),
                $"{ATLAS_FOLDER} is the one place atlases live (see CLAUDE.md). " +
                "If it moved, this whole fixture silently stops checking anything.");
        }

        [Test]
        public void ProjectHasAtLeastOneAtlas()
        {
            Assert.IsNotEmpty(AllAtlases().ToList(),
                "Zero atlases found — the query is wrong, not the project.");
        }

        [Test]
        public void NoAtlasPacksVendorDemoContent()
        {
            var offenders = new List<string>();

            foreach (var atlas in AllAtlases())
            {
                var atlasPath = AssetDatabase.GetAssetPath(atlas);
                foreach (var packable in SpriteAtlasExtensions.GetPackables(atlas))
                {
                    if (packable == null) continue;
                    foreach (var tex in TexturesReachedBy(packable))
                        if (IsDemoContent(tex))
                            offenders.Add($"{Path.GetFileName(atlasPath)} <- {tex}");
                }
            }

            Assert.IsEmpty(offenders,
                "These atlases pack demo art, which then ships in the build. A folder packable " +
                "takes everything beneath it, so pointing one at a vendor pack's root drags in its " +
                "Demo directory too. Point the packable at the specific texture folder instead.\n\n" +
                string.Join("\n  ", offenders));
        }

        [Test]
        public void NoAtlasPacksAFolderThatContainsAnotherAtlasesTerritory()
        {
            // Two atlases over the same sprites makes Unity warn once per sprite and ships
            // the atlas twice — the 3077-warning incident that killed Art/Tiles/Atlas_Tiles.
            var claims = new Dictionary<string, string>();
            var conflicts = new List<string>();

            foreach (var atlas in AllAtlases())
            {
                var atlasPath = AssetDatabase.GetAssetPath(atlas);
                foreach (var packable in SpriteAtlasExtensions.GetPackables(atlas))
                {
                    if (packable == null) continue;
                    var p = AssetDatabase.GetAssetPath(packable);
                    if (string.IsNullOrEmpty(p)) continue;

                    if (claims.TryGetValue(p, out var owner) && owner != atlasPath)
                        conflicts.Add($"{p} claimed by both {Path.GetFileName(owner)} and {Path.GetFileName(atlasPath)}");
                    else
                        claims[p] = atlasPath;
                }
            }

            Assert.IsEmpty(conflicts,
                "The same folder is packed by more than one atlas:\n  " + string.Join("\n  ", conflicts));
        }

        [Test]
        public void VfxAtlas_PacksTextureFoldersNotTheWholeArtTree()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>($"{ATLAS_FOLDER}/vfx.spriteatlas");
            Assert.IsNotNull(atlas, "vfx.spriteatlas is missing.");

            var packables = SpriteAtlasExtensions.GetPackables(atlas)
                .Where(p => p != null)
                .Select(AssetDatabase.GetAssetPath)
                .ToList();

            Assert.IsNotEmpty(packables);
            CollectionAssert.DoesNotContain(packables, "Assets/_Project/Art/VFX",
                "Packing the whole Art/VFX tree is what pulled the SlashVFX demo art into the " +
                "build. List the texture folders explicitly so adding a vendor pack does not " +
                "silently add its demo scene's art to the atlas.");
        }
    }
}
