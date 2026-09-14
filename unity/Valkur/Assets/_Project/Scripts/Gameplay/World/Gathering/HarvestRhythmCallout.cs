using TMPro;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Interaction;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// The judgement line of the rhythm gear, shown over the trunk the way a dance game shows it
    /// over the arrows: "¡CORTE PERFECTO!" down to "¡CORTE SOQUETE!", with a combo counter under it
    /// that grows and changes colour as the streak climbs — and, over a soquete, one of twenty lines
    /// telling the player exactly what they are (<see cref="SoquetePhrases"/>).
    ///
    /// <para><b>ONE JUDGEMENT ON SCREEN AT A TIME.</b> A floating number per tap would stack a
    /// column of words over a player tapping twice a second, and the newest — the only one they
    /// need — would be the one hardest to find. Like DDR's judgement line, each tap REPLACES the
    /// last one, which re-punches in place.</para>
    ///
    /// <para><b>A GOOD CUT RISES, A WEAK ONE SAGS, A SOQUETE SHAKES AND DROPS.</b> Colour is the
    /// second cue, motion is the first: a player watching the trunk reads "that one did not count"
    /// before reading the word. Milestone streaks (x5, x10, x25…) punch bigger and throw sparks.</para>
    ///
    /// <para><b>THE TAUNT OUTLIVES THE WORD.</b> A judgement is gone in two thirds of a second, which
    /// is right for a tap a player makes twice a second and far too short to read a sentence. The
    /// taunt holds for a couple of seconds above the judgement, and a following good cut does not
    /// wipe it — being told off lasts a moment longer than the mistake did.</para>
    ///
    /// <para>What is SAID comes from <see cref="RhythmCallouts"/>, pure and testable; this only
    /// animates it. Like <see cref="HarvestWorkMark"/> it builds nothing in Edit Mode, but it keeps
    /// the current texts in fields so a fixture can read what the player would.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestRhythmCallout : MonoBehaviour
    {
        private const float JUDGE_SECONDS = 0.62f;
        private const float COMBO_BREAK_SECONDS = 0.7f;
        private const float COMBO_IDLE_FADE = 1.4f;
        private const float HEIGHT_ABOVE_MARK = 0.95f;
        private const float COMBO_BELOW_JUDGE = 0.42f;
        private const float JUDGE_SIZE = 5.4f;
        private const float COMBO_SIZE = 3.6f;
        private const float TAUNT_SIZE = 2.9f;
        private const float TAUNT_SECONDS = 2.3f;
        private const float TAUNT_ABOVE_JUDGE = 0.62f;
        private const float TAUNT_WIDTH = 4.4f;

        private static readonly Color TauntColour = new Color(1f, 0.93f, 0.90f, 1f);

        private TextMeshPro _judge, _judgeShadow, _combo, _comboShadow, _taunt, _tauntShadow;
        private Transform _root;

        private HarvestNode _node;
        private RhythmCallout _current;
        private SoquetePhrases _phrases;
        private float _tauntAge = 999f;
        private float _judgeAge = 999f;
        private int _streak;
        private float _comboPulse;
        private float _comboBreakAge = 999f;
        private float _lastHitTime;

        /// <summary>The judgement the player is reading. Empty before the first tap. A test seam.</summary>
        public string JudgementText => _current.Text ?? string.Empty;

        /// <summary>The combo line: "COMBO x7", "COMBO ROTO", or empty. A test seam.</summary>
        public string ComboLine { get; private set; } = string.Empty;

        /// <summary>How many milestone bursts have fired. A test seam.</summary>
        public int MilestoneBursts { get; private set; }

        /// <summary>The line the last soquete earned. Empty before the first one. A test seam.</summary>
        public string TauntText { get; private set; } = string.Empty;

        /// <summary>The callout for a worker, created on first use. Null for anything but the player.</summary>
        public static HarvestRhythmCallout For(GameObject worker)
        {
            if (worker == null) return null;
            if (!worker.CompareTag("Player") && !worker.transform.root.CompareTag("Player")) return null;
            var c = worker.GetComponent<HarvestRhythmCallout>();
            return c != null ? c : worker.AddComponent<HarvestRhythmCallout>();
        }

        /// <summary>One tap was judged on <paramref name="node"/>.</summary>
        public void Judge(HarvestNode node, RhythmTap tap)
        {
            if (!tap.Counted) return;
            _node = node;
            _current = RhythmCallouts.ForTap(tap);
            _judgeAge = 0f;

            if (tap.Outcome == RhythmTapOutcome.Lost)
            {
                if (_phrases == null) _phrases = new SoquetePhrases(System.Environment.TickCount ^ GetInstanceID());
                TauntText = _phrases.Next();
                _tauntAge = 0f;
            }

            if (tap.Landed && SkillDefinition.CutKeepsStreak(tap.Grade))
            {
                _streak = tap.StreakAfter;
                _comboBreakAge = 999f;
                _comboPulse = 1f;
                _lastHitTime = Time.time;
                ComboLine = RhythmCallouts.ComboText(_streak);
                if (RhythmCallouts.IsMilestone(_streak)) Burst(RhythmCallouts.ComboColour(_streak));
            }
            else if (tap.Outcome == RhythmTapOutcome.Retry || tap.Outcome == RhythmTapOutcome.Started)
            {
                // A held-back cut keeps the combo on screen; the starting tap has none yet.
            }
            else if (RhythmCallouts.AnnouncesBreak(tap))
            {
                _streak = 0;
                _comboBreakAge = 0f;
                ComboLine = "COMBO ROTO";
            }
            else
            {
                _streak = 0;
                ComboLine = string.Empty;
            }

            if (Application.isPlaying && _root == null) Build();
            enabled = true;
        }

        private void Build()
        {
            _root = new GameObject("HarvestRhythmCallout").transform;
            var container = GameObject.Find("[VFX]");
            if (container != null) _root.SetParent(container.transform, false);

            _judgeShadow = Label("JudgeShadow", JUDGE_SIZE);
            _judge = Label("Judge", JUDGE_SIZE);
            _comboShadow = Label("ComboShadow", COMBO_SIZE);
            _combo = Label("Combo", COMBO_SIZE);
            _tauntShadow = Label("TauntShadow", TAUNT_SIZE);
            _taunt = Label("Taunt", TAUNT_SIZE);
            foreach (var t in new[] { _taunt, _tauntShadow })
            {
                // A sentence, not a word: it wraps onto two lines instead of running off both sides.
                t.enableWordWrapping = true;
                t.fontStyle = FontStyles.Bold | FontStyles.Italic;
                t.rectTransform.sizeDelta = new Vector2(TAUNT_WIDTH, 1.2f);
            }
        }

        private TextMeshPro Label(string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.text = string.Empty;
            t.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_UI_WORLD);
            return t;
        }

        private void LateUpdate()
        {
            if (_root == null) { enabled = false; return; }
            float dt = Time.deltaTime;
            _judgeAge += dt;
            _tauntAge += dt;
            _comboBreakAge += dt;
            _comboPulse = Mathf.MoveTowards(_comboPulse, 0f, dt * 5f);

            bool judging = _judgeAge < JUDGE_SECONDS && !_current.IsEmpty;
            bool taunting = _tauntAge < TAUNT_SECONDS && !string.IsNullOrEmpty(TauntText);
            bool comboLive = _streak >= RhythmCallouts.ComboShownFrom && Time.time - _lastHitTime < COMBO_IDLE_FADE;
            bool breaking = _comboBreakAge < COMBO_BREAK_SECONDS;
            if (!judging && !taunting && !comboLive && !breaking)
            {
                if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
                if (_streak < RhythmCallouts.ComboShownFrom) ComboLine = string.Empty;
                enabled = false;
                return;
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

            Vector3 anchor = Anchor();
            int order = SortingConfig.ComputeSortingOrder(SortingConfig.Z_UI, anchor.y) + WorldBarRig.SORT_SPAN + 40;

            AnimateJudge(anchor, order, judging);
            AnimateCombo(anchor, order, comboLive, breaking);
            AnimateTaunt(anchor, order, taunting);
        }

        private void AnimateTaunt(Vector3 anchor, int order, bool taunting)
        {
            _taunt.gameObject.SetActive(taunting);
            _tauntShadow.gameObject.SetActive(taunting);
            if (!taunting) return;

            float t = _tauntAge / TAUNT_SECONDS;
            // Slams in slightly oversized and wobbles once, then holds still long enough to read.
            float slam = Mathf.Clamp01(_tauntAge / 0.18f);
            float scale = Mathf.LerpUnclamped(1.35f, 1f, EaseOutBack(slam));
            float wobble = (1f - Mathf.Clamp01(_tauntAge / 0.5f)) * Mathf.Sin(_tauntAge * 38f) * 4f;
            Vector3 pos = anchor + Vector3.up * (HEIGHT_ABOVE_MARK + TAUNT_ABOVE_JUDGE);
            float alpha = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
            Place(_taunt, _tauntShadow, TauntText, pos, scale, wobble, TauntColour, alpha, order + 2);
        }

        private void AnimateJudge(Vector3 anchor, int order, bool judging)
        {
            _judge.gameObject.SetActive(judging);
            _judgeShadow.gameObject.SetActive(judging);
            if (!judging) return;

            float t = _judgeAge / JUDGE_SECONDS;
            float weight = _current.Weight;

            // The punch: in from oversized with one overshoot, settled by a quarter of the life.
            float punchT = Mathf.Clamp01(t / 0.25f);
            float scale = Mathf.LerpUnclamped(1.9f, 1f, EaseOutBack(punchT)) * weight;

            Vector3 pos = anchor + Vector3.up * HEIGHT_ABOVE_MARK;
            float tilt = 0f;
            switch (_current.Motion)
            {
                case RhythmCalloutMotion.Shake:
                {
                    // Shakes hard and decays, then drops: "that did not land".
                    float shake = 1f - Mathf.Clamp01(t / 0.45f);
                    pos.x += Mathf.Sin(_judgeAge * 70f) * 0.09f * shake;
                    pos.y -= t * t * 0.45f;
                    tilt = Mathf.Sin(_judgeAge * 55f) * 7f * shake;
                    break;
                }
                case RhythmCalloutMotion.Sag:
                    // It landed, badly: no shake, it simply droops and tips to the side it missed on.
                    pos.y -= EaseOutCubic(t) * 0.22f;
                    tilt = EaseOutCubic(t) * 6f * (_current.Text.StartsWith("«") ? 1f : -1f);
                    break;
                default:
                    pos.y += EaseOutCubic(t) * 0.35f;
                    break;
            }

            float alpha = t > 0.65f ? 1f - (t - 0.65f) / 0.35f : 1f;
            Place(_judge, _judgeShadow, _current.Text, pos, scale, tilt, _current.Colour, alpha, order);
        }

        private void AnimateCombo(Vector3 anchor, int order, bool comboLive, bool breaking)
        {
            bool show = comboLive || breaking;
            _combo.gameObject.SetActive(show);
            _comboShadow.gameObject.SetActive(show);
            if (!show) return;

            Vector3 pos = anchor + Vector3.up * (HEIGHT_ABOVE_MARK - COMBO_BELOW_JUDGE);
            if (breaking)
            {
                float b = _comboBreakAge / COMBO_BREAK_SECONDS;
                pos.x += Mathf.Sin(_comboBreakAge * 60f) * 0.06f * (1f - b);
                pos.y -= b * b * 0.5f;
                Place(_combo, _comboShadow, "COMBO ROTO", pos, 1f, (1f - b) * 6f, RhythmCallouts.Soquete, 1f - b, order - 2);
                return;
            }

            // Grows a little with the streak (capped) and pulses on every hit, so a long combo is
            // physically bigger than a short one without ever crowding the judgement above it.
            float size = 1f + Mathf.Min(_streak, 30) * 0.012f + _comboPulse * 0.25f;
            float idle = Time.time - _lastHitTime;
            float alpha = idle > COMBO_IDLE_FADE - 0.4f ? Mathf.Clamp01((COMBO_IDLE_FADE - idle) / 0.4f) : 1f;
            Place(_combo, _comboShadow, ComboLine, pos, size, 0f, RhythmCallouts.ComboColour(_streak), alpha, order - 2);
        }

        private static void Place(TextMeshPro label, TextMeshPro shadow, string text, Vector3 pos, float scale,
            float tilt, Color colour, float alpha, int order)
        {
            if (label.text != text) { label.text = text; shadow.text = text; }
            label.transform.position = pos;
            shadow.transform.position = pos + new Vector3(0.04f, -0.04f, 0f);
            var rot = Quaternion.Euler(0f, 0f, tilt);
            label.transform.rotation = rot;
            shadow.transform.rotation = rot;
            label.transform.localScale = Vector3.one * scale;
            shadow.transform.localScale = Vector3.one * scale;

            colour.a = Mathf.Clamp01(alpha);
            label.color = colour;
            shadow.color = new Color(0f, 0f, 0f, 0.8f * Mathf.Clamp01(alpha));
            label.sortingOrder = order;
            shadow.sortingOrder = order - 1;
        }

        /// <summary>Above the trunk mark when there is one; otherwise above the tree's work anchor.</summary>
        private Vector3 Anchor()
        {
            var mark = GetComponent<HarvestWorkMark>();
            if (mark != null && mark.IsShowing) return mark.MarkPosition;
            if (_node != null)
            {
                var b = _node.InteractionBounds;
                return new Vector3(b.center.x, b.min.y + 0.85f, 0f);
            }
            return transform.position + Vector3.up * 1.5f;
        }

        private void Burst(Color colour)
        {
            MilestoneBursts++;
            if (!Application.isPlaying) return;
            Vector3 p = Anchor() + Vector3.up * HEIGHT_ABOVE_MARK;
            HarvestFx.Flash(p, colour, 1.6f);
            HarvestFx.Chips(p, colour, 16, Vector2.up);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
        }
    }
}
