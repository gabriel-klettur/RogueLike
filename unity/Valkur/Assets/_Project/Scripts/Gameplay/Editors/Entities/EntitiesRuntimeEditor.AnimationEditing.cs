using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Editing half of the Animation panel: the three speed multipliers, the hold, and the
    /// direction layout.
    ///
    /// <para>These are the knobs that survive a re-import — <c>PlayerFramesImporter</c> and
    /// <c>MonsterFramesImporter</c> apply pacing only when they CREATE a variant, so an
    /// authored value always wins. Frame ORDER is deliberately not editable here for the
    /// opposite reason: the importers rewrite the frame lists wholesale from the Python
    /// manifest, so a reordering made in this panel would last exactly until the next wave.</para>
    ///
    /// <para>Everything writes through <see cref="CommitDefinitionEdit"/>, the same seam the
    /// stat rows use: <c>EditorUtility.SetDirty</c> (never <c>Undo.RecordObject</c> — the
    /// incident that cost 193 building templates), re-apply to live monsters, and an explicit
    /// Save to reach the disk.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>The three layouts, in enum order — the dropdown index IS the enum value.</summary>
        [Valkur.Core.SelfHealingStatic("Constant layout table; written once at class init and never mutated.")]
        private static readonly string[] AnimLayoutNames =
        {
            "Auto", "8 directions", "4 directions (S,W,E,N)"
        };

        /// <summary>Floor shared with <c>EntityAssetConfig</c>'s own [Min] attributes. Below
        /// this a multiplier stops being slow and becomes stopped.</summary>
        private const float ANIM_MIN_SPEED = 0.05f;

        /// <summary>
        /// Pushes the current values into the pacing widgets. Called from the same refresh that
        /// redraws the strip, so the fields always describe the pose on screen.
        /// </summary>
        private void RefreshAnimationPacingEditors()
        {
            var config = ResolveSelectedAssetConfig(out _);
            bool editable = config != null && !_selectedIsPlayer;

            SetNumberField(_ui.AnimEntitySpeedInput, _animPreview.EntitySpeedMultiplier, editable);
            SetNumberField(_ui.AnimStateSpeedInput,  _animPreview.StateSpeedMultiplier,  editable);

            // A variant's pacing is only addressable when a variant is selected; on the base
            // set the field would be a control with nothing behind it.
            bool variantSelected = editable && _animPreview.CurrentVariant >= 0;
            SetNumberField(_ui.AnimVariantSpeedInput, _animPreview.VariantSpeedMultiplier, variantSelected);

            if (_ui.AnimHoldToggle != null)
            {
                _ui.AnimHoldToggle.SetIsOnWithoutNotify(_animPreview.HoldsLastFrame);
                _ui.AnimHoldToggle.interactable = variantSelected;
            }

            int layout = config != null ? (int)config.directionLayout : 0;
            SetDropdownOptions(_ui.AnimLayoutDd, AnimLayoutNames, layout);
            if (_ui.AnimLayoutDd != null) _ui.AnimLayoutDd.interactable = editable;
        }

        private static void SetNumberField(TMPro.TMP_InputField field, float value, bool editable)
        {
            if (field == null) return;
            field.SetTextWithoutNotify(value.ToString("0.##", CultureInfo.InvariantCulture));
            field.interactable = editable;
        }

        // ── Validation ───────────────────────────────────────────────────────────

        /// <summary>
        /// The two ways a frame list breaks SILENTLY, checked against the asset the stage is
        /// showing.
        ///
        /// <para>A linear list is cut into eight CONTIGUOUS buckets of <c>n/8</c>, and whatever
        /// does not divide is DROPPED with nothing logged. Worse, a missing sprite shortens the
        /// list, which shifts every later bucket — the entity then faces the wrong way, in an
        /// arc, and the art itself is fine. Neither is visible in the Inspector, where both
        /// read as a list of sprites.</para>
        /// </summary>
        private string DescribeAnimationWarnings()
        {
            var config = ResolveSelectedAssetConfig(out _);
            if (config == null) return "";

            List<Sprite> sheets = SheetsForState(config, _animPreview.CurrentState);
            if (sheets == null || sheets.Count == 0) return "";

            var problems = new List<string>();

            int nulls = 0;
            for (int i = 0; i < sheets.Count; i++) if (sheets[i] == null) nulls++;
            if (nulls > 0)
                problems.Add($"{nulls} empty slot(s) — every later direction is shifted");

            // The four-direction layout is the one case where eight is the wrong divisor.
            if (config.directionLayout != EntitySheetDirectionLayout.FourDirectional_S_W_E_N)
            {
                int remainder = sheets.Count % 8;
                if (remainder != 0)
                    problems.Add($"{sheets.Count} frames is not a multiple of 8 — {remainder} dropped");
            }
            else if (sheets.Count % 4 != 0)
            {
                problems.Add($"{sheets.Count} frames is not a multiple of 4 — {sheets.Count % 4} dropped");
            }

            return problems.Count == 0 ? "" : "! " + string.Join("; ", problems);
        }

        private static List<Sprite> SheetsForState(EntityAssetConfig config,
                                                   DirectionalAnimator.AnimState state)
            => state switch
            {
                DirectionalAnimator.AnimState.Idle    => config.idleSheets,
                DirectionalAnimator.AnimState.Walk    => config.walkSheets,
                DirectionalAnimator.AnimState.Chase   => config.chaseSheets,
                DirectionalAnimator.AnimState.Cast    => config.castSheets,
                DirectionalAnimator.AnimState.Attack  => config.attackSheets,
                DirectionalAnimator.AnimState.Damage  => config.damageSheets,
                DirectionalAnimator.AnimState.Death   => config.deathSheets,
                DirectionalAnimator.AnimState.Recover => config.recoverSheets,
                _                                     => null
            };

        // ── Commit ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a multiplier and hands it to <paramref name="apply"/>, refusing garbage
        /// rather than writing a broken value — the contract <see cref="AddFloatStat"/> already
        /// uses for the stat rows.
        /// </summary>
        private bool TryCommitAnimationSpeed(string raw, string label, System.Action<float> apply)
        {
            var def = CurrentEditableMonster();
            if (def == null)
            {
                SetStatus("Animation pacing is editable on monsters only.");
                RefreshAnimationPacingEditors();
                return false;
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                SetStatus($"'{raw}' is not a number — {label} unchanged.");
                RefreshAnimationPacingEditors();
                return false;
            }

            apply(Mathf.Max(ANIM_MIN_SPEED, parsed));
            CommitDefinitionEdit(def, label);
            // Re-bind the stage so the change is visible on the very animation being watched,
            // which is the whole reason these fields sit under the preview.
            StageSelectedEntityForAnimation();
            return true;
        }

        /// <summary>The selected definition when it is one this editor may write to.</summary>
        private MonsterDefinition CurrentEditableMonster()
        {
            if (_selectedIsPlayer || string.IsNullOrEmpty(_selectedKey)) return null;
            ResolveMonsterCatalogFallback();
            var def = _monsterCatalog != null ? _monsterCatalog.GetByKey(_selectedKey) : null;
            // Seeded HERE and not at commit time: by commit time the edit has already happened,
            // so a lazy seed would make the FIRST change to each definition the one change that
            // cannot be undone -- silently, and on the edit an author is most likely trying out.
            SeedDefinitionSnapshot(def);
            return def;
        }

        private void OnAnimationEntitySpeedCommitted(string raw)
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null) { RefreshAnimationPacingEditors(); return; }
            TryCommitAnimationSpeed(raw, "Entity anim speed",
                v => def.assetConfig.scaleConfig.animationSpeedMultiplier = v);
        }

        private void OnAnimationStateSpeedCommitted(string raw)
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null) { RefreshAnimationPacingEditors(); return; }

            string state = AnimStateNames[(int)_animPreview.CurrentState].ToLowerInvariant();
            TryCommitAnimationSpeed(raw, $"{state} speed",
                v => def.assetConfig.SetStateSpeedMultiplier(state, v));
        }

        private void OnAnimationVariantSpeedCommitted(string raw)
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null || _animPreview.CurrentVariant < 0)
            {
                RefreshAnimationPacingEditors();
                return;
            }

            TryCommitAnimationSpeed(raw, "Variant speed",
                v => ApplyToSelectedVariant(def.assetConfig, speed: v, hold: null));
        }

        private void OnAnimationHoldToggled(bool hold)
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null || _animPreview.CurrentVariant < 0)
            {
                RefreshAnimationPacingEditors();
                return;
            }

            ApplyToSelectedVariant(def.assetConfig, speed: null, hold: hold);
            CommitDefinitionEdit(def, "Hold last frame");
            StageSelectedEntityForAnimation();
        }

        /// <summary>
        /// Writes pacing onto the variant the stage is showing, found BY ITS AUTHORED KEY.
        ///
        /// <para>By index would be wrong and silently so: the binder DROPS variants that
        /// resolved to no frames, so the rig's index 2 can be the asset's index 3, and a write
        /// by position would retune the neighbour. The key is the same one
        /// <c>MonsterFramesImporter</c> matches on when it refreshes sheets.</para>
        /// </summary>
        private void ApplyToSelectedVariant(EntityAssetConfig config, float? speed, bool? hold)
        {
            string key = _animPreview.Animator != null
                ? _animPreview.Animator.VariantLabel(_animPreview.CurrentState, _animPreview.CurrentVariant)
                : null;
            if (string.IsNullOrEmpty(key))
            {
                SetStatus("That variant has no key in the asset — nothing to write to.");
                return;
            }

            switch (_animPreview.CurrentState)
            {
                case DirectionalAnimator.AnimState.Attack:
                    foreach (var variant in config.attackVariants ?? new List<AttackVariant>())
                    {
                        if (variant == null || !KeyMatches(variant.key, key)) continue;
                        if (speed.HasValue) variant.animationSpeedMultiplier = speed.Value;
                        if (hold.HasValue)  variant.holdLastFrame = hold.Value;
                        return;
                    }
                    break;

                case DirectionalAnimator.AnimState.Cast:
                    foreach (var variant in config.castVariants ?? new List<CastVariant>())
                    {
                        if (variant == null || !KeyMatches(variant.key, key)) continue;
                        if (speed.HasValue) variant.animationSpeedMultiplier = speed.Value;
                        if (hold.HasValue)  variant.holdLastFrame = hold.Value;
                        return;
                    }
                    break;

                default:
                    string state = AnimStateNames[(int)_animPreview.CurrentState].ToLowerInvariant();
                    var group = config.FindStateVariants(state);
                    if (group != null)
                    {
                        foreach (var variant in group)
                        {
                            if (variant == null || !KeyMatches(variant.key, key)) continue;
                            if (speed.HasValue) variant.animationSpeedMultiplier = speed.Value;
                            if (hold.HasValue)  variant.holdLastFrame = hold.Value;
                            return;
                        }
                    }
                    break;
            }

            SetStatus($"No variant called '{key}' in the asset — nothing written. " +
                      "A loadout's variants are edited on the loadout itself.");
        }

        private static bool KeyMatches(string a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The direction layout: how a linear frame list is cut into facings. Auto guesses from
        /// the frame count, which is what every asset authored before the field existed still
        /// relies on — so changing it is a real decision and not a default worth hiding.
        /// </summary>
        private void OnAnimationLayoutChanged(int index)
        {
            var def = CurrentEditableMonster();
            if (def?.assetConfig == null)
            {
                SetStatus("Direction layout is editable on monsters only.");
                RefreshAnimationPacingEditors();
                return;
            }

            if (index < 0 || index >= AnimLayoutNames.Length) return;
            def.assetConfig.directionLayout = (EntitySheetDirectionLayout)index;
            CommitDefinitionEdit(def, "Direction layout");
            StageSelectedEntityForAnimation();
        }
    }
}
