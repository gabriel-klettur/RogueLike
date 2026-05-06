using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Finds the single canonical <see cref="AudioCatalogSO"/> asset. The catalog
    /// is the in-Unity source of truth for music + SFX (the Python audio.json
    /// importer is a one-shot legacy recovery path only).
    /// Canonical location: Assets/_Project/Resources/AudioCatalog.asset.
    /// </summary>
    public static class AudioCatalogLocator
    {
        public const string CanonicalPath = "Assets/_Project/Resources/AudioCatalog.asset";

        public static AudioCatalogSO Find()
        {
            var direct = AssetDatabase.LoadAssetAtPath<AudioCatalogSO>(CanonicalPath);
            if (direct != null) return direct;

            var guids = AssetDatabase.FindAssets("t:" + nameof(AudioCatalogSO));
            if (guids.Length == 0)
            {
                Debug.LogError(
                    "[AudioCatalog] No AudioCatalogSO found in project. " +
                    "Create one via Assets > Create > Valkur > Audio > Audio Catalog, " +
                    $"or place it at {CanonicalPath}.");
                return null;
            }
            if (guids.Length > 1)
            {
                Debug.LogWarning(
                    $"[AudioCatalog] Multiple AudioCatalogSO assets found ({guids.Length}). " +
                    $"Using the first match. Canonical location: {CanonicalPath}.");
            }
            return AssetDatabase.LoadAssetAtPath<AudioCatalogSO>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        [MenuItem("Valkur/Audio/Open Audio Catalog")]
        public static void Open()
        {
            var catalog = Find();
            if (catalog == null) return;
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
        }
    }
}
