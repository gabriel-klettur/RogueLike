#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// The alpha of one sprite's rect, read from its SOURCE PNG. Editor only.
    ///
    /// <para>Shared by the collision baker and the Entities editor's "re-measure this frame", so
    /// the two read a frame the same way. Two reasons it cannot use the imported texture: the
    /// imported textures are not readable (and flipping that on shipped art is not a measuring
    /// tool's decision), and in Play Mode <c>sprite.texture</c> is the ATLAS page — a rect or a
    /// scale taken from it maps a whole frame onto a few pixels and the fit fails with nothing
    /// visibly wrong.</para>
    /// </summary>
    public sealed class SpriteAlphaReader
    {
        private sealed class SourcePixels
        {
            public Color32[] Pixels;
            public int Width, Height;
        }

        // A decoded PNG per path for the life of the reader, so a strip is decoded once.
        private readonly Dictionary<string, SourcePixels> _cache = new Dictionary<string, SourcePixels>();

        public void Clear() => _cache.Clear();

        public bool TryRead(Sprite sprite, out byte[] alpha, out int width, out int height)
        {
            alpha = null;
            width = height = 0;
            if (sprite == null) return false;

            string path = AssetDatabase.GetAssetPath(sprite);
            if (string.IsNullOrEmpty(path))
            {
                foreach (string guid in AssetDatabase.FindAssets($"{sprite.name} t:Texture2D"))
                {
                    string candidate = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(candidate) != sprite.name) continue;
                    path = candidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            if (!_cache.TryGetValue(path, out var tex))
            {
                tex = null;
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                try
                {
                    if (decoded.LoadImage(File.ReadAllBytes(path)))
                        tex = new SourcePixels { Pixels = decoded.GetPixels32(), Width = decoded.width, Height = decoded.height };
                }
                finally
                {
                    Object.DestroyImmediate(decoded);
                }
                _cache[path] = tex;
            }
            if (tex == null) return false;

            if (!TrySourceRect(path, sprite, tex, out Rect r)) return false;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, tex.Width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, tex.Height - 1);
            width = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, tex.Width - x0);
            height = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, tex.Height - y0);

            Color32[] px = tex.Pixels;
            alpha = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int src = (y0 + y) * tex.Width + x0;
                int dst = y * width;
                for (int x = 0; x < width; x++) alpha[dst + x] = px[src + x].a;
            }
            return true;
        }

        /// <summary>
        /// Which part of the PNG a sprite is, asked of the IMPORTER: a single-sprite texture is
        /// the whole image, a sheet names its rects in source pixels.
        /// </summary>
        private static bool TrySourceRect(string path, Sprite sprite, SourcePixels tex, out Rect rect)
        {
            rect = new Rect(0, 0, tex.Width, tex.Height);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.spriteImportMode != SpriteImportMode.Multiple) return true;

            importer.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
            float sx = srcW > 0 ? tex.Width / (float)srcW : 1f;
            float sy = srcH > 0 ? tex.Height / (float)srcH : 1f;
#pragma warning disable CS0618 // spritesheet is the rect source every importer version this project ships answers
            foreach (var meta in importer.spritesheet)
#pragma warning restore CS0618
            {
                if (meta.name != sprite.name) continue;
                rect = new Rect(meta.rect.x * sx, meta.rect.y * sy, meta.rect.width * sx, meta.rect.height * sy);
                return true;
            }
            return false;
        }
    }
}
#endif
