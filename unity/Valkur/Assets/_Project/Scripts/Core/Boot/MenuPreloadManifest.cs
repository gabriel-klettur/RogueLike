namespace Valkur.Core.Boot
{
    /// <summary>
    /// The asset folders worth touching while the player is still in the menu.
    ///
    /// <para><b>Why this exists, in measured numbers.</b> A first boot of the gameplay
    /// scene cost <b>5 649 ms</b> while a second boot in the same session cost
    /// <b>3 115 ms</b> — a 2 534 ms penalty paid once per launch for first-touch asset
    /// paging. Loading these two trees costs <b>1 315 ms</b> (Buildings 334 ms over 1 256
    /// sprites, Tiles 981 ms over 4 117), and doing it from the menu took that first boot
    /// to <b>3 748 ms</b>: <b>1 901 ms recovered, 75 % of the penalty</b>, spent while the
    /// player is reading a menu instead of watching a bar.</para>
    ///
    /// <para><b>Why a declared list rather than a folder walk.</b> A built player cannot
    /// enumerate <c>Resources/</c> — the folder structure does not survive into
    /// <c>resources.assets</c>, only the paths do. So the list is data, and
    /// <c>MenuPreloadManifestTests</c> compares it against the directories on disk in both
    /// directions, which is what stops it becoming the twelfth authored-and-inert table in
    /// this project.</para>
    ///
    /// <para><b>Why per-subfolder and not one root call.</b> <c>Resources.LoadAll</c> is
    /// all-or-nothing and synchronous: one call on <c>Tiles</c> is a 981 ms freeze, which
    /// on an animated menu is a visible stall. Fifty-six chunks average ~23 ms, and the
    /// preloader spends a frame budget on top of that.</para>
    /// </summary>
    public static class MenuPreloadManifest
    {
        /// <summary>Resources subfolders under <c>Buildings/</c>. 1 256 sprites, ~334 ms cold.</summary>
        [SelfHealingStatic("Immutable table of literal path segments, written once at type " +
                           "init and never mutated. Holds no Unity object.")]
        public static readonly string[] BuildingFolders =
        {
            "arcane", "backgrounds", "bandit", "blacksmith", "castles", "combat", "domestic",
            "forest_decoration", "gardens", "graveyard", "houses", "lights", "market",
            "military", "nature", "others", "portals", "props", "quest", "shops", "signs",
            "statues", "temples", "totems", "vegetation", "water",
        };

        /// <summary>Resources subfolders under <c>Tiles/</c>. 4 117 sprites, ~981 ms cold.</summary>
        [SelfHealingStatic("Immutable table of literal path segments, written once at type " +
                           "init and never mutated. Holds no Unity object.")]
        public static readonly string[] TileFolders =
        {
            "castle_pandora", "dirt_sand", "grass_dirt", "grass_dirt2", "grass_dirt3",
            "grass_dirt4", "grass_dirt5", "grass_dirt6", "grass_rock", "grass_rock_1",
            "grass_rock_2", "grass_rock_3", "grass_rock_4", "grass_sand", "multi_tiles",
            "ocean_grass", "ready", "rock_grass", "rock_lava", "rock_water", "sand_grass",
            "sand_ocean", "sand_ocean_2", "sand_ocean_3", "sand_rock", "tileset4", "tileset5",
            "tileset6", "tileset_1", "water_water_deep",
        };

        /// <summary>Every Resources path the menu preloader walks, in order.</summary>
        public static System.Collections.Generic.IEnumerable<string> Paths()
        {
            // Buildings first: it is the smaller tree and the one the boot reaches
            // earliest, so a player who starts immediately still gets some of the benefit.
            foreach (var f in BuildingFolders) yield return "Buildings/" + f;
            foreach (var f in TileFolders) yield return "Tiles/" + f;
        }
    }
}
