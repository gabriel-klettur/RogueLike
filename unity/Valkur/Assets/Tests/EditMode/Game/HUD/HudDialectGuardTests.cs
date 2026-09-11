using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// The HUD speaks one language (<c>.github/HUD_VISUAL_LANGUAGE.md</c>), and three ways of
    /// breaking it have each shipped: a player-facing window wearing the EDITOR theme (the inventory
    /// wore <c>TileEditorTheme</c>, a live-tweakable static, so retuning the Tile editor recoloured
    /// the player's bag); a runtime sprite loaded through <c>AssetDatabase</c> (the tray buttons
    /// were grey squares in a player build); and a hint naming a key by hand (the inventory taught
    /// "Tab/I close | Q drop" — Tab is the stance, Q the teleport).
    ///
    /// <para>A guard over SOURCE, because none of the three fails at runtime in the Editor: the
    /// theme looks fine until an author retunes it, AssetDatabase works in the Editor, and a wrong
    /// key label is only wrong in the player's hands.</para>
    /// </summary>
    public class HudDialectGuardTests
    {
        /// <summary>Folders whose code draws what the PLAYER sees in a build.</summary>
        private static readonly string[] PlayerFacing =
        {
            "_Project/Scripts/Gameplay/Inventory",
            "_Project/Scripts/Gameplay/HUD",
            "_Project/Scripts/Gameplay/Chat",
            "_Project/Scripts/Gameplay/Vendors",
            "_Project/Scripts/Gameplay/Crafting",
            "_Project/Scripts/Gameplay/Quests",
            "_Project/Scripts/UI/HUD",
            "_Project/Scripts/UI/PauseMenu",
        };

        private static IEnumerable<string> Files()
        {
            foreach (var rel in PlayerFacing)
            {
                string dir = Path.Combine(Application.dataPath, rel);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)) yield return f;
            }
        }

        [Test]
        public void NoPlayerFacingSurface_WearsTheEditorTheme()
        {
            int scanned = 0;
            var offenders = new List<string>();
            foreach (var f in Files())
            {
                scanned++;
                string src = File.ReadAllText(f);
                if (src.Contains("TileEditorTheme") || src.Contains("TileEditorUIHelpers") || src.Contains("EditorUIHelpers"))
                    offenders.Add(Path.GetFileName(f));
            }
            Assert.Greater(scanned, 50, "Vacuous: the scan found almost no files.");
            Assert.IsEmpty(offenders, "Editor chrome on a player surface: " + string.Join(", ", offenders));
        }

        [Test]
        public void NoPlayerFacingSurface_LoadsRuntimeArtThroughAssetDatabase()
        {
            var offenders = new List<string>();
            foreach (var f in Files())
                if (File.ReadAllText(f).Contains("AssetDatabase.LoadAssetAtPath"))
                    offenders.Add(Path.GetFileName(f));
            Assert.IsEmpty(offenders,
                "AssetDatabase does not exist in a player build; reference the sprite from a style asset: " +
                string.Join(", ", offenders));
        }

        [Test]
        public void NoPlayerFacingSurface_TeachesTheOldInventoryKeys()
        {
            var offenders = new List<string>();
            foreach (var f in Files())
            {
                string src = File.ReadAllText(f);
                if (src.Contains("Tab/I") || src.Contains("Q drop")) offenders.Add(Path.GetFileName(f));
            }
            Assert.IsEmpty(offenders, "Read key labels from the live binding: " + string.Join(", ", offenders));
        }
    }
}
