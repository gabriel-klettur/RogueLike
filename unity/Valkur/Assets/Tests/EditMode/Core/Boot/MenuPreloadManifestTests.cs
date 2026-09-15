using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Boot;

namespace Valkur.Tests.EditMode.Core.Boot
{
    /// <summary>
    /// The menu preloader's folder list against the folders that actually exist.
    ///
    /// <para>A built player cannot enumerate <c>Resources/</c> — the directory structure
    /// does not survive into <c>resources.assets</c> — so the list has to be data. Data
    /// that describes another part of the project is exactly the shape this repository
    /// keeps finding rotted: a constant that used to be true. Checking it in BOTH
    /// directions is what makes it a claim instead of a hope.</para>
    ///
    /// <para>The failure it prevents is silent by construction: a folder missing from the
    /// list is simply not preloaded, so the boot pays that first-touch cost again and
    /// nothing anywhere says so.</para>
    /// </summary>
    [TestFixture]
    public class MenuPreloadManifestTests
    {
        private static readonly string ResourcesRoot =
            Path.Combine(Application.dataPath, "_Project/Resources");

        private static List<string> FoldersOnDisk(string relative)
        {
            string dir = Path.Combine(ResourcesRoot, relative);
            Assert.IsTrue(Directory.Exists(dir), $"No existe {dir}");
            var names = new List<string>();
            foreach (var d in Directory.GetDirectories(dir))
                names.Add(Path.GetFileName(d));
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        private static void AssertMatches(string relative, string[] declared)
        {
            var onDisk = FoldersOnDisk(relative);
            var listed = new List<string>(declared);
            listed.Sort(System.StringComparer.Ordinal);

            var missing = new List<string>();
            foreach (var d in onDisk) if (!listed.Contains(d)) missing.Add(d);
            Assert.IsEmpty(missing,
                $"Carpetas de Resources/{relative} que NADIE precarga: {string.Join(", ", missing)}. " +
                "El arranque volvera a pagar su primer toque y nada lo dira.");

            var ghosts = new List<string>();
            foreach (var l in listed) if (!onDisk.Contains(l)) ghosts.Add(l);
            Assert.IsEmpty(ghosts,
                $"El manifiesto nombra carpetas que ya no existen en Resources/{relative}: " +
                $"{string.Join(", ", ghosts)}.");
        }

        [Test]
        public void BuildingFolders_MatchWhatIsOnDisk()
            => AssertMatches("Buildings", MenuPreloadManifest.BuildingFolders);

        [Test]
        public void TileFolders_MatchWhatIsOnDisk()
            => AssertMatches("Tiles", MenuPreloadManifest.TileFolders);

        [Test]
        public void EveryPathIsUnique_AndProperlyPrefixed()
        {
            var seen = new HashSet<string>();
            int count = 0;
            foreach (var p in MenuPreloadManifest.Paths())
            {
                count++;
                Assert.IsTrue(seen.Add(p), $"Ruta duplicada en el manifiesto: {p}");
                Assert.IsTrue(p.StartsWith("Buildings/") || p.StartsWith("Tiles/"),
                    $"Ruta sin prefijo de arbol: {p}");
            }
            Assert.AreEqual(MenuPreloadManifest.BuildingFolders.Length +
                            MenuPreloadManifest.TileFolders.Length, count);
        }

        /// <summary>
        /// Buildings before Tiles, because a player who presses Start immediately still
        /// keeps whatever was paged — and the boot reaches the buildings earlier.
        /// </summary>
        [Test]
        public void BuildingsArePreloadedFirst()
        {
            string first = null;
            foreach (var p in MenuPreloadManifest.Paths()) { first = p; break; }
            StringAssert.StartsWith("Buildings/", first);
        }

        /// <summary>
        /// The preloader must give up the moment a real load starts: the player's boot is
        /// the priority, and a half-finished preload is worth exactly what it paged.
        /// </summary>
        [Test]
        public void ThePreloaderYieldsToARealLoad()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/UI/Loading/MenuAssetPreloader.cs"));
            StringAssert.Contains("LoadingScreenController.IsShowing", src,
                "El precargador ya no se aparta cuando el jugador arranca una partida.");
            StringAssert.Contains("LoadFrameBudget", src,
                "Sin presupuesto por fotograma el precargador congela el menu casi un segundo.");
        }
    }
}
