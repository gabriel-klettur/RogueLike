using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Fits a chain of capsules to one frame's silhouette. Pure: pixels in, shapes out, so the
    /// offline baker, the Entities editor's "re-measure this frame" and the tests all run the
    /// same arithmetic.
    ///
    /// <para><b>A CHAIN along the body's long axis, not one box and not a polygon.</b> A
    /// humanoid is taller than wide, so it is cut into legs / torso / head bands; a dragon is
    /// longer than tall, so it is cut into tail / body / neck / head columns. Each band then
    /// takes the TRIMMED extent of its own ink (the 12th to 88th percentile across the band),
    /// which is what lets a raised sword, a trailing cape or a whip of tail leave the hurtbox
    /// instead of dragging a whole band out to the tip of it.</para>
    ///
    /// <para><b>Detached ink is ignored.</b> Several sheets draw a loosed arrow, a rune or a
    /// spell flash as its own object; the flood fill keeps the body and anything large enough
    /// to be part of it, so a spell effect never becomes something a fireball can hit.</para>
    /// </summary>
    public static class HurtShapeFitter
    {
        /// <summary>Alpha above which a pixel is body. Higher than the muzzle's 32 so soft glows stay out.</summary>
        public const byte OPAQUE = 48;

        /// <summary>Cells along the long side of the working grid. Fine enough for a head, cheap enough for thousands of frames.</summary>
        private const int GRID_LONG_SIDE = 96;

        /// <summary>A cell is body when this share of its pixels is.</summary>
        private const float CELL_FILL = 0.35f;

        /// <summary>
        /// The dials of the fit. A struct with measured defaults rather than constants, so a
        /// probe can compare settings on the shipped art without a recompile per try — which is
        /// how these numbers were chosen.
        /// </summary>
        public struct Settings
        {
            /// <summary>A component smaller than this share of the largest one is detached FX, not body.</summary>
            public float KeepComponent;
            /// <summary>Percentiles of a band's ink kept across the band.</summary>
            public float TrimLow, TrimHigh;
            /// <summary>Each band reaches this share of its own length into its neighbours, so the chain has no gaps.</summary>
            public float BandOverlap;
            /// <summary>Two neighbouring bands this alike across the axis are one body part and merge.</summary>
            public float MergeSimilarity;
            /// <summary>Bands per unit of length/width on an upright body (clamped 2..3).</summary>
            public float BandsPerAspectUpright;
            /// <summary>Bands per unit of length/height on a long body (clamped 3..5).</summary>
            public float BandsPerAspectLong;

            public static Settings Default => new Settings
            {
                KeepComponent = 0.12f,
                TrimLow = 0.12f,
                TrimHigh = 0.88f,
                BandOverlap = 0.2f,
                MergeSimilarity = 0.86f,
                BandsPerAspectUpright = 1.7f,
                BandsPerAspectLong = 1.8f,
            };
        }

        /// <summary>Result of measuring one frame.</summary>
        public struct Fit
        {
            public List<HurtShape> Shapes;
            /// <summary>Width of the ink at the feet, as a fraction of the frame width.</summary>
            public float FootWidthFraction;
            /// <summary>Height of the ink, as a fraction of the frame height.</summary>
            public float InkHeightFraction;
            /// <summary>Width of the ink, as a fraction of the frame width.</summary>
            public float InkWidthFraction;
        }

        /// <summary>
        /// Measure a frame. <paramref name="alpha"/> is row-major and BOTTOM-UP (index 0 is the
        /// bottom-left pixel, which is what <c>Texture2D.GetPixels32</c> returns), because the
        /// sprites import with pivot (0.5, 0) and the bottom row is the ground line.
        /// </summary>
        public static bool TryFit(byte[] alpha, int width, int height, bool facesEast, out Fit fit)
            => TryFit(alpha, width, height, facesEast, Settings.Default, out fit);

        public static bool TryFit(byte[] alpha, int width, int height, bool facesEast, Settings settings, out Fit fit)
        {
            fit = default;
            if (alpha == null || width < 4 || height < 4 || alpha.Length < width * height) return false;

            int cell = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(width, height) / (float)GRID_LONG_SIDE));
            int gw = (width + cell - 1) / cell;
            int gh = (height + cell - 1) / cell;
            var grid = BuildGrid(alpha, width, height, cell, gw, gh);
            if (!KeepBody(grid, gw, gh, settings.KeepComponent)) return false;

            int minX = gw, maxX = -1, minY = gh, maxY = -1, total = 0;
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    if (!grid[y * gw + x]) continue;
                    total++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (total < 4) return false;

            int inkW = maxX - minX + 1, inkH = maxY - minY + 1;

            // The long axis is decided on the TRIMMED width: a spear held out sideways makes a
            // humanoid's box wider than it is tall, and cutting a person into columns puts one
            // capsule on the spear and splits the body down the middle.
            float trimmedW = TrimmedExtent(grid, gw, minX, maxX, minY, maxY, vertical: true, settings);
            float trimmedH = TrimmedExtent(grid, gw, minX, maxX, minY, maxY, vertical: false, settings);
            bool vertical = trimmedH >= trimmedW * 0.9f;

            var bands = vertical
                ? FitBands(grid, gw, minY, maxY, minX, maxX, total, vertical: true, settings,
                           count: Mathf.Clamp(Mathf.RoundToInt(trimmedH / Mathf.Max(1f, trimmedW) * settings.BandsPerAspectUpright), 2, 3))
                : FitBands(grid, gw, minX, maxX, minY, maxY, total, vertical: false, settings,
                           count: Mathf.Clamp(Mathf.RoundToInt(trimmedW / Mathf.Max(1f, trimmedH) * settings.BandsPerAspectLong), 3,
                                              EntityCollisionProfile.MaxShapesPerFrame));
            if (bands.Count == 0) return false;
            Merge(bands, vertical, settings.MergeSimilarity);

            float halfW = width * 0.5f, halfH = height * 0.5f;
            var shapes = new List<HurtShape>(bands.Count);
            for (int i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                // Grid cells back to pixels; the band's max is inclusive, so +1 cell.
                float x0 = b.X0 * cell, x1 = Mathf.Min(width, (b.X1 + 1) * cell);
                float y0 = b.Y0 * cell, y1 = Mathf.Min(height, (b.Y1 + 1) * cell);
                float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
                float texX = (cx - halfW) / halfW;
                shapes.Add(new HurtShape(
                    new Vector2(facesEast ? texX : -texX, (cy - halfH) / halfH),
                    new Vector2((x1 - x0) / width, (y1 - y0) / height)));
            }

            fit = new Fit
            {
                Shapes = shapes,
                FootWidthFraction = MeasureFeet(grid, gw, minX, maxX, minY, maxY) * cell / (float)width,
                InkHeightFraction = Mathf.Min(1f, inkH * cell / (float)height),
                InkWidthFraction = Mathf.Min(1f, inkW * cell / (float)width),
            };
            return true;
        }

        private struct Band
        {
            public int X0, X1, Y0, Y1;
        }

        private static bool[] BuildGrid(byte[] alpha, int width, int height, int cell, int gw, int gh)
        {
            var grid = new bool[gw * gh];
            for (int gy = 0; gy < gh; gy++)
            {
                int py0 = gy * cell, py1 = Mathf.Min(height, py0 + cell);
                for (int gx = 0; gx < gw; gx++)
                {
                    int px0 = gx * cell, px1 = Mathf.Min(width, px0 + cell);
                    int solid = 0, area = (py1 - py0) * (px1 - px0);
                    for (int y = py0; y < py1; y++)
                    {
                        int row = y * width;
                        for (int x = px0; x < px1; x++)
                            if (alpha[row + x] > OPAQUE) solid++;
                    }
                    grid[gy * gw + gx] = area > 0 && solid >= area * CELL_FILL;
                }
            }
            return grid;
        }

        /// <summary>Clears every component too small to be body. False when nothing is left.</summary>
        private static bool KeepBody(bool[] grid, int gw, int gh, float keepShare)
        {
            var label = new int[grid.Length];
            var sizes = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (!grid[i] || label[i] != 0) continue;
                int id = sizes.Count, size = 0;
                label[i] = id;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    size++;
                    int cx = c % gw, cy = c / gw;
                    TryPush(grid, label, stack, gw, gh, cx - 1, cy, id);
                    TryPush(grid, label, stack, gw, gh, cx + 1, cy, id);
                    TryPush(grid, label, stack, gw, gh, cx, cy - 1, id);
                    TryPush(grid, label, stack, gw, gh, cx, cy + 1, id);
                }
                sizes.Add(size);
            }

            int largest = 0;
            for (int i = 1; i < sizes.Count; i++) if (sizes[i] > largest) largest = sizes[i];
            if (largest == 0) return false;

            int keep = Mathf.Max(2, Mathf.CeilToInt(largest * keepShare));
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] && sizes[label[i]] < keep) grid[i] = false;
            return true;
        }

        private static void TryPush(bool[] grid, int[] label, Stack<int> stack, int gw, int gh, int x, int y, int id)
        {
            if (x < 0 || y < 0 || x >= gw || y >= gh) return;
            int i = y * gw + x;
            if (!grid[i] || label[i] != 0) return;
            label[i] = id;
            stack.Push(i);
        }

        /// <summary>
        /// The 10th-90th percentile extent of the ink across one axis, in cells. Vertical
        /// measures x (width), otherwise y (height).
        /// </summary>
        private static float TrimmedExtent(bool[] grid, int gw, int minX, int maxX, int minY, int maxY, bool vertical, Settings settings)
        {
            int lo = vertical ? minX : minY, hi = vertical ? maxX : maxY;
            var histogram = new int[hi - lo + 1];
            int total = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!grid[y * gw + x]) continue;
                    histogram[(vertical ? x : y) - lo]++;
                    total++;
                }
            Percentiles(histogram, total, out int p0, out int p1, settings.TrimLow, settings.TrimHigh);
            return p1 - p0 + 1;
        }

        /// <summary>
        /// Cuts the ink into <paramref name="count"/> equal bands along the long axis and fits
        /// each band's trimmed extent. <paramref name="a0"/>/<paramref name="a1"/> are the long
        /// axis bounds, <paramref name="b0"/>/<paramref name="b1"/> the cross axis bounds.
        /// </summary>
        private static List<Band> FitBands(bool[] grid, int gw, int a0, int a1, int b0, int b1,
                                           int total, bool vertical, Settings settings, int count)
        {
            var bands = new List<Band>(count);
            int length = a1 - a0 + 1;
            float bandLength = length / (float)count;
            int minCells = Mathf.Max(2, Mathf.CeilToInt(total * 0.03f));

            for (int i = 0; i < count; i++)
            {
                int s = a0 + Mathf.FloorToInt(i * bandLength);
                int e = i == count - 1 ? a1 : a0 + Mathf.FloorToInt((i + 1) * bandLength) - 1;
                if (e < s) continue;

                var across = new int[b1 - b0 + 1];
                int cells = 0, along0 = int.MaxValue, along1 = int.MinValue;
                for (int a = s; a <= e; a++)
                    for (int b = b0; b <= b1; b++)
                    {
                        if (!Cell(grid, gw, vertical, a, b)) continue;
                        across[b - b0]++;
                        cells++;
                    }
                if (cells < minCells) continue;

                Percentiles(across, cells, out int p0, out int p1, settings.TrimLow, settings.TrimHigh);
                int c0 = b0 + p0, c1 = b0 + p1;

                // The band's own extent along the axis, counting only ink inside the trimmed
                // cross range, so an outstretched arm does not stretch the band either.
                for (int a = s; a <= e; a++)
                    for (int b = c0; b <= c1; b++)
                    {
                        if (!Cell(grid, gw, vertical, a, b)) continue;
                        if (a < along0) along0 = a;
                        if (a > along1) along1 = a;
                    }
                if (along0 > along1) continue;

                int overlap = Mathf.RoundToInt(bandLength * settings.BandOverlap);
                along0 = Mathf.Max(a0, along0 - (i > 0 ? overlap : 0));
                along1 = Mathf.Min(a1, along1 + (i < count - 1 ? overlap : 0));

                bands.Add(vertical
                    ? new Band { X0 = c0, X1 = c1, Y0 = along0, Y1 = along1 }
                    : new Band { X0 = along0, X1 = along1, Y0 = c0, Y1 = c1 });
            }
            return bands;
        }

        private static bool Cell(bool[] grid, int gw, bool vertical, int along, int across)
            => vertical ? grid[along * gw + across] : grid[across * gw + along];

        /// <summary>Folds neighbours that cover nearly the same cross range into one capsule.</summary>
        private static void Merge(List<Band> bands, bool vertical, float similarity)
        {
            // Bands are produced in order along the long axis, so neighbours in the list are
            // neighbours in the body; the comparison is across that axis.
            for (int i = bands.Count - 1; i > 0; i--)
            {
                Band a = bands[i - 1], b = bands[i];
                int a0 = vertical ? a.X0 : a.Y0, a1 = vertical ? a.X1 : a.Y1;
                int b0 = vertical ? b.X0 : b.Y0, b1 = vertical ? b.X1 : b.Y1;
                int inter = Mathf.Min(a1, b1) - Mathf.Max(a0, b0) + 1;
                int union = Mathf.Max(a1, b1) - Mathf.Min(a0, b0) + 1;
                if (union <= 0 || inter / (float)union < similarity) continue;

                bands[i - 1] = new Band
                {
                    X0 = Mathf.Min(a.X0, b.X0), X1 = Mathf.Max(a.X1, b.X1),
                    Y0 = Mathf.Min(a.Y0, b.Y0), Y1 = Mathf.Max(a.Y1, b.Y1),
                };
                bands.RemoveAt(i);
            }
        }

        /// <summary>Trimmed width of the ink in the lowest eighth of its height, in cells.</summary>
        private static float MeasureFeet(bool[] grid, int gw, int minX, int maxX, int minY, int maxY)
        {
            int rows = Mathf.Max(2, Mathf.RoundToInt((maxY - minY + 1) * 0.125f));
            var histogram = new int[maxX - minX + 1];
            int total = 0;
            for (int y = minY; y <= Mathf.Min(maxY, minY + rows - 1); y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!grid[y * gw + x]) continue;
                    histogram[x - minX]++;
                    total++;
                }
            if (total == 0) return maxX - minX + 1;
            Percentiles(histogram, total, out int p0, out int p1, 0.05f, 0.95f);
            return p1 - p0 + 1;
        }

        private static void Percentiles(int[] histogram, int total, out int low, out int high,
                                        float lowQ, float highQ)
        {
            low = 0;
            high = histogram.Length - 1;
            if (total <= 0) return;

            int lowTarget = Mathf.FloorToInt(total * lowQ);
            int highTarget = Mathf.CeilToInt(total * highQ);
            int running = 0;
            bool lowFound = false;
            for (int i = 0; i < histogram.Length; i++)
            {
                running += histogram[i];
                if (!lowFound && running > lowTarget) { low = i; lowFound = true; }
                if (running >= highTarget) { high = i; break; }
            }
            if (high < low) high = low;
        }
    }
}
