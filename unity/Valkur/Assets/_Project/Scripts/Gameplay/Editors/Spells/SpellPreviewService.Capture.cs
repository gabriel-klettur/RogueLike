using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    public sealed partial class SpellPreviewService
    {
        // ── Frame capture ────────────────────────────────────────────────────────

        /// <summary>
        /// Copies the current RenderTexture contents into a new Texture2D and
        /// appends it to the capture buffer. When the buffer is at capacity the
        /// oldest frame is disposed before the new one is appended so memory is
        /// strictly bounded by MAX_CAPTURED_FRAMES.
        /// </summary>
        private void CaptureCurrentFrame()
        {
            if (_rt == null) return;

            if (_frames.Count >= MAX_CAPTURED_FRAMES)
            {
                var oldest = _frames[0];
                _frames.RemoveAt(0);
                SafeDestroy.Of(oldest);
            }

            var tex = new Texture2D(RT_SIZE, RT_SIZE, TextureFormat.RGB24, mipChain: false);
            tex.filterMode = FilterMode.Point;   // preserve pixel-art crispness

            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            tex.ReadPixels(new Rect(0, 0, RT_SIZE, RT_SIZE), destX: 0, destY: 0);
            tex.Apply(updateMipmaps: false);
            RenderTexture.active = prev;

            _frames.Add(tex);
            _displayedFrame = _frames.Count - 1;
        }

        /// <summary>Destroy every Texture2D in _frames and clear the list.</summary>
        private void DisposeAllFrames()
        {
            for (int i = 0; i < _frames.Count; i++)
                SafeDestroy.Of(_frames[i]);
            _frames.Clear();
            _displayedFrame = 0;
        }
    }
}
