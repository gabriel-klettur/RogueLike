using UnityEngine;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// How much of the node is left, drawn over it while it is being worked.
    ///
    /// <para>It answers ONE question — "how much longer" — and it is the only continuous
    /// element in the activity on purpose. Everything else a shift produces is a beat
    /// (<see cref="HarvestFeedback"/>), and a beat cannot express a total; a bar can express
    /// nothing else. Splitting them that way is what stops the bar being the whole
    /// experience.</para>
    ///
    /// <para>It is shown only while a session runs, and for a short beat afterwards. A bar
    /// hanging permanently over every tree in a forest would turn the world into a UI, and a
    /// full bar tells the player nothing they cannot see from the un-chipped rock.</para>
    ///
    /// <para>Built from <see cref="WorldHealthBar"/>'s shared pixel sprite and material, the
    /// same way <c>WorldDashBar</c> is, so the world-space bars stay one family and one
    /// material.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestNodeBar : MonoBehaviour
    {
        private const float BAR_WIDTH = 1.10f;
        private const float BAR_HEIGHT = 0.11f;
        private const float BORDER = 0.02f;

        /// <summary>Gap between the top of the node and the bottom of the bar.</summary>
        private const float VERTICAL_CLEARANCE = 0.16f;

        /// <summary>How long the bar lingers after the last blow, so a finished node reads.</summary>
        private const float LINGER_SECONDS = 1.2f;

        private const string SORTING_LAYER = "UI_World";
        private const int SORT_BORDER = 210;
        private const int SORT_BG = 211;
        private const int SORT_FILL = 212;

        private static readonly Color BorderColor = new Color(0f, 0f, 0f, 0.9f);
        private static readonly Color BackColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
        private static readonly Color FillFull = new Color(0.98f, 0.78f, 0.30f, 1f);
        private static readonly Color FillLow = new Color(0.88f, 0.36f, 0.24f, 1f);

        private IWorkProgress _work;
        private Transform _root;
        private SpriteRenderer _fill;
        private float _hideAt;

        /// <summary>Whether the bar is currently drawn. A test seam.</summary>
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        /// <summary>
        /// Attach (or find) the bar for one piece of work. Idempotent.
        ///
        /// <para>Takes the interface rather than a <see cref="HarvestNode"/> so a fishing cast
        /// or anything else with a visible amount left gets the same rig instead of a copy of
        /// it. <paramref name="host"/> is only where the component lives; the bar reads
        /// nothing from it.</para>
        /// </summary>
        public static HarvestNodeBar Attach(IWorkProgress work, GameObject host)
        {
            if (work == null || host == null) return null;

            var existing = host.GetComponent<HarvestNodeBar>();
            if (existing != null) return existing;

            var bar = host.AddComponent<HarvestNodeBar>();
            bar._work = work;
            bar.Build();
            return bar;
        }

        /// <summary>Convenience for a harvest node, which is both the work and the host.</summary>
        public static HarvestNodeBar Attach(HarvestNode node) =>
            node == null ? null : Attach(node, node.gameObject);

        private void Build()
        {
            _root = new GameObject("HarvestBar").transform;
            _root.SetParent(transform, worldPositionStays: false);

            // The node is a BUILDING: authored at PPU 32 and freely resized per instance, so
            // a bar inheriting that scale would be twice the size over a big rock as over a
            // small one. Neutralising the parent scale here keeps every bar in the world the
            // same size, which is what makes them comparable at a glance.
            var lossy = transform.lossyScale;
            _root.localScale = new Vector3(
                lossy.x != 0f ? 1f / lossy.x : 1f,
                lossy.y != 0f ? 1f / lossy.y : 1f,
                1f);

            MakePart("Border", new Vector3(BAR_WIDTH + BORDER * 2f, BAR_HEIGHT + BORDER * 2f, 1f),
                Vector3.zero, BorderColor, SORT_BORDER);
            MakePart("Back", new Vector3(BAR_WIDTH, BAR_HEIGHT, 1f),
                Vector3.zero, BackColor, SORT_BG);
            _fill = MakePart("Fill", new Vector3(BAR_WIDTH, BAR_HEIGHT, 1f),
                Vector3.zero, FillFull, SORT_FILL);

            _root.gameObject.SetActive(false);
        }

        private SpriteRenderer MakePart(string name, Vector3 scale, Vector3 localPosition,
            Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldHealthBar.GetSharedPixelSprite();
            sr.sharedMaterial = WorldHealthBar.GetSharedSpriteMaterial();
            sr.color = color;
            sr.sortingLayerName = SORTING_LAYER;
            sr.sortingOrder = order;
            return sr;
        }

        private void LateUpdate()
        {
            if (_work == null || _root == null) return;

            bool working = _work.IsWorking;
            if (working) _hideAt = Time.time + LINGER_SECONDS;

            bool visible = working || Time.time < _hideAt;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            Layout();
        }

        private void Layout()
        {
            Vector2 anchor = _work.ProgressAnchor;
            _root.position = new Vector3(anchor.x, anchor.y + VERTICAL_CLEARANCE, 0f);

            float fraction = Mathf.Clamp01(_work.Progress01);

            // Anchored on the LEFT edge rather than scaled about the centre: a bar that
            // shrinks from both ends reads as the whole thing being consumed rather than as
            // progress running one way, and the two are indistinguishable at a glance.
            _fill.transform.localScale = new Vector3(BAR_WIDTH * fraction, BAR_HEIGHT, 1f);
            _fill.transform.localPosition = new Vector3(-BAR_WIDTH * (1f - fraction) * 0.5f, 0f, 0f);
            _fill.color = Color.Lerp(FillLow, FillFull, fraction);
        }
    }
}
