using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor window that scans <c>Assets/_Project/Audio/Music/</c> recursively
    /// (Biomes/, Zones/, Bosses/, Events/, Stingers/) and lists every AudioClip
    /// not yet registered in the <see cref="AudioCatalogSO"/>. Lets the user
    /// add them one-by-one or in bulk with auto-derived id + title from the
    /// filename — eliminating the need to edit the legacy Python audio.json.
    /// </summary>
    public class AudioMusicScannerWindow : EditorWindow
    {
        private const string MusicFolder = "Assets/_Project/Audio/Music";

        private AudioCatalogSO _catalog;
        private List<ScannedClip> _missing = new List<ScannedClip>();
        private List<string> _orphans = new List<string>();
        private Vector2 _scroll;

        private class ScannedClip
        {
            public AudioClip clip;
            public string proposedId;
            public string proposedTitle;
            public string subfolder;
        }

        [MenuItem("Valkur/Audio/Music Scanner")]
        public static void Open()
        {
            var w = GetWindow<AudioMusicScannerWindow>("Audio Music Scanner");
            w.minSize = new Vector2(560, 480);
            w.LoadCatalog();
            w.Scan();
        }

        private void OnEnable()
        {
            if (_catalog == null) LoadCatalog();
        }

        private void LoadCatalog() => _catalog = AudioCatalogLocator.Find();

        private void Scan()
        {
            _missing.Clear();
            _orphans.Clear();
            if (_catalog == null) return;

            var registeredPaths = new HashSet<string>();
            foreach (var t in _catalog.Tracks)
            {
                if (t.clip != null)
                    registeredPaths.Add(AssetDatabase.GetAssetPath(t.clip));
                else if (!string.IsNullOrEmpty(t.id))
                    _orphans.Add(t.id);
            }

            if (!AssetDatabase.IsValidFolder(MusicFolder))
            {
                Debug.LogWarning($"[AudioScanner] Music folder not found: {MusicFolder}");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (registeredPaths.Contains(path)) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                string folder = Path.GetDirectoryName(path).Replace('\\', '/');
                if (folder.StartsWith(MusicFolder))
                    folder = folder.Substring(MusicFolder.Length).TrimStart('/');

                _missing.Add(new ScannedClip
                {
                    clip = clip,
                    proposedId = ToSnakeCase(fileName),
                    proposedTitle = ToTitleCase(fileName),
                    subfolder = string.IsNullOrEmpty(folder) ? "(root)" : folder
                });
            }

            _missing.Sort((a, b) => string.Compare(a.subfolder + a.proposedId, b.subfolder + b.proposedId, System.StringComparison.OrdinalIgnoreCase));
        }

        private void OnGUI()
        {
            DrawHeader();
            if (_catalog == null) return;
            DrawToolbar();
            DrawScrollList();
            DrawOrphans();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Audio Music Scanner", EditorStyles.largeLabel);
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox("No AudioCatalogSO found.", MessageType.Error);
                if (GUILayout.Button("Reload"))
                    LoadCatalog();
                return;
            }
            EditorGUILayout.LabelField("Catalog", AssetDatabase.GetAssetPath(_catalog), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"Registered tracks: {_catalog.Tracks.Length}   |   Unregistered: {_missing.Count}   |   Orphans (no clip): {_orphans.Count}",
                EditorStyles.miniLabel);
            EditorGUILayout.Space();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Scan", GUILayout.Height(26)))
                Scan();
            using (new EditorGUI.DisabledScope(_missing.Count == 0))
            {
                if (GUILayout.Button($"Add ALL {_missing.Count} new tracks", GUILayout.Height(26)))
                    AddAll();
            }
            if (GUILayout.Button("Validate Catalog", GUILayout.Height(26)))
                AudioCatalogValidator.Validate(_catalog);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        private void DrawScrollList()
        {
            EditorGUILayout.LabelField($"Unregistered MP3s under {MusicFolder}/", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));
            for (int i = 0; i < _missing.Count; i++)
                DrawMissingRow(_missing[i]);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMissingRow(ScannedClip item)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.ObjectField("Clip", item.clip, typeof(AudioClip), false);
            EditorGUILayout.LabelField("Subfolder", item.subfolder);
            item.proposedId = EditorGUILayout.TextField("ID", item.proposedId);
            item.proposedTitle = EditorGUILayout.TextField("Title", item.proposedTitle);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(item.proposedId)))
            {
                if (GUILayout.Button("Add to Catalog"))
                {
                    AddOne(item);
                    Scan();
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawOrphans()
        {
            if (_orphans.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Orphan track entries (id but no AudioClip): {_orphans.Count}", EditorStyles.boldLabel);
            foreach (var id in _orphans)
                EditorGUILayout.LabelField("• " + id);
        }

        private void AddOne(ScannedClip s)
        {
            var list = new List<MusicTrackEntry>(_catalog.Tracks);
            list.Add(new MusicTrackEntry { id = s.proposedId, title = s.proposedTitle, clip = s.clip });
            _catalog.EditorSetTracks(list.ToArray());
            _catalog.InvalidateCache();
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AudioScanner] Added '{s.proposedId}' -> {AssetDatabase.GetAssetPath(s.clip)}");
        }

        private void AddAll()
        {
            var list = new List<MusicTrackEntry>(_catalog.Tracks);
            int added = 0;
            foreach (var s in _missing)
            {
                if (string.IsNullOrEmpty(s.proposedId)) continue;
                list.Add(new MusicTrackEntry { id = s.proposedId, title = s.proposedTitle, clip = s.clip });
                added++;
            }
            _catalog.EditorSetTracks(list.ToArray());
            _catalog.InvalidateCache();
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AudioScanner] Bulk-added {added} tracks.");
            Scan();
        }

        private static string ToSnakeCase(string s) =>
            s.ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        private static string ToTitleCase(string s)
        {
            var parts = s.Split('_', '-', ' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }
    }
}
