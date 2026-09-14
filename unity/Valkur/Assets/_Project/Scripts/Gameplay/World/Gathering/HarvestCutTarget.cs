using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Interaction;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The target drawn on the trunk while the player taps: the impact area of a manual cut, with
    /// its centre told apart from everything around it.
    ///
    /// <para><b>FIVE BANDS AND A BULL.</b> Gold centre (PERFECTO), cyan (BUENO), green (OK), orange
    /// (MALO), red edge (PÉSIMO); off the disc is SOQUETE. Each band is a flat fill with a crisp rim
    /// and a white dot marks the dead centre, over a dark disc so the bands read on pale bark
    /// (<see cref="CutTargetTextures"/>). The sizes are the grading itself
    /// (<see cref="CutTargetGeometry"/>), so the band a player sees is the band they are graded in.</para>
    ///
    /// <para><b>A RING CLOSES ON THE CENTRE.</b> The approach ring's radius is the time left to the
    /// beat, so it crosses the red edge, the orange, the green and the cyan and meets the bull on the
    /// beat — the osu! approach circle, which is the most legible "when" a rhythm game has. It takes
    /// the colour of the band it is crossing, which is the grade a tap made now would get; after the
    /// beat it opens again through the same bands. While the centre is live the bull flares.</para>
    ///
    /// <para><b>EVERY CUT LEAVES A MARK WHERE IT LANDED.</b> Early on the left, late on the right, at
    /// its distance from the centre, in its grade's colour, the last few fading out. A player who is
    /// consistently a hair late sees a cluster just right of the bull and corrects — which no word
    /// and no single flash could teach.</para>
    ///
    /// <para>Owned by <see cref="HarvestWorkMark"/> and built only in Play Mode, like it.</para>
    /// </summary>
    public sealed class HarvestCutTarget
    {
        private const int MARKS = 5;
        private const float MARK_SECONDS = 1.5f;
        private const float MARK_SIZE = 0.13f;
        private const float PRESENCE_SECONDS = 0.18f;

        /// <summary>Sorting slots this rig claims above the base order it is given.</summary>
        public const int SLOT_COUNT = 5;

        private static readonly Color LostTone = new Color(0.45f, 0.42f, 0.46f, 1f);

        private readonly Transform _root;
        private readonly SpriteRenderer _backing, _target, _bull, _approach;
        private readonly SpriteRenderer[] _marks = new SpriteRenderer[MARKS];
        private readonly float[] _markAge = new float[MARKS];
        private readonly Vector2[] _markPos = new Vector2[MARKS];
        private readonly Color[] _markColour = new Color[MARKS];
        private readonly bool[] _markShake = new bool[MARKS];
        private readonly float[] _bands = new float[5];
        private SkillDefinition _paintedFor;
        private int _nextMark;
        private int _dealt;

        /// <summary>0..1: how present the target is. The cross dims under it by this much.</summary>
        public float Presence { get; private set; }

        public HarvestCutTarget(Transform parent)
        {
            _root = new GameObject("CutTarget").transform;
            _root.SetParent(parent, false);

            _backing = Layer("Backing", CutTargetTextures.Backing, ElementalSprites.SharedUnlitMaterial);
            _target = Layer("Bands", null, ElementalSprites.SharedAdditiveMaterial);
            _bull = Layer("Bull", ElementalSprites.HotCore, ElementalSprites.SharedAdditiveMaterial);
            _approach = Layer("Approach", CutTargetTextures.ApproachRing, ElementalSprites.SharedAdditiveMaterial);
            for (int i = 0; i < MARKS; i++)
            {
                _marks[i] = Layer("Mark" + i, ElementalSprites.HotCore, ElementalSprites.SharedAdditiveMaterial);
                _markAge[i] = MARK_SECONDS;
            }
            _root.gameObject.SetActive(false);
        }

        private SpriteRenderer Layer(string name, Sprite sprite, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>Sort the rig at <paramref name="order"/> and the next few orders on <paramref name="layer"/>.</summary>
        public void SetDepth(int layer, int order)
        {
            Depth(_backing, layer, order);
            Depth(_target, layer, order + 1);
            Depth(_bull, layer, order + 2);
            Depth(_approach, layer, order + 3);
            for (int i = 0; i < MARKS; i++) Depth(_marks[i], layer, order + 4);
        }

        /// <summary>A tap was judged: leave its mark where it landed.</summary>
        public void Mark(HarvestNode node, RhythmTap tap)
        {
            if (node == null || tap.Grade == CutGrade.None) return;
            int i = _nextMark;
            _nextMark = (_nextMark + 1) % MARKS;
            _markAge[i] = 0f;
            _markPos[i] = CutTargetGeometry.MarkPosition(node.RhythmSkill, tap.Grade, tap.OffsetSeconds, tap.WindowSeconds, _dealt++);
            // A held-back cut marks where it WOULD have landed, dimmer: it did not count.
            var c = RhythmCallouts.CutColour(tap.Grade);
            if (tap.Outcome == RhythmTapOutcome.Retry) c = Color.Lerp(c, LostTone, 0.5f);
            _markColour[i] = c;
            _markShake[i] = tap.Outcome == RhythmTapOutcome.Lost;
        }

        public void Tick(HarvestNode node, float dt, float alpha)
        {
            bool wanted = node != null && node.InRhythmMode;
            Presence = Mathf.MoveTowards(Presence, wanted ? 1f : 0f, dt / PRESENCE_SECONDS);
            float a = Presence * alpha;
            bool visible = a > 0.001f;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            var skill = node != null ? node.RhythmSkill : null;
            Repaint(skill);

            float outer = CutTargetGeometry.OuterRadius;
            float diameter = outer * 2f;
            bool lost = node != null && node.RhythmBeatLost;

            _backing.transform.localScale = Vector3.one * diameter * 1.12f;
            _backing.color = new Color(1f, 1f, 1f, 0.55f * a);

            _target.transform.localScale = Vector3.one * diameter;
            _target.color = lost ? new Color(0.55f, 0.52f, 0.56f, 0.6f * a) : new Color(1f, 1f, 1f, a);

            TickApproach(node, skill, a, lost);
            TickMarks(dt, a);
        }

        private void Repaint(SkillDefinition skill)
        {
            if (skill == _paintedFor && _target.sprite != null) return;
            _paintedFor = skill;
            CutTargetGeometry.BandFractions(skill, _bands);
            _target.sprite = CutTargetTextures.Target(_bands);
        }

        private void TickApproach(HarvestNode node, SkillDefinition skill, float a, bool lost)
        {
            float perfectR = CutTargetGeometry.BandRadius(skill, CutGrade.Perfect);
            float offset = node != null ? node.SecondsFromBeat : 0f;
            float window = node != null ? node.HitWindowSeconds : 0f;
            float r = CutTargetGeometry.RadiusFor(skill, offset, window, CutTargetGeometry.ApproachStartRadius);
            var grade = node != null ? node.GradeIfTappedNow : CutGrade.None;

            // Invisible until it is about to cross the edge, so it arrives rather than hovering.
            float edgeFade = Mathf.Clamp01((CutTargetGeometry.OuterRadius * CutTargetGeometry.ApproachStartRadius - r)
                                           / (CutTargetGeometry.OuterRadius * 0.35f));
            bool show = !lost && grade != CutGrade.None;
            _approach.enabled = show;
            if (show)
            {
                // Never smaller than the bull: at the centre it hugs the dot rather than vanishing.
                float ringR = Mathf.Max(r, perfectR * 0.55f);
                _approach.transform.localScale = Vector3.one * ringR * 2f;
                var c = grade == CutGrade.Soquete ? Color.white : RhythmCallouts.CutColour(grade);
                float gain = grade == CutGrade.Perfect ? 1.8f : 1.25f;
                _approach.color = new Color(c.r * gain, c.g * gain, c.b * gain, a * edgeFade);
            }

            // The bull flares while a tap would be perfect.
            float live = grade == CutGrade.Perfect ? 1f : 0f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 18f);
            _bull.transform.localScale = Vector3.one * perfectR * (1.1f + live * 1.4f * pulse);
            var gold = RhythmCallouts.Perfect;
            float bullGain = 0.8f + live * 1.4f;
            _bull.color = new Color(gold.r * bullGain, gold.g * bullGain, gold.b * bullGain, (0.35f + live * 0.65f) * a);
        }

        private void TickMarks(float dt, float a)
        {
            int newest = (_nextMark + MARKS - 1) % MARKS;
            for (int i = 0; i < MARKS; i++)
            {
                _markAge[i] += dt;
                float age = _markAge[i];
                bool live = age < MARK_SECONDS;
                _marks[i].enabled = live;
                if (!live) continue;

                float t = age / MARK_SECONDS;
                float pop = Mathf.Lerp(2.2f, 1f, Mathf.Clamp01(age / 0.12f));
                // The newest mark is full size; older ones shrink a little so the latest reads first.
                float size = MARK_SIZE * pop * (i == newest ? 1f : 0.78f);
                Vector2 p = _markPos[i];
                if (_markShake[i]) p.x += Mathf.Sin(age * 60f) * 0.05f * (1f - Mathf.Clamp01(age / 0.4f));

                _marks[i].transform.localPosition = new Vector3(p.x, p.y, 0f);
                _marks[i].transform.localScale = Vector3.one * size;
                var c = _markColour[i];
                float gain = 1.6f - t * 0.6f;
                _marks[i].color = new Color(c.r * gain, c.g * gain, c.b * gain, (1f - t * t) * a);
            }
        }

        private static void Depth(SpriteRenderer sr, int layer, int order)
        {
            if (sr.sortingLayerID != layer) sr.sortingLayerID = layer;
            if (sr.sortingOrder != order) sr.sortingOrder = order;
        }
    }
}
