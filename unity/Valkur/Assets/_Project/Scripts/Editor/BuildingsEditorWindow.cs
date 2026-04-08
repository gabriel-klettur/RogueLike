using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    /// <summary>
    /// Buildings Editor â€” Unity EditorWindow equivalent of the Python buildings editor.
    ///
    /// Features (maps to Python buildings editor):
    ///   - Template palette: browse all BuildingTemplateData from BuildingCatalog (thumbnail grid).
    ///   - Place mode: click in the SceneView to drop the selected template.
    ///   - Selection: click existing buildings to select them.
    ///   - Inspect: edit split ratio and scale override for the selected building.
    ///   - Save: serialize all scene BuildingObjects to StreamingAssets/Buildings/buildings_instances.json.
    ///   - Load / Reload: respawn from the instances JSON via BuildingLoader.
    ///   - Delete: remove selected building from scene.
    ///   - Undo supported via Undo.RegisterCreatedObjectUndo / DestroyObjectImmediate.
    ///
    /// Open via: Valkur > Buildings Editor
    /// </summary>
    public partial class BuildingsEditorWindow : EditorWindow
    {
        // â”€â”€ Layout constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private const float  PALETTE_WIDTH    = 220f;
        private const float  THUMB_SIZE       = 64f;
        private const float  THUMB_PAD        = 6f;
        private const int    THUMB_COLS       = 3;
        private const float  SPLIT_HANDLE_H   = 4f;

        // â”€â”€ State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private BuildingCatalog _catalog;
        private BuildingTemplateData _selectedTemplate;
        private BuildingObject       _selectedBuilding;
        private bool                 _placeMode;
        private Vector2              _paletteScroll;
        private Vector2              _instanceScroll;
        private List<BuildingObject> _sceneBuildings = new List<BuildingObject>();

        // Drag-to-place preview state (world position of ghost under mouse)
        private Vector3 _ghostWorldPos;
#pragma warning disable CS0414
        private bool    _ghostVisible;
#pragma warning restore CS0414

        // â”€â”€ Menu item â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [MenuItem("Valkur/Buildings Editor")]
        public static void Open()
        {
            var window = GetWindow<BuildingsEditorWindow>("Buildings Editor");
            window.minSize = new Vector2(500f, 400f);
            window.Show();
        }

        // â”€â”€ Lifecycle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            RefreshSceneBuildings();
            LoadCatalog();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _placeMode = false;
        }

        private void OnFocus()
        {
            RefreshSceneBuildings();
        }

        // â”€â”€ Main GUI â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawPalette();
            DrawMainPanel();
            EditorGUILayout.EndHorizontal();
        }

        // â”€â”€ Toolbar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    }
}
