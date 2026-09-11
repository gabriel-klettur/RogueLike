using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Core.UI
{
    /// <summary>One piece of the world-bar sheet: where it lives and how it is sliced.</summary>
    public struct WorldBarPieceRect
    {
        /// <summary>Canonical id. The same string names the slot on <c>WorldBarStyle.skin</c>.</summary>
        public string Id;

        /// <summary>Rect in texture space, origin BOTTOM-LEFT the way Unity counts.</summary>
        public int X, Y, Width, Height;

        /// <summary>9-slice border as Unity orders it: left, bottom, right, top.</summary>
        public Vector4 Border;

        /// <summary>The status kind this piece draws, or -1 when it is not an icon.</summary>
        public int IconIndex;

        public RectInt Rect => new RectInt(X, Y, Width, Height);
    }

    /// <summary>
    /// Where every piece of the world-bar art sits on one sheet.
    ///
    /// <para><b>Why this is shared rather than owned by the generator.</b> Four things have to
    /// agree on these rectangles: the procedural atlas <c>WorldBarArt</c> builds at runtime, the
    /// template PNG an artist paints over, the editor importer that slices and assigns the painted
    /// sheet, and the test that refuses a piece of the wrong size. Four copies of a coordinate
    /// table is four chances for the painted art to land one texel off the rect the game reads,
    /// which is invisible in code and shows up as a sliver of the neighbouring piece welded to the
    /// end cap of a bar.</para>
    ///
    /// <para><b>The gutter is not tidiness.</b> A stretched 9-slice samples right up to its rect
    /// edge, so without a transparent column between two rects a wide bar pulls one texel of its
    /// neighbour along with it.</para>
    ///
    /// <para>Sizes come FROM the style's texel numbers, so a sheet is only ever built for the
    /// geometry the game is actually drawing. That is also the contract an artist inherits: once
    /// the art is painted, <c>healthRowTexels</c> and its siblings stop being free dials —
    /// changing one means repainting, and <c>WorldBarArt</c> refuses a piece whose size no longer
    /// matches rather than stretching it.</para>
    /// </summary>
    public static class WorldBarSheetLayout
    {
        /// <summary>Blank texels between two rects.</summary>
        public const int GUTTER = 1;

        /// <summary>Nominal width of a stretched piece. Only its border columns survive slicing.</summary>
        public const int STRETCH_WIDTH = 8;

        /// <summary>Side of the plain white square used for quarter marks and the overflow pip.</summary>
        public const int SOLID_SIZE = 4;

        // Canonical ids. These strings are the join between the sheet, the style's slots and the
        // importer, so they are spelled once.
        public const string FRAME_HEALTH = "frame_health";
        public const string FRAME_RESOURCE = "frame_resource";
        public const string PLATE_HEALTH = "plate_health";
        public const string PLATE_RESOURCE = "plate_resource";
        public const string FILL_HEALTH = "fill_health";
        public const string FILL_RESOURCE = "fill_resource";
        public const string SOLID = "solid";
        public const string PIP_FRAME = "pip_frame";
        public const string PIP_CORE = "pip_core";
        public const string CAPS_HEALTH = "caps_health";
        public const string CAPS_RESOURCE = "caps_resource";

        /// <summary>Id of the glyph for status kind <paramref name="index"/>.</summary>
        public static string IconId(int index) => "icon_" + index;

        /// <summary>A whole sheet: its size and every piece on it.</summary>
        public struct Sheet
        {
            public int Width;
            public int Height;
            public List<WorldBarPieceRect> Pieces;

            /// <summary>The piece with this id, or false when the sheet has none.</summary>
            public bool TryFind(string id, out WorldBarPieceRect piece)
            {
                for (int i = 0; i < Pieces.Count; i++)
                    if (Pieces[i].Id == id) { piece = Pieces[i]; return true; }
                piece = default;
                return false;
            }
        }

        /// <summary>
        /// Build the sheet for a given geometry.
        ///
        /// <para>A row's frame is the full row height and is sliced vertically (1 texel top and
        /// bottom); its plate and fill are the INTERIOR only and are not sliced vertically at all,
        /// which is why their height is fixed at <c>row - 2</c>: they are stretched whole, so art
        /// painted at any other height is resampled and loses exactly the crispness this grid
        /// exists for.</para>
        /// </summary>
        public static Sheet Build(int healthRowTexels, int resourceRowTexels, int pipTexels,
                                  int iconTexels, int iconCount)
        {
            int healthRow = Mathf.Max(3, healthRowTexels);
            int resourceRow = Mathf.Max(3, resourceRowTexels);
            int pip = Mathf.Max(3, pipTexels);
            int icon = Mathf.Max(3, iconTexels);
            iconCount = Mathf.Max(0, iconCount);

            var pieces = new List<WorldBarPieceRect>(11 + iconCount);
            // Border widths are how much of a piece's END CAP survives stretching, so they are a
            // property of the ART, not a round number. Measured on the hand-drawn sheet at a
            // six-texel row height: the frame's ornate cap is three texels wide and the plate and
            // fill caps two. At the previous 2/1 the frame lost a third of its cap and the fill
            // lost its bevelled end entirely, which is invisible in the sheet and obvious on a bar.
            var stretchBorder = new Vector4(3, 1, 3, 1);   // frames: ends and rims survive
            var flatBorder = new Vector4(2, 0, 2, 0);      // plate and fill: ends only
            var capBorder = new Vector4(1, 0, 1, 0);       // end caps: one metal column each end

            int x = 0, shelfY = 0, shelfH = 0, width = 0;

            void NewShelf()
            {
                width = Mathf.Max(width, x - GUTTER);
                x = 0;
                shelfY += shelfH + GUTTER;
                shelfH = 0;
            }

            void Add(string id, int w, int h, Vector4 border, int iconIndex = -1)
            {
                pieces.Add(new WorldBarPieceRect
                {
                    Id = id, X = x, Y = shelfY, Width = w, Height = h,
                    Border = border, IconIndex = iconIndex,
                });
                x += w + GUTTER;
                if (h > shelfH) shelfH = h;
            }

            // Shelf 1 - everything a bar row is made of, plus the plain square.
            Add(FRAME_HEALTH, STRETCH_WIDTH, healthRow, stretchBorder);
            Add(FRAME_RESOURCE, STRETCH_WIDTH, resourceRow, stretchBorder);
            Add(PLATE_HEALTH, STRETCH_WIDTH, healthRow - 2, flatBorder);
            Add(PLATE_RESOURCE, STRETCH_WIDTH, resourceRow - 2, flatBorder);
            Add(FILL_HEALTH, STRETCH_WIDTH, healthRow - 2, flatBorder);
            Add(FILL_RESOURCE, STRETCH_WIDTH, resourceRow - 2, flatBorder);
            Add(SOLID, SOLID_SIZE, SOLID_SIZE, Vector4.zero);
            // The metal end caps sit inside the outline, one column at each end of the interior.
            // Their own piece rather than part of the frame because they are tinted by RANK while
            // the outline is not, and a multiply can only give one sprite one colour.
            Add(CAPS_HEALTH, STRETCH_WIDTH, healthRow - 2, capBorder);
            Add(CAPS_RESOURCE, STRETCH_WIDTH, resourceRow - 2, capBorder);

            // Shelf 2 - the dash pip.
            NewShelf();
            Add(PIP_FRAME, pip, pip, Vector4.zero);
            Add(PIP_CORE, Mathf.Max(1, pip - 2), Mathf.Max(1, pip - 2), Vector4.zero);

            // Shelf 3 - the status glyphs, in StatusEffectKind order.
            if (iconCount > 0)
            {
                NewShelf();
                for (int i = 0; i < iconCount; i++)
                    Add(IconId(i), icon, icon, Vector4.zero, i);
            }

            width = Mathf.Max(width, x - GUTTER);
            int height = shelfY + shelfH;

            return new Sheet
            {
                Width = NextPowerOfTwo(Mathf.Max(32, width)),
                Height = NextPowerOfTwo(Mathf.Max(32, height)),
                Pieces = pieces,
            };
        }

        /// <summary>
        /// Power-of-two sheet dimensions. Not required by anything at this size, and done anyway
        /// because a hand-painted sheet ends up in a SpriteAtlas eventually and a POT source is
        /// the one shape every packer and every platform agrees about.
        /// </summary>
        private static int NextPowerOfTwo(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }
    }
}
