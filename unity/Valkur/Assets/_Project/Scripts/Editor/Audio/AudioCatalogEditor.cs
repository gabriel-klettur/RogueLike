using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="AudioCatalogSO"/> — adds a stats banner
    /// and a toolbar (Music Scanner, Validate, Open Music Folder) on top of the
    /// default field-by-field Inspector. The catalog is the single source of
    /// truth for Valkur audio data; the legacy Python audio.json importer
    /// (Valkur > Audio > Re-Import from Legacy Python JSON) is one-shot only.
    /// </summary>
    [CustomEditor(typeof(AudioCatalogSO))]
    public class AudioCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var catalog = (AudioCatalogSO)target;

            EditorGUILayout.LabelField("Valkur Audio Catalog", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox(
                "Single source of truth for music + SFX. Edit fields directly below, " +
                "or use the Music Scanner to bulk-add new MP3s dropped into Audio/Music/.",
                MessageType.Info);
            EditorGUILayout.Space();

            DrawStatsRow(catalog);
            EditorGUILayout.Space();
            DrawToolbar(catalog);
            EditorGUILayout.Space();

            DrawDefaultInspector();
        }

        private static void DrawStatsRow(AudioCatalogSO catalog)
        {
            EditorGUILayout.BeginHorizontal();
            DrawStat("Tracks", catalog.Tracks.Length);
            DrawStat("SFX", catalog.SfxEntries.Length);
            DrawStat("Music Scopes", catalog.MusicOverrides.Length);
            DrawStat("Ambient Scopes", catalog.AmbientOverrides.Length);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawStat(string label, int value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(80));
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(value.ToString(), EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private static void DrawToolbar(AudioCatalogSO catalog)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Music Scanner", GUILayout.Height(28)))
                AudioMusicScannerWindow.Open();
            if (GUILayout.Button("Validate", GUILayout.Height(28)))
                AudioCatalogValidator.Validate(catalog);
            if (GUILayout.Button("Open Music Folder", GUILayout.Height(28)))
            {
                var folder = AssetDatabase.LoadAssetAtPath<Object>("Assets/_Project/Audio/Music");
                if (folder != null) EditorGUIUtility.PingObject(folder);
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
