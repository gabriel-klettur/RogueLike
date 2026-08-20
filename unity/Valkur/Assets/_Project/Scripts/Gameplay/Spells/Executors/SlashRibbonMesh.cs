using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// One layer of a slash: an annular strip whose per-vertex alpha and radial thickness
    /// are rewritten every frame, so the same mesh can be a swept crescent, an outward
    /// lance or a static telegraph outline.
    ///
    /// The vertex and colour arrays are allocated once and reused — a slash can fire
    /// several times a second from every NPC on screen, so per-frame garbage here is the
    /// difference between a free effect and a stutter.
    /// </summary>
    internal sealed class SlashRibbonMesh
    {
        private readonly Mesh _mesh;
        private readonly Vector3[] _vertices;
        private readonly Color[] _colors;
        private readonly Vector2[] _radials;
        private readonly Color _color;
        private readonly float _trailWindow;
        private readonly float _innerFactor;
        private readonly float _outerFactor;
        private readonly float _taperPower;
        private readonly int _segments;

        public SlashRibbonMesh(Transform parent, string name, Material material, int segments,
                               float arcDegrees, float innerFactor, float outerFactor,
                               Color color, float trailWindow, int sortingOrder,
                               float taperPower = 0.38f)
        {
            _segments = Mathf.Max(3, segments);
            _color = color;
            _trailWindow = Mathf.Max(0.08f, trailWindow);
            _innerFactor = innerFactor;
            _outerFactor = outerFactor;
            _taperPower = taperPower;

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
            _radials = new Vector2[pointCount];
            var uvs = new Vector2[_vertices.Length];
            var triangles = new int[_segments * 6];

            float halfArc = arcDegrees * 0.5f;
            for (int i = 0; i < pointCount; i++)
            {
                float p = i / (float)_segments;
                float angle = Mathf.Lerp(-halfArc, halfArc, p) * Mathf.Deg2Rad;
                _radials[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                int v = i * 2;
                uvs[v] = new Vector2(p, 0f);
                uvs[v + 1] = new Vector2(p, 1f);
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
        /// Head sweeps through the arc. Alpha decays behind it and the radial thickness
        /// collapses at both ends of the lit stretch, so the strip reads as a pointed
        /// crescent rather than a radar-fan wedge.
        /// </summary>
        public void Angular(float head01, float linger, float radius, float alphaScale)
        {
            float step = 1f / _segments;
            float centerFactor = (_innerFactor + _outerFactor) * 0.5f;
            float halfFactor = (_outerFactor - _innerFactor) * 0.5f;

            for (int i = 0; i <= _segments; i++)
            {
                float p = i / (float)_segments;
                float behind = head01 - p;
                float alpha = 0f;
                float taper = 0f;

                if (behind >= -step && behind <= _trailWindow)
                {
                    float tail = Mathf.Clamp01(1f - Mathf.Max(0f, behind) / _trailWindow);
                    float tip = Mathf.Clamp01((behind + step) / step);
                    alpha = Mathf.Pow(tail, 1.35f) * tip * linger * _color.a * alphaScale;

                    float along = Mathf.Clamp01(Mathf.Max(0f, behind) / _trailWindow);
                    taper = Mathf.Pow(Mathf.Sin(along * Mathf.PI), _taperPower);
                }

                WriteSegment(i, centerFactor * radius, halfFactor * radius * taper, alpha);
            }
            Apply();
        }

        /// <summary>
        /// Static outline of the full reach, used during the wind-up of a wide swing.
        /// Deliberately thin and dim: it must announce the danger zone without competing
        /// with the swing that follows it.
        /// </summary>
        public void Telegraph(float alpha01, float radius)
        {
            float thickness = (_outerFactor - _innerFactor) * radius * 0.35f;
            float center = _outerFactor * radius - thickness * 0.5f;
            for (int i = 0; i <= _segments; i++)
            {
                float p = i / (float)_segments;
                float side = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI), 0.28f);
                WriteSegment(i, center, thickness * 0.5f * side, alpha01 * _color.a * side);
            }
            Apply();
        }

        public void Hide()
        {
            for (int i = 0; i <= _segments; i++) WriteSegment(i, 0f, 0f, 0f);
            Apply();
        }

        public void Dispose()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Object.Destroy(_mesh);
            else Object.DestroyImmediate(_mesh);
        }

        private void WriteSegment(int index, float centerRadius, float halfWidth, float alpha)
        {
            Vector2 radial = _radials[index];
            int v = index * 2;
            _vertices[v] = radial * (centerRadius - halfWidth);
            _vertices[v + 1] = radial * (centerRadius + halfWidth);
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
