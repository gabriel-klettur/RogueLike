using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;

namespace Valkur.Editor.UI
{
    /// <summary>
    /// The three buttons that turn a painted PNG into the art the world bars draw with.
    ///
    /// <para><b>Export Template</b> writes the sheet an artist paints over: the pieces the game
    /// generates today, at exactly the rectangles the game reads them from, plus a magnified guide
    /// with the boxes outlined and numbered and a layout table beside it. Nobody has to compute a
    /// coordinate — repaint inside the boxes.</para>
    ///
    /// <para><b>Import</b> applies the import settings, slices the sheet, sets the 9-slice borders
    /// and assigns the sprites into <c>WorldBarStyle.asset</c>. The borders are the half that gets
    /// forgotten: they are not import SETTINGS, they live per-sprite in the Sprite Editor, and a
    /// frame with no border stretches its own chamfer into a wedge.</para>
    ///
    /// <para><b>Clear</b> drops every slot and puts the readout back on the generated art, which is
    /// also the way to check whether a piece of painted art is actually an improvement.</para>
    ///
    /// <para>The rectangles come from <see cref="WorldBarSheetLayout"/> — the same table the
    /// runtime generator draws into and the tests assert against — so the template, the slice and
    /// the game cannot disagree about where a piece is.</para>
    /// </summary>
    public static class WorldBarSkinImporter
    {
        // NOT under Art/UI/. That folder is packed WHOLE into ui.spriteatlas, whose textureSettings
        // carry filterMode 1 — Bilinear — and an atlas overrides the filter its member textures
        // were imported with. Measured: the sheet imported at PPU 16 with Point filtering, landed
        // in "sactx-71-2048x2048-Uncompressed-ui", and every bar and glyph came back soft. PPU,
        // rect, border and pivot were all correct; only the filter was wrong, which is why the
        // runtime check now asks about the filter too.
        private const string FOLDER = "Assets/_Project/Art/WorldBars";
        private const string SHEET_PATH = FOLDER + "/world_bars.png";
        private const string GUIDE_PATH = FOLDER + "/world_bars_guide.png";
        private const string DOC_PATH = FOLDER + "/world_bars_layout.md";
        private const string STYLE_PATH = "Assets/_Project/Resources/UI/WorldBarStyle.asset";

        /// <summary>How much the guide is blown up. Big enough to see a single texel.</summary>
        private const int GUIDE_SCALE = 8;

        // -- Menu -------------------------------------------------------------

        [MenuItem("Valkur/UI/Export World Bar Skin Template", priority = 300)]
        public static void ExportTemplate()
        {
            var style = LoadStyle();
            if (style == null) return;

            var sheet = BuildSheet(style);
            var atlas = WorldBarArt.GeneratedAtlas;
            if (atlas == null)
            {
                Debug.LogError("[WorldBarSkin] The generated atlas is not available.");
                return;
            }

            Directory.CreateDirectory(FOLDER);

            // The template is the artist's file. Refusing to overwrite it is the whole reason this
            // is a separate button from the guide: re-exporting to pick up a layout change must
            // never be able to erase a day of painting.
            if (File.Exists(SHEET_PATH))
            {
                Debug.LogWarning($"[WorldBarSkin] '{SHEET_PATH}' already exists and was left alone. " +
                                 "Delete it by hand if you really want the generated art back. " +
                                 "The guide and the layout table were rewritten.");
            }
            else
            {
                File.WriteAllBytes(SHEET_PATH, EncodeAtlas(atlas, sheet));
            }

            File.WriteAllBytes(GUIDE_PATH, EncodeGuide(atlas, sheet));
            File.WriteAllText(DOC_PATH, BuildDoc(style, sheet), new UTF8Encoding(false));

            AssetDatabase.Refresh();
            ApplyImportSettings(SHEET_PATH, sheet);
            ApplyGuideImportSettings(GUIDE_PATH);

            Debug.Log($"[WorldBarSkin] Template ready in '{FOLDER}': {sheet.Pieces.Count} pieces on " +
                      $"a {sheet.Width}x{sheet.Height} sheet. Paint over world_bars.png, then run " +
                      "Valkur > UI > Import World Bar Skin.");
        }

