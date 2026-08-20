using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data.Feel;

namespace Valkur.Gameplay.Editors.CameraFeelEditor
{
    /// <summary>
    /// Panel visibility, the tutorial, and the undo stack.
    ///
    /// Undo matters more here than in most editors: tuning is a search, and a search you
    /// cannot back out of is one where people stop trying things. Every slider drag pushes
    /// one entry, so a bad experiment costs a click rather than a memory of what the number
    /// used to be.
    /// </summary>
    public sealed partial class CameraRuntimeEditor
    {
        private const int UNDO_DEPTH = 64;

        /// <summary>
        /// One reversible change. Tunables and cue fields share a stack, so undo walks the
        /// edit history in the order it happened rather than in two independent orders.
        /// </summary>
        private readonly struct Edit
        {
            public readonly bool IsCue;
            public readonly CameraFeelTunable Tunable;
            public readonly CameraFeelCue Cue;
            public readonly FeelCue Before;
            public readonly FeelCue After;
            public readonly float BeforeValue;
            public readonly float AfterValue;

            public Edit(CameraFeelTunable tunable, float before, float after)
            {
                IsCue = false; Tunable = tunable; Cue = default;
                Before = default; After = default;
                BeforeValue = before; AfterValue = after;
            }

            public Edit(CameraFeelCue cue, FeelCue before, FeelCue after)
            {
                IsCue = true; Tunable = default; Cue = cue;
                Before = before; After = after;
                BeforeValue = 0f; AfterValue = 0f;
            }
        }

        private readonly List<Edit> _undo = new List<Edit>(UNDO_DEPTH);
        private readonly List<Edit> _redo = new List<Edit>(UNDO_DEPTH);
        private readonly HashSet<string> _openPanels = new HashSet<string>();

        private GameObject _tutorial;
        private TextMeshProUGUI _tutorialBody;
        private TextMeshProUGUI _tutorialStepLabel;
        private int _tutorialStep;

        /// <summary>
        /// What a newcomer to this editor has to know before touching anything, in the order
        /// the mistakes happen.
        /// </summary>
        [Valkur.Core.SelfHealingStatic("Immutable tutorial copy, built once from string " +
            "literals. Never written to and holds no Unity objects.")]
        private static readonly (string title, string body)[] TUTORIAL_STEPS =
        {
            ("1 / 5  What moves the camera",
             "The camera is not moved directly. A proxy transform is moved, and Cinemachine " +
             "copies it exactly — the transposer's damping is zero, so the copy is 1:1.\n\n" +
             "Its position is:\n" +
             "    smoothFollow(player) + lead + shake + kick\n\n" +
             "Every panel here edits one of those four terms."),

            ("2 / 5  Follow and lead fight each other",
             "A critically damped spring chasing a walking player settles 2 x speed / omega " +
             "BEHIND them. At 4 units per second and a follow spring of 16 that is half a " +
             "unit of lag, and it is subtracted from the forward lead.\n\n" +
             "So lowering the follow spring to 'smooth things out' does not smooth anything: " +
             "it drags the camera backwards until it trails the character.\n\n" +
             "Put softness in the LEAD spring instead. The Live panel shows the net result " +
             "and says outright whether the camera is ahead or trailing."),

            ("3 / 5  Anything under one pixel does not exist",
             "CameraPixelSnap rounds the final camera position to the screen-pixel grid, so a " +
             "motion smaller than a pixel is not subtle — it is erased, or it flickers between " +
             "two rows.\n\n" +
             "Shake is trauma SQUARED times max shake, which means a trauma of 0.15 is roughly " +
             "a quarter as strong as 0.30, not half. Three shipped cues were authored below " +
             "the pixel floor and fired into nothing until a test caught them.\n\n" +
             "The Live panel reports the applied offset in pixels."),

            ("4 / 5  Weight is not amplitude",
             "Two cues that shake equally hard still feel different. What separates them:\n\n" +
             "  frequency  - high reads metallic, low reads heavy\n" +
             "  damping    - zeta 1 snaps back, below 1 overshoots once\n" +
             "  direction  - a hit you land pushes toward the victim; one you take pushes away\n" +
             "  lead freeze- stopping the anticipation is what reads as being interrupted\n\n" +
             "Rewards carry no kick at all. A reward that punches the frame reads as damage."),

            ("5 / 5  Working here",
             "Every slider applies immediately — no apply button, no restart.\n\n" +
             "  TEST THIS CUE   fires the selected beat so you can feel it now\n" +
             "  Undo / Redo     walks the edit history\n" +
             "  Presets         whole-camera starting points; Rigid is how it behaved before\n" +
             "  MovementOnly    silences every transient so you can judge the motion alone\n" +
             "  SAVE TO ASSET   writes CameraFeelProfile.asset (Editor only)\n\n" +
             "Nothing is written to disk until you press SAVE."),
        };

