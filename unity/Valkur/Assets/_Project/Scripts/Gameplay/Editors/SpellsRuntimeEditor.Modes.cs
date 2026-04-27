using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Editors.EditorKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — Modes panel callbacks (Add/Remove/Save/Reload/Undo/Redo)
    /// and Tutorial logic. PHASE 1: callbacks are stubs that emit a status toast
    /// and (where applicable) push a logging Undo command. The catalog is NOT
    /// mutated.
    /// </summary>
    public partial class SpellsRuntimeEditor : SingletonMonoBehaviour<SpellsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Add / Remove ──

        private void OnAddSpell()
        {
            EditorModal.Prompt(_canvas.transform, "New Spell — key:", "new_spell",
                onOk: v =>
                {
                    var key = (v ?? string.Empty).Trim();
                    Debug.Log($"[SpellsEditor] Add Spell stub — key='{key}'");
                    SetStatus($"Add Spell — phase 2 (key={key})");
                    // Phase 1: undo command is a no-op log so the stack isn't empty.
                    _undo.Do(new UndoStack.LambdaCommand(
                        $"Add '{key}'",
                        () => Debug.Log($"[SpellsEditor] (stub) redo Add '{key}'"),
                        () => Debug.Log($"[SpellsEditor] (stub) undo Add '{key}'")));
                });
        }

        private void OnRemoveSpell()
        {
            if (string.IsNullOrEmpty(_selectedKey))
            {
                Toast("Remove: select a spell first.");
                return;
            }
            var key = _selectedKey;
            EditorModal.Confirm(_canvas.transform,
                "Delete spell?",
                $"Are you sure you want to delete '{key}'?\nThis is a phase-1 stub — no data will be modified.",
                onOk: () =>
                {
                    Debug.Log($"[SpellsEditor] Remove Spell stub — key='{key}'");
                    SetStatus($"Remove Spell — phase 2 (key={key})");
                    _undo.Do(new UndoStack.LambdaCommand(
                        $"Remove '{key}'",
                        () => Debug.Log($"[SpellsEditor] (stub) redo Remove '{key}'"),
                        () => Debug.Log($"[SpellsEditor] (stub) undo Remove '{key}'")));
                });
        }

        // ── File ──

        private void OnSave()
        {
            Debug.Log("[SpellsEditor] Save stub — would persist to JSON in phase 2.");
            SetStatus("Save — phase 2");
        }

        private void OnReload()
        {
            Debug.Log("[SpellsEditor] Reload stub — would reload catalog from JSON in phase 2.");
            SetStatus("Reload — phase 2");
        }

        // ── Undo / Redo ──

        private void OnUndo()
        {
            if (_undo.CanUndo)
            {
                _undo.Undo();
                SetStatus($"Undo — {_undo.UndoCount} steps remain");
            }
            else
            {
                SetStatus("Undo — nothing to undo");
            }
        }

        private void OnRedo()
        {
            if (_undo.CanRedo)
            {
                _undo.Redo();
                SetStatus($"Redo — {_undo.RedoCount} steps remain");
            }
            else
            {
                SetStatus("Redo — nothing to redo");
            }
        }

        // ── Tutorial ──

        private void StepTutorial(int delta)
        {
            int n = TUTORIAL_STEPS.Length;
            _tutorialStep = ((_tutorialStep + delta) % n + n) % n;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_uiRefs.TutorialStepLabel == null || _uiRefs.TutorialBodyTmp == null) return;
            int n = TUTORIAL_STEPS.Length;
            if (n == 0) return;
            if (_tutorialStep < 0 || _tutorialStep >= n) _tutorialStep = 0;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _uiRefs.TutorialStepLabel.text = $"{title}   (Step {_tutorialStep + 1}/{n})";
            _uiRefs.TutorialBodyTmp.text   = body;
        }

        private void CloseTutorial()
        {
            if (_uiRefs.TutorialDropdown == null) return;
            _uiRefs.TutorialDropdown.SetActive(false);
            _openDropdowns.Remove("tutorial");
            RefreshMenuBtnHighlights();
        }
    }
}