using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UI.Frontend;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// What is drawn OVER the fill: the etapas.
    ///
    /// <para>A divider where each etapa begins, with a diamond notch on the frame above and below
    /// it that turns gold once that etapa is behind the bar; the etapa being loaded right now
    /// marked as a faintly lit target zone the fill is travelling into; a flash across an etapa
    /// the moment it completes; and slanted bands of light flowing along the filled part so the
    /// fill reads as energy rather than paint.</para>
    ///
    /// <para><b>The dividers are where the bar will really stop.</b> Their positions come from
    /// <c>BootScreenPlan</c>, whose shares are the same weights <c>BootProgress</c> advances by,
    /// so a notch is a promise the fill keeps: it arrives exactly on it when the last step of the
    /// etapa finishes.</para>
    ///
    /// <para>Rebuilt every frame the FX ticks — the flow never stops — which is a few dozen
    /// quads on a canvas that already redraws its fire.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LoadingBarSegmentsGraphic : MaskableGraphic
    {
        private const float StripeSpacing = 22f;
        private const float StripeWidth = 8f;
        private const float StripeSpeed = 42f;

        private readonly List<float> _starts = new List<float>();
        private readonly List<float> _flash = new List<float>();

        public float Thickness { get; set; } = 4f;
        public float Progress { get; set; }
        public int Current { get; set; } = -1;
        public float Clock { get; set; }
        public Color Tint { get; set; } = new Color(0.95f, 0.75f, 0.35f, 1f);
        /// <summary>0..1 over the whole bar, for the moment the boot is ready.</summary>
        public float CompleteFlash { get; set; }

        public int SegmentCount => _starts.Count;

        public static LoadingBarSegmentsGraphic Create(Transform parent)
        {
            var go = new GameObject("BarSegments", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var g = go.AddComponent<LoadingBarSegmentsGraphic>();
            g.raycastTarget = false;
            return g;
        }

        /// <summary>Where each etapa starts, in bar fractions. The first is always 0.</summary>
        public void SetStarts(IReadOnlyList<float> starts)
        {
            _starts.Clear();
            if (starts != null) for (int i = 0; i < starts.Count; i++) _starts.Add(Mathf.Clamp01(starts[i]));
            if (_starts.Count == 0) _starts.Add(0f);
            while (_flash.Count < _starts.Count) _flash.Add(0f);
            while (_flash.Count > _starts.Count) _flash.RemoveAt(_flash.Count - 1);
            SetVerticesDirty();
        }

        public float StartOf(int i) => i >= 0 && i < _starts.Count ? _starts[i] : 0f;
        public float EndOf(int i) => i + 1 < _starts.Count ? _starts[i + 1] : 1f;

        public void Flash(int segment)
        {
            if (segment >= 0 && segment < _flash.Count) _flash[segment] = 1f;
        }

        public void Decay(float dt)
        {
            for (int i = 0; i < _flash.Count; i++)
                if (_flash[i] > 0f) _flash[i] = Mathf.Max(0f, _flash[i] - dt / 0.65f);
        }

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float t = Thickness;
            float ix = r.xMin + t, iy = r.yMin + t, iw = r.width - 2f * t, ih = r.height - 2f * t;
            if (iw <= 0f || ih <= 0f) return;
            float p = Mathf.Clamp01(Progress);
            float fillW = iw * p;

            DrawTargetZone(vh, ix, iy, iw, ih, p);
            DrawFlow(vh, ix, iy, ih, fillW);

            // Completed etapas flash across their own span.
            for (int i = 0; i < _starts.Count; i++)
            {
                if (_flash[i] <= 0.001f) continue;
                float s = ix + iw * StartOf(i), e = ix + iw * Mathf.Min(EndOf(i), p);
                float a = _flash[i] * _flash[i] * 0.42f;
                FrontendMesh.QuadV(vh, s, iy, e - s, ih, FrontendMesh.WithAlpha(Tint, a), FrontendMesh.WithAlpha(Color.white, a));
            }

            if (CompleteFlash > 0.001f)
                FrontendMesh.Quad(vh, ix, iy, fillW, ih, FrontendMesh.WithAlpha(Color.white, CompleteFlash * CompleteFlash * 0.3f));

            for (int i = 1; i < _starts.Count; i++)
            {
                float dx = ix + iw * _starts[i];
                bool passed = p >= _starts[i] - 0.0005f;
                FrontendMesh.Quad(vh, dx - 1f, iy, 2f, ih, new Color(0f, 0f, 0f, passed ? 0.42f : 0.7f));
                FrontendMesh.Quad(vh, dx + 1f, iy, 1f, ih, new Color(1f, 0.9f, 0.62f, passed ? 0.30f : 0.10f));

                float glow = i - 1 < _flash.Count ? _flash[i - 1] : 0f;
                float n = 3.4f + glow * 1.6f;
                FrontendDraw.Notch(vh, dx, r.yMax + 1f, n, passed, Tint, glow);
                FrontendDraw.Notch(vh, dx, r.yMin - 1f, n, passed, Tint, glow);
            }
        }

        /// <summary>
        /// The etapa in progress, ahead of the fill: a faint lit channel with a rim along its top
        /// and bottom, breathing. It is the answer to "how far is the next notch" without a number.
        /// </summary>
        private void DrawTargetZone(VertexHelper vh, float ix, float iy, float iw, float ih, float p)
        {
            if (Current < 0 || Current >= _starts.Count || p >= 0.999f) return;
            float s = Mathf.Max(StartOf(Current), p), e = EndOf(Current);
            if (e <= s) return;
            float x0 = ix + iw * s, x1 = ix + iw * e;
            float breathe = 0.5f + 0.5f * Mathf.Sin(Clock * 3.1f);
            Color zone = Color.Lerp(Tint, Color.white, 0.2f);
            FrontendMesh.QuadV(vh, x0, iy, x1 - x0, ih,
                                 FrontendMesh.WithAlpha(zone, 0.04f + 0.05f * breathe),
                                 FrontendMesh.WithAlpha(zone, 0.10f + 0.07f * breathe));
            FrontendMesh.Quad(vh, x0, iy + ih - 1f, x1 - x0, 1f, FrontendMesh.WithAlpha(zone, 0.35f + 0.3f * breathe));
            FrontendMesh.Quad(vh, x0, iy, x1 - x0, 1f, FrontendMesh.WithAlpha(zone, 0.18f + 0.15f * breathe));
        }

        /// <summary>Slanted bands of light sliding along the filled part, fading before the edge so none is cut.</summary>
        private void DrawFlow(VertexHelper vh, float ix, float iy, float ih, float fillW)
        {
            if (fillW < 6f) return;
            float slant = ih * 0.9f;
            float offset = Mathf.Repeat(Clock * StripeSpeed, StripeSpacing);
            for (float sx = -StripeSpacing - slant + offset; sx < fillW; sx += StripeSpacing)
            {
                float left = Mathf.Max(0f, sx);
                float right = sx + slant + StripeWidth;
                if (right > fillW || sx < 0f) continue;
                float edge = Mathf.Clamp01((fillW - right) / 26f) * Mathf.Clamp01((sx - 0f) / 12f);
                if (edge <= 0.01f) continue;
                FrontendMesh.Parallelogram(vh, ix + left, iy, StripeWidth, ih, slant,
                                             FrontendMesh.WithAlpha(Color.white, 0.075f * edge));
            }
        }
    }
}
