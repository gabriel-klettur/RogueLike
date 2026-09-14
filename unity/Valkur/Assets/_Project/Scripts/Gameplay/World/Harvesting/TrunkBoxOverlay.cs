using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Draws the box each nearby tree is hit and worked on — the drawn trunk, or the footprint
    /// where the art has none — so the hand-drawn boxes can be checked in the world, on the real
    /// scale and sorting, rather than only on contact sheets. Toggled by <c>tronco ver</c>.
    ///
    /// <para>It reads <see cref="HarvestNode.InteractionBounds"/>, the same bounds the prompt,
    /// the blow contact and the trunk mark use, so what it outlines is what the game uses: an
    /// overlay that recomputed the box from the template would agree with the data and not
    /// necessarily with the code.</para>
    /// </summary>
    public sealed class TrunkBoxOverlay : MonoBehaviour
    {
        private const float Radius = 24f;
        private const float RescanSeconds = 0.5f;

        private static readonly Color TrunkColour = new Color(0.35f, 1f, 0.45f, 1f);
        private static readonly Color FootprintColour = new Color(1f, 0.6f, 0.25f, 1f);

        private readonly List<HarvestNode> _nodes = new List<HarvestNode>();
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private Material _material;
        private float _rescanAt;

        public int Outlined { get; private set; }

        /// <summary>Turn the overlay on or off; returns the new state.</summary>
        public static bool SetVisible(bool on)
        {
            var existing = FindObjectOfType<TrunkBoxOverlay>();
            if (!on)
            {
                if (existing != null) Destroy(existing.gameObject);
                return false;
            }
            if (existing == null) new GameObject("TrunkBoxOverlay").AddComponent<TrunkBoxOverlay>();
            return true;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private void LateUpdate()
        {
            var player = GameObject.FindWithTag("Player");
            Vector2 centre = player != null ? (Vector2)player.transform.position : Vector2.zero;

            if (Time.unscaledTime >= _rescanAt)
            {
                _rescanAt = Time.unscaledTime + RescanSeconds;
                _nodes.Clear();
                foreach (var node in FindObjectsOfType<HarvestNode>())
                {
                    if (node == null || node.Building == null) continue;
                    if (((Vector2)node.transform.position - centre).sqrMagnitude > Radius * Radius) continue;
                    _nodes.Add(node);
                }
            }

            int used = 0;
            foreach (var node in _nodes)
            {
                if (node == null) continue;
                var b = node.InteractionBounds;
                bool trunk = node.Building.TryGetTrunkBounds(out _);
                var line = Line(used++);
                const float z = -0.3f;
                line.SetPosition(0, new Vector3(b.min.x, b.min.y, z));
                line.SetPosition(1, new Vector3(b.max.x, b.min.y, z));
                line.SetPosition(2, new Vector3(b.max.x, b.max.y, z));
                line.SetPosition(3, new Vector3(b.min.x, b.max.y, z));
                var colour = trunk ? TrunkColour : FootprintColour;
                line.startColor = colour;
                line.endColor = colour;
                line.enabled = true;
            }
            for (int i = used; i < _lines.Count; i++) _lines[i].enabled = false;
            Outlined = used;
        }

        private LineRenderer Line(int index)
        {
            while (_lines.Count <= index)
            {
                var go = new GameObject("TrunkBox");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.positionCount = 4;
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.numCornerVertices = 0;
                if (_material == null) _material = new Material(Shader.Find("Sprites/Default"));
                lr.sharedMaterial = _material;
                lr.sortingLayerName = SortingConfig.LAYER_VFX;
                lr.sortingOrder = 60;
                _lines.Add(lr);
            }
            return _lines[index];
        }
    }
}
