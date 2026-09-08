using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Combat.Death
{
    /// <summary>
    /// Draws the trails a dead player needs: one to the altar that will revive them, and one back
    /// to the body holding everything they were carrying.
    ///
    /// <para><b>The second trail is not decoration.</b> Death drops the entire inventory and the
    /// entire purse at the death position and then walks the player away from it — measured live
    /// during the audit, 64 units away — with nothing on screen pointing back. A player who
    /// revives at an altar has no way to find their own loot except by remembering the geography
    /// of a fight they just lost. The two trails are different colours because they are different
    /// destinations, and one colour would make them one thing.</para>
    ///
    /// <para><b>The altar trail's shape is a setting, and the two options are honest in different
    /// worlds.</b> <see cref="SpiritPathMode.StraightLine"/> is a magical compass that cuts through
    /// walls — correct exactly when the spirit can too, which is what
    /// <c>DeathTuning.spiritPassesThroughWalls</c> says. <see cref="SpiritPathMode.Pathfound"/>
    /// routes through <c>PathFinder</c> and is the honest answer for a solid spirit. Shipping the
    /// straight line while the ghost bounced off walls is what made the original a compass
    /// pointing down a route that did not exist.</para>
    ///
    /// <para>Markers are pooled — the same SpriteRenderers are recycled on every recompute instead
    /// of spawning and destroying GameObjects per tick.</para>
    /// </summary>
    public class SpiritAltarPathHighlighter : MonoBehaviour
    {
        private const float TileGridSize = 1f;
        private const float LineSampleStep = 0.25f;
        private const int OutlineTextureSize = 16;
        private const int OutlineThicknessPx = 2;
        private const float TileFillAlpha = 0.35f;
        private const float PulseSpeed = 3f;
        private const float PulseAmplitude = 0.25f;

        private readonly List<SpriteRenderer> _markers = new List<SpriteRenderer>();
        private readonly List<Vector2Int> _cells = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> _cellSet = new HashSet<Vector2Int>();
        private readonly List<Vector2> _waypoints = new List<Vector2>();

        /// <summary>Per-marker colour, so the altar trail and the corpse trail can share one pool.</summary>
        private readonly List<Color> _markerTints = new List<Color>();

        private Transform _markerRoot;
        private Sprite _outlineSprite;
        private Material _markerMaterial;
        private PlayerSpiritState _spiritState;
        private Transform _spiritTransform;
        private DeathSequenceController _death;
        private float _timer;
        private bool _wasSpirit;
        private int _activeMarkers;

        /// <summary>Cells the last rebuild drew for the altar. Exposed for the console probe and tests.</summary>
        public int AltarTrailLength { get; private set; }

        /// <summary>Cells the last rebuild drew for the corpse.</summary>
        public int CorpseTrailLength { get; private set; }

        /// <summary>Why the altar trail is empty, or empty string when it is not.</summary>
        public string AltarTrailBlockedReason { get; private set; } = string.Empty;

        private static DeathTuning Tuning => DeathTuning.Active;

        /// <summary>
        /// Parent of the pooled tile markers. Exposed so <see cref="SpiritWorldGrayscale"/> can
        /// exempt them from the per-sprite desaturation — the trails are supposed to stay coloured
        /// when the rest of the world drops to monochrome, which is most of how they read.
        ///
        /// <para>Lazily created so EditMode tests, and any path that runs before Awake completes,
        /// can rely on a non-null root without forcing a full Awake cycle.</para>
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
            ResolveReferences();

            bool isSpiritNow = _spiritState != null && _spiritState.IsSpirit;
            if (isSpiritNow != _wasSpirit)
            {
                _wasSpirit = isSpiritNow;
                // Force an immediate refresh on the transition in BOTH directions: entering, so
                // the trail is up before the player's first step; leaving, so a stale altar trail
                // is not left painted on a world that has just come back to colour.
                _timer = float.MaxValue;
            }

            // The corpse trail outlives the spirit — that is exactly the walk it exists for — so
            // the tick cannot stop at "is the player a spirit".
            if (!isSpiritNow && !CorpseTrailWanted()) { HideAllMarkers(); return; }

            _timer += Time.unscaledDeltaTime;
            if (_timer >= Mathf.Max(0.05f, Tuning.pathUpdateInterval))
            {
                _timer = 0f;
                Rebuild(isSpiritNow);
            }

            PulseMarkers();
        }

        private void ResolveReferences()
        {
            // EntityRegistry can swap players across a death/restart cycle, so this resolves every
            // Update — a single dictionary lookup.
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
            if (_death == null) _death = ServiceLocator.Get<DeathSequenceController>();
        }

        private bool CorpseTrailWanted()
        {
            if (!Tuning.showCorpseCompass) return false;
            return _death != null && _death.ActiveCorpse != null;
        }

        // ── Rebuild ─────────────────────────────────────────────────────────────

        private void Rebuild(bool isSpirit)
        {
            _activeMarkers = 0;
            AltarTrailLength = 0;
            CorpseTrailLength = 0;
            AltarTrailBlockedReason = string.Empty;

            if (_spiritTransform == null) { HideUnusedMarkers(); return; }
            Vector2 start = _spiritTransform.position;

            if (isSpirit) AltarTrailLength = BuildAltarTrail(start);
            if (CorpseTrailWanted()) CorpseTrailLength = BuildCorpseTrail(start);

            HideUnusedMarkers();
        }

        private int BuildAltarTrail(Vector2 start)
        {
            var tuning = Tuning;
            if (tuning.pathMode == SpiritPathMode.None)
            {
                AltarTrailBlockedReason = "el camino esta desactivado en DeathTuning.pathMode";
                return 0;
            }

            if (!ResurrectionAltarRegistry.TryGetNearest(start, out var altar, out _))
            {
                AltarTrailBlockedReason = "no hay ningun altar registrado en el mundo cargado";
                return 0;
            }

            Vector2 end = altar.AnchorPoint;

            if (tuning.pathMode == SpiritPathMode.Pathfound && TryBuildRoutedCells(start, end))
                return EmitCells(tuning.pathTint, tuning.pathMaxMarkers);

            BuildLineCells(start, end);
            return EmitCells(tuning.pathTint, tuning.pathMaxMarkers);
        }

        private int BuildCorpseTrail(Vector2 start)
        {
            var corpse = _death.ActiveCorpse;
            if (corpse == null) return 0;

            BuildLineCells(start, corpse.transform.position);

            // Deliberately always a straight line, whatever pathMode says, and deliberately
            // shorter: the corpse trail answers "which way is my stuff", not "how do I walk
            // there". A routed corpse trail beside a routed altar trail would put two dense
            // ribbons of tiles over the same floor and neither would be readable.
            return EmitCells(Tuning.corpseTint, Mathf.Max(8, Tuning.pathMaxMarkers / 2));
        }

        /// <summary>
        /// Fill <see cref="_cells"/> from a real walkable route. Returns false when the router
        /// could not answer, which is NOT the same as "no route exists".
        ///
        /// <para>A refusal is <c>PathFinder</c> saying "not this frame" — its per-frame search
        /// budget — and the caller must fall back to the straight line rather than draw nothing,
        /// or the trail would blink out whenever the fight around the player is busy. A search
        /// that ran and found nothing returns true with an empty list, and that IS a real answer:
        /// there is no walkable route, so the straight-line compass is the best remaining one.</para>
        /// </summary>
        private bool TryBuildRoutedCells(Vector2 start, Vector2 end)
        {
            var finder = PathFinder.Instance;
            if (finder == null) return false;
            if (!finder.TryFindPath(start, end, _waypoints)) return false;
            if (_waypoints.Count == 0) return false;

            _cells.Clear();
            _cellSet.Clear();

            Vector2 cursor = start;
            for (int i = 0; i < _waypoints.Count; i++)
            {
                AppendSegmentCells(cursor, _waypoints[i]);
                cursor = _waypoints[i];
            }
            return _cells.Count > 0;
        }

        /// <summary>
        /// Walk a straight segment and collect every grid cell it touches, in order, with no
        /// duplicates. The step is fine enough that a shallow diagonal never skips a cell.
        /// </summary>
        private void BuildLineCells(Vector2 start, Vector2 end)
        {
            _cells.Clear();
            _cellSet.Clear();
            AppendSegmentCells(start, end);
        }

        private void AppendSegmentCells(Vector2 start, Vector2 end)
        {
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
                if (_cellSet.Add(cell)) _cells.Add(cell);
                p += stepVec;
            }
        }

        /// <summary>
        /// Realise the cells collected so far as markers, and return how many were drawn.
        ///
        /// <para><b>The cap trims the FAR end.</b> An altar 300 units away would otherwise paint
        /// 300 sprites, and the ones that matter are the ones under the player's feet — the far
        /// half of a trail is a direction the player will re-read when they get there. Trimming
        /// the near end instead would leave a floating ribbon starting somewhere out in the fog,
        /// which reads as a different trail belonging to something else.</para>
        /// </summary>
        private int EmitCells(Color tint, int cap)
        {
            int count = Mathf.Min(_cells.Count, Mathf.Max(1, cap));
            EnsureMarkerCount(_activeMarkers + count);

            for (int i = 0; i < count; i++)
            {
                var cell = _cells[i];
                var marker = _markers[_activeMarkers];
                marker.gameObject.SetActive(true);
                marker.transform.position = new Vector3(
                    cell.x * TileGridSize + TileGridSize * 0.5f,
                    cell.y * TileGridSize + TileGridSize * 0.5f,
                    0f);
                marker.color = tint;
                _markerTints[_activeMarkers] = tint;
                _activeMarkers++;
            }
            return count;
        }

        private void HideUnusedMarkers()
        {
            for (int i = _activeMarkers; i < _markers.Count; i++)
                if (_markers[i] != null) _markers[i].gameObject.SetActive(false);
        }

        private void PulseMarkers()
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * PulseSpeed) * PulseAmplitude;
            for (int i = 0; i < _activeMarkers && i < _markers.Count; i++)
            {
                var m = _markers[i];
                if (m == null || !m.gameObject.activeSelf) continue;

                // Pulsed from the marker's OWN stored tint, never from the live colour: reading
                // back what the last pulse wrote compounds the multiplier every frame and the
                // trail ramps to white in about a second. The old single-trail version got away
                // with it by rebuilding from a constant.
                Color c = _markerTints[i];
                c.r = Mathf.Clamp01(c.r * pulse);
                c.g = Mathf.Clamp01(c.g * pulse);
                c.b = Mathf.Clamp01(c.b * pulse);
                m.color = c;
            }
        }

        private void EnsureMarkerCount(int needed)
        {
            while (_markers.Count < needed)
            {
                _markers.Add(CreateMarker());
                _markerTints.Add(Color.white);
            }
        }

        private SpriteRenderer CreateMarker()
        {
            var go = new GameObject("PathTile");
            go.transform.SetParent(MarkerRoot, false);
            go.transform.localScale = Vector3.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _outlineSprite;
            sr.sortingLayerName = SortingConfig.LAYER_FLOOR_DECALS;
            sr.sortingOrder = 50;
            sr.sharedMaterial = _markerMaterial;
            return sr;
        }

        /// <summary>
        /// Hide every marker AND zero the reported lengths.
        ///
        /// <para>The second half is not tidiness. Those two counters are what the Death Editor's
        /// live readout and the <c>death</c> console command print, and the early-out path used to
        /// skip <see cref="Rebuild"/> entirely — so after a revive the readout went on reporting a
        /// 70-tile corpse trail with nothing drawn. A diagnostic that survives the thing it
        /// describes is worse than none, which is the whole reason this subsystem was hard to
        /// diagnose in the first place.</para>
        /// </summary>
        private void HideAllMarkers()
        {
            _activeMarkers = 0;
            AltarTrailLength = 0;
            CorpseTrailLength = 0;
            for (int i = 0; i < _markers.Count; i++)
                if (_markers[i] != null) _markers[i].gameObject.SetActive(false);
        }

        /// <summary>
        /// Procedural sprite: a translucent fill with a fully opaque 2-px border, tinted per
        /// marker. The border reads as the breadcrumb edge while the fill keeps the floor faintly
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