        // ── Panels ────────────────────────────────────────────────────────────

        private void TogglePanel(string panelId)
        {
            if (!_openPanels.Remove(panelId)) _openPanels.Add(panelId);
            ApplyPanelVisibility();
        }

        /// <summary>The layout the editor opens with. See CameraEditorUIBuilder.DefaultPanels.</summary>
        private void OpenDefaultPanels()
        {
            _openPanels.Clear();
            foreach (var id in CameraEditorUIBuilder.DefaultPanels) _openPanels.Add(id);
            ApplyPanelVisibility();
        }

        private void OpenAllPanels()
        {
            _openPanels.Clear();
            foreach (var id in CameraEditorUIBuilder.AllPanels) _openPanels.Add(id);
            ApplyPanelVisibility();
        }

        private void ApplyPanelVisibility()
        {
            if (_ui == null) return;

            foreach (var pair in _ui.Panels)
            {
                bool open = _openPanels.Contains(pair.Key);
                if (pair.Value != null) pair.Value.SetActive(open);

                _ui.MenuButtons.TryGetValue(pair.Key, out Image img);
                _ui.MenuLabels.TryGetValue(pair.Key, out TextMeshProUGUI tmp);
                CameraEditorUIBuilder.ApplyMenuButtonStyle(img, tmp, open);
            }
        }

        // ── Tutorial ──────────────────────────────────────────────────────────

        private void BuildTutorial()
        {
            _tutorial = EditorUIHelpers.MakePanel("CameraTutorial", _root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 300f));

            var vlg = _tutorial.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 14);
            vlg.spacing = 8f;
            vlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeTitleBar(_tutorial.transform, "CAMERA EDITOR");

            _tutorialStepLabel = EditorUIHelpers.AddLabel(_tutorial.transform, "", 13f);
            _tutorialStepLabel.fontStyle = FontStyles.Bold;
            _tutorialStepLabel.color = EditorUIHelpers.ACCENT;

            var bodyGo = EditorUIHelpers.CreateUI("Body", _tutorial.transform);
            bodyGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _tutorialBody = bodyGo.AddComponent<TextMeshProUGUI>();
            _tutorialBody.fontSize = 11.5f;
            _tutorialBody.color = EditorUIHelpers.TEXT_PRIMARY;
            _tutorialBody.alignment = TextAlignmentOptions.TopLeft;
            _tutorialBody.enableWordWrapping = true;

            var nav = EditorUIHelpers.CreateUI("Nav", _tutorial.transform);
            nav.AddComponent<LayoutElement>().preferredHeight = 30f;
            var hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(nav.transform, "Prev", () => StepTutorial(-1), 26f, 11f);
            EditorUIHelpers.MakeButton(nav.transform, "Next", () => StepTutorial(+1), 26f, 11f);
            EditorUIHelpers.MakeButton(nav.transform, "Close",
                                       () => _tutorial.SetActive(false), 26f, 11f);

            _tutorialStep = 0;
            RefreshTutorial();
            _tutorial.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorial == null) return;
            bool show = !_tutorial.activeSelf;
            _tutorial.SetActive(show);
            if (!show) return;
            _tutorial.transform.SetAsLastSibling();
            RefreshTutorial();
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialBody == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = title;
            _tutorialBody.text = body;
        }

        // ── Undo ──────────────────────────────────────────────────────────────

        private void PushEdit(Edit edit)
        {
            _undo.Add(edit);
            if (_undo.Count > UNDO_DEPTH) _undo.RemoveAt(0);
            _redo.Clear();   // a fresh edit invalidates the redo branch
        }

        private void Undo()
        {
            if (_undo.Count == 0) { SetStatus("Nothing to undo."); return; }

            Edit edit = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(edit);
            ApplyEdit(edit, forward: false);
            SetStatus($"Undo — {Describe(edit)} ({_undo.Count} left)");
        }

        private void Redo()
        {
            if (_redo.Count == 0) { SetStatus("Nothing to redo."); return; }

            Edit edit = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(edit);
            ApplyEdit(edit, forward: true);
            SetStatus($"Redo — {Describe(edit)}");
        }

        private void ApplyEdit(Edit edit, bool forward)
        {
            if (_profile == null) return;

            if (edit.IsCue)
            {
                _profile.SetCue(edit.Cue, forward ? edit.After : edit.Before);
                if (edit.Cue == _selectedCue) SyncCueRows(_profile.GetCue(edit.Cue));
                else SelectCue(edit.Cue);
                return;
            }

            _profile.SetTunable(edit.Tunable, forward ? edit.AfterValue : edit.BeforeValue);
            SyncOneRow(edit.Tunable);
        }

        private static string Describe(Edit edit)
            => edit.IsCue
                ? edit.Cue.ToString()
                : CameraFeelProfile.GetInfo(edit.Tunable).Label;
    }
}