        [MenuItem("Valkur/UI/Import World Bar Skin", priority = 301)]
        public static void Import()
        {
            var style = LoadStyle();
            if (style == null) return;

            if (!File.Exists(SHEET_PATH))
            {
                Debug.LogError($"[WorldBarSkin] No sheet at '{SHEET_PATH}'. " +
                               "Run Valkur > UI > Export World Bar Skin Template first.");
                return;
            }

            var sheet = BuildSheet(style);
            ApplyImportSettings(SHEET_PATH, sheet);

            var byName = new Dictionary<string, Sprite>();
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(SHEET_PATH))
                if (obj is Sprite sprite) byName[sprite.name] = sprite;

            // A sheet may deliberately paint only SOME cells. The colour sheet paints twelve of
            // seventeen: both plates, both fills and the plain square are absent because the
            // bar's interior is transparent by design and its filled part is drawn by a shader.
            // The PNG alone cannot tell "not painted" from "painted transparent" - both are empty
            // texels - so the builder writes the list and this reads it.
            var paintedOnly = ReadPaintedList();
            if (paintedOnly != null)
                Debug.Log($"[WorldBarSkin] Manifest lists {paintedOnly.Count} painted piece(s); " +
                          "the rest keep the generated art.");

            // No Undo.RecordObject, and that is deliberate. CLAUDE.md records what it cost the
            // last time a bulk asset tool used it: 193 building templates landed on the GLOBAL
            // editor undo stack and the EditMode suite, which exercises the runtime editors' undo,
            // popped them all back to their empty creation state in memory while the good data sat
            // on disk. This is a tool an operator re-runs, not one they undo.
            int assigned = 0, missing = 0;
            var missingIds = new List<string>();
            foreach (var piece in sheet.Pieces)
            {
                if (paintedOnly != null && !paintedOnly.Contains(piece.Id))
                {
                    style.skin.Set(piece.Id, null, StatusGlyphCount);
                    continue;
                }

                if (byName.TryGetValue(piece.Id, out var sprite))
                {
                    style.skin.Set(piece.Id, sprite, StatusGlyphCount);
                    assigned++;
                }
                else
                {
                    style.skin.Set(piece.Id, null, StatusGlyphCount);
                    missing++;
                    missingIds.Add(piece.Id);
                }
            }

            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
            WorldBarStyle.InvalidateCache();
            WorldBarArt.Invalidate();

            string tail = missing == 0
                ? "."
                : $", {missing} left on the generated art ({string.Join(", ", missingIds)}).";
            Debug.Log($"[WorldBarSkin] Assigned {assigned} painted piece(s){tail}");
        }

        [MenuItem("Valkur/UI/Clear World Bar Skin", priority = 302)]
        public static void Clear()
        {
            var style = LoadStyle();
            if (style == null) return;

            style.skin.Clear();
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
            WorldBarStyle.InvalidateCache();
            WorldBarArt.Invalidate();
            Debug.Log("[WorldBarSkin] Cleared. The bars are back on the generated art.");
        }

        // -- Pieces -----------------------------------------------------------

        /// <summary>
        /// How many status glyphs the sheet carries. Read off the enum rather than off the
        /// generator, so a kind appended to <c>StatusEffectKind</c> gets a cell on the next export
        /// whether or not anybody has drawn its fallback yet.
        /// </summary>
        private static int StatusGlyphCount => System.Enum.GetValues(typeof(StatusEffectKind)).Length;

        private static WorldBarSheetLayout.Sheet BuildSheet(WorldBarStyle style)
            => WorldBarSheetLayout.Build(style.healthRowTexels, style.resourceRowTexels,
                                         style.pipTexels, style.iconTexels, StatusGlyphCount);

