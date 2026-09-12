using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Editing half of the cast-timeline panel.
    ///
    /// <para>Every write goes to the VARIANT's asset through <see cref="CommitDefinitionEdit"/>,
    /// the same seam the stat rows and the pacing dials use. The one exception is
    /// <see cref="ApplyTimelineToSpell"/>, which writes a <c>SpellDefinition</c> instead — and
    /// that asymmetry is the point of the whole design: the plan is the truth of AUTHORING and
    /// the spell is the truth of EXECUTION, so making them agree is an explicit act with an
    /// explicit warning rather than a silent sync.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>Seconds a seeded step gets: the entity's own frame interval, so a fresh plan
        /// plays exactly like the animation it was seeded from.</summary>
        private const float TIMELINE_SEED_STEP = 0.15f;

        private void OnTimelineStepClicked(int index)
        {
            _timelineSelectedStep = index;
            RefreshTimelinePanel();
        }

        /// <summary>
        /// Builds a plan from the frames the animation already has, in their natural order and at
        /// the rate they already play. Seeding rather than starting empty is what makes the
        /// feature approachable: the first thing an author sees is their own animation, unchanged,
        /// which they then bend.
        /// </summary>
        private void OnTimelineSeed()
        {
            var target = ResolveTimelineTarget();
            if (!target.IsValid) { SetStatus("Select a variant in the Animation panel first."); return; }

            var def = CurrentEditableMonster();
            if (def == null) { SetStatus("Timelines are editable on monsters only."); return; }

            int frames = _animPreview.FrameCount;
            if (frames <= 0) { SetStatus("That variant has no frames to seed from."); return; }

            float seconds = _animPreview.FrameSeconds > 0.0001f
                ? _animPreview.FrameSeconds
                : TIMELINE_SEED_STEP;

            var timeline = target.Timeline;
            timeline.steps = new List<AnimationTimelineStep>(frames);
            for (int i = 0; i < frames; i++)
                timeline.steps.Add(new AnimationTimelineStep { frame = i, duration = seconds });

            // A seeded plan is all wind-up by default: the release lands on the LAST step, which
            // is the reading that changes nothing about when the spell fires until the author
            // moves it. A release at 0 would silently make the whole animation a follow-through.
            timeline.releaseStep = frames;
            timeline.recoverStep = frames;
            timeline.prepareMode = TimelineSegmentMode.Stretch;
            timeline.channelMode = TimelineSegmentMode.Stretch;

            _timelineSelectedStep = 0;
            CommitDefinitionEdit(def, $"{target.Key} timeline");
            StageSelectedEntityForAnimation();
            RefreshTimelinePanel();
        }

        private void OnTimelineClear()
        {
            var target = ResolveTimelineTarget();
            var def = CurrentEditableMonster();
            if (!target.IsValid || def == null) return;

            target.Timeline.steps = new List<AnimationTimelineStep>();
            _timelineSelectedStep = -1;
            CommitDefinitionEdit(def, $"{target.Key} timeline cleared");
            StageSelectedEntityForAnimation();
            RefreshTimelinePanel();
        }

        private void OnTimelineStepDuration(string raw)
        {
            var target = ResolveTimelineTarget();
            var def = CurrentEditableMonster();
            if (!target.IsValid || def == null) { RefreshTimelinePanel(); return; }

            var timeline = target.Timeline;
            if (_timelineSelectedStep < 0 || _timelineSelectedStep >= timeline.steps.Count)
            { RefreshTimelinePanel(); return; }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                SetStatus($"'{raw}' is not a number — step unchanged.");
                RefreshTimelinePanel();
                return;
            }

            timeline.steps[_timelineSelectedStep].duration = Mathf.Max(0.001f, parsed);
            CommitDefinitionEdit(def, $"{target.Key} step {_timelineSelectedStep}");
            StageSelectedEntityForAnimation();
            RefreshTimelinePanel();
        }

        private void OnTimelineReleaseChanged(int index) => SetTimelineBoundary(index, release: true);
        private void OnTimelineRecoverChanged(int index) => SetTimelineBoundary(index, release: false);

        /// <summary>
        /// Moves a phase boundary. The release is the one that matters: it is the step on screen
        /// when the executor fires, so moving it is moving what the player SEES happen at the
        /// moment the spell happens.
        /// </summary>
        private void SetTimelineBoundary(int index, bool release)
        {
            var target = ResolveTimelineTarget();
            var def = CurrentEditableMonster();
            if (!target.IsValid || def == null) { RefreshTimelinePanel(); return; }

            var timeline = target.Timeline;
            if (release)
            {
                timeline.releaseStep = index;
                // A recovery cannot start before the launch. Pushed rather than refused: the
                // author is dragging one boundary and the other following it is what they mean.
                if (timeline.recoverStep < index) timeline.recoverStep = index;
            }
            else
            {
                timeline.recoverStep = Mathf.Max(index, timeline.ClampedRelease);
            }

            CommitDefinitionEdit(def, $"{target.Key} boundary");
            StageSelectedEntityForAnimation();
            RefreshTimelinePanel();
        }

        private void OnTimelinePrepareMode(int index) => SetTimelineMode(index, prepare: true);
        private void OnTimelineChannelMode(int index) => SetTimelineMode(index, prepare: false);

        private void SetTimelineMode(int index, bool prepare)
        {
            var target = ResolveTimelineTarget();
            var def = CurrentEditableMonster();
            if (!target.IsValid || def == null) { RefreshTimelinePanel(); return; }

            var mode = (TimelineSegmentMode)Mathf.Clamp(index, 0, 2);
            if (prepare) target.Timeline.prepareMode = mode;
            else         target.Timeline.channelMode = mode;

            CommitDefinitionEdit(def, $"{target.Key} {(prepare ? "wind-up" : "launch")} mode");
            StageSelectedEntityForAnimation();
            RefreshTimelinePanel();
        }

        /// <summary>
        /// Writes the plan's own times onto the spell, so the two clocks agree by construction
        /// instead of the plan being stretched to fit.
        ///
        /// <para>This is the only place in the editor that writes a <c>SpellDefinition</c>, and
        /// it is deliberately a BUTTON rather than something that happens as you author: a spell
        /// is shared by everything that casts it, so the wind-up measured from one character's
        /// art lands on every other caster too. The panel counts them and says so before the
        /// click; this method says it again in the status line, with the number.</para>
        /// </summary>
        private void ApplyTimelineToSpell()
        {
            var target = ResolveTimelineTarget();
            if (!target.IsValid || target.Timeline == null || !target.Timeline.HasSteps)
            { SetStatus("No plan to write."); return; }

            var spell = ResolveTimelineSpell(target);
            if (spell == null)
            { SetStatus("That variant reserves no spell, so there is nothing to write to."); return; }

#if UNITY_EDITOR
            float prepare = target.Timeline.AuthoredPrepareDuration;
            float channel = target.Timeline.AuthoredChannelDuration;

            var so = new UnityEditor.SerializedObject(spell);
            var prepareProp = so.FindProperty("prepareDuration");
            var channelProp = so.FindProperty("channelDuration");
            if (prepareProp == null || channelProp == null)
            { SetStatus("SpellDefinition has no phase fields to write."); return; }

            prepareProp.floatValue = prepare;
            channelProp.floatValue = channel;
            so.ApplyModifiedPropertiesWithoutUndo();

            // SetDirty alone, never Undo.RecordObject — the rule the whole editor follows since
            // the 193 building templates. The explicit save is targeted at this one asset so a
            // project-wide flush cannot carry somebody else's dirty object with it.
            UnityEditor.EditorUtility.SetDirty(spell);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(spell);

            int sharers = CountSpellSharers(spell.spellKey);
            SetStatus($"{spell.spellKey}: prepare {prepare:0.00}s, channel {channel:0.00}s" +
                      (sharers > 1 ? $" — applies to all {sharers} casters." : "."));
            RefreshTimelinePanel();
#else
            SetStatus("Writing a spell is Editor-only — a built game has no .asset to write.");
#endif
        }
    }
}
