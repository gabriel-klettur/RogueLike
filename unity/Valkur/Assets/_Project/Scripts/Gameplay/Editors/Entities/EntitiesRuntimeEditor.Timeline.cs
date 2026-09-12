using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The cast-timeline panel: the spell's phases and the animation's steps on ONE axis, and
    /// the editing that makes them meet.
    ///
    /// <para>The two clocks were independent for the life of the project. The spell decides when
    /// it fires (<c>prepareDuration</c>) and the animation decided how long it took
    /// (<c>GetStateLength</c>), and nothing reconciled them — so the frame a character is drawn
    /// on when a fireball leaves their hand was whatever the frame rate happened to land on. The
    /// plan edited here is the reconciliation, and this panel is where it is legible: both tracks
    /// share a time axis, so a wind-up that ends before the spell fires LOOKS wrong.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private bool _timelinePanelOpen;

        /// <summary>Which step the author has selected, or -1. The duration box edits this one.</summary>
        private int _timelineSelectedStep = -1;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void SetTimelinePanelOpen(bool open)
        {
            _timelinePanelOpen = open;
            if (open) RefreshTimelinePanel();
        }

        private void TickTimelinePanel()
        {
            if (!_timelinePanelOpen) return;
            HighlightTimelinePlayhead();
        }

        // ── Reading the plan ─────────────────────────────────────────────────────

        /// <summary>
        /// The variant whose plan is on screen: whatever the Animation panel has selected. A
        /// timeline belongs to a variant, so with none selected there is nothing to edit — and
        /// saying so is better than editing the base set behind the author's back.
        /// </summary>
        private AttackOrCastVariant ResolveTimelineTarget()
        {
            var config = ResolveSelectedAssetConfig(out _);
            if (config == null || _animPreview.CurrentVariant < 0) return default;

            var state = _animPreview.CurrentState;
            string key = _animPreview.Animator != null
                ? _animPreview.Animator.VariantLabel(state, _animPreview.CurrentVariant)
                : null;
            if (string.IsNullOrEmpty(key)) return default;

            if (state == DirectionalAnimator.AnimState.Cast && config.castVariants != null)
            {
                foreach (var variant in config.castVariants)
                    if (variant != null && KeyMatches(variant.key, key))
                        return new AttackOrCastVariant { Cast = variant, Key = key };
            }
            if (state == DirectionalAnimator.AnimState.Attack && config.attackVariants != null)
            {
                foreach (var variant in config.attackVariants)
                    if (variant != null && KeyMatches(variant.key, key))
                        return new AttackOrCastVariant { Attack = variant, Key = key };
            }
            return default;
        }

        /// <summary>One of the two variant types, since only they carry a timeline and they are
        /// deliberately unrelated classes.</summary>
        private struct AttackOrCastVariant
        {
            public CastVariant   Cast;
            public AttackVariant Attack;
            public string        Key;

            public bool IsValid => Cast != null || Attack != null;
            public AnimationTimeline Timeline => Cast != null ? Cast.timeline : Attack?.timeline;
            public IReadOnlyList<string> SpellKeys => Cast != null ? Cast.spellKeys : Attack?.spellKeys;
        }

        /// <summary>The spell this plan is meant to coordinate with: the first key the variant
        /// reserves. A variant reserving none has no phases to fit, and the panel says so.</summary>
        private SpellDefinition ResolveTimelineSpell(AttackOrCastVariant target)
        {
            var keys = target.SpellKeys;
            if (keys == null || keys.Count == 0) return null;
            ResolveSpellCatalogFallback();
            if (_spellCatalog == null) return null;
            return _spellCatalog.TryGet(keys[0], out var spell) ? spell : null;
        }

        // ── Drawing ──────────────────────────────────────────────────────────────

        private void RefreshTimelinePanel()
        {
            if (_ui.TimelineStepImgs == null) return;

            var target = ResolveTimelineTarget();
            var timeline = target.Timeline;
            var spell = ResolveTimelineSpell(target);

            float prepare = spell != null ? Mathf.Max(0f, spell.prepareDuration) : 0f;
            float channel = spell != null ? Mathf.Max(0f, spell.channelDuration) : 0f;

            if (_ui.TimelineSubjectText != null)
            {
                _ui.TimelineSubjectText.text = !target.IsValid
                    ? "Select a VARIANT in the Animation panel — a plan belongs to one."
                    : spell == null
                        ? $"{target.Key} · no spell reserved"
                        : $"{target.Key} · {spell.spellKey}";
            }

            DrawPhaseTrack(spell, prepare, channel, timeline);
            DrawStepTrack(timeline, prepare, channel);
            RefreshTimelineControls(timeline);
            RefreshTimelineWarning(target, timeline, spell, prepare, channel);
        }

        /// <summary>
        /// The three phases, each as wide as its seconds. A phase of zero collapses to nothing,
        /// which is the honest picture for the ninety-six spells that fire instantly.
        /// </summary>
        private void DrawPhaseTrack(SpellDefinition spell, float prepare, float channel,
                                    AnimationTimeline timeline)
        {
            if (_ui.TimelinePhaseImgs == null) return;

            float recover = timeline != null && timeline.HasSteps
                ? Mathf.Max(0f, timeline.AuthoredDuration
                                - timeline.AuthoredPrepareDuration
                                - timeline.AuthoredChannelDuration)
                : 0f;

            float[] spans = { prepare, channel, recover };
            var colours = new[] { ACCENT_DIM_PREPARE, ACCENT_DIM_LAUNCH, EditorUIHelpers.SLOT_BG };
            string[] names = { "WIND-UP", "LAUNCH", "RECOVER" };

            for (int i = 0; i < 3 && i < _ui.TimelinePhaseImgs.Length; i++)
            {
                var img = _ui.TimelinePhaseImgs[i];
                if (img == null) continue;

                var le = img.GetComponent<LayoutElement>();
                // A hair of width for an empty phase rather than zero: a collapsed cell with a
                // label in it reads as a layout fault, and the numbers below say the truth.
                if (le != null) le.flexibleWidth = Mathf.Max(0.02f, spans[i]);
                img.color = spans[i] > 0f ? colours[i] : EditorUIHelpers.SLOT_BG;

                if (_ui.TimelinePhaseTmps != null && i < _ui.TimelinePhaseTmps.Length &&
                    _ui.TimelinePhaseTmps[i] != null)
                {
                    _ui.TimelinePhaseTmps[i].text = spans[i] > 0f
                        ? $"{names[i]} {spans[i]:0.00}s"
                        : names[i];
                    _ui.TimelinePhaseTmps[i].color = spans[i] > 0f
                        ? EditorUIHelpers.TEXT_PRIMARY
                        : EditorUIHelpers.TEXT_MUTED;
                }
            }
        }

        private static readonly Color ACCENT_DIM_PREPARE = new Color(0.22f, 0.34f, 0.52f, 1f);
        private static readonly Color ACCENT_DIM_LAUNCH  = new Color(0.46f, 0.34f, 0.16f, 1f);

        /// <summary>
        /// The resolved steps, each as wide as the seconds it will really be on screen. Resolved
        /// rather than authored on purpose: under Stretch the authored numbers are ratios, and a
        /// track drawn from them would show a plan nobody plays.
        /// </summary>
        private void DrawStepTrack(AnimationTimeline timeline, float prepare, float channel)
        {
            var resolved = timeline != null && timeline.HasSteps
                ? CastTimelineResolver.Resolve(timeline, prepare, channel)
                : new List<ResolvedTimelineStep>();

            int cells = _ui.TimelineStepImgs.Length;
            for (int i = 0; i < cells; i++)
            {
                var img = _ui.TimelineStepImgs[i];
                if (img == null) continue;

                bool used = i < resolved.Count;
                img.gameObject.SetActive(used);
                if (!used) continue;

                var le = img.GetComponent<LayoutElement>();
                if (le != null) le.flexibleWidth = Mathf.Max(0.02f, resolved[i].Seconds);
                img.color = i == _timelineSelectedStep
                    ? EditorUIHelpers.SLOT_SELECTED
                    : EditorUIHelpers.SLOT_BG;

                if (_ui.TimelineStepTmps != null && _ui.TimelineStepTmps[i] != null)
                    _ui.TimelineStepTmps[i].text = resolved[i].Frame.ToString();
            }

            if (_ui.TimelineReadoutText != null)
            {
                float total = 0f;
                for (int i = 0; i < resolved.Count; i++) total += resolved[i].Seconds;

                _ui.TimelineReadoutText.text = resolved.Count == 0
                    ? "No plan. Seed one to start, or leave it empty and the animation plays as drawn."
                    : $"{resolved.Count} step(s) · {total:0.00}s total" +
                      (resolved.Count > cells ? $"  (showing the first {cells})" : "") +
                      (_timelineSelectedStep >= 0 && _timelineSelectedStep < resolved.Count
                          ? $"\nstep {_timelineSelectedStep}: frame {resolved[_timelineSelectedStep].Frame}" +
                            $" · {resolved[_timelineSelectedStep].Seconds * 1000f:0} ms on screen"
                          : "\nclick a step to edit its seconds");
            }
        }

        private void RefreshTimelineControls(AnimationTimeline timeline)
        {
            bool has = timeline != null && timeline.HasSteps;
            int count = has ? timeline.steps.Count : 0;

            var boundaries = new List<string>(count + 1);
            for (int i = 0; i <= count; i++) boundaries.Add(i == count ? $"{i} (end)" : i.ToString());

            SetDropdownOptions(_ui.TimelineReleaseDd, boundaries, has ? timeline.ClampedRelease : 0);
            SetDropdownOptions(_ui.TimelineRecoverDd, boundaries, has ? timeline.ClampedRecover : 0);
            SetDropdownOptions(_ui.TimelinePrepModeDd, EntitiesEditorUIBuilder.TimelineModeLabels,
                               has ? (int)timeline.prepareMode : 0);
            SetDropdownOptions(_ui.TimelineChanModeDd, EntitiesEditorUIBuilder.TimelineModeLabels,
                               has ? (int)timeline.channelMode : 0);

            if (_ui.TimelineReleaseDd  != null) _ui.TimelineReleaseDd.interactable  = has;
            if (_ui.TimelineRecoverDd  != null) _ui.TimelineRecoverDd.interactable  = has;
            if (_ui.TimelinePrepModeDd != null) _ui.TimelinePrepModeDd.interactable = has;
            if (_ui.TimelineChanModeDd != null) _ui.TimelineChanModeDd.interactable = has;

            if (_ui.TimelineStepDurationInput != null)
            {
                bool editable = has && _timelineSelectedStep >= 0 && _timelineSelectedStep < count;
                var step = editable ? timeline.steps[_timelineSelectedStep] : null;
                _ui.TimelineStepDurationInput.SetTextWithoutNotify(
                    step != null ? step.duration.ToString("0.###", CultureInfo.InvariantCulture) : "");
                _ui.TimelineStepDurationInput.interactable = editable;
            }
        }

        /// <summary>
        /// What the author cannot see from the two tracks: whether the plan's own times agree
        /// with the spell's, and who else would be changed by making them agree.
        /// </summary>
        private void RefreshTimelineWarning(AttackOrCastVariant target, AnimationTimeline timeline,
                                            SpellDefinition spell, float prepare, float channel)
        {
            if (_ui.TimelineWarningText == null) return;

            if (!target.IsValid || timeline == null || !timeline.HasSteps)
            {
                _ui.TimelineWarningText.text = "";
                return;
            }

            var sb = new System.Text.StringBuilder();
            if (spell == null)
            {
                sb.Append("This variant reserves no spell, so there are no phases to fit: the " +
                          "plan plays exactly as authored.");
            }
            else
            {
                float authoredPrepare = timeline.AuthoredPrepareDuration;
                float authoredChannel = timeline.AuthoredChannelDuration;
                bool differs = Mathf.Abs(authoredPrepare - prepare) > 0.01f ||
                               Mathf.Abs(authoredChannel - channel) > 0.01f;

                sb.Append($"authored {authoredPrepare:0.00}s / {authoredChannel:0.00}s  ·  " +
                          $"spell {prepare:0.00}s / {channel:0.00}s");
                if (differs)
                    sb.Append("\nThe plan is being fitted to the spell. \"Write to spell\" makes " +
                              "the spell agree instead.");

                int sharers = CountSpellSharers(spell.spellKey);
                if (sharers > 1)
                    sb.Append($"\n{sharers} entities cast {spell.spellKey}: writing changes it " +
                              "for every one of them.");
            }
            _ui.TimelineWarningText.text = sb.ToString();
        }

        /// <summary>
        /// How many shipped entities reserve this spell key on some variant. A
        /// <c>SpellDefinition</c> is shared by everything that casts it — the same reason
        /// <c>castMuzzle</c> lives on the creature — so writing a wind-up measured from one
        /// character's art reaches every other caster, and the panel has to say so BEFORE the
        /// click rather than after.
        /// </summary>
        private int CountSpellSharers(string spellKey)
        {
            if (string.IsNullOrEmpty(spellKey)) return 0;
            ResolveMonsterCatalogFallback();

            int count = 0;
            if (_monsterCatalog != null)
            {
                foreach (var def in _monsterCatalog.Definitions)
                {
                    if (def?.assetConfig == null) continue;
                    if (ConfigReservesSpell(def.assetConfig, spellKey)) count++;
                }
            }
            foreach (var preset in PlayerClassCatalog.AllPresets)
            {
                var playerDef = FindPlayerDefinition(preset.PlayerKey);
                if (playerDef?.assetConfig == null) continue;
                if (ConfigReservesSpell(playerDef.assetConfig, spellKey)) count++;
            }
            return count;
        }

        private static bool ConfigReservesSpell(EntityAssetConfig config, string spellKey)
        {
            if (config.castVariants != null)
            {
                foreach (var variant in config.castVariants)
                    if (variant?.spellKeys != null)
                        foreach (var key in variant.spellKeys)
                            if (KeyMatches(key, spellKey)) return true;
            }
            if (config.attackVariants != null)
            {
                foreach (var variant in config.attackVariants)
                    if (variant?.spellKeys != null)
                        foreach (var key in variant.spellKeys)
                            if (KeyMatches(key, spellKey)) return true;
            }
            return false;
        }

        /// <summary>Moves the highlight to the step the rig is really on.</summary>
        private void HighlightTimelinePlayhead()
        {
            var animator = _animPreview.Animator;
            if (animator == null || !animator.HasTimeline || _ui.TimelineStepImgs == null) return;

            int playing = animator.TimelineStepIndex;
            for (int i = 0; i < _ui.TimelineStepImgs.Length; i++)
            {
                var img = _ui.TimelineStepImgs[i];
                if (img == null || !img.gameObject.activeSelf) continue;
                img.color = i == playing
                    ? EditorUIHelpers.ACCENT_BG
                    : (i == _timelineSelectedStep ? EditorUIHelpers.SLOT_SELECTED
                                                  : EditorUIHelpers.SLOT_BG);
            }
        }
    }
}
