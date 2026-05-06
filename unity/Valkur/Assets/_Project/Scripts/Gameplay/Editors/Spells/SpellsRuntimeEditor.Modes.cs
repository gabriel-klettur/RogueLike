using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

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
            if (_catalog == null) { Toast("No catalog assigned."); return; }

            UIModal.Prompt(_canvas.transform, "New Spell — key:", "new_spell",
                onOk: v =>
                {
                    var key = (v ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(key))
                    {
                        UIModal.Message(_canvas.transform, "Invalid key", "Spell key cannot be empty.");
                        return;
                    }
                    if (_catalog.TryGet(key, out var existing) && existing != null)
                    {
                        UIModal.Message(_canvas.transform, "Duplicate key",
                            $"A spell with key '{key}' already exists.");
                        return;
                    }

                    var s = ScriptableObject.CreateInstance<SpellDefinition>();
                    s.name        = key;
                    s.spellKey    = key;
                    s.displayName = key;
                    s.type        = SpellType.Projectile;

                    var current = _catalog.AllSpells ?? System.Array.Empty<SpellDefinition>();
                    var appended = current.Concat(new[] { s }).ToArray();
                    _catalog.SetSpellsRuntime(appended);

#if UNITY_EDITOR
                    const string SPELL_DIR = "Assets/_Project/Data/Catalogs/Spells";
                    if (!UnityEditor.AssetDatabase.IsValidFolder(SPELL_DIR))
                    {
                        var parent = "Assets/_Project/Data/Catalogs";
                        if (!UnityEditor.AssetDatabase.IsValidFolder(parent))
                        {
                            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/Data"))
                                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Data");
                            UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Data", "Catalogs");
                        }
                        UnityEditor.AssetDatabase.CreateFolder(parent, "Spells");
                    }
                    var assetPath = $"{SPELL_DIR}/{key}.asset";
                    UnityEditor.AssetDatabase.CreateAsset(s, assetPath);
                    UnityEditor.EditorUtility.SetDirty(_catalog);
                    UnityEditor.AssetDatabase.SaveAssets();
#endif

                    _undo.Record(new UndoStack.LambdaCommand(
                        $"Add '{key}'",
                        doAction: () =>
                        {
                            var arr = _catalog.AllSpells.Concat(new[] { s }).ToArray();
                            _catalog.SetSpellsRuntime(arr);
                            _selectedKey = key;
                            RefreshPicker();
                            RefreshPropertiesForm();
                        },
                        undoAction: () =>
                        {
                            var arr = _catalog.AllSpells.Where(x => x != s).ToArray();
                            _catalog.SetSpellsRuntime(arr);
                            if (_selectedKey == key) _selectedKey = null;
                            RefreshPicker();
                            RefreshPropertiesForm();
                        }));

                    _selectedKey = key;
                    RefreshPicker();
                    RefreshPropertiesForm();
                    Toast($"Added '{key}'");
                });
        }

        private void OnRemoveSpell()
        {
            if (_catalog == null) { Toast("No catalog assigned."); return; }
            if (string.IsNullOrEmpty(_selectedKey))
            {
                Toast("Remove: select a spell first.");
                return;
            }
            var key = _selectedKey;
            UIModal.Confirm(_canvas.transform,
                "Delete spell?",
                $"Are you sure you want to delete '{key}'?",
                onOk: () =>
                {
                    if (!_catalog.TryGet(key, out var removed) || removed == null)
                    {
                        Toast($"'{key}' not found.");
                        return;
                    }
                    int removedIndex = System.Array.IndexOf(_catalog.AllSpells, removed);
                    var newArr = _catalog.AllSpells.Where(x => x != removed).ToArray();
                    _catalog.SetSpellsRuntime(newArr);

#if UNITY_EDITOR
                    var path = UnityEditor.AssetDatabase.GetAssetPath(removed);
                    if (!string.IsNullOrEmpty(path))
                        UnityEditor.AssetDatabase.DeleteAsset(path);
                    UnityEditor.EditorUtility.SetDirty(_catalog);
                    UnityEditor.AssetDatabase.SaveAssets();
#endif

                    _undo.Record(new UndoStack.LambdaCommand(
                        $"Remove '{key}'",
                        doAction: () =>
                        {
                            var arr = _catalog.AllSpells.Where(x => x != removed).ToArray();
                            _catalog.SetSpellsRuntime(arr);
                            if (_selectedKey == key) _selectedKey = null;
                            RefreshPicker();
                            RefreshPropertiesForm();
                        },
                        undoAction: () =>
                        {
                            var list = new List<SpellDefinition>(_catalog.AllSpells);
                            int idx = Mathf.Clamp(removedIndex, 0, list.Count);
                            list.Insert(idx, removed);
                            _catalog.SetSpellsRuntime(list.ToArray());
                            _selectedKey = key;
                            RefreshPicker();
                            RefreshPropertiesForm();
                        }));

                    _selectedKey = null;
                    RefreshPicker();
                    RefreshPropertiesForm();
                    Toast($"Removed '{key}'");
                });
        }

        // ── File ──

        private void OnSave()
        {
#if UNITY_EDITOR
            if (_catalog == null) { Toast("No catalog assigned."); return; }
            int n = 0;
            foreach (var s in _catalog.AllSpells)
            {
                if (s == null) continue;
                UnityEditor.EditorUtility.SetDirty(s);
                n++;
            }
            UnityEditor.EditorUtility.SetDirty(_catalog);
            UnityEditor.AssetDatabase.SaveAssets();
            Toast($"Saved {n} spells");
#else
            Toast("Save not supported in build (use Unity Editor)");
#endif
        }

        private void OnReload()
        {
#if UNITY_EDITOR
            try
            {
                UnityEditor.AssetDatabase.Refresh();
                if (_catalog != null) _catalog.SetSpellsRuntime(_catalog.AllSpells);
                _selectedKey = null;
                _undo.Clear();
                RefreshPicker();
                RefreshPropertiesForm();
                Toast("Reloaded catalog from disk");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                Toast("Reload failed: " + ex.Message);
            }
#else
            Toast("Reload requires Editor mode");
#endif
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