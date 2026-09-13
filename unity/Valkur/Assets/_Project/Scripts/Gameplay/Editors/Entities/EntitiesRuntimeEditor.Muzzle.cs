using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The muzzle picker: placing, by hand and on the frame itself, the point a spell is born
    /// at on THIS creature's art.
    ///
    /// <para>It exists because the shared <c>SpellCastAnchor</c> enum is a signed fraction of
    /// the caster's half-HEIGHT, which is exactly right for a humanoid and cannot say anything
    /// at all about a creature whose business end is in FRONT of its pivot. The red dragon is
    /// 8.23 x 4.55 world units with a mouth three and a half units forward: measured, its
    /// breath was born out of the middle of its own back, and no value of that enum reaches
    /// the mouth because the error is horizontal and the enum is vertical.</para>
    ///
    /// <para><b>The unit an author places is the ANIMATION.</b> "The dragon breathes from its
    /// mouth" is a statement about its cast animation; nobody was ever going to place thirty
    /// fractions one frame at a time, which is what the per-frame table alone asked for. The
    /// per-frame rows stay as the REFINEMENT for a body part that sweeps, and the baker keeps
    /// whatever a human placed.</para>
    ///
    /// <para>Writes go through <see cref="CommitDefinitionEdit"/> like every other edit in this
    /// editor: <c>EditorUtility.SetDirty</c> only, live monsters reconfigured, an explicit Save
    /// to reach the disk.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>What a placed point is scoped to. The dropdown index IS this value.</summary>
        private enum MuzzleScope
        {
            /// <summary>The creature-wide fallback pair — every animation, every spell.</summary>
            Creature = 0,
            /// <summary>The state and variant currently on the stage. The normal choice.</summary>
            Animation = 1,
            /// <summary>That animation, for ONE spell. The case an animation cannot separate.</summary>
            Spell = 2,
        }

        [Valkur.Core.SelfHealingStatic("Constant label table; written once at class init, never mutated.")]
        private static readonly string[] MuzzleScopeNames =
        {
            "Criatura (todo)", "Esta animacion", "Este hechizo",
        };

        private bool _muzzlePlacing;
        private MuzzleScope _muzzleScope = MuzzleScope.Animation;
        private string _muzzleSpellKey = string.Empty;
        private readonly List<string> _muzzleSpellOptions = new List<string>();
        private EntityStagePointerProbe _stageProbe;

        // -- Callbacks from the panel ---------------------------------------------

        /// <summary>
        /// Arm or disarm placement.
        ///
        /// <para>Arming is what makes the stage a raycast target. It is NOT left on: the stage
        /// sits in the middle of a draggable panel, so a permanently clickable stage is a hole
        /// in that panel's own drag surface — the author would grab the picture instead of the
        /// window every time they tried to move it.</para>
        /// </summary>
        private void OnToggleMuzzlePlacement()
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null)
            {
                SetStatus("Selecciona un monstruo del catalogo para colocar su origen de hechizo.");
                return;
            }

            _muzzlePlacing = !_muzzlePlacing;
            ApplyStageProbeState();
            if (_muzzlePlacing) RaiseAnimationPanel();
            RefreshMuzzleEditor();

            SetStatus(_muzzlePlacing
                ? "Arrastra sobre la vista previa para colocar el origen. Pausa la animacion para afinar."
                : "Colocacion terminada.");
        }

        private void OnMuzzleScopeChanged(int index)
        {
            _muzzleScope = (MuzzleScope)Mathf.Clamp(index, 0, MuzzleScopeNames.Length - 1);
            RefreshMuzzleEditor();
        }

        private void OnMuzzleSpellChanged(int index)
        {
            _muzzleSpellKey = index >= 0 && index < _muzzleSpellOptions.Count
                ? _muzzleSpellOptions[index]
                : string.Empty;
            RefreshMuzzleEditor();
        }

        /// <summary>
        /// Remove whatever the current scope wrote, and nothing else.
        ///
        /// <para>Scoped deliberately: a single "clear the muzzle" that emptied everything would
        /// take a creature-wide pair away because the author wanted to undo one per-spell
        /// exception, and there is nothing on screen that would have warned them.</para>
        /// </summary>
        private void OnMuzzleClear()
        {
            var def = CurrentEditableMonster();
            var config = def?.assetConfig;
            if (config == null) return;

            if (_muzzleScope == MuzzleScope.Creature)
            {
                config.castMuzzle = Vector2.zero;
                CommitDefinitionEdit(def, "Origen de hechizo (criatura)");
                RefreshMuzzleEditor();
                return;
            }

            var existing = FindExactMuzzlePoint(config, out _, out _);
            if (existing == null)
            {
                SetStatus("No hay origen propio para este ambito.");
                return;
            }

            config.castMuzzlePoints.Remove(existing);
            CommitDefinitionEdit(def, "Origen de hechizo");
            RefreshMuzzleEditor();
        }

        /// <summary>
        /// A point on the stage became a muzzle.
        ///
        /// <para>Fires on every drag frame, so it UPSERTS rather than appending — an author
        /// dragging across the sprite would otherwise leave one point per frame of the drag,
        /// all but the last of them dead, and the list would be unreadable after one gesture.</para>
        /// </summary>
        private void OnStageMuzzlePoint(Vector2 viewport)
        {
            if (!_muzzlePlacing) return;

            var def = CurrentEditableMonster();
            var config = def?.assetConfig;
            if (config == null) return;

            if (!_animPreview.TryMuzzleNormalizedFromViewport(viewport, out Vector2 normalized))
                return;

            string frameOnStage = _animPreview.FrameNameOnStage();

            // Clamped to the frame's own bounds plus a little: a muzzle outside the sprite is
            // expressible and is never what anybody means, and an unclamped drag off the edge
            // of the stage writes a number that puts the cast metres away.
            normalized = new Vector2(Mathf.Clamp(normalized.x, -1.5f, 1.5f),
                                     Mathf.Clamp(normalized.y, -1.5f, 1.5f));

            if (_muzzleScope == MuzzleScope.Creature)
            {
                config.castMuzzle = normalized;
            }
            else
            {
                var point = FindExactMuzzlePoint(config, out string state, out string variant);
                if (point == null)
                {
                    point = new CastMuzzlePoint { state = state, variantKey = variant };
                    if (_muzzleScope == MuzzleScope.Spell && !string.IsNullOrEmpty(_muzzleSpellKey))
                        point.spellKeys.Add(_muzzleSpellKey);
                    // Named AFTER the spell is claimed, so the label says which of two points
                    // on the same animation this is. Both reading "Cast" is a list a human
                    // cannot tell apart in the Inspector, which is where a mistake gets found.
                    point.key = DescribeScope(state, variant, point.spellKeys);
                    config.castMuzzlePoints.Add(point);
                }

                // The INVERSE of the composition, so the crosshair lands under the cursor on
                // the frame being looked at while the baked sweep still shapes the others.
                // Storing the raw click instead would draw the mark somewhere the author did
                // not put it the instant the creature has baked rows -- and the dragon, the
                // one creature that has them, is the whole reason this feature exists.
                point.offset = normalized - FrameSweepDelta(config, frameOnStage);
            }

            CommitDefinitionEdit(def, "Origen de hechizo");
            RefreshMuzzleEditor();
        }

        // -- State --------------------------------------------------------------

        /// <summary>
        /// The point that matches the CURRENT scope exactly — never the one that merely
        /// resolves for it.
        ///
        /// <para>The difference matters on every edit: resolution falls back, so asking "what
        /// answers here" while scoped to a spell returns the ANIMATION's point when the spell
        /// has none, and writing into it would silently retarget every other spell that shares
        /// the animation.</para>
        /// </summary>
        private CastMuzzlePoint FindExactMuzzlePoint(EntityAssetConfig config,
                                                     out string state, out string variant)
        {
            state = _animPreview.CurrentState.ToString();
            variant = _animPreview.HasSubject
                ? _animPreview.VariantKeyOnStage()
                : null;

            if (config.castMuzzlePoints == null) return null;

            string wantedSpell = _muzzleScope == MuzzleScope.Spell ? _muzzleSpellKey : null;

            for (int i = 0; i < config.castMuzzlePoints.Count; i++)
            {
                var point = config.castMuzzlePoints[i];
                if (point == null) continue;
                if (!SameScopeField(point.state, state)) continue;
                if (!SameScopeField(point.variantKey, variant)) continue;

                bool claimsSpell = point.spellKeys != null && point.spellKeys.Count > 0;
                if (string.IsNullOrEmpty(wantedSpell))
                {
                    if (claimsSpell) continue;
                }
                else
                {
                    if (!claimsSpell) continue;
                    if (!point.spellKeys.Contains(wantedSpell)) continue;
                }
                return point;
            }
            return null;
        }

        /// <summary>
        /// How far the bake says this frame's muzzle sits from the creature-wide pair, or zero
        /// when there is nothing to displace from.
        ///
        /// <para>The same term <see cref="EntityAssetConfig.ComposeMuzzleOffset"/> adds, so
        /// placing a point and reading it back is an identity on the frame it was placed on.
        /// Two spellings of one displacement is how a picker comes to put the mark somewhere
        /// the author did not click.</para>
        /// </summary>
        private static Vector2 FrameSweepDelta(EntityAssetConfig config, string frameName)
        {
            if (!config.HasCastMuzzlePair) return Vector2.zero;
            if (!config.TryCreatureFrameOffset(frameName, out Vector2 row)) return Vector2.zero;
            return row - config.castMuzzle;
        }

        private static bool SameScopeField(string authored, string current)
            => string.Equals(authored ?? string.Empty, current ?? string.Empty,
                             System.StringComparison.OrdinalIgnoreCase);

        private static string DescribeScope(string state, string variant, List<string> spellKeys)
        {
            string label = string.IsNullOrEmpty(variant) ? state : state + " / " + variant;
            if (spellKeys != null && spellKeys.Count > 0)
                label += " : " + string.Join(", ", spellKeys);
            return label;
        }

        // -- Refresh --------------------------------------------------------------

        /// <summary>
        /// Push the current muzzle state into the widgets and onto the stage.
        ///
        /// <para>The marker shows what RESOLVES for the frame on screen, not what the current
        /// scope happens to hold — so an author scoped to a spell that has no point of its own
        /// sees the animation's answer, which is the point the cast will really use. Showing
        /// the empty scope instead would hide the thing they are about to override.</para>
        /// </summary>
        private void RefreshMuzzleEditor()
        {
            var def = CurrentEditableMonster();
            var config = def?.assetConfig;
            bool editable = config != null;

            if (_ui.AnimMuzzleScopeDd != null)
            {
                SetDropdownOptions(_ui.AnimMuzzleScopeDd, MuzzleScopeNames, (int)_muzzleScope);
                _ui.AnimMuzzleScopeDd.interactable = editable;
            }

            RefreshMuzzleSpellOptions(def);

            if (_ui.AnimMuzzleBtnTmp != null)
                _ui.AnimMuzzleBtnTmp.text = _muzzlePlacing ? "Colocando" : "Colocar";
            if (_ui.AnimMuzzleBtnImg != null)
                _ui.AnimMuzzleBtnImg.color = _muzzlePlacing ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;

            if (!editable)
            {
                _animPreview.SetMuzzleMarker(false, Vector2.zero);
                if (_ui.AnimMuzzleReadout != null)
                    _ui.AnimMuzzleReadout.text =
                        "Solo para monstruos del catalogo. Los personajes jugables se autoran en su wave.";
                return;
            }

            string frame = _animPreview.FrameNameOnStage();
            string state = _animPreview.CurrentState.ToString();
            string variant = _animPreview.VariantKeyOnStage();
            string spell = _muzzleScope == MuzzleScope.Spell ? _muzzleSpellKey : null;

            var resolved = config.ResolveMuzzlePoint(state, variant, spell);
            bool hasRow = config.TryCreatureFrameOffset(frame, out Vector2 row);
            Vector2 normalized = EntityAssetConfig.ComposeMuzzleOffset(
                resolved, frame, config.castMuzzle, config.HasCastMuzzlePair, row, hasRow);

            bool hasAny = resolved != null || config.HasCastMuzzle;
            _animPreview.SetMuzzleMarker(hasAny || _muzzlePlacing, normalized);

            if (_ui.AnimMuzzleReadout == null) return;

            var sb = new System.Text.StringBuilder();
            if (!hasAny)
            {
                sb.Append("Sin origen propio: usa el ancla del hechizo (")
                  .Append("centro + altura). Pulsa Colocar.");
            }
            else
            {
                sb.Append(resolved != null
                        ? "Origen de: " + (string.IsNullOrEmpty(resolved.key) ? "(sin nombre)" : resolved.key)
                        : "Origen de: criatura (todo)");
                if (resolved != null && resolved.spellKeys != null && resolved.spellKeys.Count > 0)
                    sb.Append("  [").Append(string.Join(", ", resolved.spellKeys)).Append(']');
                sb.AppendLine();
                sb.Append("frac ").Append(normalized.x.ToString("0.###"))
                  .Append(" / ").Append(normalized.y.ToString("0.###"));
                if (_animPreview.TryMuzzleWorldOffset(normalized, out Vector2 world))
                    sb.Append("   =  ").Append(world.x.ToString("0.##"))
                      .Append(" / ").Append(world.y.ToString("0.##")).Append(" u");
            }
            _ui.AnimMuzzleReadout.text = sb.ToString();
        }

        /// <summary>
        /// The spells this creature could plausibly be scoping a muzzle to: the ones it
        /// auto-casts, plus any its current animation is reserved for.
        ///
        /// <para>A free-text field was the alternative and it is the shape this project has
        /// paid for repeatedly: a plain string nothing validates costs one silent miss and a
        /// muzzle that never applies, which looks exactly like a muzzle nobody placed.</para>
        /// </summary>
        private void RefreshMuzzleSpellOptions(MonsterDefinition def)
        {
            _muzzleSpellOptions.Clear();

            if (def?.autoCastList != null)
            {
                for (int i = 0; i < def.autoCastList.Length; i++)
                {
                    string key = def.autoCastList[i];
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (!_muzzleSpellOptions.Contains(key)) _muzzleSpellOptions.Add(key);
                }
            }

            var animator = _animPreview.Animator;
            if (animator != null)
            {
                var reserved = animator.ReservedSpellKeys(animator.CurrentState, animator.ActiveVariant);
                if (reserved != null)
                {
                    for (int i = 0; i < reserved.Count; i++)
                    {
                        string key = reserved[i];
                        if (string.IsNullOrWhiteSpace(key)) continue;
                        if (!_muzzleSpellOptions.Contains(key)) _muzzleSpellOptions.Add(key);
                    }
                }
            }

            if (_ui.AnimMuzzleSpellDd == null) return;

            if (_muzzleSpellOptions.Count == 0)
            {
                SetDropdownOptions(_ui.AnimMuzzleSpellDd, new[] { "(ninguno)" }, 0);
                _ui.AnimMuzzleSpellDd.interactable = false;
                _muzzleSpellKey = string.Empty;
                return;
            }

            int selected = _muzzleSpellOptions.IndexOf(_muzzleSpellKey);
            if (selected < 0) { selected = 0; _muzzleSpellKey = _muzzleSpellOptions[0]; }
            SetDropdownOptions(_ui.AnimMuzzleSpellDd, _muzzleSpellOptions, selected);
            _ui.AnimMuzzleSpellDd.interactable = _muzzleScope == MuzzleScope.Spell;
        }

        /// <summary>
        /// Leave placement mode, whoever asked.
        ///
        /// <para>Idempotent and safe before the UI exists, because it is called from the panel
        /// teardown and from every selection change — a guard at each call site is how one of
        /// them ends up missing it.</para>
        /// </summary>
        private void DisarmMuzzlePlacement()
        {
            if (!_muzzlePlacing) return;
            _muzzlePlacing = false;
            ApplyStageProbeState();
            RefreshMuzzleEditor();
        }

        /// <summary>
        /// Bring the Animation panel in front of its neighbours when placement is armed.
        ///
        /// <para>Measured live at 1600x800: the stage occupied x=[246..562] and the Picker
        /// panel x=[244..628] on top of it, so an <c>EventSystem.RaycastAll</c> at the middle
        /// of the stage returned four hits and every one of them was the Picker. The gesture
        /// the button invites could not reach the surface it names.</para>
        ///
        /// <para>It was INTERMITTENT, which is worse than broken and is the same shape the
        /// Controls editor's capture scrim shipped in: <c>DraggablePanel.OnPointerDown</c>
        /// calls <c>SetAsLastSibling</c>, so an author who had dragged the Animation panel once
        /// in this session had already raised it and the click DID land. Raising it here makes
        /// the gesture a property of pressing the button rather than of a drag nobody connects
        /// to it.</para>
        /// </summary>
        private void RaiseAnimationPanel()
        {
            if (_ui.AnimDropdown == null) return;
            _ui.AnimDropdown.transform.SetAsLastSibling();
        }

        private void ApplyStageProbeState()
        {
            if (_ui.AnimStage == null) return;

            _ui.AnimStage.raycastTarget = _muzzlePlacing;

            if (!_muzzlePlacing) return;
            if (_stageProbe == null)
            {
                _stageProbe = _ui.AnimStage.gameObject.GetComponent<EntityStagePointerProbe>();
                if (_stageProbe == null)
                    _stageProbe = _ui.AnimStage.gameObject.AddComponent<EntityStagePointerProbe>();
                _stageProbe.Bind(OnStageMuzzlePoint);
            }
        }
    }
}
