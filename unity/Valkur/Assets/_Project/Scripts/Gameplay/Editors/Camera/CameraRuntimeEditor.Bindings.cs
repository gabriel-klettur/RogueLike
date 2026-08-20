using System;
using UnityEngine;
using Valkur.Data.Feel;
using Valkur.Gameplay.Feel;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// The wiring between the sliders and the live profile, plus the derived readout that
    /// makes the one non-obvious interaction in the whole system visible.
    /// </summary>
    public sealed partial class CameraRuntimeEditor
    {
        /// <summary>Walking speed used for the readout. Matches PlayerController's default.</summary>
        private const float REFERENCE_PLAYER_SPEED = 4f;

        private void OnTunableChanged(CameraFeelTunable id, float value)
        {
            if (_syncing || _profile == null) return;

            float before = _profile.GetTunable(id);
            _profile.SetTunable(id, value);
            float after = _profile.GetTunable(id);
            if (Mathf.Approximately(before, after)) return;   // clamped to where it already was

            PushEdit(new Edit(id, before, after));
            UpdateRowLabel(id);
            SetStatus($"{CameraFeelProfile.GetInfo(id).Label} = {after:0.###} (unsaved)");
        }

        private void OnCueFieldChanged(string field, float value)
        {
            if (_syncing || _profile == null) return;

            FeelCue cue = _profile.GetCue(_selectedCue);
            switch (field)
            {
                case "traumaAdd":            cue.traumaAdd = value; break;
                case "traumaDecayPerSecond": cue.traumaDecayPerSecond = value; break;
                case "shakeFrequencyHz":     cue.shakeFrequencyHz = value; break;
                case "kickAmplitudeWu":      cue.kickAmplitudeWu = value; break;
                case "kickOmega":            cue.kickOmega = value; break;
                case "kickZeta":             cue.kickZeta = value; break;
                case "leadFreezeSeconds":    cue.leadFreezeSeconds = value; break;
                case "hitStopSeconds":       cue.hitStopSeconds = value; break;
                case "minIntervalSeconds":   cue.minIntervalSeconds = value; break;
                default: return;
            }

            FeelCue before = _profile.GetCue(_selectedCue);
            _profile.SetCue(_selectedCue, cue);
            PushEdit(new Edit(_selectedCue, before, cue));
            UpdateCueLabels(cue);
            SetStatus($"{_selectedCue}.{field} = {value:0.###} (unsaved)");
        }

        private void SelectCue(CameraFeelCue cue)
        {
            _selectedCue = cue;
            if (_ui == null || _profile == null) return;

            foreach (var pair in _ui.CueButtons)
                if (pair.Value != null)
                    pair.Value.color = pair.Key == cue
                        ? EditorUIHelpers.ACCENT_BG
                        : EditorUIHelpers.BTN_NORMAL;

            if (_ui.CueTitle != null) _ui.CueTitle.text = cue.ToString();
            SyncCueRows(_profile.GetCue(cue));
        }

        /// <summary>
        /// Fires the selected cue with a diagonal push, so a kick's direction and damping are
        /// visible rather than inferred. Firing it here rather than waiting to be hit by
        /// something is the difference between tuning and guessing.
        /// </summary>
        private void TestCue(CameraFeelCue cue)
        {
            CameraFeel.Cue(cue, new Vector2(0.7f, -0.7f));
            SetStatus($"Fired {cue}.");
        }

        // ── Sync ──────────────────────────────────────────────────────────────

        private void SyncFromProfile()
        {
            if (_ui == null || _profile == null) return;

            _syncing = true;
            foreach (var row in _ui.Rows)
            {
                row.Slider.value = _profile.GetTunable(row.Id);
                SetRowLabel(row);
            }
            _syncing = false;
        }

        private void SyncCueRows(FeelCue cue)
        {
            if (_ui == null) return;

            _syncing = true;
            foreach (var row in _ui.CueRows)
            {
                row.Slider.value = ReadCueField(cue, row.Field);
                row.Value.text = row.Slider.value.ToString("0.###");
            }
            _syncing = false;
        }

        private void UpdateCueLabels(FeelCue cue)
        {
            if (_ui == null) return;
            foreach (var row in _ui.CueRows)
                row.Value.text = ReadCueField(cue, row.Field).ToString("0.###");
        }

        private static float ReadCueField(FeelCue cue, string field)
        {
            switch (field)
            {
                case "traumaAdd":            return cue.traumaAdd;
                case "traumaDecayPerSecond": return cue.traumaDecayPerSecond;
                case "shakeFrequencyHz":     return cue.shakeFrequencyHz;
                case "kickAmplitudeWu":      return cue.kickAmplitudeWu;
                case "kickOmega":            return cue.kickOmega;
                case "kickZeta":             return cue.kickZeta;
                case "leadFreezeSeconds":    return cue.leadFreezeSeconds;
                case "hitStopSeconds":       return cue.hitStopSeconds;
                case "minIntervalSeconds":   return cue.minIntervalSeconds;
                default:                     return 0f;
            }
        }

        /// <summary>Re-reads one row's slider and label after an undo moved it underneath.</summary>
        private void SyncOneRow(CameraFeelTunable id)
        {
            if (_ui == null || _profile == null) return;
            _syncing = true;
            foreach (var row in _ui.Rows)
            {
                if (row.Id != id) continue;
                row.Slider.value = _profile.GetTunable(id);
                SetRowLabel(row);
                break;
            }
            _syncing = false;
        }

        private void ApplyPreset(CameraFeelPreset preset)
        {
            if (_profile == null) return;
            _profile.ApplyPreset(preset);
            _undo.Clear();
            _redo.Clear();
            SyncFromProfile();
            SelectCue(_selectedCue);
            SetStatus($"Preset '{preset}' applied (unsaved). Undo history cleared.");
        }

        private void UpdateRowLabel(CameraFeelTunable id)
        {
            if (_ui == null) return;
            foreach (var row in _ui.Rows)
                if (row.Id == id) { SetRowLabel(row); return; }
        }

        private void SetRowLabel(CameraEditorUIBuilder.SliderRow row)
            => row.Value.text = $"{row.Slider.value:0.###}{row.Suffix}";

        /// <summary>
        /// The derived numbers nobody can work out from the sliders alone.
        ///
        /// A critically damped follow spring settles <c>2*speed/omega</c> behind a walking
        /// player, and that lag is subtracted from the forward lead. Without seeing the net
        /// result, softening the follow to "smooth it out" quietly makes the camera trail the
        /// character — the opposite of what was intended, and the single easiest way to make
        /// this system feel wrong.
        /// </summary>
        private void RefreshReadout()
        {
            RefreshDiagnostics();
            if (_ui?.Readout == null || _profile == null) return;

            float lag = _profile.FollowOmega > 0f
                ? 2f * REFERENCE_PLAYER_SPEED / _profile.FollowOmega
                : 0f;
            float net = _profile.MoveLeadWu - lag;
            float leadSettle = _profile.LeadOmega > 0f ? 4f / _profile.LeadOmega : 0f;

            string verdict = net > 0.05f ? "ahead of the player"
                           : net < -0.05f ? "TRAILING the player"
                           : "centred";

            _ui.Readout.text =
                $"At {REFERENCE_PLAYER_SPEED:0.#} u/s walking:\n" +
                $"  follow lag   {lag:0.00} wu behind\n" +
                $"  move lead    {_profile.MoveLeadWu:0.00} wu ahead\n" +
                $"  NET          {net:+0.00;-0.00} wu — {verdict}\n" +
                $"  lead settles in {leadSettle:0.00} s";
        }

        /// <summary>
        /// What the solver is doing right now.
        ///
        /// A cue rounded away by the pixel snap looks exactly like one that never fired, and
        /// a rate limit swallowing repeats looks exactly like a broken event. Numbers are the
        /// only way to tell those apart.
        /// </summary>
        private void RefreshDiagnostics()
        {
            if (_ui?.Diagnostics == null) return;

            if (!Feel.CameraFeelDirector.HasInstance)
            {
                _ui.Diagnostics.text = "No CameraFeelDirector in the scene — nothing is driving " +
                                       "the camera.";
                return;
            }

            var live = Feel.CameraFeelDirector.Instance.Live;
            string gate = live.Suppressed ? "SUPPRESSED (editor open or paused)" : "running";
            string proxy = live.ProxyIsFollowTarget ? "installed" : "NOT the follow target";

            _ui.Diagnostics.text =
                $"{gate} · proxy {proxy}\n" +
                $"trauma {live.Trauma:0.00}  decay {live.TraumaDecay:0.0}/s  " +
                $"{live.ShakeFrequencyHz:0} Hz  spent {live.TraumaSpentThisSecond:0.0}\n" +
                $"lead {live.Lead.magnitude:0.00}  kick {live.Kick.magnitude:0.00}  " +
                $"lag {live.FollowLag.magnitude:0.00} wu\n" +
                $"applied {live.Applied.magnitude:0.000} wu = {live.AppliedPixels:0.0} px" +
                (live.AppliedPixels > 0f && live.AppliedPixels < 1f ? "  (below the snap)" : "") +
                (live.LeadFreezeRemaining > 0f ? $"\nlead frozen {live.LeadFreezeRemaining:0.00}s" : "");
        }

        // ── Persistence ───────────────────────────────────────────────────────

        private void SaveToAsset()
        {
            if (_profile == null) return;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_profile);
            UnityEditor.AssetDatabase.SaveAssets();
            SetStatus("Saved to CameraFeelProfile.asset.");
#else
            // In a build the live edit still applies for the session; there is no asset
            // database to write back to, and saying so beats a button that silently lies.
            SetStatus("Applied for this session. Saving needs the Editor.");
#endif
        }

        private void ResetToDefaults()
        {
            if (_profile == null) return;
            _profile.ResetToDefaults();
            SyncFromProfile();
            SelectCue(_selectedCue);
            SetStatus("Reset to the shipped tuning (unsaved).");
        }
    }
}
