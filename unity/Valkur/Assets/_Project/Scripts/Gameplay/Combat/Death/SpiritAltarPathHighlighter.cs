using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// While the player is in spirit form, draws a yellow tile-outline trail
    /// along the straight line from the spirit to the nearest
    /// <see cref="ResurrectionZone"/> altar so the player has a visible compass
    /// pointing at the revive point.
    ///
    /// Deliberately uses straight-line tile rasterization (no pathfinding):
    /// the path is meant to read as a magical compass, not as a walkable
    /// route. It cuts through walls and zone boundaries — its only failure
    /// mode is "no altar in the loaded scene", which we log once on entry.
    ///
    /// Markers are pooled — we recycle the same set of SpriteRenderers each
    /// recompute instead of spawning/destroying GameObjects every tick.
    /// </summary>
    public class SpiritAltarPathHighlighter : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds between path recomputes while in spirit form.")]
        private float updateInterval = 0.25f;

        [SerializeField, Tooltip("Yellow tint applied to the tile outlines.")]
        private Color tileTint = new Color(1f, 0.95f, 0.2f, 0.9f);

        [SerializeField, Tooltip("World-size of each tile marker (default 1 = grid cell).")]
        private float tileWorldSize = 1f;

        [SerializeField, Tooltip("Pulse the brightness so the path reads as 'magical' rather than static decal.")]
        private bool animatePulse = true;

        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float pulseAmplitude = 0.25f;

        [SerializeField, Tooltip("Log a one-shot diagnostic when entering spirit form (altar found / distance).")]
        private bool debugLogs = true;

        private const float TileGridSize = 1f;
        private const float LineSampleStep = 0.25f;
        private const int OutlineTextureSize = 16;
        private const int OutlineThicknessPx = 2;
        private const float TileFillAlpha = 0.35f;

        private readonly List<SpriteRenderer> _markers = new List<SpriteRenderer>();
        private readonly List<Vector2Int> _lineCells = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> _lineCellsSet = new HashSet<Vector2Int>();

        private Transform _markerRoot;
        private Sprite _outlineSprite;
        private Material _markerMaterial;
        private PlayerSpiritState _spiritState;
        private Transform _spiritTransform;
        private float _timer;
        private bool _wasSpirit;

        /// <summary>
        /// Parent of the pooled tile markers. Exposed so SpiritWorldGrayscale can
        /// exempt them from the per-sprite desaturation that runs while the
        /// player is in spirit form (the path is supposed to stay yellow even
        /// when the rest of the world drops to monochrome).
        ///
        /// Lazily creates the GameObject on first access so EditMode tests (and
        /// any code path that runs before Awake completes) can rely on a
        /// non-null root without forcing a full Awake cycle.
        /// </summary>
        public Transform MarkerRoot
        {
            get
            {
                if (_markerRoot == null) EnsureMarkerRoot();
                return _markerRoot;
            }
        }

        private void EnsureMarkerRoot()
        {
            if (_markerRoot != null) return;
            var go = new GameObject("SpiritPathMarkers");
            _markerRoot = go.transform;
            _markerRoot.SetParent(transform, false);
        }

        private void Awake()
        {
            ServiceLocator.Register<SpiritAltarPathHighlighter>(this);
            EnsureMarkerRoot();
            _outlineSprite = CreateTileOutlineSprite();
            _markerMaterial = CreateUnlitMaterial();
        }

        private void OnDestroy()
        {
            if (ServiceLocator.Get<SpiritAltarPathHighlighter>() == this)
                ServiceLocator.Unregister<SpiritAltarPathHighlighter>();
        }

        private void Update()
        {
            ResolveSpiritReferences();

            bool isSpiritNow = _spiritState != null && _spiritState.IsSpirit;
            if (isSpiritNow != _wasSpirit)
            {
                _wasSpirit = isSpiritNow;
                if (!isSpiritNow) HideAllMarkers();
                else
                {
                    _timer = updateInterval; // force an immediate refresh
                    if (debugLogs) LogSpiritEntry();
                }
            }
            if (!isSpiritNow) return;

            _timer += Time.unscaledDeltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                RebuildPath();
            }

            if (animatePulse) PulseMarkers();
        }

        private void ResolveSpiritReferences()
        {
            // EntityRegistry can swap players (death/restart cycle), so we resolve
            // every Update — it's a single dictionary lookup.
            var player = EntityRegistry.Player;
            if (player == null)
            {
                _spiritState = null;
                _spiritTransform = null;
                return;
            }
            if (_spiritTransform != player.transform)
            {
                _spiritState = player.GetComponent<PlayerSpiritState>();
                _spiritTransform = player.transform;
            }
        }

        private void RebuildPath()
        {
            if (_spiritTransform == null) { HideAllMarkers(); return; }

            ResurrectionZone altar = FindNearestAltar(_spiritTransform.position);
            if (altar == null) { HideAllMarkers(); return; }

            Vector2 start = _spiritTransform.position;
            Vector2 end = ResolveAltarPoint(altar);

            BuildLineCells(start, end);
            if (_lineCells.Count == 0) { HideAllMarkers(); return; }

            EnsureMarkerCount(_lineCells.Count);
            for (int i = 0; i < _lineCells.Count; i++)
            {
                var cell = _lineCells[i];
                var marker = _markers[i];
                marker.gameObject.SetActive(true);
                marker.transform.position = new Vector3(
                    cell.x * TileGridSize + TileGridSize * 0.5f,
                    cell.y * TileGridSize + TileGridSize * 0.5f,
                    0f);
                marker.color = tileTint;
            }
            for (int i = _lineCells.Count; i < _markers.Count; i++)
            {
                if (_markers[i] != null) _markers[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Resolve the altar's anchor point — prefer the BuildingObject's rect
        /// center (more accurate visual center) and fall back to the transform.
        /// </summary>
        private static Vector2 ResolveAltarPoint(ResurrectionZone altar)
        {
            var building = altar.GetComponent<BuildingObject>();
            if (building != null && building.TryGetWorldRect(out Rect rect))
                return rect.center;
            return altar.transform.position;
        }

        /// <summary>
        /// Walk a straight segment from <paramref name="start"/> to
        /// <paramref name="end"/> and collect every grid cell the segment
        /// touches, in order, with no duplicates. Step size is fine enough
        /// (<see cref="LineSampleStep"/>) that we never skip a cell on a
        /// shallow diagonal.
        /// </summary>
        private void BuildLineCells(Vector2 start, Vector2 end)
        {
            _lineCells.Clear();
            _lineCellsSet.Clear();

            Vector2 delta = end - start;
            float dist = delta.magnitude;
            if (dist < 0.01f) return;

            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / LineSampleStep));
            Vector2 stepVec = delta / steps;
            Vector2 p = start;
            for (int i = 0; i <= steps; i++)
            {
                var cell = new Vector2Int(
                    Mathf.FloorToInt(p.x / TileGridSize),
                    Mathf.FloorToInt(p.y / TileGridSize));
                if (_lineCellsSet.Add(cell)) _lineCells.Add(cell);
                p += stepVec;
            }
        }

        private static ResurrectionZone FindNearestAltar(Vector3 from)
        {
            var zones = FindObjectsOfType<ResurrectionZone>();
            ResurrectionZone best = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null) continue;
                float d = (zones[i].transform.position - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = zones[i]; }
            }
            return best;
        }

        private void PulseMarkers()
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmplitude;
            Color c = tileTint;
            c.r = Mathf.Clamp01(c.r * pulse);
            c.g = Mathf.Clamp01(c.g * pulse);
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                if (m != null && m.gameObject.activeSelf) m.color = c;
            }
        }

        private void EnsureMarkerCount(int needed)
        {
            while (_markers.Count < needed)
            {
                _markers.Add(CreateMarker());
            }
        }

        private SpriteRenderer CreateMarker()
        {
            var go = new GameObject("PathTile");
            go.transform.SetParent(_markerRoot, false);
            go.transform.localScale = new Vector3(tileWorldSize, tileWorldSize, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _outlineSprite;
            sr.color = tileTint;
            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 50;
            sr.sharedMaterial = _markerMaterial;
            return sr;
        }

        private void HideAllMarkers()
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                if (_markers[i] != null) _markers[i].gameObject.SetActive(false);
            }
        }

        private void LogSpiritEntry()
        {
            if (_spiritTransform == null) return;
            var altar = FindNearestAltar(_spiritTransform.position);
            if (altar == null)
            {
                Debug.LogWarning("[SpiritAltarPathHighlighter] No ResurrectionZone in the loaded scene — path will not be drawn.");
                return;
            }
            float d = Vector3.Distance(_spiritTransform.position, altar.transform.position);
            Debug.Log($"[SpiritAltarPathHighlighter] Spirit entered. Nearest altar at {altar.transform.position} ({d:F1} units away).");
        }

        /// <summary>
        /// Procedural sprite: a translucent yellow fill with a fully opaque
        /// 2-px border. Tinted via SpriteRenderer.color so each cell renders
        /// as a filled yellow tile with a brighter outline — the border reads
        /// as the breadcrumb edge while the fill keeps the floor faintly
        /// visible underneath.
        /// </summary>
        private static Sprite CreateTileOutlineSprite()
        {
            const int size = OutlineTextureSize;
            const int t = OutlineThicknessPx;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = "SpiritPathTileOutlineTex",
            };
            var fill = new Color(1f, 1f, 1f, TileFillAlpha);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool border = x < t || y < t || x >= size - t || y >= size - t;
                    pixels[y * size + x] = border ? Color.white : fill;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit: size);
            sprite.name = "SpiritPathTileOutlineSprite";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        private static Material CreateUnlitMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                  ?? Shader.Find("Sprites/Default");
            return new Material(sh) { name = "SpiritPathTileMaterial", hideFlags = HideFlags.DontSave };
        }
    }
}
