using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Persistence for the Boss Editor.
    ///
    /// Charts are saved as .asset files (they are ScriptableObjects). The
    /// containing BossDefinition is also marked dirty so its chart references
    /// persist through the asset database.
    ///
    /// All asset I/O is wrapped in <c>#if UNITY_EDITOR</c> because this editor
    /// authors .asset files and only runs inside the Unity Editor.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Charts directory ───────────────────────────────────────────────────

        private const string CHARTS_DIR = "Assets/_Project/Data/Bosses/Charts";

        // ── Save ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Saves the currently selected chart (and its parent BossDefinition).
        /// </summary>
        private void SaveSelectedChart()
        {
            if (_selectedChart == null)
            {
                SetStatus("No chart selected to save.");
                return;
            }
#if UNITY_EDITOR
            try
            {
                EnsureChartsDirectory();
                UnityEditor.EditorUtility.SetDirty(_selectedChart);
                if (_selectedBoss != null)
                    UnityEditor.EditorUtility.SetDirty(_selectedBoss);
                UnityEditor.AssetDatabase.SaveAssets();
                SetStatus($"Saved chart '{_selectedChart.name}'.");
                Debug.Log($"[BossEditor] Saved chart: {UnityEditor.AssetDatabase.GetAssetPath(_selectedChart)}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BossEditor] Save failed: {ex.Message}");
                SetStatus("Save FAILED — see console.");
            }
#else
            SetStatus("Save requires Unity Editor.");
#endif
        }

        /// <summary>
        /// Marks an asset dirty without saving immediately (called after every
        /// undoable edit so the dirty state is visible while authoring).
        /// </summary>
        private static void MarkDirty(Object asset)
        {
#if UNITY_EDITOR
            if (asset != null) UnityEditor.EditorUtility.SetDirty(asset);
#endif
        }

        // ── Directory helpers ──────────────────────────────────────────────────

        private static void EnsureChartsDirectory()
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.IsValidFolder(CHARTS_DIR))
                UnityEditor.AssetDatabase.CreateFolder(
                    "Assets/_Project/Data/Bosses", "Charts");
#endif
        }
    }
}
