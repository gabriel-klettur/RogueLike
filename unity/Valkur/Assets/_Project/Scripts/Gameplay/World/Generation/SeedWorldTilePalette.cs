using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.WorldGen;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.World.Generation
{
    /// <summary>
    /// What the shipped auto-tile packs can draw, asked the way a generated world needs it:
    /// "which pairs of terrain have a pack" and "what tile has THESE four corners".
    ///
    /// <para><b>Corner16 only.</b> The three Blob16 rulesets in the catalogue carry zero slots and
    /// resolve nothing, and <c>FindTransitionRuleset</c> returns the first pair match whatever its
    /// model — for sand/ocean that is an empty Blob16 sheet — so every lookup here filters to the
    /// model the art actually exists in.</para>
    ///
    /// <para><b>A solid tile comes from ONE canonical pack per terrain</b>
    /// (<c>FindPaintRuleset</c>, the same choice the Tile editor's auto-brush makes), so a field of
    /// grass is drawn from one sheet instead of alternating between the grass half of every pack
    /// that happens to contain grass.</para>
    /// </summary>
    public sealed class SeedWorldTilePalette
    {
        private readonly TerrainCatalog _catalog;
        private readonly Dictionary<string, TilesetRuleset> _solid = new Dictionary<string, TilesetRuleset>();
        private readonly Dictionary<long, TilesetRuleset> _pairs = new Dictionary<long, TilesetRuleset>();
        private readonly Dictionary<Sprite, string> _names = new Dictionary<Sprite, string>();
        private readonly Dictionary<string, int> _terrainIds = new Dictionary<string, int>();
        private readonly Dictionary<TilesetRuleset, AutoTileSheetFilter> _sheets = new Dictionary<TilesetRuleset, AutoTileSheetFilter>();

        /// <summary>
        /// A sheet is named by what precedes a sprite's grid suffix: <c>grass_dirt2_r00_c00</c>,
        /// <c>tileset3_r3_c1</c> and <c>tileset7_32_96</c> are sheets grass_dirt2, tileset3 and
        /// tileset7. Read off the NAME so the bake needs no TileCatalog — building one loads every
        /// tile sprite in the project.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("A compiled Regex is immutable and holds no Unity objects, so it " +
            "cannot go stale across a Play session.")]
        private static readonly Regex SheetSuffix = new Regex(@"^(.*?)_(r\d+_c\d+|\d+_\d+)$", RegexOptions.CultureInvariant);
        private readonly List<TilesetRuleset> _corner16 = new List<TilesetRuleset>();
        private readonly string[] _corners = new string[4];

        public SeedWorldTilePalette(TerrainCatalog catalog)
        {
            _catalog = catalog;
            if (catalog == null) return;

            foreach (var r in catalog.Rulesets)
            {
                if (r == null || r.Model != AutoTileModel.Corner16) continue;
                if (string.IsNullOrEmpty(r.TerrainPrimary) || string.IsNullOrEmpty(r.TerrainSecondary)) continue;
                _corner16.Add(r);
                long key = PairKey(r.TerrainPrimary, r.TerrainSecondary);
                if (!_pairs.ContainsKey(key)) _pairs[key] = r;
            }
        }

        public bool HasCatalog => _catalog != null && _corner16.Count > 0;

        /// <summary>True when some Corner16 pack draws the boundary between the two terrains.</summary>
        public bool Compatible(string a, string b)
            => a == b || (a != null && b != null && _pairs.ContainsKey(PairKey(a, b)));

        /// <summary>
        /// The tile whose corners are <paramref name="sw"/>, <paramref name="se"/>,
        /// <paramref name="ne"/>, <paramref name="nw"/>. Returns null only when no pack contains
        /// any of them. <paramref name="hardCut"/> is set when the corners could not be drawn as
        /// they are (three terrains, or a pair with no pack) and the majority terrain was used.
        /// </summary>
        public Sprite Resolve(string sw, string se, string ne, string nw, int hashSeed, out bool hardCut)
        {
            hardCut = false;
            _corners[0] = sw; _corners[1] = se; _corners[2] = ne; _corners[3] = nw;

            string a = sw, b = null;
            for (int i = 1; i < 4; i++)
            {
                string c = _corners[i];
                if (c == a || c == b) continue;
                if (b == null) { b = c; continue; }
                hardCut = true; // a third terrain
                break;
            }

            if (!hardCut && b == null) return Solid(a, hashSeed);

            if (!hardCut && _pairs.TryGetValue(PairKey(a, b), out var pack))
            {
                byte mask = 0;
                if (sw == pack.TerrainSecondary) mask |= BitmaskCalculator.BitCornerSW;
                if (se == pack.TerrainSecondary) mask |= BitmaskCalculator.BitCornerSE;
                if (ne == pack.TerrainSecondary) mask |= BitmaskCalculator.BitCornerNE;
                if (nw == pack.TerrainSecondary) mask |= BitmaskCalculator.BitCornerNW;
                var sprite = RulesetSolver.ResolveCorner(pack, mask, hashSeed, SheetOf(pack));
                if (sprite != null) return sprite;
            }

            hardCut = true;
            return Solid(Majority(), hashSeed);
        }

        /// <summary>A solid tile of one terrain, from its canonical pack.</summary>
        public Sprite Solid(string terrain, int hashSeed)
        {
            var pack = SolidPack(terrain);
            if (pack == null) return null;
            byte mask = terrain == pack.TerrainSecondary ? (byte)0x0F : (byte)0;
            return RulesetSolver.ResolveCorner(pack, mask, hashSeed, SheetOf(pack));
        }

        private TilesetRuleset SolidPack(string terrain)
        {
            if (string.IsNullOrEmpty(terrain)) return null;
            if (_solid.TryGetValue(terrain, out var cached)) return cached;

            TilesetRuleset pack = _catalog != null ? _catalog.FindPaintRuleset(terrain) : null;
            if (pack == null || pack.Model != AutoTileModel.Corner16)
            {
                pack = null;
                // A terrain that is only ever a SECONDARY (water_deep, dirt, lava) is still drawable
                // solid, from the all-secondary slot of a pack that contains it.
                for (int i = 0; i < _corner16.Count; i++)
                    if (_corner16[i].TerrainPrimary == terrain || _corner16[i].TerrainSecondary == terrain)
                    { pack = _corner16[i]; break; }
            }

            _solid[terrain] = pack;
            return pack;
        }

        /// <summary>
        /// ONE sheet per pack for the whole world. A pack merges several sheets of the same pair
        /// and the solver picks among all their variants by cell hash, so an open field comes out
        /// as a checkerboard of slightly different greens — measured on the first bake, grass from
        /// grass_dirt, grass_dirt2, grass_dirt3 and grass_dirt6 in adjacent tiles. The Tile editor
        /// fixes the same thing with the sheet the author clicked; a generated world has no click,
        /// so it takes the first sheet (in ordinal order, so it is the same every bake) that covers
        /// all sixteen slots. Variety WITHIN that sheet still alternates by hash.
        /// </summary>
        private AutoTileSheetFilter SheetOf(TilesetRuleset pack)
        {
            if (_sheets.TryGetValue(pack, out var cached)) return cached;

            var bySheet = new SortedDictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
            var coverage = new Dictionary<string, HashSet<int>>();
            int slotCount = 0;
            foreach (var slot in pack.CornerSlots)
            {
                slotCount++;
                if (slot.variants == null) continue;
                foreach (var sprite in slot.variants)
                {
                    if (sprite == null) continue;
                    var m = SheetSuffix.Match(sprite.name);
                    string sheet = m.Success ? m.Groups[1].Value : sprite.name;
                    if (!bySheet.TryGetValue(sheet, out var names)) bySheet[sheet] = names = new HashSet<string>();
                    names.Add(sprite.name);
                    if (!coverage.TryGetValue(sheet, out var covered)) coverage[sheet] = covered = new HashSet<int>();
                    covered.Add((int)slot.slot);
                }
            }

            AutoTileSheetFilter filter = null;
            foreach (var kv in bySheet)
                if (coverage[kv.Key].Count == slotCount)
                {
                    filter = AutoTileSheetFilter.ForNames(kv.Key, kv.Value);
                    break;
                }

            _sheets[pack] = filter;
            return filter;
        }

        private string Majority()
        {
            string best = _corners[0];
            int bestCount = 0;
            for (int i = 0; i < 4; i++)
            {
                int count = 0;
                for (int j = 0; j < 4; j++) if (_corners[j] == _corners[i]) count++;
                if (count > bestCount) { bestCount = count; best = _corners[i]; }
            }
            return best;
        }

        /// <summary>
        /// The name an overlay file stores for this sprite — its path under <c>Resources/Tiles/</c>
        /// without extension, which <c>OverlayLoader</c> loads directly. A bare sprite name would
        /// also load for most packs, but sand_rock keeps its sprites one folder deeper
        /// (<c>tileset7_slices/</c>), and a name the category probe misses falls through to a
        /// synchronous <c>Resources.LoadAll</c> of every tile in the project — a hard freeze.
        /// </summary>
        public string NameOf(Sprite sprite)
        {
            if (sprite == null) return string.Empty;
            if (_names.TryGetValue(sprite, out var cached)) return cached;

            string name = sprite.name;
#if UNITY_EDITOR
            const string Prefix = "Assets/_Project/Resources/Tiles/";
            string path = UnityEditor.AssetDatabase.GetAssetPath(sprite);
            if (!string.IsNullOrEmpty(path) && path.StartsWith(Prefix))
            {
                string rel = path.Substring(Prefix.Length);
                int dot = rel.LastIndexOf('.');
                if (dot > 0) rel = rel.Substring(0, dot);
                // A sprite sliced out of a sheet shares the sheet's asset path; only a sprite that
                // IS its file may be addressed by the path.
                if (System.IO.Path.GetFileName(rel) == sprite.name) name = rel;
            }
#endif
            _names[sprite] = name;
            return name;
        }

        private long PairKey(string a, string b)
        {
            int ia = TerrainId(a), ib = TerrainId(b);
            if (ia > ib) { int t = ia; ia = ib; ib = t; }
            return ((long)ia << 32) | (uint)ib;
        }

        private int TerrainId(string terrain)
        {
            if (!_terrainIds.TryGetValue(terrain, out int id))
            {
                id = _terrainIds.Count;
                _terrainIds[terrain] = id;
            }
            return id;
        }
    }
}
