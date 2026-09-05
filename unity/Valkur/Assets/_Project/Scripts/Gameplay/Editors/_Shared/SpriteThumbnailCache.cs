using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Editors
{
    /// <summary>
    /// Picker-sized copies of production sprites, filtered properly.
    ///
    /// <para>WHY. The catalog pickers draw a template's own sprite into a 64 px icon. The
    /// buildings atlas is <c>filterMode: Point</c> with no mipmaps — correct for a 32-PPU world
    /// drawn at integer scale, and wrong for a 1024 px castle squeezed into 64 px: point
    /// sampling keeps one texel in sixteen and throws the rest away, so big buildings came
    /// out as speckle. Nothing in the asset pipeline can fix that per Image: a texture has one
    /// filter mode and every sampler of it inherits it.</para>
    ///
    /// <para>HOW. Two GPU blits. The first crops the sprite's rect out of its atlas page into a
    /// temporary render texture with a mip chain; the second samples that chain trilinearly
    /// down to the requested size, which is a proper box-filtered reduction. One 64 KB
    /// readback per thumbnail, then the source is never touched again. Sprites already at or
    /// under the target size are returned as they are — at 1:1 or 2:1 the point-filtered art
    /// is the correct look, and a copy would only soften it.</para>
    ///
    /// <para>The cache is bounded (<see cref="MAX_ENTRIES"/>) and lazy, so a virtualised
    /// picker builds thumbnails for the rows it shows and nothing else.</para>
    /// </summary>
    public static class SpriteThumbnailCache
    {
        public const int DEFAULT_SIZE = 128;

        /// <summary>
        /// Above this many thumbnails the whole cache is dropped and rebuilt lazily: 512 at
        /// 128 px is ~34 MB with mips, which is as much as a picker should ever hold.
        /// </summary>
        public const int MAX_ENTRIES = 512;

        private static readonly Dictionary<Sprite, Sprite> _cache = new Dictionary<Sprite, Sprite>();
        private static readonly List<Texture2D> _ownedTextures = new List<Texture2D>();

        public static int Count => _cache.Count;

        /// <summary>
        /// A thumbnail of <paramref name="source"/> no larger than <paramref name="size"/> on
        /// its longest side, bilinear with mipmaps. The source itself when it is already
        /// small enough; null for a null source.
        /// </summary>
        public static Sprite Get(Sprite source, int size = DEFAULT_SIZE)
        {
            if (source == null) return null;
            if (source.rect.width <= size && source.rect.height <= size) return source;

            if (_cache.TryGetValue(source, out var cached) && cached != null) return cached;

            if (_cache.Count >= MAX_ENTRIES) Clear();

            var thumb = Build(source, size);
            if (thumb == null) return source;
            _cache[source] = thumb;
            return thumb;
        }

        /// <summary>Whether a thumbnail for this sprite has already been built.</summary>
        public static bool Has(Sprite source) => source != null && _cache.ContainsKey(source);

        /// <summary>Destroys every thumbnail texture and forgets them.</summary>
        public static void Clear()
        {
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                var tex = _ownedTextures[i];
                if (tex == null) continue;
                if (Application.isPlaying) Object.Destroy(tex);
                else Object.DestroyImmediate(tex);
            }
            _ownedTextures.Clear();
            _cache.Clear();
        }

        private static Sprite Build(Sprite source, int size)
        {
            var tex = source.texture;
            if (tex == null) return null;

            // textureRect is the sprite's own pixels on whatever page it lives on — the
            // whole page for a loose sprite, its packed cell for an atlased one.
            Rect r = source.textureRect;
            int srcW = Mathf.Max(1, Mathf.RoundToInt(r.width));
            int srcH = Mathf.Max(1, Mathf.RoundToInt(r.height));

            float fit = Mathf.Min((float)size / srcW, (float)size / srcH);
            int dstW = Mathf.Max(1, Mathf.RoundToInt(srcW * fit));
            int dstH = Mathf.Max(1, Mathf.RoundToInt(srcH * fit));

            // Pass 1 — crop into a mip-mapped RT at native size. autoGenerateMips gives the
            // box-filtered chain that the point-filtered source can never provide.
            var fullDesc = new RenderTextureDescriptor(srcW, srcH, RenderTextureFormat.ARGB32, 0)
            {
                useMipMap        = true,
                autoGenerateMips = true,
                msaaSamples      = 1,
                sRGB             = QualitySettings.activeColorSpace == ColorSpace.Linear,
            };
            var full = RenderTexture.GetTemporary(fullDesc);
            full.filterMode = FilterMode.Trilinear;
            full.wrapMode   = TextureWrapMode.Clamp;

            var scale  = new Vector2(r.width / tex.width, r.height / tex.height);
            var offset = new Vector2(r.x / tex.width, r.y / tex.height);
            Graphics.Blit(tex, full, scale, offset);

            // Pass 2 — trilinear reduction to the thumbnail size.
            var small = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
            small.filterMode = FilterMode.Bilinear;
            Graphics.Blit(full, small);

            var previous = RenderTexture.active;
            Texture2D result;
            try
            {
                RenderTexture.active = small;
                result = new Texture2D(dstW, dstH, TextureFormat.RGBA32, mipChain: true, linear: false)
                {
                    name       = source.name + "_thumb",
                    filterMode = FilterMode.Bilinear,
                    wrapMode   = TextureWrapMode.Clamp,
                    hideFlags  = HideFlags.HideAndDontSave,
                };
                result.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                result.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(small);
                RenderTexture.ReleaseTemporary(full);
            }

            _ownedTextures.Add(result);
            var sprite = Sprite.Create(result, new Rect(0, 0, dstW, dstH), new Vector2(0.5f, 0.5f), 100f);
            sprite.name      = result.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // The textures themselves died with the previous session's objects; only the
            // references need forgetting.
            _cache.Clear();
            _ownedTextures.Clear();
        }
    }
}
