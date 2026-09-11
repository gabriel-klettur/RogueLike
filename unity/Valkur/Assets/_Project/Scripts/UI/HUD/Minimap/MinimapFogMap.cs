using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// What the player has explored, one byte per world unit, per WORLD.
    ///
    /// <para><b>Per world, never per zone.</b> The minimap this replaced cleared its fog on
    /// every <c>OnZoneChanged</c> — and zones are 50x50 tiles laid edge to edge, so walking
    /// across town wiped the map of the street the player had just come down. The key is the
    /// world the player is in: the outdoor world, or one interior. Stepping into a house starts
    /// that house's fog; stepping back out returns the town's, untouched.</para>
    ///
    /// <para><b>A value, not a bit.</b> A cell stores 0..255 and a reveal writes a ramp across
    /// its last two units, so the bilinear sample the shader takes is a soft, round frontier
    /// instead of a staircase of whole cells. "Explored" is the upper half.</para>
    ///
    /// <para><b>It grows.</b> A layer starts as a 128-unit square around the first reveal and
    /// grows by whole margins when a reveal leaves it. It knows nothing about the terrain
    /// atlas's bounds: the two are sampled through separate world rects in the shader, which
    /// is what lets an interior's fog exist before its terrain has been baked.</para>
    /// </summary>
    public sealed class MinimapFogMap
    {
        public const string WorldKey = "world";
        private const int InitialSize = 128;
        private const int GrowMargin = 48;
        private const byte ExploredThreshold = 128;
        private const float EdgeRamp = 2f;

        private sealed class Layer
        {
            public int X0, Y0, W, H;
            public byte[] Cells;
        }

        private readonly Dictionary<string, Layer> _layers = new Dictionary<string, Layer>();
        private Layer _active;
        private Texture2D _texture;
        private bool _textureDirty;
        private bool _textureStale;
        private int _revision;

        /// <summary>The world whose fog is live.</summary>
        public string ActiveKey { get; private set; } = WorldKey;

        /// <summary>Bumped on every change, so a reader can tell "nothing new" cheaply.</summary>
        public int Revision => _revision;

        /// <summary>World rect the fog texture covers: (x0, y0, width, height).</summary>
        public Vector4 WorldRect => _active == null ? new Vector4(0f, 0f, 1f, 1f)
                                                    : new Vector4(_active.X0, _active.Y0, _active.W, _active.H);

        /// <summary>Switch the live world. The previous one's fog is kept.</summary>
        public void SetActiveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) key = WorldKey;
            if (key == ActiveKey && _active != null) return;
            ActiveKey = key;
            _layers.TryGetValue(key, out _active);
            _textureStale = true;
            _revision++;
        }

        /// <summary>
        /// Mark every cell within <paramref name="radius"/> of <paramref name="centre"/> explored.
        /// Cells that crossed into "explored" on this call are appended to
        /// <paramref name="newlyRevealed"/> (up to <paramref name="maxNewly"/>) as world centres.
        /// Returns how many crossed.
        /// </summary>
        public int Reveal(Vector2 centre, float radius, List<Vector2> newlyRevealed = null, int maxNewly = 0)
        {
            if (radius <= 0f) return 0;
            // Everything inside the radius is fully explored; the ramp lies OUTSIDE it, so the
            // soft edge never costs the player ground they have actually walked near.
            float ramp = EdgeRamp;
            float outer = radius + ramp;
            int minX = Mathf.FloorToInt(centre.x - outer), maxX = Mathf.CeilToInt(centre.x + outer);
            int minY = Mathf.FloorToInt(centre.y - outer), maxY = Mathf.CeilToInt(centre.y + outer);
            EnsureCovers(minX, minY, maxX, maxY);

            var l = _active;
            int crossed = 0;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = y + 0.5f - centre.y;
                int row = (y - l.Y0) * l.W;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x + 0.5f - centre.x;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= outer) continue;
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01((outer - dist) / ramp) * 255f);
                    int idx = row + (x - l.X0);
                    byte old = l.Cells[idx];
                    if (v <= old) continue;
                    l.Cells[idx] = v;
                    _textureDirty = true;
                    if (old < ExploredThreshold && v >= ExploredThreshold)
                    {
                        crossed++;
                        if (newlyRevealed != null && newlyRevealed.Count < maxNewly)
                            newlyRevealed.Add(new Vector2(x + 0.5f, y + 0.5f));
                    }
                }
            }
            if (_textureDirty) _revision++;
            return crossed;
        }

        /// <summary>True when the cell under <paramref name="world"/> is explored in the live world.</summary>
        public bool IsExplored(Vector2 world)
        {
            var l = _active;
            if (l == null) return false;
            int x = Mathf.FloorToInt(world.x) - l.X0, y = Mathf.FloorToInt(world.y) - l.Y0;
            if (x < 0 || y < 0 || x >= l.W || y >= l.H) return false;
            return l.Cells[y * l.W + x] >= ExploredThreshold;
        }

        /// <summary>Fraction (0..1) of a world rect that is explored in the live world.</summary>
        public float ExploredFraction(RectInt rect)
        {
            var l = _active;
            if (l == null || rect.width <= 0 || rect.height <= 0) return 0f;
            int hit = 0;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                int ly = y - l.Y0;
                if (ly < 0 || ly >= l.H) continue;
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    int lx = x - l.X0;
                    if (lx < 0 || lx >= l.W) continue;
                    if (l.Cells[ly * l.W + lx] >= ExploredThreshold) hit++;
                }
            }
            return (float)hit / (rect.width * rect.height);
        }

        /// <summary>Forget the live world's fog.</summary>
        public void ClearActive()
        {
            _layers.Remove(ActiveKey);
            _active = null;
            _textureStale = true;
            _revision++;
        }

        /// <summary>Forget every world's fog (new run).</summary>
        public void ClearAll()
        {
            _layers.Clear();
            _active = null;
            _textureStale = true;
            _revision++;
        }

        /// <summary>
        /// The fog texture for the live world, uploaded if anything changed. R8, linear,
        /// bilinear — the filter is what turns the byte ramp into a smooth frontier.
        /// </summary>
        public Texture GetTexture()
        {
            var l = _active;
            if (l == null)
            {
                if (_texture == null || _textureStale) RebuildTexture(1, 1, new byte[1]);
                _textureStale = false;
                return _texture;
            }
            if (_textureStale || _texture == null || _texture.width != l.W || _texture.height != l.H)
            {
                RebuildTexture(l.W, l.H, l.Cells);
                _textureStale = false;
                _textureDirty = false;
            }
            else if (_textureDirty)
            {
                _texture.SetPixelData(l.Cells, 0);
                _texture.Apply(false, false);
                _textureDirty = false;
            }
            return _texture;
        }

        /// <summary>Release the GPU texture.</summary>
        public void Dispose()
        {
            if (_texture != null) DestroySafe(_texture);
            _texture = null;
        }

        private void RebuildTexture(int w, int h, byte[] cells)
        {
            if (_texture != null) DestroySafe(_texture);
            _texture = new Texture2D(w, h, TextureFormat.R8, false, true)
            {
                name = "MinimapFog",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _texture.SetPixelData(cells, 0);
            _texture.Apply(false, false);
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        private void EnsureCovers(int minX, int minY, int maxX, int maxY)
        {
            if (_active == null)
            {
                int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
                int size = Mathf.Max(InitialSize, Mathf.Max(maxX - minX, maxY - minY) + GrowMargin * 2);
                _active = new Layer { X0 = cx - size / 2, Y0 = cy - size / 2, W = size, H = size, Cells = new byte[size * size] };
                _layers[ActiveKey] = _active;
                _textureStale = true;
                return;
            }

            var l = _active;
            if (minX >= l.X0 && minY >= l.Y0 && maxX < l.X0 + l.W && maxY < l.Y0 + l.H) return;

            int nx0 = Mathf.Min(l.X0, minX - GrowMargin);
            int ny0 = Mathf.Min(l.Y0, minY - GrowMargin);
            int nx1 = Mathf.Max(l.X0 + l.W, maxX + 1 + GrowMargin);
            int ny1 = Mathf.Max(l.Y0 + l.H, maxY + 1 + GrowMargin);
            Resize(l, nx0, ny0, nx1 - nx0, ny1 - ny0);
            _textureStale = true;
        }

        private static void Resize(Layer l, int x0, int y0, int w, int h)
        {
            var cells = new byte[w * h];
            for (int y = 0; y < l.H; y++)
                Buffer.BlockCopy(l.Cells, y * l.W, cells, (y + l.Y0 - y0) * w + (l.X0 - x0), l.W);
            l.X0 = x0; l.Y0 = y0; l.W = w; l.H = h; l.Cells = cells;
        }

        // ── Persistence ────────────────────────────────────────────────────

        [Serializable] private sealed class SaveDoc { public int version = 1; public List<SaveLayer> layers = new List<SaveLayer>(); }
        [Serializable] private sealed class SaveLayer { public string key; public int x0, y0, w, h; public string rle; }

        /// <summary>Every world's fog as compact JSON (run-length encoded, base64).</summary>
        public string ToJson()
        {
            var doc = new SaveDoc();
            foreach (var kv in _layers)
            {
                var l = kv.Value;
                doc.layers.Add(new SaveLayer { key = kv.Key, x0 = l.X0, y0 = l.Y0, w = l.W, h = l.H, rle = Convert.ToBase64String(Rle(l.Cells)) });
            }
            return JsonUtility.ToJson(doc);
        }

        /// <summary>
        /// Merge fog from <see cref="ToJson"/> into what is already known — a MERGE, taking the
        /// larger value per cell, so loading can never un-explore anything.
        /// Returns false on a document it cannot read.
        /// </summary>
        public bool MergeJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            SaveDoc doc;
            try { doc = JsonUtility.FromJson<SaveDoc>(json); }
            catch (Exception) { return false; }
            if (doc == null || doc.layers == null) return false;

            string keep = ActiveKey;
            foreach (var s in doc.layers)
            {
                if (s == null || string.IsNullOrEmpty(s.key) || s.w <= 0 || s.h <= 0 || string.IsNullOrEmpty(s.rle)) continue;
                byte[] cells;
                try { cells = UnRle(Convert.FromBase64String(s.rle), s.w * s.h); }
                catch (Exception) { continue; }
                if (cells == null) continue;

                ActiveKey = s.key;
                _layers.TryGetValue(s.key, out _active);
                EnsureCovers(s.x0, s.y0, s.x0 + s.w - 1, s.y0 + s.h - 1);
                var l = _active;
                for (int y = 0; y < s.h; y++)
                {
                    int dst = (y + s.y0 - l.Y0) * l.W + (s.x0 - l.X0);
                    int src = y * s.w;
                    for (int x = 0; x < s.w; x++)
                        if (cells[src + x] > l.Cells[dst + x]) l.Cells[dst + x] = cells[src + x];
                }
            }
            ActiveKey = keep;
            _layers.TryGetValue(keep, out _active);
            _textureStale = true;
            _revision++;
            return true;
        }

        private static byte[] Rle(byte[] data)
        {
            var o = new List<byte>(data.Length / 8 + 16);
            int i = 0;
            while (i < data.Length)
            {
                byte v = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == v && run < 255) run++;
                o.Add((byte)run);
                o.Add(v);
                i += run;
            }
            return o.ToArray();
        }

        private static byte[] UnRle(byte[] rle, int expected)
        {
            var o = new byte[expected];
            int p = 0;
            for (int i = 0; i + 1 < rle.Length; i += 2)
            {
                int run = rle[i];
                byte v = rle[i + 1];
                if (p + run > expected) return null;
                for (int k = 0; k < run; k++) o[p++] = v;
            }
            return p == expected ? o : null;
        }
    }
}
