using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// While the player works a tree, the aim chevron at their feet steps aside and a mark appears
    /// ON THE TRUNK, where the axe lands: an X of two blades of light that close as the next blow
    /// comes and flash when it hits, with a ring at the foot of the tree saying which one is being
    /// worked.
    ///
    /// <para><b>WHY A SEPARATE MARK AND NOT A MODE OF THE CHEVRON.</b> <see cref="FacingIndicator"/>
    /// answers one question — which way am I pointing — and its source guard exists because every
    /// second job loaded onto it read as small and made the shape under the character mean four
    /// things at once. Chopping has a different question: WHERE is my work landing and WHEN is the
    /// next blow. So this is its own object with its own look, and the chevron is only told to
    /// step aside while it is up — pushed in, the way <c>Pulse</c> is, so the rig still knows
    /// nothing about trees.</para>
    ///
    /// <para><b>THE X IS A METRONOME.</b> The two strokes open to either side of the cut straight
    /// after a blow and close into a cross as the session clock approaches the next one, so the
    /// player can read the rhythm off the trunk itself and does not have to watch the animation to
    /// know when the axe will fall. On the frame of the strike the cross flashes, punches outward,
    /// throws a ring of light and sparks. Without a session clock (a tree chopped with the attack
    /// button) the cross simply stays closed and flashes on every blow.</para>
    ///
    /// <para><b>ON THE CANOPY'S LAYER, AT CHEST HEIGHT.</b> A building is split at its split ratio,
    /// and on a big tree the split sits low — measured on a shipped common tree, nearly all of the
    /// visible trunk belongs to the CANOPY half. A cross drawn on the footprint's layer was hidden
    /// behind the very bark it was meant to mark. It draws just above the canopy instead, centred on
    /// the trunk and leaning toward the worker, at the height the axe actually lands.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestWorkMark : MonoBehaviour
    {
        private const float FADE_SECONDS = 0.14f;
        private const float LINGER_SECONDS = 0.8f;
        private const float STRIKE_SECONDS = 0.28f;
        /// <summary>
        /// World length of one blade. Sized against the character (a 1.8-unit body): the first
        /// live capture at 0.62 read as a sparkle on the woodcutter's shoulder, not as a cross.
        /// </summary>
        private const float STROKE_LENGTH = 1.0f;
        private const float STROKE_WIDTH = 0.22f;
        private const float OPEN_OFFSET = 0.22f;
        private const int MOTES = 4;
        private const float MOTE_RADIUS_OPEN = 0.62f;
        private const float MOTE_RADIUS_CLOSED = 0.22f;
        private const float RING_SECONDS = 0.35f;

        /// <summary>World units above the ground line the axe lands at — a character's chest.</summary>
        private const float CHEST_HEIGHT = 0.85f;

        /// <summary>How far off the trunk's centre line the cross may lean toward the worker.</summary>
        private const float TRUNK_LEAN = 0.18f;

        private static readonly Color Core = new Color(1f, 0.93f, 0.68f, 1f);
        private static readonly Color HaloTone = new Color(1f, 0.70f, 0.30f, 1f);

        /// <summary>The window is open: tap NOW. Cool white-cyan, the one moment the cross changes hue.</summary>
        private static readonly Color WindowTone = new Color(0.72f, 0.98f, 1f, 1f);

        /// <summary>A perfect tap: the strike burns hotter and gold.</summary>
        private static readonly Color PerfectTone = new Color(1f, 0.82f, 0.30f, 1f);

        /// <summary>A miss: the cross cracks red and jolts.</summary>
        private static readonly Color MissTone = new Color(1f, 0.30f, 0.22f, 1f);

        private const float MISS_SECONDS = 0.32f;

        /// <summary>The beat is lost: the cross goes dull and taps are ignored until the next one.</summary>
        private static readonly Color LostTone = new Color(0.45f, 0.42f, 0.46f, 1f);

        private static Sprite _strokeSprite;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _strokeSprite = null;

        private GameObject _worker;
        private HarvestNode _node;
        private FacingIndicator _chevron;

        private Transform _root;
        private HarvestCutTarget _target;
        private SpriteRenderer _strokeA, _strokeB, _edgeA, _edgeB, _halo, _shock, _groundRing;
        private readonly SpriteRenderer[] _motes = new SpriteRenderer[MOTES];
        private float _alpha;
        private float _visibleUntil;
        private float _strikeLeft;
        private float _ringLeft;
        private float _lastCadence;
        private float _missLeft;
        private Valkur.Data.CutGrade _lastStrikeGrade;

        /// <summary>Whether the mark is on screen. A test seam.</summary>
        public bool IsShowing => _root != null && _root.gameObject.activeSelf;

        /// <summary>Where the cross sits on the trunk. What the rhythm callout hangs above.</summary>
        public Vector3 MarkPosition => _root != null ? _root.position : transform.position;

        /// <summary>The node the mark is on. A test seam.</summary>
        public HarvestNode Node => _node;

        /// <summary>The mark for a worker, created on first use. Null for anything but the player.</summary>
        public static HarvestWorkMark For(GameObject worker)
        {
            if (worker == null) return null;
            if (!worker.CompareTag("Player") && !worker.transform.root.CompareTag("Player")) return null;
            var mark = worker.GetComponent<HarvestWorkMark>();
            return mark != null ? mark : worker.AddComponent<HarvestWorkMark>();
        }

        /// <summary>
        /// The worker is working <paramref name="node"/> right now. Keeps the mark up for a
        /// moment past the call, so a swing-chopped tree (no session) holds it between blows.
        /// </summary>
        public void Engage(HarvestNode node)
        {
            if (node == null) return;
            // Edit Mode builds nothing: a component added outside Play never receives OnDestroy,
            // so a rig built here would outlive the fixture that asked for it.
            if (_root == null && Application.isPlaying) Build();

            _worker = gameObject;
            if (_node != node) { _node = node; _lastCadence = 0f; }
            _visibleUntil = Time.time + LINGER_SECONDS;
            enabled = true;

            StepAsideChevron(true);
        }

        /// <summary>
        /// A blow landed: flash, punch, ring, sparks — sized by the cut's grade while tapping, so a
        /// perfect cut bursts gold and a pésimo barely scuffs the bark.
        /// </summary>
        public void Strike(HarvestNode node)
        {
            Engage(node);
            _missLeft = 0f;
            var tap = node != null ? node.LastTap : default;
            _lastStrikeGrade = node != null && node.InRhythmMode && tap.Landed ? tap.Grade : Valkur.Data.CutGrade.None;

            float weight = StrikeWeight(_lastStrikeGrade);
            _strikeLeft = STRIKE_SECONDS * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(weight));
            _ringLeft = RING_SECONDS;

            if (_root == null) return;
            Vector3 p = _root.position;
            bool graded = _lastStrikeGrade != Valkur.Data.CutGrade.None;
            Color flash = !graded ? new Color(1f, 0.9f, 0.62f, 0.9f)
                : _lastStrikeGrade == Valkur.Data.CutGrade.Perfect ? PerfectTone
                : RhythmCallouts.CutColour(_lastStrikeGrade);
            HarvestFx.Flash(p, flash, 0.45f + 0.7f * weight);
            HarvestFx.Chips(p, new Color(1f, 0.82f, 0.45f, 1f), Mathf.RoundToInt(2f + 7f * weight), Vector2.up);
        }

        /// <summary>How loud a strike is: 1 for a perfect cut, 0.6 for the automatic swing, fading to a scuff.</summary>
        private static float StrikeWeight(Valkur.Data.CutGrade grade)
        {
            switch (grade)
            {
                case Valkur.Data.CutGrade.Perfect: return 1f;
                case Valkur.Data.CutGrade.Good:    return 0.72f;
                case Valkur.Data.CutGrade.Ok:      return 0.5f;
                case Valkur.Data.CutGrade.Bad:     return 0.28f;
                case Valkur.Data.CutGrade.Awful:   return 0.12f;
                default:                           return 0.6f;
            }
        }

        /// <summary>A tap was judged: the target keeps a mark of where it landed.</summary>
        public void Judged(HarvestNode node, Valkur.Gameplay.Interaction.RhythmTap tap)
        {
            if (node == null || !tap.Counted) return;
            Engage(node);
            _target?.Mark(node, tap);
        }

        /// <summary>
        /// A tap missed the beat: the cross cracks red, jolts sideways and flashes. It has to be
        /// unmistakable and brief: a player keeping time needs to know instantly that the last tap
        /// did not count, and nothing about it should linger into the next beat.
        /// </summary>
        public void Miss(HarvestNode node)
        {
            Engage(node);
            _missLeft = MISS_SECONDS;
            _strikeLeft = 0f;
            if (_root == null) return;
            HarvestFx.Flash(_root.position, new Color(MissTone.r, MissTone.g, MissTone.b, 0.7f), 0.55f);
        }

        /// <summary>Stop marking this node (the tree fell, or the player walked away).</summary>
        public void Release(HarvestNode node)
        {
            if (node != null && node != _node) return;
            _visibleUntil = Mathf.Min(_visibleUntil, Time.time + 0.2f);
        }

        private void Build()
        {
            if (_strokeSprite == null)
            {
                var tex = HarvestFxTextures.Stroke;
                // FullRect: Sprite.Create defaults to Tight, which traces the alpha outline — cheap
                // on a 48x12 texture, but it is the default that cost 7 seconds of boot on atlases.
                _strokeSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f), tex.width, 0, SpriteMeshType.FullRect);
                _strokeSprite.name = "HarvestStroke";
            }

            _root = new GameObject("HarvestWorkMark").transform;
            var container = GameObject.Find("[VFX]");
            if (container != null) _root.SetParent(container.transform, false);

            _halo = Layer("Halo", ElementalSprites.Glow, 0);
            // The target sits under the blades and the shock ring: the cross still says WHEN, the
            // target says HOW WELL, and the blades dim over it while tapping so its centre shows.
            _target = new HarvestCutTarget(_root);
            _shock = Layer("Shock", ElementalSprites.Ring, 1);
            _strokeA = Layer("StrokeA", _strokeSprite, 2);
            _strokeB = Layer("StrokeB", _strokeSprite, 3);
            // A white-hot EDGE inside each blade: the stroke alone is a warm glow, and a glow
            // without a crisp spine reads as smoke rather than as a cut.
            _edgeA = Layer("EdgeA", _strokeSprite, 4);
            _edgeB = Layer("EdgeB", _strokeSprite, 5);
            for (int i = 0; i < MOTES; i++) _motes[i] = Layer("Mote" + i, ElementalSprites.HotCore, 6);

            var groundGo = new GameObject("GroundRing");
            groundGo.transform.SetParent(_root, false);
            _groundRing = groundGo.AddComponent<SpriteRenderer>();
            _groundRing.sprite = ElementalSprites.Ring;
            _groundRing.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;

            _root.gameObject.SetActive(false);
        }

        private SpriteRenderer Layer(string name, Sprite sprite, int sub)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            return sr;
        }

        private void LateUpdate()
        {
            if (_root == null) { enabled = false; return; }

            bool wanted = _node != null && Time.time < _visibleUntil && !_node.IsSpent;
            if (_node != null && _node.IsInteracting) { wanted = true; _visibleUntil = Time.time + LINGER_SECONDS; }

            _alpha = Mathf.MoveTowards(_alpha, wanted ? 1f : 0f, Time.deltaTime / FADE_SECONDS);
            bool visible = _alpha > 0.001f && _node != null;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);

            if (!visible)
            {
                StepAsideChevron(false);
                if (!wanted) { _node = null; enabled = false; }
                return;
            }

            Place();
            Animate(Time.deltaTime);
        }

        /// <summary>
        /// On the trunk where the worker's blows land: the trunk's centre line, leaning a little
        /// toward the worker's side, at chest height above the ground line.
        /// </summary>
        private void Place()
        {
            var b = _node.InteractionBounds;
            Vector3 worker = _worker != null ? _worker.transform.position : b.center;
            // The bounds are the drawn TRUNK when the art has one, so the cross must stay on it:
            // a short trunk under a low canopy is shorter than chest height, and a lean wider
            // than half the trunk puts the cross on the bark's edge.
            float leanCap = b.size.x > 0f ? Mathf.Min(TRUNK_LEAN, b.size.x * 0.25f) : TRUNK_LEAN;
            float lean = Mathf.Clamp(worker.x - b.center.x, -leanCap, leanCap);
            float height = b.size.y > 0f ? Mathf.Min(CHEST_HEIGHT, b.size.y * 0.6f) : CHEST_HEIGHT;
            _root.position = new Vector3(b.center.x + lean, b.min.y + height, 0f);

            var canopy = _node.Building != null ? _node.Building.CanopyRenderer : null;
            var footprint = _node.Building != null ? _node.Building.FootprintRenderer : null;
            var host = canopy != null && canopy.enabled && canopy.gameObject.activeInHierarchy ? canopy : footprint;
            int layer = host != null ? host.sortingLayerID : SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            int order = host != null ? host.sortingOrder + 2 : 10;

            SetDepth(_halo, layer, order);
            _target.SetDepth(layer, order + 1);
            int above = order + 1 + HarvestCutTarget.SLOT_COUNT;
            SetDepth(_shock, layer, above);
            SetDepth(_strokeA, layer, above + 1);
            SetDepth(_strokeB, layer, above + 2);
            SetDepth(_edgeA, layer, above + 3);
            SetDepth(_edgeB, layer, above + 4);
            for (int i = 0; i < MOTES; i++) SetDepth(_motes[i], layer, above + 5);
            // The foot ring lies on the GROUND, so it takes the footprint's layer, under everything.
            int groundLayer = footprint != null ? footprint.sortingLayerID : layer;
            int groundOrder = footprint != null ? footprint.sortingOrder + 1 : order - 1;
            SetDepth(_groundRing, groundLayer, groundOrder);

            // The ring lies on the ground at the foot of the trunk, flattened into the floor.
            _groundRing.transform.position = new Vector3(b.center.x, b.min.y + 0.02f, 0f);
        }

        private void Animate(float dt)
        {
            if (_strikeLeft > 0f) _strikeLeft = Mathf.Max(0f, _strikeLeft - dt);
            if (_ringLeft > 0f) _ringLeft = Mathf.Max(0f, _ringLeft - dt);
            if (_missLeft > 0f) _missLeft = Mathf.Max(0f, _missLeft - dt);

            float miss = _missLeft / MISS_SECONDS;
            bool window = _node.InHitWindow;
            bool lost = _node.RhythmBeatLost;
            bool gradedStrike = _lastStrikeGrade != Valkur.Data.CutGrade.None && _strikeLeft > 0f;
            Color blade = miss > 0f
                ? Color.Lerp(Core, MissTone, miss)
                : lost ? LostTone
                : gradedStrike ? RhythmCallouts.CutColour(_lastStrikeGrade)
                : window ? WindowTone : Core;

            _target.Tick(_node, dt, _alpha);
            // While the target is up the blades step back, so its centre is what the eye finds.
            float bladeAlpha = Mathf.Lerp(1f, 0.4f, _target.Presence);

            // A miss jolts the whole cross sideways, decaying: the same "the blow did not land"
            // language the camera whiff cue speaks.
            if (miss > 0f)
                _root.position += new Vector3(Mathf.Sin(miss * 40f) * 0.06f * miss, 0f, 0f);

            float cadence = _node.BlowCadence01;
            bool clocked = cadence >= 0f;
            if (clocked && cadence < _lastCadence - 0.5f && _strikeLeft <= 0f) _strikeLeft = STRIKE_SECONDS;
            _lastCadence = clocked ? cadence : 0f;

            float strike = _strikeLeft / STRIKE_SECONDS;          // 1 at impact, 0 after
            float open = clocked ? 1f - Smooth(cadence) : 0f;      // 1 right after a blow, 0 at the next
            float punch = 1f + strike * strike * 0.45f;
            float gain = 1f + strike * 1.6f + (window ? 0.7f : 0f);

            // The two blades, ±45°, pushed apart along their own normals while open.
            PlaceStroke(_strokeA, 45f, open, punch, gain, 1f, blade, bladeAlpha);
            PlaceStroke(_strokeB, -45f, open, punch, gain, 1f, blade, bladeAlpha);
            PlaceStroke(_edgeA, 45f, open, punch, gain * 1.3f, 0.32f, Color.white, bladeAlpha);
            PlaceStroke(_edgeB, -45f, open, punch, gain * 1.3f, 0.32f, Color.white, bladeAlpha);

            // Four sparks orbit the cross and draw in with the swing: wide and slow straight after
            // a blow, tight and fast as the next one comes, scattering outward on the strike.
            // While tapping, only as many are lit as there are CHANCES left on this beat — the life
            // counter of a rhythm game, read off the trunk the player is already watching.
            float radius = Mathf.Lerp(MOTE_RADIUS_CLOSED, MOTE_RADIUS_OPEN, open) * (1f + strike * 0.9f);
            float spin = Time.time * Mathf.Lerp(7f, 2.2f, open);
            for (int i = 0; i < MOTES; i++)
            {
                float a = spin + i * Mathf.PI * 0.5f;
                _motes[i].transform.localPosition = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.8f, 0f);
                _motes[i].transform.localScale = Vector3.one * (0.16f + strike * 0.12f);
                bool litMote = !_node.InRhythmMode || i < _node.RhythmTriesLeft;
                _motes[i].color = litMote
                    ? Tint(Core, 1.2f + strike, (0.5f + (1f - open) * 0.5f) * _alpha)
                    : Tint(LostTone, 0.6f, 0.18f * _alpha);
            }

            // A warm halo that swells with the strike and breathes gently while waiting.
            float breathe = 0.85f + 0.15f * Mathf.Sin(Time.time * 6f);
            _halo.transform.localScale = Vector3.one * (0.9f + strike * 0.5f);
            _halo.color = Tint(HaloTone, (0.45f + strike * 0.9f) * breathe, 0.55f * _alpha);

            // The shock ring expands and fades once per blow.
            float ring = 1f - _ringLeft / RING_SECONDS;
            bool ringLive = _ringLeft > 0f;
            _shock.enabled = ringLive;
            if (ringLive)
            {
                _shock.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.4f, ring);
                _shock.color = Tint(Core, 1.4f, (1f - ring) * 0.8f * _alpha);
            }

            // The foot ring: the tree being worked. Pulses on each blow, otherwise steady and faint.
            float footPulse = ringLive ? 1f - ring : 0f;
            var b = _node.InteractionBounds;
            // Sized to a TRUNK, not to the footprint: a big tree's footprint is several units wide
            // with its roots, and a ring that wide read as a spell circle rather than "this tree".
            // The bounds are the trunk box now (footprint only for art with no trunk drawn), so
            // a ring a little wider than the bark hugs its foot.
            float span = Mathf.Clamp(b.size.x * 1.3f, 0.9f, 1.8f) * (1f + footPulse * 0.15f);
            _groundRing.transform.localScale = new Vector3(span, span * 0.38f, 1f);
            _groundRing.color = Tint(HaloTone, 1.0f + footPulse, (0.6f + footPulse * 0.4f) * _alpha);
        }

        private void PlaceStroke(SpriteRenderer sr, float angleDeg, float open, float punch, float gain,
            float thickness, Color tone, float alphaScale)
        {
            float rad = (angleDeg + 90f) * Mathf.Deg2Rad;
            var normal = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            float side = angleDeg > 0f ? 1f : -1f;

            sr.transform.localPosition = normal * (OPEN_OFFSET * open * side);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            // The sprite is 1 unit long and 0.25 tall, so height 4x the wanted width.
            sr.transform.localScale = new Vector3(STROKE_LENGTH * punch * (0.85f + 0.15f * thickness),
                STROKE_WIDTH * 4f * punch * thickness, 1f);
            sr.color = Tint(tone, gain, Mathf.Lerp(0.95f, 0.6f, open) * _alpha * alphaScale);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static Color Tint(Color c, float gain, float alpha) =>
            new Color(c.r * gain, c.g * gain, c.b * gain, Mathf.Clamp01(alpha));

        private static void SetDepth(SpriteRenderer sr, int layer, int order)
        {
            if (sr.sortingLayerID != layer) sr.sortingLayerID = layer;
            if (sr.sortingOrder != order) sr.sortingOrder = order;
        }

        /// <summary>
        /// Tell the chevron to step aside, or to come back. Pushed, never read by the chevron:
        /// it has no idea why, which is what keeps its one-job guard true.
        /// </summary>
        private void StepAsideChevron(bool aside)
        {
            if (_chevron == null) _chevron = GetComponent<FacingIndicator>();
            if (_chevron != null) _chevron.SetSteppedAside(aside);
        }

        private void OnDisable() => StepAsideChevron(false);

        private void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
        }
    }
}
