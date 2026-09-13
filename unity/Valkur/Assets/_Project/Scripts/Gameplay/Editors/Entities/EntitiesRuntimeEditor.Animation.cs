using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// The Animation panel: the selected entity, animating, per state, variant, loadout and
    /// facing — the one thing this editor could not do.
    ///
    /// <para>It owns no art and no timing of its own: every question is put to
    /// <see cref="EntityAnimationPreviewService"/>, which binds the entity through the game's
    /// own <see cref="EntityAnimationBinder"/>. That is what makes the panel a VIEW of the
    /// data rather than a second opinion about it.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        private readonly EntityAnimationPreviewService _animPreview = new EntityAnimationPreviewService();

        /// <summary>True while the Animation dropdown is open. The stage costs a camera and a
        /// RenderTexture, so nothing is built until somebody asks to look.</summary>
        private bool _animPanelOpen;

        /// <summary>Set when the picker selection changes while the panel is closed, so opening
        /// it stages what is selected NOW rather than what was selected when it last closed.</summary>
        private bool _animSubjectDirty = true;

        /// <summary>The eight states in enum order — the dropdown index IS the enum value.</summary>
        [Valkur.Core.SelfHealingStatic("Constant state table; written once at class init and never mutated.")]
        private static readonly string[] AnimStateNames =
        {
            "Idle", "Walk", "Chase", "Cast", "Attack", "Damage", "Death", "Recover"
        };

        /// <summary>Which facing each cell of the 3x3 pad selects. Slot 4, the centre, is the
        /// "show all eight" toggle: no body faces the camera, so that cell is free for the one
        /// control the pad itself implies.</summary>
        [Valkur.Core.SelfHealingStatic("Constant pad-to-facing table; written once at class init and never mutated.")]
        private static readonly DirectionalAnimator.Direction[] AnimDirBySlot =
        {
            DirectionalAnimator.Direction.NorthWest, DirectionalAnimator.Direction.North,
            DirectionalAnimator.Direction.NorthEast,
            DirectionalAnimator.Direction.West,      DirectionalAnimator.Direction.South /*unused*/,
            DirectionalAnimator.Direction.East,
            DirectionalAnimator.Direction.SouthWest, DirectionalAnimator.Direction.South,
            DirectionalAnimator.Direction.SouthEast
        };

        private const int ANIM_DIR_SLOT_ALL = 4;

        /// <summary>Which strip cell currently wears the highlight; -1 = none. Kept so the
        /// per-frame tick can write only when the rig really changed frame.</summary>
        private int _animHighlightedFrame = -1;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void EnsureAnimationPreview()
        {
            // Parented to this component rather than to the editor CANVAS: the stage is world
            // geometry, and a SpriteRenderer under a RectTransform inherits the canvas's own
            // scale, which would silently resize every body it shows.
            _animPreview.Initialize(transform);
            if (_ui.AnimStage != null) _ui.AnimStage.texture = _animPreview.GetPreviewTexture();
        }

        private void SetAnimationPanelOpen(bool open)
        {
            _animPanelOpen = open;
            if (!open)
            {
                // Closing the panel must also drop the raycast target the placement mode put
                // on the stage: the stage is hidden but still live, and its RawImage sits
                // over the world the other panels click into.
                DisarmMuzzlePlacement();
                _animPreview.Close();
                return;
            }

            EnsureAnimationPreview();
            _animPreview.Open();
            if (_animSubjectDirty) StageSelectedEntityForAnimation();
        }

        private void TickAnimationPreview()
        {
            if (!_animPanelOpen) return;
            _animPreview.Tick();
            // Only the highlight moves per frame, and only when the rig actually changed
            // frame: a colour write per cell per frame would dirty the canvas sixty times a
            // second for a panel that is mostly still.
            int shown = _animPreview.DisplayedFrame;
            if (shown != _animHighlightedFrame) HighlightAnimationFrame(shown);
        }

        private void ShutdownAnimationPreview()
        {
            _animPreview.Shutdown();
            _animPanelOpen = false;
        }

        // ── Subject ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Called whenever the picker selection changes. Staging is deferred while the panel is
        /// closed — a bind builds up to eight rigs, and browsing the picker with the panel shut
        /// would pay for every entity walked past.
        /// </summary>
        private void NotifyAnimationSelectionChanged()
        {
            // Placement is disarmed on every selection change. Left armed, the author picks
            // another creature, clicks the stage to look at it, and writes a muzzle onto the
            // new one without meaning to -- and the only sign would be a crosshair they
            // assumed was already there.
            DisarmMuzzlePlacement();
            _animSubjectDirty = true;
            if (_animPanelOpen) StageSelectedEntityForAnimation();
        }

        private void StageSelectedEntityForAnimation()
        {
            _animSubjectDirty = false;

            var config = ResolveSelectedAssetConfig(out string label);
            _animPreview.SetSubject(config, label);
            RebuildAnimationSelectors();
            RefreshAnimationDirectionPad();
            RefreshAnimationReadouts();

            if (_ui.AnimSubjectText != null)
            {
                _ui.AnimSubjectText.text = config == null
                    ? (string.IsNullOrEmpty(_selectedKey) ? "No entity selected." : $"{label}: no asset config")
                    : label;
            }
        }

        /// <summary>
        /// The asset config behind whatever the picker has selected, monster or player, plus a
        /// name to put above the stage.
        /// </summary>
        private EntityAssetConfig ResolveSelectedAssetConfig(out string label)
        {
            label = _selectedKey ?? "";
            if (string.IsNullOrEmpty(_selectedKey)) return null;

            if (_selectedIsPlayer)
            {
                var playerDef = FindPlayerDefinition(_selectedKey);
                if (playerDef == null) return null;
                if (!string.IsNullOrEmpty(playerDef.displayName)) label = playerDef.displayName;
                return playerDef.assetConfig;
            }

            ResolveMonsterCatalogFallback();
            var def = _monsterCatalog != null ? _monsterCatalog.GetByKey(_selectedKey) : null;
            if (def == null) return null;
            if (!string.IsNullOrEmpty(def.displayName)) label = def.displayName;
            return def.assetConfig;
        }

        // ── Selectors ────────────────────────────────────────────────────────────

        private void RebuildAnimationSelectors()
        {
            SetDropdownOptions(_ui.AnimStateDd, AnimStateNames, (int)_animPreview.CurrentState);

            var variants = new List<string> { "Base set" };
            int variantCount = _animPreview.VariantCount(_animPreview.CurrentState);
            for (int i = 0; i < variantCount; i++)
                variants.Add(_animPreview.DescribeVariant(_animPreview.CurrentState, i));
            SetDropdownOptions(_ui.AnimVariantDd, variants, _animPreview.CurrentVariant + 1);

            var loadouts = new List<string> { "Base art" };
            var config = _animPreview.HasSubject ? ResolveSelectedAssetConfig(out _) : null;
            if (config?.loadouts != null)
            {
                for (int i = 0; i < config.loadouts.Count; i++)
                {
                    var loadout = config.loadouts[i];
                    if (loadout != null && !string.IsNullOrEmpty(loadout.key)) loadouts.Add(loadout.key);
                }
            }
            int loadoutIndex = 0;
            for (int i = 1; i < loadouts.Count; i++)
            {
                if (string.Equals(loadouts[i], _animPreview.CurrentLoadout,
                                  System.StringComparison.OrdinalIgnoreCase))
                {
                    loadoutIndex = i;
                    break;
                }
            }
            SetDropdownOptions(_ui.AnimLoadoutDd, loadouts, loadoutIndex);
        }

        /// <summary>
        /// Refills a dropdown without firing its callback — <c>ClearOptions</c> alone leaves
        /// the value at an index the new list may not have, and <c>SetValueWithoutNotify</c> is
        /// what keeps a rebuild from looking like the author having chosen something.
        /// </summary>
        private static void SetDropdownOptions(TMPro.TMP_Dropdown dropdown,
                                               IReadOnlyList<string> options, int selected)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            var copy = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++) copy.Add(options[i]);
            dropdown.AddOptions(copy);
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, Mathf.Max(0, copy.Count - 1)));
            dropdown.RefreshShownValue();
        }

        private void RefreshAnimationDirectionPad()
        {
            if (_ui.AnimDirBtnImgs == null) return;

            bool all = _animPreview.ShowAllDirections;
            for (int slot = 0; slot < _ui.AnimDirBtnImgs.Length; slot++)
            {
                var img = _ui.AnimDirBtnImgs[slot];
                if (img == null) continue;

                bool on = slot == ANIM_DIR_SLOT_ALL
                    ? all
                    : !all && AnimDirBySlot[slot] == _animPreview.CurrentDirection;
                img.color = on ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;

                var tmp = _ui.AnimDirBtnTmps != null && slot < _ui.AnimDirBtnTmps.Length
                    ? _ui.AnimDirBtnTmps[slot] : null;
                if (tmp != null) tmp.color = on ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_SECONDARY;
            }
        }

        /// <summary>
        /// Everything the panel says about the pose on screen: the info line, the frame strip
        /// and the transport. One entry point so no caller can update two of the three and
        /// leave the panel describing a pose it is no longer showing.
        /// </summary>
        private void RefreshAnimationReadouts()
        {
            RefreshAnimationInfo();
            RefreshAnimationStrip();
            RefreshAnimationTransport();
            RefreshAnimationPacingEditors();
            // The muzzle readout describes the FRAME on screen, so it belongs in the one
            // refresh the panel has -- a separate call site is how a panel comes to show a
            // measurement about a pose it stopped drawing.
            RefreshMuzzleEditor();
        }

        private void RefreshAnimationInfo()
        {
            if (_ui.AnimInfoText == null) return;

            if (!_animPreview.HasSubject)
            {
                _ui.AnimInfoText.text = string.IsNullOrEmpty(_selectedKey)
                    ? "Pick an entity in the Picker."
                    : "This entity binds no art: its idle state resolved to no frames.";
                return;
            }

            var sb = new System.Text.StringBuilder();

            int frames = _animPreview.FrameCount;
            float frameSeconds = _animPreview.FrameSeconds;
            float fps = frameSeconds > 0.0001f ? 1f / frameSeconds : 0f;

            sb.Append(_animPreview.CurrentState).Append(" · ")
              .Append(_animPreview.ShowAllDirections ? "all 8" : _animPreview.CurrentDirection.ToString())
              .Append(" · ").Append(frames).Append(" frame(s)\n");

            sb.Append($"{frameSeconds * 1000f:0} ms/frame ({fps:0.#} fps) · total {_animPreview.StateSeconds:0.00}s\n");

            // The three multipliers, separately: a single product hides WHICH dial is doing
            // the work, and they are authored in three different places.
            sb.Append($"speed: entity {_animPreview.EntitySpeedMultiplier:0.##}x · " +
                      $"state {_animPreview.StateSpeedMultiplier:0.##}x · " +
                      $"variant {_animPreview.VariantSpeedMultiplier:0.##}x\n");

            sb.Append(_animPreview.DescribePlaybackRule());

            string fallback = _animPreview.DescribeFallback();
            if (!string.IsNullOrEmpty(fallback)) sb.Append("\n! ").Append(fallback);

            string warnings = DescribeAnimationWarnings();
            if (!string.IsNullOrEmpty(warnings)) sb.Append('\n').Append(warnings);

            string combat = DescribeAnimationCombatTiming();
            if (!string.IsNullOrEmpty(combat)) sb.Append('\n').Append(combat);

            if (_animPreview.IsPaused) sb.Append("\npaused — the ghost is the previous frame");

            _ui.AnimInfoText.text = sb.ToString();
        }

        /// <summary>
        /// What retiming this animation does to the FIGHT, for an attack on a monster.
        ///
        /// <para><c>AttackState</c> attempts exactly one blow per swing, at the windup, and the
        /// melee cooldown only REFUSES attempts — it does not schedule them. So the realised
        /// interval between hits is the swing period rounded UP to the next multiple that
        /// clears the cooldown, and lengthening an animation can change a monster's damage per
        /// second without anybody touching a damage number: measured on knight_red, a 0.75 s
        /// swing landed every 1.5 s and a 1.2 s swing every 1.2 s — 25 % more melee DPS.</para>
        /// </summary>
        private string DescribeAnimationCombatTiming()
        {
            if (_animPreview.CurrentState != DirectionalAnimator.AnimState.Attack) return "";
            if (_selectedIsPlayer || string.IsNullOrEmpty(_selectedKey)) return "";

            var def = _monsterCatalog != null ? _monsterCatalog.GetByKey(_selectedKey) : null;
            if (def == null) return "";

            float windup   = def.stats.attackWindupSeconds;
            float cooldown = def.stats.meleeCooldown;

            // The swing period AttackState actually uses: the historical windup + 0.3 s floor,
            // or the animation when it is longer.
            float swing = Mathf.Max(windup + 0.3f, _animPreview.StateSeconds);
            if (swing <= 0.0001f) return "";

            int attemptsPerHit = Mathf.Max(1, Mathf.CeilToInt(cooldown / swing));
            float realised = attemptsPerHit * swing;

            string refused = attemptsPerHit > 1
                ? $" ({attemptsPerHit - 1} attempt(s) refused by the cooldown)"
                : "";
            return $"hit: windup {windup:0.00}s · swing {swing:0.00}s · cooldown {cooldown:0.00}s " +
                   $"-> one blow every {realised:0.00}s{refused}";
        }

        // ── Frame strip ──────────────────────────────────────────────────────────

        /// <summary>
        /// Refills the strip for the pose on screen. Called on every change of subject, state,
        /// direction, variant or loadout — never per frame.
        /// </summary>
        private void RefreshAnimationStrip()
        {
            if (_ui.AnimStripCellImgs == null) return;

            var frames = _animPreview.CurrentFrames;
            int count  = frames?.Length ?? 0;
            int slots  = _ui.AnimStripCellImgs.Length;

            for (int i = 0; i < slots; i++)
            {
                var cell = _ui.AnimStripCellImgs[i];
                if (cell == null) continue;

                bool used = i < count;
                cell.gameObject.SetActive(used);
                if (!used) continue;

                var thumb = _ui.AnimStripThumbs[i];
                if (thumb != null)
                {
                    thumb.sprite  = frames[i];
                    thumb.enabled = frames[i] != null;
                }
                if (_ui.AnimStripLabels[i] != null) _ui.AnimStripLabels[i].text = i.ToString();
                cell.color = EditorUIHelpers.SLOT_BG;
            }

            if (_ui.AnimStripOverflowText != null)
            {
                _ui.AnimStripOverflowText.text = count > slots
                    ? $"+{count - slots} more frame(s) not shown"
                    : "";
            }

            _animHighlightedFrame = -1;
            HighlightAnimationFrame(_animPreview.DisplayedFrame);
        }

        /// <summary>Moves the highlight to <paramref name="index"/>, clearing the previous one.</summary>
        private void HighlightAnimationFrame(int index)
        {
            if (_ui.AnimStripCellImgs == null) return;

            if (_animHighlightedFrame >= 0 && _animHighlightedFrame < _ui.AnimStripCellImgs.Length)
            {
                var previous = _ui.AnimStripCellImgs[_animHighlightedFrame];
                if (previous != null) previous.color = EditorUIHelpers.SLOT_BG;
            }

            _animHighlightedFrame = index;
            if (index < 0 || index >= _ui.AnimStripCellImgs.Length) return;

            var cell = _ui.AnimStripCellImgs[index];
            if (cell != null && cell.gameObject.activeSelf) cell.color = EditorUIHelpers.SLOT_SELECTED;
        }

        private void RefreshAnimationTransport()
        {
            if (_ui.AnimPlayPauseTmp != null)
                _ui.AnimPlayPauseTmp.text = _animPreview.IsPaused ? "Play" : "Pause";

            if (_ui.AnimReverseImg != null)
                _ui.AnimReverseImg.color = _animPreview.IsReversed
                    ? EditorUIHelpers.BTN_ACTIVE
                    : EditorUIHelpers.BTN_NORMAL;
            if (_ui.AnimReverseTmp != null)
                _ui.AnimReverseTmp.color = _animPreview.IsReversed
                    ? EditorUIHelpers.ACCENT
                    : EditorUIHelpers.TEXT_SECONDARY;
        }

        // ── Panel callbacks ──────────────────────────────────────────────────────

        private void OnAnimationStateChanged(int index)
        {
            if (index < 0 || index >= AnimStateNames.Length) return;
            _animPreview.SetState((DirectionalAnimator.AnimState)index);
            RebuildAnimationSelectors();
            RefreshAnimationReadouts();
        }

        private void OnAnimationVariantChanged(int index)
        {
            _animPreview.SetVariant(index - 1);
            RefreshAnimationReadouts();
        }

        private void OnAnimationLoadoutChanged(int index)
        {
            string key = null;
            if (index > 0 && _ui.AnimLoadoutDd != null &&
                index < _ui.AnimLoadoutDd.options.Count)
                key = _ui.AnimLoadoutDd.options[index].text;

            _animPreview.SetLoadout(key);
            // A loadout REPLACES the rotations it declares, so the variant list this panel is
            // showing may not exist under the new art.
            RebuildAnimationSelectors();
            RefreshAnimationReadouts();
        }

        private void OnAnimationDirectionSlot(int slot)
        {
            if (slot == ANIM_DIR_SLOT_ALL)
                _animPreview.SetShowAllDirections(!_animPreview.ShowAllDirections);
            else if (slot >= 0 && slot < AnimDirBySlot.Length)
                _animPreview.SetDirection(AnimDirBySlot[slot]);

            RefreshAnimationDirectionPad();
            RefreshAnimationReadouts();
        }

        private void OnAnimationZoom(float steps)
        {
            _animPreview.ZoomBy(steps);
        }

        private void OnAnimationTogglePlay()
        {
            _animPreview.TogglePaused();
            RefreshAnimationTransport();
        }

        private void OnAnimationStep(int delta)
        {
            _animPreview.StepFrame(delta);
            RefreshAnimationTransport();
            HighlightAnimationFrame(_animPreview.DisplayedFrame);
        }

        private void OnAnimationToggleReverse()
        {
            _animPreview.SetReversed(!_animPreview.IsReversed);
            RefreshAnimationTransport();
            RefreshAnimationStrip();
        }

        /// <summary>
        /// Clicking a cell of the strip jumps to that frame — and pauses, because a jump the
        /// clock immediately overwrites reads as a cell that does nothing.
        /// </summary>
        private void OnAnimationStripCell(int index)
        {
            _animPreview.ShowFrame(index);
            RefreshAnimationTransport();
            HighlightAnimationFrame(_animPreview.DisplayedFrame);
        }
    }
}
