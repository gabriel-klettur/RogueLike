using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Spells Editor — EditorWindow for browsing and editing SpellDefinition assets.
    ///
    /// Features:
    ///   - Scrollable list of all SpellDefinition assets in the project.
    ///   - Inline inspector for key spell fields (type, damage, range, timings, particles).
    ///   - Create New Spell shortcut.
    ///   - Save dirty assets with one click.
    ///
    /// Open via: Valkur > Spells Editor
    /// </summary>
    public class SpellsEditorWindow : EditorWindow
    {
        private const float LIST_WIDTH = 220f;
        private const float HANDLE_W = 4f;

        private List<SpellDefinition> _allSpells = new List<SpellDefinition>();
        private SpellDefinition _selected;
        private Vector2 _listScroll;
        private Vector2 _inspectorScroll;
        private string _searchFilter = "";
        private SerializedObject _serializedSpell;

        // ── Menu item ────────────────────────────────────────────────────────────────

        [MenuItem("Valkur/Spells Editor")]
        public static void Open()
        {
            var win = GetWindow<SpellsEditorWindow>("Spells Editor");
            win.minSize = new Vector2(600f, 420f);
            win.Show();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshSpellList();
        }

        private void OnFocus()
        {
            RefreshSpellList();
        }

        // ── Main GUI ─────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();

            // Left: spell list
            DrawSpellList();

            // Resize handle (visual only)
            var handleRect = GUILayoutUtility.GetRect(HANDLE_W, position.height, GUILayout.Width(HANDLE_W));
            EditorGUI.DrawRect(handleRect, new Color(0.1f, 0.1f, 0.1f, 1f));

            // Right: inspector
            DrawInspector();

            EditorGUILayout.EndHorizontal();
        }

        // ── Toolbar ──────────────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                RefreshSpellList();

            if (GUILayout.Button("New Spell", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                CreateNewSpell();

            if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("[SpellsEditor] All assets saved.");
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label("Search:", EditorStyles.label, GUILayout.Width(50f));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(160f));

            EditorGUILayout.EndHorizontal();
        }

        // ── Spell list ───────────────────────────────────────────────────────────────

        private void DrawSpellList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LIST_WIDTH));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            string filter = _searchFilter.ToLowerInvariant();

            foreach (var spell in _allSpells)
            {
                if (spell == null) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    !spell.spellKey.ToLowerInvariant().Contains(filter) &&
                    !spell.displayName.ToLowerInvariant().Contains(filter))
                    continue;

                bool isSelected = spell == _selected;
                var bgColor = isSelected
                    ? new Color(0.2f, 0.45f, 0.8f, 1f)
                    : new Color(0.18f, 0.18f, 0.18f, 1f);

                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = bgColor;

                if (GUILayout.Button(
                    $"{spell.displayName}\n<color=#888><size=10>{spell.spellKey} · {spell.type}</size></color>",
                    new GUIStyle(GUI.skin.button)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        richText = true,
                        wordWrap = false,
                        fixedHeight = 42f,
                        fontSize = 12,
                        padding = new RectOffset(6, 4, 4, 4)
                    }, GUILayout.Height(42f)))
                {
                    Select(spell);
                }

                GUI.backgroundColor = oldBg;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── Inspector ────────────────────────────────────────────────────────────────

        private void DrawInspector()
        {
            EditorGUILayout.BeginVertical();

            if (_selected == null || _serializedSpell == null)
            {
                EditorGUILayout.HelpBox("Select a spell from the list to inspect it.", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            _serializedSpell.Update();

            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            // Header
            EditorGUILayout.LabelField($"{_selected.displayName}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            DrawSection("Identity", () =>
            {
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("spellKey"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("displayName"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("type"));
            });

            DrawSection("Casting", () =>
            {
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("manaCost"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("maxInstances"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("allowOverlap"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("allowMovement"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("automatic"));
            });

            DrawSection("Timings", () =>
            {
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("prepareDuration"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("channelDuration"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("cooldownDuration"));
            });

            DrawSection("Combat", () =>
            {
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("damage"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("range"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("radius"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("knockback"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("speed"));
            });

            DrawSection("Visual", () =>
            {
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("sprite"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("scale"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("particleColor"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("vfxPreset"));
                EditorGUILayout.PropertyField(_serializedSpell.FindProperty("impactPreset"));
            });

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Ping Asset", GUILayout.Height(24f)))
                EditorGUIUtility.PingObject(_selected);

            if (GUILayout.Button("Open in Inspector", GUILayout.Height(24f)))
                Selection.activeObject = _selected;

            EditorGUILayout.EndScrollView();

            if (_serializedSpell.hasModifiedProperties)
            {
                _serializedSpell.ApplyModifiedProperties();
                EditorUtility.SetDirty(_selected);
            }

            EditorGUILayout.EndVertical();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private void DrawSection(string title, System.Action content)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            content();
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
        }

        private void Select(SpellDefinition spell)
        {
            _selected = spell;
            _serializedSpell = spell != null ? new SerializedObject(spell) : null;
            Repaint();
        }

        private void RefreshSpellList()
        {
            _allSpells.Clear();
            var guids = AssetDatabase.FindAssets("t:SpellDefinition");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                if (def != null) _allSpells.Add(def);
            }
            _allSpells.Sort((a, b) => string.Compare(a.spellKey, b.spellKey, System.StringComparison.Ordinal));

            if (_selected != null && !_allSpells.Contains(_selected))
                Select(null);
        }

        private void CreateNewSpell()
        {
            const string folder = "Assets/_Project/Data/Spells";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Spells");
            }

            var spell = CreateInstance<SpellDefinition>();
            spell.spellKey = "new_spell";
            spell.displayName = "New Spell";

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NewSpell.asset");
            AssetDatabase.CreateAsset(spell, path);
            AssetDatabase.SaveAssets();

            RefreshSpellList();
            Select(spell);
            EditorGUIUtility.PingObject(spell);
        }
    }
}
