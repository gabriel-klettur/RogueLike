using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The work bar over a tree being felled: how far the job has come, where each log will come
    /// out, how long is left, and where the next blow is in its swing.
    ///
    /// <para><b>ONE LOOK WITH THE REST OF THE WORLD'S BARS.</b> It is drawn by the same
    /// <see cref="WorldBarLine"/> every creature's health bar is — outline, plate, metal caps, a
    /// four-tone fill on the texel grid — in the woodcutter's gold. The old bar was three scaled
    /// white quads, the look the health bars were rebuilt out of.</para>
    ///
    /// <para><b>IT FILLS, AND THE NOTCHES ARE THE LOGS.</b> Progress runs toward the tree coming
    /// down, and a notch sits at every point a log is paid (<see cref="IWorkSegments"/>). When the
    /// fill crosses one the notch lights up in the same frame the log leaves the trunk, so the bar
    /// is not a wait to endure but a row of rewards to reach — the next one visibly two blows away.
    /// </para>
    ///
    /// <para><b>THE SWING IS ON THE BAR TOO.</b> A thin line under the plate sweeps across once per
    /// blow and snaps back when the axe lands. It is the only thing that tells a player standing
    /// still that the work is running, and it makes the rhythm of the activity something to watch
    /// rather than something to infer from the animation.</para>
    ///
    /// <para><b>THE COUNTDOWN IS HONEST.</b> The seconds label comes from the node, which resolves
    /// the next blow through the same resolver the blow uses — it shortens the moment an axe is
    /// equipped or the skill ticks up, instead of promising a time the tool cannot keep.</para>
    ///
    /// <para><b>A FINISH IS AN EVENT.</b> When the bar completes it flashes, pops and fades; it does
    /// not simply stop being drawn.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestNodeBar : MonoBehaviour
    {
        private const int WIDTH_TEXELS = 30;
        private const int ROW_TEXELS = 6;
        private const float VERTICAL_CLEARANCE_TEXELS = 4f;

        /// <summary>How long the bar stays after the last blow, so a finished tree reads.</summary>
        private const float LINGER_SECONDS = 1.1f;
        private const float FADE_SECONDS = 0.25f;
        private const float NOTCH_FLASH_SECONDS = 0.35f;
        private const float COMPLETE_POP_SECONDS = 0.35f;

        private static readonly Color Gold = new Color(0.98f, 0.74f, 0.28f, 1f);
        private static readonly Color GoldLow = new Color(0.86f, 0.52f, 0.20f, 1f);
        private static readonly Color NotchIdle = new Color(0.02f, 0.03f, 0.05f, 0.7f);
        private static readonly Color NotchPassed = new Color(0.35f, 0.22f, 0.08f, 0.9f);
        private static readonly Color NotchLit = new Color(1f, 0.97f, 0.80f, 1f);
        private static readonly Color CadenceColour = new Color(1f, 0.92f, 0.70f, 0.85f);

        private IWorkProgress _work;
        private IWorkSegments _segments;
        private Transform _root;
        private WorldBarLine _line;
        private SpriteRenderer _cadence;

        /// <summary>The cut target's bands at the end of the sweep, outermost (Awful) first.</summary>
        private readonly SpriteRenderer[] _bands = new SpriteRenderer[5];
        private SpriteRenderer _beatTick;
        private TextMeshPro _eta;
        private readonly List<SpriteRenderer> _notches = new List<SpriteRenderer>();
        private readonly List<float> _marks = new List<float>();
        private readonly List<float> _notchFlash = new List<float>();
        private readonly List<bool> _notchPassed = new List<bool>();

        private float _hideAt;
        private float _alpha;
        private float _lastProgress = -1f;
        private float _lastCadence;
        private float _completeLeft;
        private bool _completed;
        private int _sortingBase = int.MinValue;

        /// <summary>Whether the bar is drawn at all. A test seam.</summary>
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        /// <summary>The fill the bar is heading toward, 0..1. A test seam.</summary>
        public float Progress => _line != null ? _line.Target : 0f;

        /// <summary>How many log notches are drawn. A test seam.</summary>
        public int NotchCount { get; private set; }

        /// <summary>The countdown text currently shown. A test seam.</summary>
        public string EtaText => _eta != null ? _eta.text : string.Empty;

        /// <summary>
        /// Attach (or find) the bar for one piece of work. Takes the interface rather than a
        /// <see cref="HarvestNode"/> so anything with a visible amount of work gets the same rig.
        /// </summary>
        public static HarvestNodeBar Attach(IWorkProgress work, GameObject host)
        {
            if (work == null || host == null) return null;

            var existing = host.GetComponent<HarvestNodeBar>();
            if (existing != null) return existing;

            var bar = host.AddComponent<HarvestNodeBar>();
            bar._work = work;
            bar._segments = work as IWorkSegments;
            bar.Build();
            return bar;
        }

        /// <summary>Convenience for a harvest node, which is both the work and the host.</summary>
        public static HarvestNodeBar Attach(HarvestNode node) =>
            node == null ? null : Attach(node, node.gameObject);

        private void Build()
        {
            _root = new GameObject("WorkBar").transform;
            _root.SetParent(transform, worldPositionStays: false);

            _line = new WorldBarLine(_root, "Progress", WorldBarRow.Health, ROW_TEXELS, 0, withNotches: false);
            _line.Layout(WorldBarGeometry.Texels(WIDTH_TEXELS), 0f, 0f, notchesWanted: false);

            var style = WorldBarStyle.Active;
            _line.SetColours(Gold, GoldLow, Color.white, style.outline, style.plate, style.notch, 0f);
            _line.SetRankColours(style.capPlayer, Color.clear, style.lowPulse);
            _line.SetRatio(0f, instant: true, leaveChip: false, style);

            _cadence = MakeSolid("Cadence", CadenceColour);
            for (int i = 0; i < _bands.Length; i++) _bands[i] = MakeSolid("CutBand" + i, Color.clear);
            _beatTick = MakeSolid("BeatTick", Color.white);
            BuildEta();
            RebuildNotches();

            _root.gameObject.SetActive(false);
        }

        private SpriteRenderer MakeSolid(string name, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldBarArt.Solid;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.sharedMaterial = WorldBarArt.Material;
            sr.sortingLayerName = SortingConfig.LAYER_UI_WORLD;
            sr.color = colour;
            return sr;
        }

        private void BuildEta()
        {
            var go = new GameObject("Eta");
            go.transform.SetParent(_root, false);
            _eta = go.AddComponent<TextMeshPro>();
            _eta.fontSize = 2.2f;
            _eta.alignment = TextAlignmentOptions.Left;
            _eta.enableWordWrapping = false;
            _eta.color = new Color(1f, 0.95f, 0.82f, 1f);
            _eta.fontStyle = FontStyles.Bold;
            _eta.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_UI_WORLD);
            _eta.rectTransform.sizeDelta = new Vector2(1.2f, 0.4f);
            _eta.rectTransform.pivot = new Vector2(0f, 0.5f);
            go.transform.localPosition = new Vector3(
                WorldBarGeometry.Texels(WIDTH_TEXELS) * 0.5f + WorldBarGeometry.Texels(2), 0f, 0f);
            _eta.text = string.Empty;
        }

        private void RebuildNotches()
        {
            _marks.Clear();
            if (_segments != null) _segments.GetSegmentMarks(_marks);

            while (_notches.Count < _marks.Count) _notches.Add(MakeSolid("Notch" + _notches.Count, NotchIdle));
            for (int i = 0; i < _notches.Count; i++) _notches[i].gameObject.SetActive(i < _marks.Count);

            _notchFlash.Clear();
            _notchPassed.Clear();
            float progress = _work != null ? Mathf.Clamp01(_work.Progress01) : 0f;
            for (int i = 0; i < _marks.Count; i++)
            {
                _notchFlash.Add(0f);
                _notchPassed.Add(progress >= _marks[i] - 1e-4f);
            }

            NotchCount = _marks.Count;
            LayoutNotches();
        }

        private void LayoutNotches()
        {
            float t = WorldBarGeometry.TEXEL;
            float inner = _line.InnerHeight;
            for (int i = 0; i < _marks.Count; i++)
            {
                var n = _notches[i];
                n.size = new Vector2(t, inner);
                float x = WorldBarGeometry.SnapToTexel(_line.XAtRatio(_marks[i]));
                n.transform.localPosition = new Vector3(x, 0f, 0f);
            }

            _cadence.size = new Vector2(t, t);
            _cadence.transform.localPosition =
                new Vector3(_line.FillLeftX, -WorldBarGeometry.Texels(ROW_TEXELS) * 0.5f - t, 0f);
        }

        private void LateUpdate()
        {
            if (_work == null || _root == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>
        /// One frame of the bar. Public so an Edit Mode fixture — which gets no LateUpdate — can
        /// drive it and read back what it would draw.
        /// </summary>
        public void Step(float dt)
        {
            float progress = Mathf.Clamp01(_work.Progress01);
            bool working = _work.IsWorking;
            if (working) _hideAt = Time.time + LINGER_SECONDS;

            bool wanted = working || Time.time < _hideAt || _completeLeft > 0f;
            _alpha = Mathf.MoveTowards(_alpha, wanted ? 1f : 0f, dt / FADE_SECONDS);
            if (wanted && _alpha <= 0f) _alpha = Mathf.Min(1f, dt / FADE_SECONDS + 0.01f);

            bool visible = _alpha > 0.001f;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible)
            {
                // A new job on the same node (a regrown tree) starts from an empty bar.
                if (progress < 0.001f) { _completed = false; _lastProgress = -1f; }
                return;
            }

            Place();
            Advance(progress);
            Apply(dt);
        }

        /// <summary>
        /// Sit over the work, texel-snapped, at a neutral scale. Recomputed every visible frame:
        /// the host's scale is not constant (a felled tree drops to the stump's scale, a regrow
        /// animates it), and a compensation computed once at build is wrong for both.
        /// </summary>
        private void Place()
        {
            Vector2 anchor = _work.ProgressAnchor;
            float y = anchor.y - WorldBarGeometry.Texels(VERTICAL_CLEARANCE_TEXELS + ROW_TEXELS * 0.5f);
            _root.position = new Vector3(WorldBarGeometry.SnapToTexel(anchor.x), WorldBarGeometry.SnapToTexel(y), 0f);

            var lossy = transform.lossyScale;
            float pop = _completeLeft > 0f
                ? 1f + Mathf.Sin(_completeLeft / COMPLETE_POP_SECONDS * Mathf.PI) * 0.12f
                : 1f;
            _root.localScale = new Vector3(
                (lossy.x != 0f ? 1f / lossy.x : 1f) * pop,
                (lossy.y != 0f ? 1f / lossy.y : 1f) * pop,
                1f);

            int order = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, _root.position.y);
            if (order == _sortingBase) return;
            _sortingBase = order;
            _line.SetSortingBase(order);
            _cadence.sortingOrder = order + WorldBarLine.SLOT_COUNT;
            // Nested bands: the outermost draws first and each narrower one over it, centre on top.
            for (int i = 0; i < _bands.Length; i++) _bands[i].sortingOrder = order + WorldBarLine.SLOT_COUNT + 1 + i;
            _beatTick.sortingOrder = order + WorldBarLine.SLOT_COUNT + 1 + _bands.Length;
            for (int i = 0; i < _notches.Count; i++) _notches[i].sortingOrder = order + WorldBarLine.SLOT_COUNT;
            _eta.sortingOrder = order + WorldBarLine.SLOT_COUNT + 1;
        }

        private void Advance(float progress)
        {
            var style = WorldBarStyle.Active;

            if (_lastProgress < 0f || progress < _lastProgress - 0.01f)
            {
                // First frame, or the job restarted (a regrown tree): rebuild the notches.
                RebuildNotches();
                _line.SetRatio(progress, instant: true, leaveChip: false, style);
                _completed = progress >= 0.999f;
            }
            else if (!Mathf.Approximately(progress, _lastProgress))
            {
                _line.SetRatio(progress, instant: false, leaveChip: false, style);
                _line.Flash(0.08f);
            }

            // Light every notch the fill has just crossed: that is the log leaving the trunk.
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_notchPassed[i] || progress < _marks[i] - 1e-4f) continue;
                _notchPassed[i] = true;
                _notchFlash[i] = NOTCH_FLASH_SECONDS;
            }

            if (!_completed && progress >= 0.999f)
            {
                _completed = true;
                _completeLeft = COMPLETE_POP_SECONDS;
                _line.Flash(COMPLETE_POP_SECONDS);
            }

            _lastProgress = progress;
        }

        private void Apply(float dt)
        {
            var style = WorldBarStyle.Active;
            var cam = Camera.main;
            float ppu = cam != null ? WorldBarGeometry.PixelsPerUnit(cam.pixelHeight, cam.orthographicSize) : 0f;

            _line.SetAlpha(_alpha);
            _line.Tick(dt, ppu, style, heartbeat: false);

            if (_completeLeft > 0f) _completeLeft = Mathf.Max(0f, _completeLeft - dt);

            for (int i = 0; i < _marks.Count; i++)
            {
                float flash = _notchFlash[i];
                if (flash > 0f) _notchFlash[i] = Mathf.Max(0f, flash - dt);
                float k = _notchFlash[i] / NOTCH_FLASH_SECONDS;

                Color c = _notchPassed[i] ? Color.Lerp(NotchPassed, NotchLit, k) : NotchIdle;
                c.a *= _alpha;
                _notches[i].color = c;

                // A lit notch stands two texels proud of the plate while it flashes.
                float t = WorldBarGeometry.TEXEL;
                _notches[i].size = k > 0f
                    ? new Vector2(2f * t, _line.InnerHeight + 2f * t)
                    : new Vector2(t, _line.InnerHeight);
            }

            ApplyCadence();
            ApplyEta();
        }

        private void ApplyCadence()
        {
            float cadence = _segments != null ? _segments.BlowCadence01 : -1f;
            ApplyWindowZone(cadence);
            if (cadence < 0f || _completed)
            {
                if (_cadence.gameObject.activeSelf) _cadence.gameObject.SetActive(false);
                _lastCadence = 0f;
                return;
            }

            if (!_cadence.gameObject.activeSelf) _cadence.gameObject.SetActive(true);

            // The sweep snaps back when the axe lands — a wrap from ~1 to ~0 is the strike.
            bool struck = cadence < _lastCadence - 0.5f;
            _lastCadence = cadence;

            float width = Mathf.Max(WorldBarGeometry.TEXEL, WorldBarGeometry.SnapToTexel(_line.InnerWidth * cadence));
            _cadence.size = new Vector2(width, WorldBarGeometry.TEXEL);
            _cadence.transform.localPosition = new Vector3(_line.FillLeftX + width * 0.5f,
                _cadence.transform.localPosition.y, 0f);

            var c = struck ? Color.white : CadenceColour;
            c.a *= _alpha * Mathf.Lerp(0.45f, 1f, cadence);
            _cadence.color = c;
        }

        /// <summary>
        /// While tapping, the last stretch of the cadence track is the cut TARGET laid flat: nested
        /// bands in the grades' colours — red edge, orange, green, cyan, gold centre — ending in a
        /// white tick where the beat is, which the sweep reaches exactly on the beat. The band the
        /// sweep is crossing lights up (it is the grade a tap now would get); the others sit dim. It
        /// is the bar's half of the target on the trunk, and every band visibly WIDENS with skill.
        /// Each band keeps at least one texel, one wider than the band inside it, so the centre is
        /// never swallowed by its neighbours on a beginner's narrow window.
        /// </summary>
        private void ApplyWindowZone(float cadence)
        {
            bool show = _segments != null && _segments.CutBandReach01(CutGrade.Ok) > 0f && cadence >= 0f && !_completed;
            for (int i = 0; i < _bands.Length; i++)
                if (_bands[i].gameObject.activeSelf != show) _bands[i].gameObject.SetActive(show);
            if (_beatTick.gameObject.activeSelf != show) _beatTick.gameObject.SetActive(show);
            if (!show) return;

            float t = WorldBarGeometry.TEXEL;
            float right = _line.FillLeftX + _line.InnerWidth;
            float y = -WorldBarGeometry.Texels(ROW_TEXELS) * 0.5f - 2f * t;
            var live = _segments.GradeIfTappedNow;

            float inner = 0f;
            for (int k = 0; k < _bands.Length; k++)
            {
                // k = 0 is the centre (Perfect), drawn LAST; _bands[0] is the outermost (Awful).
                var grade = CutGrade.Perfect + k;
                float width = Mathf.Max(inner + t, WorldBarGeometry.SnapToTexel(_line.InnerWidth * _segments.CutBandReach01(grade)));
                width = Mathf.Min(width, _line.InnerWidth);
                inner = width;

                var sr = _bands[_bands.Length - 1 - k];
                sr.size = new Vector2(width, t);
                sr.transform.localPosition = new Vector3(right - width * 0.5f, y, 0f);

                var c = RhythmCallouts.CutColour(grade);
                c.a = (grade == live ? 1f : 0.42f) * _alpha;
                sr.color = grade == live ? Color.Lerp(c, Color.white, 0.25f) : c;
            }

            _beatTick.size = new Vector2(t, 3f * t);
            _beatTick.transform.localPosition = new Vector3(right - t * 0.5f, y, 0f);
            var tick = Color.white;
            tick.a = _alpha * (live == CutGrade.Perfect ? 1f : 0.7f);
            _beatTick.color = tick;
        }

        private void ApplyEta()
        {
            float seconds = _segments != null ? _segments.SecondsRemaining : -1f;
            string text = _completed || seconds < 0f
                ? string.Empty
                : seconds < 1f ? "<1 s" : Mathf.CeilToInt(seconds) + " s";

            if (_eta.text != text) _eta.text = text;
            var c = _eta.color;
            c.a = _alpha;
            _eta.color = c;
        }
    }
}
