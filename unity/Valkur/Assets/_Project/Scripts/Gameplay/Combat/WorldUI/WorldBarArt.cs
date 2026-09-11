using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Every sprite a world bar is drawn from — hand-painted where an artist has painted one,
    /// generated where nobody has yet.
    ///
    /// <para><b>Why one texture.</b> Frame, plate, fill, quarter mark, dash pip and the eight
    /// status glyphs share a single atlas and a single material, so the whole readout over every
    /// creature on screen stays batchable. Giving each piece its own <c>Texture2D</c> — the
    /// obvious way to write this — would break the batch once per piece.</para>
    ///
    /// <para><b>Why the generated half exists at all.</b> Every piece is one to twelve texels of
    /// flat colour, ramp or ring, and its size comes FROM <see cref="WorldBarStyle"/>: changing a
    /// row's height regenerates art that fits it exactly instead of resampling art that does not.
    /// That is the whole reason the bars this replaced were blurry — one 4x4 white square
    /// stretched to 64 x 8 screen pixels. It is also what makes a hand-painted sheet optional: the
    /// game looks finished before the art exists, and each painted piece replaces its generated
    /// twin the moment it is assigned.</para>
    ///
    /// <para><b>A painted piece is CHECKED, not trusted, and both failures it checks for have
    /// already happened here.</b> <c>ValkurAssetPostprocessor</c> forces PPU 100 on anything under
    /// <c>Art/UI/</c>, so a sheet in the wrong folder comes back a sixth of its intended size. And
    /// one level further down, <c>ui.spriteatlas</c> packs that whole folder with
    /// <c>filterMode: 1</c> — an atlas OVERRIDES the filter its members were imported with — so
    /// the first sheet exported there rendered soft while its PPU, rect, border and pivot were all
    /// correct. A piece that disagrees on any of the four is refused, warned about ONCE, and falls
    /// back to the generated one: visibly unchanged art plus a console line beats a readout that
    /// is quietly wrong.</para>
    ///
    /// <para>Sprites are <b>PPU 16</b>, so one texel is exactly one texel of the world grid, and
    /// <b>FullRect</b> — <c>Sprite.Create</c> defaults to <c>Tight</c>, which traces the alpha
    /// outline to build a fitted mesh. Free at this size, and stated anyway because the project
    /// has already paid 6.9 s of a boot for that default.</para>
    /// </summary>
    public static class WorldBarArt
    {
        [SelfHealingStatic("Unity destroys the generated texture, sprites and material on play-mode " +
                           "exit, so after a domain reload these hold Unity-null and the lazy build " +
                           "below runs again. Nulling them in a reset hook would leak the previous " +
                           "session's texture instead of letting Unity collect it.")]
        private static Texture2D s_texture;

        [SelfHealingStatic("Rebuilt with s_texture. See its note.")]
        private static Material s_material;

        /// <summary>What each layout id resolves to: a painted sprite, or a generated one.</summary>
        private static readonly Dictionary<string, Sprite> s_pieces = new Dictionary<string, Sprite>();

        /// <summary>Ids already complained about, so a bad piece costs one line and not one a frame.</summary>
        private static readonly HashSet<string> s_warned = new HashSet<string>();

        // The style values the current build was made for. Any change rebuilds rather than
        // stretching art authored for the old numbers.
        private static int s_builtHealthRow;
        private static int s_builtResourceRow;
        private static int s_builtIcon;
        private static int s_builtPip;
        private static int s_builtSkinStamp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorldBarArtStatics()
        {
            s_pieces.Clear();
            s_warned.Clear();
            s_builtHealthRow = 0;
            s_builtResourceRow = 0;
            s_builtIcon = 0;
            s_builtPip = 0;
            s_builtSkinStamp = 0;
        }

        // -- Public surface ---------------------------------------------------

        /// <summary>The shared unlit material every bar renderer points at.</summary>
        public static Material Material
        {
            get { EnsureBuilt(); return s_material; }
        }

        /// <summary>A plain white square. Sliced with no border, so it stretches exactly.</summary>
        public static Sprite Solid => Piece(WorldBarSheetLayout.SOLID);

        /// <summary>The chamfered ring drawn around a row.</summary>
        public static Sprite Frame(WorldBarRow row)
            => Piece(row == WorldBarRow.Health
                ? WorldBarSheetLayout.FRAME_HEALTH
                : WorldBarSheetLayout.FRAME_RESOURCE);

        /// <summary>The recess a fill sits in.</summary>
        public static Sprite Plate(WorldBarRow row)
            => Piece(row == WorldBarRow.Health
                ? WorldBarSheetLayout.PLATE_HEALTH
                : WorldBarSheetLayout.PLATE_RESOURCE);

        /// <summary>The fill itself: a vertical ramp with a brightened leading edge.</summary>
        public static Sprite Fill(WorldBarRow row)
            => Piece(row == WorldBarRow.Health
                ? WorldBarSheetLayout.FILL_HEALTH
                : WorldBarSheetLayout.FILL_RESOURCE);

        /// <summary>The metal end caps inside a row's outline, tinted by rank.</summary>
        public static Sprite Caps(WorldBarRow row)
            => Piece(row == WorldBarRow.Health
                ? WorldBarSheetLayout.CAPS_HEALTH
                : WorldBarSheetLayout.CAPS_RESOURCE);

        /// <summary>The dash pip's ring.</summary>
        public static Sprite PipFrame => Piece(WorldBarSheetLayout.PIP_FRAME);

        /// <summary>The dash pip's interior, filled bottom-up while it recharges.</summary>
        public static Sprite PipCore => Piece(WorldBarSheetLayout.PIP_CORE);

        /// <summary>
        /// The glyph for a status kind, or null when that kind has no drawing. Null rather than
        /// a placeholder: an unrecognised status must cost an absent icon, never a wrong one.
        /// </summary>
        public static Sprite Icon(StatusEffectKind kind)
            => Piece(WorldBarSheetLayout.IconId((int)kind));

        /// <summary>
        /// True when this piece is coming from a painted sheet rather than the generator. For the
        /// importer's report, for the tests, and for <see cref="TintFor"/>.
        /// </summary>
        public static bool IsPainted(string layoutId)
        {
            EnsureBuilt();
            var style = WorldBarStyle.Active;
            var authored = style.skin?.Find(layoutId);
            return authored != null && s_pieces.TryGetValue(layoutId, out var used) && used == authored;
        }

        /// <summary>
        /// The colour a piece must actually be drawn with, given what the style asked for.
        ///
        /// <para><b><c>SpriteRenderer.color</c> MULTIPLIES, so a style tint is a statement about
        /// GREYSCALE art and is destructive over painted art.</b> The generated atlas is drawn in
        /// white and grey precisely so the palette can colour it; a hand-painted sheet already
        /// carries its own colour and has nothing left to be tinted with. The shipped frame tint
        /// is <c>(0.02, 0.02, 0.03)</c> — a near-black meant to darken a white ring into a rim —
        /// and multiplying the painted colour frame by it rendered the whole readout as a BLACK
        /// SLAB, which is exactly what the first live capture showed.</para>
        ///
        /// <para>The ALPHA is kept from the style in both cases: that is the rig's fade and the
        /// row's flash, neither of which is a colour decision.</para>
        /// </summary>
        public static Color TintFor(string layoutId, Color styleColour)
            => IsPainted(layoutId)
                ? new Color(1f, 1f, 1f, styleColour.a)
                : styleColour;

        /// <summary>
        /// The GENERATED atlas, readable. Exposed for one caller: the editor tool that writes the
        /// template an artist paints over, which must show exactly the pieces the game would draw
        /// at exactly the rects it reads them from. Painted pieces do not live here — this is the
        /// half that gets replaced.
        /// </summary>
        public static Texture2D GeneratedAtlas
        {
            get { EnsureBuilt(); return s_texture; }
        }

        /// <summary>Force the next access to re-resolve. Called by the skin importer.</summary>
        public static void Invalidate()
        {
            s_builtHealthRow = 0;
            s_builtResourceRow = 0;
            s_builtIcon = 0;
            s_builtPip = 0;
            s_builtSkinStamp = 0;
            s_warned.Clear();
        }

        private static Sprite Piece(string id)
        {
            EnsureBuilt();
            return s_pieces.TryGetValue(id, out var s) ? s : null;
        }

        // -- Build ------------------------------------------------------------

        private static void EnsureBuilt()
        {
            var style = WorldBarStyle.Active;
            int healthRow = Mathf.Max(3, style.healthRowTexels);
            int resourceRow = Mathf.Max(3, style.resourceRowTexels);
            int icon = Mathf.Max(4, style.iconTexels);
            int pip = Mathf.Max(3, style.pipTexels);
            int skinStamp = StampOf(style.skin);

            if (s_texture != null && s_material != null &&
                s_builtHealthRow == healthRow && s_builtResourceRow == resourceRow &&
                s_builtIcon == icon && s_builtPip == pip && s_builtSkinStamp == skinStamp)
                return;

            Build(style, healthRow, resourceRow, icon, pip);
            s_builtSkinStamp = skinStamp;
        }

        /// <summary>
        /// A cheap fingerprint of which slots are filled and with what. Instance ids rather than a
        /// dirty flag, because the skin is edited in the Inspector as often as by the importer and
        /// only one of those two can be made to call <see cref="Invalidate"/>.
        /// </summary>
        private static int StampOf(WorldBarSkin skin)
        {
            if (skin == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + IdOf(skin.frameHealth);
                hash = hash * 31 + IdOf(skin.frameResource);
                hash = hash * 31 + IdOf(skin.plateHealth);
                hash = hash * 31 + IdOf(skin.plateResource);
                hash = hash * 31 + IdOf(skin.fillHealth);
                hash = hash * 31 + IdOf(skin.fillResource);
                hash = hash * 31 + IdOf(skin.solid);
                hash = hash * 31 + IdOf(skin.pipFrame);
                hash = hash * 31 + IdOf(skin.pipCore);
                hash = hash * 31 + IdOf(skin.capsHealth);
                hash = hash * 31 + IdOf(skin.capsResource);
                if (skin.statusIcons != null)
                    for (int i = 0; i < skin.statusIcons.Length; i++)
                        hash = hash * 31 + IdOf(skin.statusIcons[i]);
                return hash;
            }
        }

        private static int IdOf(Object o) => o == null ? 0 : o.GetInstanceID();

        private static void Build(WorldBarStyle style, int healthRow, int resourceRow,
                                  int icon, int pip)
        {
            s_pieces.Clear();

            // The sheet carries one cell per STATUS KIND, not per kind that happens to have a
            // generated glyph. The importer builds its rects from the same enum, and if these two
            // counts ever disagreed every icon rect would shift and the painted art would land one
            // cell over — invisible in code, obvious and baffling on screen. It also means an
            // artist can paint a glyph for a kind the generator has none for.
            int iconCount = System.Enum.GetValues(typeof(StatusEffectKind)).Length;
            var sheet = WorldBarSheetLayout.Build(healthRow, resourceRow, pip, icon, iconCount);

            var tex = new Texture2D(sheet.Width, sheet.Height, TextureFormat.RGBA32, false)
            {
                name = "WorldBarAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[sheet.Width * sheet.Height];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            for (int i = 0; i < sheet.Pieces.Count; i++)
                DrawPiece(pixels, sheet.Width, sheet.Height, sheet.Pieces[i]);

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            s_texture = tex;

            if (s_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                             ?? Shader.Find("Sprites/Default");
                s_material = new Material(shader)
                {
                    name = "WorldBarShared",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            var skin = style.skin;
            for (int i = 0; i < sheet.Pieces.Count; i++)
            {
                var piece = sheet.Pieces[i];
                var authored = skin?.Find(piece.Id);
                if (Accepts(authored, piece))
                {
                    s_pieces[piece.Id] = authored;
                    continue;
                }
                // A kind with no glyph and no painted art registers NOTHING, so Icon() answers
                // null and the row leaves the slot empty. Registering a blank sprite instead would
                // draw an invisible icon that still takes up a place in the row.
                if (piece.IconIndex >= 0 && StatusGlyphs.Get(piece.IconIndex) == null) continue;
                s_pieces[piece.Id] = MakeSprite(tex, piece);
            }

            s_builtHealthRow = healthRow;
            s_builtResourceRow = resourceRow;
            s_builtIcon = icon;
            s_builtPip = pip;
        }

        /// <summary>
        /// Whether a painted sprite may stand in for a piece. Everything here has been a real
        /// silent failure somewhere in this project: a wrong PPU (the <c>Art/UI/</c> import rule),
        /// a wrong size (a row height changed after the art was painted), a missing 9-slice border
        /// (the Sprite Editor's borders are not set by the importer settings and are easy to
        /// forget, and a frame with no border stretches its own chamfer into a wedge).
        /// </summary>
        private static bool Accepts(Sprite sprite, WorldBarPieceRect piece)
        {
            if (sprite == null) return false;

            string complaint = null;
            if (sprite.texture == null)
                complaint = "it has no texture";
            else if (!Mathf.Approximately(sprite.pixelsPerUnit, WorldBarGeometry.TEXELS_PER_UNIT))
                complaint = $"its PPU is {sprite.pixelsPerUnit:0.##} and must be " +
                            $"{WorldBarGeometry.TEXELS_PER_UNIT}";
            else if (Mathf.RoundToInt(sprite.rect.width) != piece.Width ||
                     Mathf.RoundToInt(sprite.rect.height) != piece.Height)
                complaint = $"it measures {sprite.rect.width:0}x{sprite.rect.height:0} and must be " +
                            $"{piece.Width}x{piece.Height}";
            else if (sprite.border != piece.Border)
                complaint = $"its 9-slice border is {sprite.border} and must be {piece.Border}";
            else if (sprite.texture.filterMode != FilterMode.Point)
                complaint = $"its texture filters {sprite.texture.filterMode} and must be Point";
            else if (sprite.packed == false && sprite.triangles.Length > 6)
                complaint = $"its mesh has {sprite.triangles.Length / 3} triangles, so it was " +
                            "imported Tight rather than Full Rect - SpriteDrawMode.Sliced cannot " +
                            "use a tight mesh";

            if (complaint == null) return true;

            if (s_warned.Add(piece.Id))
                Debug.LogWarning(
                    $"[WorldBarArt] Ignoring the painted '{piece.Id}' because {complaint}. " +
                    "Falling back to the generated piece. Re-run Valkur > UI > Import World Bar " +
                    "Skin, and check the sheet is in Art/WorldBars/ — under Art/UI/ the asset " +
                    "postprocessor forces PPU 100 and ui.spriteatlas re-filters it to bilinear.");
            return false;
        }

        // -- Generated drawing ------------------------------------------------

        private static void DrawPiece(Color32[] pixels, int w, int h, WorldBarPieceRect piece)
        {
            switch (piece.Id)
            {
                case WorldBarSheetLayout.FRAME_HEALTH:
                case WorldBarSheetLayout.FRAME_RESOURCE:
                case WorldBarSheetLayout.PIP_FRAME:
                    DrawRing(pixels, w, h, piece.Rect);
                    return;

                case WorldBarSheetLayout.PLATE_HEALTH:
                case WorldBarSheetLayout.PLATE_RESOURCE:
                    DrawPlate(pixels, w, h, piece.Rect);
                    return;

                case WorldBarSheetLayout.FILL_HEALTH:
                case WorldBarSheetLayout.FILL_RESOURCE:
                    DrawFill(pixels, w, h, piece.Rect);
                    return;

                case WorldBarSheetLayout.SOLID:
                case WorldBarSheetLayout.PIP_CORE:
                    FillRect(pixels, w, h, piece.Rect, Color.white);
                    return;

                case WorldBarSheetLayout.CAPS_HEALTH:
                case WorldBarSheetLayout.CAPS_RESOURCE:
                    DrawCaps(pixels, w, h, piece.Rect);
                    return;
            }

            if (piece.IconIndex >= 0)
            {
                var glyph = StatusGlyphs.Get(piece.IconIndex);
                if (glyph != null) BlitGlyph(pixels, w, h, piece.Rect, glyph);
            }
        }

        /// <summary>
        /// A one-texel ring with its four corner texels cut away. The chamfer is the entire
        /// difference between "a rectangle" and "a frame": at this size a rounded corner is one
        /// missing pixel, and its absence is what made the old bars read as untextured quads.
        /// </summary>
        private static void DrawRing(Color32[] pixels, int w, int h, RectInt r)
        {
            for (int y = 0; y < r.height; y++)
            {
                for (int x = 0; x < r.width; x++)
                {
                    bool edge = x == 0 || y == 0 || x == r.width - 1 || y == r.height - 1;
                    if (!edge) continue;
                    if ((x == 0 || x == r.width - 1) && (y == 0 || y == r.height - 1)) continue;
                    Set(pixels, w, h, r.x + x, r.y + y, Color.white);
                }
            }
        }

        /// <summary>
        /// The recess. Its TOP row is darker, because in a top-lit scene a sunken surface catches
        /// its own shadow there — one texel of gradient is what stops the plate reading as a hole
        /// punched in the screen. Otherwise flat: the plate is tinted translucent navy and its job
        /// is contrast, which a busy texture would only spend.
        /// </summary>
        private static void DrawPlate(Color32[] pixels, int w, int h, RectInt r)
        {
            for (int y = 0; y < r.height; y++)
            {
                // y grows upward in texture space, so the TOP row is the last one.
                bool top = y == r.height - 1 && r.height > 1;
                float v = top ? 0.6f : 1f;
                for (int x = 0; x < r.width; x++)
                    Set(pixels, w, h, r.x + x, r.y + y, new Color(v, v, v, 1f));
            }
        }

        /// <summary>
        /// The fill body: flat white. Its shading is NOT baked here any more — a greyscale ramp
        /// multiplied by one colour keeps every tone on the same hue line, which is what made the
        /// bar read as programmer-drawn. The highlight row, shadow row and leading edge are
        /// separate renderers tinted with <c>WorldBarPalette.Ramp</c>'s hue-shifted tones.
        /// </summary>
        private static void DrawFill(Color32[] pixels, int w, int h, RectInt r)
        {
            FillRect(pixels, w, h, r, Color.white);
        }

        /// <summary>
        /// The two metal end caps: one column at each end, lit from above — bright at the top,
        /// mid in the body, dark at the foot — so a multiply by the rank's metal colour gives a
        /// rounded post rather than a flat stripe. Transparent in between, so the fill shows.
        /// </summary>
        private static void DrawCaps(Color32[] pixels, int w, int h, RectInt r)
        {
            for (int y = 0; y < r.height; y++)
            {
                float v = r.height <= 1 ? 1f
                        : y == r.height - 1 ? 1f
                        : y == 0 ? 0.55f
                        : 0.8f;
                var c = new Color(v, v, v, 1f);
                Set(pixels, w, h, r.x, r.y + y, c);
                Set(pixels, w, h, r.x + r.width - 1, r.y + y, c);
            }
        }

        private static void FillRect(Color32[] pixels, int w, int h, RectInt r, Color c)
        {
            for (int y = 0; y < r.height; y++)
                for (int x = 0; x < r.width; x++)
                    Set(pixels, w, h, r.x + x, r.y + y, c);
        }

        /// <summary>
        /// Blit a 6x6 authored glyph into whatever square the style asked for, taking the MAXIMUM
        /// over the source texels that map to each destination texel.
        ///
        /// <para>Not point sampling, and not a filtered resample. A filter turns a six-pixel
        /// silhouette into a smudge; point sampling DROPS whole strokes when shrinking — at 6 into
        /// 5 it discards one source row and one column outright, which is enough to take an arm
        /// off the snowflake or close the gap that makes the poison drop a drop. Max-pooling keeps
        /// every stroke and costs the glyph a little weight instead.</para>
        /// </summary>
        private static void BlitGlyph(Color32[] pixels, int w, int h, RectInt r, string[] glyph)
        {
            int size = r.width;
            int srcH = glyph.Length;
            int srcW = glyph[0].Length;
            for (int y = 0; y < size; y++)
            {
                // Glyph rows are authored top-down; texture rows grow upward.
                int flipped = size - 1 - y;
                int y0 = (flipped * srcH) / size;
                int y1 = Mathf.Max(y0 + 1, ((flipped + 1) * srcH) / size);
                for (int x = 0; x < size; x++)
                {
                    int x0 = (x * srcW) / size;
                    int x1 = Mathf.Max(x0 + 1, ((x + 1) * srcW) / size);
                    float best = 0f;
                    for (int sy = y0; sy < y1 && sy < srcH; sy++)
                    {
                        string row = glyph[sy];
                        for (int sx = x0; sx < x1 && sx < row.Length; sx++)
                        {
                            char ch = row[sx];
                            float v = ch == '#' ? 1f : (ch == ' ' ? 0f : 0.55f);
                            if (v > best) best = v;
                        }
                    }
                    if (best <= 0f) continue;
                    Set(pixels, w, h, r.x + x, r.y + y, new Color(best, best, best, 1f));
                }
            }
        }

        private static void Set(Color32[] pixels, int w, int h, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            pixels[y * w + x] = c;
        }

        private static Sprite MakeSprite(Texture2D tex, WorldBarPieceRect piece)
        {
            var sprite = Sprite.Create(
                tex,
                new Rect(piece.X, piece.Y, piece.Width, piece.Height),
                new Vector2(0.5f, 0.5f),
                WorldBarGeometry.TEXELS_PER_UNIT,
                0,
                SpriteMeshType.FullRect,
                piece.Border);
            sprite.name = piece.Id;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