        /// <summary>
        /// The `painted` list from the builder's manifest, or null when there is no manifest -
        /// in which case every sliced sprite is assigned, which is what a hand-painted template
        /// without a build step means.
        /// </summary>
        private static HashSet<string> ReadPaintedList()
        {
            string manifest = FOLDER + "/world_bars_build.json";
            if (!File.Exists(manifest)) return null;
            try
            {
                string json = File.ReadAllText(manifest);
                int at = json.IndexOf("\"painted\"", System.StringComparison.Ordinal);
                if (at < 0) return null;
                int open = json.IndexOf('[', at);
                int close = json.IndexOf(']', open);
                if (open < 0 || close < 0) return null;

                var set = new HashSet<string>();
                foreach (var raw in json.Substring(open + 1, close - open - 1).Split(','))
                {
                    string id = raw.Trim().Trim('"');
                    if (id.Length > 0) set.Add(id);
                }
                return set.Count > 0 ? set : null;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WorldBarSkin] Could not read the build manifest: {ex.Message}. " +
                                 "Assigning every sliced piece.");
                return null;
            }
        }

        private static WorldBarStyle LoadStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<WorldBarStyle>(STYLE_PATH);
            if (style == null)
                Debug.LogError($"[WorldBarSkin] No WorldBarStyle at '{STYLE_PATH}'.");
            return style;
        }

        // -- Import settings --------------------------------------------------

        private static void ApplyImportSettings(string path, WorldBarSheetLayout.Sheet sheet)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = WorldBarGeometry.TEXELS_PER_UNIT;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 512;

            // FULL RECT, and this one announces itself loudly when it is missed. Sprite.Create
            // defaults to Tight and so does the importer, which traces the alpha outline into a
            // fitted mesh — and SpriteDrawMode.Sliced REFUSES to work with one. The whole readout
            // is sliced, so the first frame after an import without this logged "Sprite Tiling
            // might not appear correctly because the Sprite used is not generated with Full Rect"
            // once per renderer, attributed to whatever unrelated file happened to run last.
            var texSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(texSettings);
            texSettings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(texSettings);

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            // The rects go through ISpriteEditorDataProvider, not TextureImporter.spritesheet:
            // that property is obsolete in 2022.3 and its replacement is the only route that also
            // owns the name-to-fileId table. Applied AFTER the reimport above, because the
            // provider reads the importer's current settings and sprite mode Multiple has to be
            // in effect before it will accept more than one rect.
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                Debug.LogError($"[WorldBarSkin] No sprite data provider for '{path}'.");
                return;
            }
            provider.InitSpriteEditorDataProvider();

            // Reuse the GUID a piece already has. A fresh GUID per import would repoint every
            // reference to that sprite — including the slots on WorldBarStyle — at a sprite that
            // no longer exists, so re-running the importer would silently unassign the skin.
            var existing = new Dictionary<string, GUID>();
            foreach (var previous in provider.GetSpriteRects())
                existing[previous.name] = previous.spriteID;

            var rects = new SpriteRect[sheet.Pieces.Count];
            for (int i = 0; i < sheet.Pieces.Count; i++)
            {
                var piece = sheet.Pieces[i];
                rects[i] = new SpriteRect
                {
                    name = piece.Id,
                    spriteID = existing.TryGetValue(piece.Id, out var id) ? id : GUID.Generate(),
                    rect = new Rect(piece.X, piece.Y, piece.Width, piece.Height),
                    // Centre pivot on every piece, because that is what the runtime generator uses
                    // and every position in WorldBarLine is expressed as a centre. A pivot that
                    // disagreed would offset each piece by half its own size.
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    // The border is not an import setting — it is per-sprite data that lives here
                    // and nowhere else, and a frame without one stretches its chamfered corners.
                    border = piece.Border,
                };
            }
            provider.SetSpriteRects(rects);

            // The name-to-fileId table is what keeps a YAML reference to "frame_health" pointing at
            // the same sub-asset across re-imports. Skipping it is invisible until the second
            // import, when every assigned slot goes null.
            var nameTable = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameTable != null)
            {
                var pairs = new List<SpriteNameFileIdPair>(rects.Length);
                foreach (var r in rects) pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
                nameTable.SetNameFileIdPairs(pairs);
            }

            provider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>The guide is a picture to look at, not art the game reads.</summary>
        private static void ApplyGuideImportSettings(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        // -- Template + guide -------------------------------------------------

        private static byte[] EncodeAtlas(Texture2D atlas, WorldBarSheetLayout.Sheet sheet)
        {
            var copy = new Texture2D(sheet.Width, sheet.Height, TextureFormat.RGBA32, false);
            try
            {
                copy.SetPixels32(atlas.GetPixels32());
                copy.Apply();
                return copy.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(copy); }
        }

        /// <summary>Blank pixels under each shelf, where its pieces' numbers are written.</summary>
        private const int LABEL_BAND = 16;

        /// <summary>
        /// The template blown up, with every rect outlined and numbered.
        ///
        /// <para>The outline sits in the GUTTER around each rect and the number sits in a band
        /// BELOW its shelf — neither is drawn over the art. The first version put the number
        /// inside the box's top-left corner and it covered the corner of every 5x5 glyph, which
        /// on a guide is worse than useless: it teaches the artist that those texels belong to
        /// the piece.</para>
        ///
        /// <para>Only the vertical spacing between shelves is expanded. Positions WITHIN a shelf
        /// stay at their true relative offsets, and the exact rectangles are in the layout table
        /// beside this image.</para>
        /// </summary>
        private static byte[] EncodeGuide(Texture2D atlas, WorldBarSheetLayout.Sheet sheet)
        {
            // Shelves are the distinct Y offsets the packer used. Ordered upward, the way the
            // texture counts, so shelf 0 is the bottom one.
            var shelfYs = new List<int>();
            foreach (var p in sheet.Pieces)
                if (!shelfYs.Contains(p.Y)) shelfYs.Add(p.Y);
            shelfYs.Sort();

            int w = sheet.Width * GUIDE_SCALE;
            int h = sheet.Height * GUIDE_SCALE + shelfYs.Count * LABEL_BAND;
            var pixels = new Color32[w * h];

            var checkerA = new Color32(48, 48, 54, 255);
            var checkerB = new Color32(38, 38, 44, 255);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = ((x / 16 + y / 16) & 1) == 0 ? checkerA : checkerB;

            var src = atlas.GetPixels32();
            var outline = new Color32(255, 0, 200, 255);

            for (int i = 0; i < sheet.Pieces.Count; i++)
            {
                var p = sheet.Pieces[i];
                int shelf = shelfYs.IndexOf(p.Y);
                int baseX = p.X * GUIDE_SCALE;
                int baseY = p.Y * GUIDE_SCALE + (shelf + 1) * LABEL_BAND;

                for (int ty = 0; ty < p.Height; ty++)
                {
                    for (int tx = 0; tx < p.Width; tx++)
                    {
                        var c = src[(p.Y + ty) * sheet.Width + (p.X + tx)];
                        float a = c.a / 255f;
                        for (int sy = 0; sy < GUIDE_SCALE; sy++)
                        {
                            for (int sx = 0; sx < GUIDE_SCALE; sx++)
                            {
                                int px = baseX + tx * GUIDE_SCALE + sx;
                                int py = baseY + ty * GUIDE_SCALE + sy;
                                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                                var under = pixels[py * w + px];
                                pixels[py * w + px] = new Color32(
                                    (byte)(c.r * a + under.r * (1f - a)),
                                    (byte)(c.g * a + under.g * (1f - a)),
                                    (byte)(c.b * a + under.b * (1f - a)),
                                    255);
                            }
                        }
                    }
                }

                int x0 = baseX - 1;
                int y0 = baseY - 1;
                int x1 = baseX + p.Width * GUIDE_SCALE;
                int y1 = baseY + p.Height * GUIDE_SCALE;
                for (int x = x0; x <= x1; x++) { Plot(pixels, w, h, x, y0, outline); Plot(pixels, w, h, x, y1, outline); }
                for (int y = y0; y <= y1; y++) { Plot(pixels, w, h, x0, y, outline); Plot(pixels, w, h, x1, y, outline); }

                DrawNumber(pixels, w, h, baseX, y0 - 12, i + 1, outline);
            }

            var guide = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                guide.SetPixels32(pixels);
                guide.Apply();
                return guide.EncodeToPNG();
            }
            finally { Object.DestroyImmediate(guide); }
        }

        private static void Plot(Color32[] pixels, int w, int h, int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            pixels[y * w + x] = c;
        }

        // A 3x5 digit font, top row first. Enough to number the boxes and nothing more — a real
        // font asset for ten glyphs drawn once would be a dependency this tool does not need.
        private static readonly string[] Digits =
        {
            "###" + "#.#" + "#.#" + "#.#" + "###", // 0
            ".#." + "##." + ".#." + ".#." + "###", // 1
            "###" + "..#" + "###" + "#.." + "###", // 2
            "###" + "..#" + "###" + "..#" + "###", // 3
            "#.#" + "#.#" + "###" + "..#" + "..#", // 4
            "###" + "#.." + "###" + "..#" + "###", // 5
            "###" + "#.." + "###" + "#.#" + "###", // 6
            "###" + "..#" + "..#" + "..#" + "..#", // 7
            "###" + "#.#" + "###" + "#.#" + "###", // 8
            "###" + "#.#" + "###" + "..#" + "###", // 9
        };

        private static void DrawNumber(Color32[] pixels, int w, int h, int x, int y, int value,
                                       Color32 c)
        {
            string text = value.ToString();
            const int scale = 2;
            int cursor = x;
            foreach (char ch in text)
            {
                int d = ch - '0';
                if (d < 0 || d > 9) continue;
                string glyph = Digits[d];
                for (int gy = 0; gy < 5; gy++)
                {
                    for (int gx = 0; gx < 3; gx++)
                    {
                        if (glyph[gy * 3 + gx] != '#') continue;
                        // Glyph rows are top-down; the texture grows upward.
                        int py = y + (4 - gy) * scale;
                        int px = cursor + gx * scale;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                                Plot(pixels, w, h, px + sx, py + sy, c);
                    }
                }
                cursor += 4 * scale;
            }
        }

        // -- The layout table -------------------------------------------------

        private static string BuildDoc(WorldBarStyle style, WorldBarSheetLayout.Sheet sheet)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# World bar skin — sheet layout");
            sb.AppendLine();
            sb.AppendLine("Generated by `Valkur > UI > Export World Bar Skin Template`. Do not edit by hand:");
            sb.AppendLine("every number below comes from `WorldBarSheetLayout`, which is also what the runtime");
            sb.AppendLine("generator draws into and what the importer slices.");
            sb.AppendLine();
            sb.AppendLine($"Sheet: **{sheet.Width} x {sheet.Height}**, PPU " +
                          $"**{WorldBarGeometry.TEXELS_PER_UNIT}**, Point filter, Uncompressed, " +
                          "sprite mode Multiple.");
            sb.AppendLine();
            sb.AppendLine("One pixel of this PNG is one texel of the world grid, which is one screen pixel at");
            sb.AppendLine("the snapped camera. There is no room for antialiasing anywhere on the sheet.");
            sb.AppendLine();
            sb.AppendLine("| # | id | rect (x, y, w, h) | 9-slice border (L,B,R,T) | what it is |");
            sb.AppendLine("|---|---|---|---|---|");
            for (int i = 0; i < sheet.Pieces.Count; i++)
            {
                var p = sheet.Pieces[i];
                sb.AppendLine($"| {i + 1} | `{p.Id}` | {p.X}, {p.Y}, {p.Width}, {p.Height} | " +
                              $"{p.Border.x:0}, {p.Border.y:0}, {p.Border.z:0}, {p.Border.w:0} | {Describe(p)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Rect coordinates count from the **bottom-left**, the way Unity does. The guide PNG");
            sb.AppendLine($"is the same sheet at {GUIDE_SCALE}x with each box outlined and numbered, so nothing here");
            sb.AppendLine("has to be measured by hand.");
            sb.AppendLine();
            sb.AppendLine("## Painting rules");
            sb.AppendLine();
            sb.AppendLine("- **Greyscale only.** `SpriteRenderer.color` MULTIPLIES, and the colour comes from");
            sb.AppendLine("  `WorldBarStyle.asset`. A red pixel tinted green renders black.");
            sb.AppendLine("- **Leave headroom on the fill.** Keep its body around 217 and the RIGHT-most column");
            sb.AppendLine("  at 255: that column is the leading edge, the part of the bar the eye tracks while it");
            sb.AppendLine("  moves, and painting everything at 255 leaves no way to make it brighter.");
            sb.AppendLine("- **The plate is mostly 255**, with its TOP row near 140 (the recess shadow) and its end");
            sb.AppendLine("  columns near 205. It is tinted with a dark colour, so what you paint light comes out");
            sb.AppendLine("  dark and what you paint dark comes out black.");
            sb.AppendLine("- **Frames: white, opaque, with the four corner texels at alpha 0.** That hole is the");
            sb.AppendLine("  chamfer, and it is the whole difference between a frame and a rectangle.");
            sb.AppendLine("- **Height is not free** for `plate_*` and `fill_*`: they are not sliced vertically, so");
            sb.AppendLine("  they stretch whole and art painted at another height gets resampled. **Width is free**");
            sb.AppendLine("  above the border sum — only the border columns survive, the middle is stretched.");
            sb.AppendLine("- Icons are white on transparent, in `StatusEffectKind` order:");
            var kinds = System.Enum.GetNames(typeof(StatusEffectKind));
            for (int i = 0; i < kinds.Length; i++)
                sb.AppendLine($"  - `{WorldBarSheetLayout.IconId(i)}` = {kinds[i]}");
            sb.AppendLine();
            sb.AppendLine("## After painting");
            sb.AppendLine();
            sb.AppendLine("Run `Valkur > UI > Import World Bar Skin`. Each slot is resolved on its own, so a");
            sb.AppendLine("half-painted sheet is a legitimate state: painted pieces switch over, the rest stay on");
            sb.AppendLine("the generated art. `Valkur > UI > Clear World Bar Skin` puts everything back.");
            sb.AppendLine();
            sb.AppendLine("A piece whose PPU, size or border disagrees with this table is refused at runtime with");
            sb.AppendLine("one warning and falls back to the generated one — which is what catches the sheet being");
            sb.AppendLine("imported under a folder the asset postprocessor treats as screen UI.");
            sb.AppendLine();
            sb.AppendLine($"Current geometry: health row {style.healthRowTexels} texels, resource row " +
                          $"{style.resourceRowTexels}, pip {style.pipTexels}, icon {style.iconTexels}.");
            sb.AppendLine("Changing any of those in the style asset invalidates the art painted for the old ones.");
            return sb.ToString();
        }

        private static string Describe(WorldBarPieceRect p)
        {
            if (p.IconIndex >= 0)
            {
                var names = System.Enum.GetNames(typeof(StatusEffectKind));
                string kind = p.IconIndex < names.Length ? names[p.IconIndex] : "unknown";
                return $"status glyph — {kind}";
            }
            switch (p.Id)
            {
                case WorldBarSheetLayout.FRAME_HEALTH:   return "ring around the health row";
                case WorldBarSheetLayout.FRAME_RESOURCE: return "ring around the resource row";
                case WorldBarSheetLayout.PLATE_HEALTH:   return "recess inside the health frame";
                case WorldBarSheetLayout.PLATE_RESOURCE: return "recess inside the resource frame";
                case WorldBarSheetLayout.FILL_HEALTH:    return "health fill — ramp + leading edge";
                case WorldBarSheetLayout.FILL_RESOURCE:  return "mana fill — ramp + leading edge";
                case WorldBarSheetLayout.SOLID:          return "flat white — quarter marks, overflow pip";
                case WorldBarSheetLayout.PIP_FRAME:      return "dash pip ring";
                case WorldBarSheetLayout.PIP_CORE:       return "dash pip interior";
                default:                                 return "";
            }
        }
    }
}
