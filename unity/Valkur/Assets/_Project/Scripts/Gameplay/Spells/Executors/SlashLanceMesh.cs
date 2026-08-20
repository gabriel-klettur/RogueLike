using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One layer of a thrust: a spindle laid along the cast axis, pointed at the tip,
    /// widest just behind it, tapering to nothing at the tail.
    ///
    /// A thrust was originally drawn with the same annular strip as a swing, which is what
    /// made it read as a white lens instead of a stab: an arc band 40 degrees wide and half
    /// a radius thick is almost as broad as it is long, and three of them sharing an outer
    /// edge stack into one opaque blob. Travel along the axis needs its own geometry.
    ///
    /// Cross-sections and their alphas are constant in shape, so they are computed once and
    /// only scaled per frame.
    /// </summary>
    internal sealed class SlashLanceMesh
    {
        /// <summary>Radial fraction the tip starts at before travelling outward.</summary>
        public const float RADIAL_START = 0.12f;

        /// <summary>Exponents of the silhouette u^FRONT * (1-u)^BACK, u = 0 tail, 1 tip.</summary>
        private const float SHAPE_FRONT = 1.4f;
        private const float SHAPE_BACK = 0.6f;

        /// <summary>Alpha ramp from tail to tip. Below 1 keeps the tail visible as a streak.</summary>
        private const float ALPHA_RAMP = 0.8f;

        private readonly Mesh _mesh;
        private readonly Vector3[] _vertices;
        private readonly Color[] _colors;
        private readonly float[] _shape;
        private readonly float[] _alphaCurve;
        private readonly Color _color;
        private readonly float _halfWidthFactor;
        private readonly float _lengthFactor;
        private readonly int _segments;

        public SlashLanceMesh(Transform parent, string name, Material material, int segments,
                              float halfWidthFactor, float lengthFactor, Color color,
                              int sortingOrder)
        {
            _segments = Mathf.Max(3, segments);
            _color = color;
            _halfWidthFactor = halfWidthFactor;
            _lengthFactor = lengthFactor;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingLayerName = SortingConfig.LAYER_VFX;
            meshRenderer.sortingOrder = sortingOrder;

            int pointCount = _segments + 1;
            _vertices = new Vector3[pointCount * 2];
            _colors = new Color[_vertices.Length];
            _shape = new float[pointCount];
            _alphaCurve = new float[pointCount];
            var uvs = new Vector2[_vertices.Length];
            var triangles = new int[_segments * 6];

            // Normalising by the peak keeps halfWidthFactor meaning "half-width at the
            // widest point" whatever the exponents are tuned to.
            float peakU = SHAPE_FRONT / (SHAPE_FRONT + SHAPE_BACK);
            float peak = Mathf.Pow(peakU, SHAPE_FRONT) * Mathf.Pow(1f - peakU, SHAPE_BACK);

            for (int i = 0; i < pointCount; i++)
            {
                float u = i / (float)_segments;
                _shape[i] = Mathf.Pow(u, SHAPE_FRONT) * Mathf.Pow(1f - u, SHAPE_BACK) / peak;
                _alphaCurve[i] = Mathf.Pow(u, ALPHA_RAMP);
                int v = i * 2;
                uvs[v] = new Vector2(u, 0f);
                uvs[v + 1] = new Vector2(u, 1f);
                _colors[v] = _colors[v + 1] = SlashProfile.WithAlpha(color, 0f);
            }

            for (int i = 0; i < _segments; i++)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            _mesh = new Mesh { name = name + "Mesh", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.uv = uvs;
            _mesh.colors = _colors;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            filter.sharedMesh = _mesh;
        }

        /// <summary>
        /// Places the tip at <paramref name="head01"/> of the reach. While the tip is still
        /// closer than the lance is long the tail stays pinned at the caster, so the thrust
        /// grows out of the hand instead of appearing whole in mid-air.
        ///
        /// <paramref name="contract01"/> pulls the tail up to the tip as the attack ends.
        /// A thrust that only fades out reads as a spike left hanging in the air; one whose
        /// body is drawn back behind its point reads as the blade being recovered.
        /// </summary>
        public void Draw(float head01, float linger, float radius, float alphaScale,
                         float contract01 = 0f)
        {
            float head = Mathf.Lerp(RADIAL_START, 1f, head01) * radius;
            float length = _lengthFactor * radius * (1f - Mathf.Clamp01(contract01));
            float tail = Mathf.Max(0f, head - length);
            float maxHalfWidth = _halfWidthFactor * radius;

            for (int i = 0; i <= _segments; i++)
            {
                float u = i / (float)_segments;
                float x = Mathf.Lerp(tail, head, u);
                float halfWidth = maxHalfWidth * _shape[i];
                float alpha = _alphaCurve[i] * linger * _color.a * alphaScale;
                Write(i, x, halfWidth, alpha);
            }
            Apply();
        }

        public void Hide()
        {
            for (int i = 0; i <= _segments; i++) Write(i, 0f, 0f, 0f);
            Apply();
        }

        public void Dispose()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Object.Destroy(_mesh);
            else Object.DestroyImmediate(_mesh);
        }

        private void Write(int index, float x, float halfWidth, float alpha)
        {
            int v = index * 2;
            _vertices[v] = new Vector3(x, -halfWidth, 0f);
            _vertices[v + 1] = new Vector3(x, halfWidth, 0f);
            Color c = SlashProfile.WithAlpha(_color, alpha);
            _colors[v] = c;
            _colors[v + 1] = c;
        }

        private void Apply()
        {
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
        }
    }
}
