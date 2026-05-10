using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Input;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Map Editor "Stamp" flow — paints a sliced tilesheet (read from
    /// <c>Resources/Tiles/&lt;cat&gt;/_manifest.json</c>) at the cursor cell with a
    /// single click. The stamp covers <c>cols × rows</c> cells anchored at the
    /// click; transparent cells in the manifest are skipped so multi-layer
    /// patterns can later be reassembled with F8 once placed.
    ///
    /// The flow follows the same arm-then-click pattern as Add Zone / Place
    /// Portal: clicking "Place" in the Stamp panel sets <see cref="_isStampFlowActive"/>;
    /// the next non-UI left click on the world commits the stamp at that
    /// position, and the flow disarms.
    /// </summary>
    public partial class MapEditorManager
    {
        private bool _isStampFlowActive;
        private int _stampFlowStartedFrame = -1;
        private TilesheetManifest _activeStampManifest;
        private TilemapLayerSetup.TilemapLayer _stampTargetLayer = TilemapLayerSetup.TilemapLayer.Ground;

        // Cached so manifest discovery doesn't have to walk Resources every
        // panel open — re-built on demand from Resources.LoadAll.
        private List<MapEditorUIBuilder.StampDescriptor> _stampDescriptorCache;

        private List<MapEditorUIBuilder.StampDescriptor> DiscoverStampManifests()
        {
            if (_stampDescriptorCache != null) return _stampDescriptorCache;
            var list = new List<MapEditorUIBuilder.StampDescriptor>();
            var manifests = Resources.LoadAll<TextAsset>("Tiles");
            if (manifests != null)
            {
                foreach (var ta in manifests)
                {
                    if (ta == null || ta.name != "_manifest") continue;
                    var manifest = JsonUtility.FromJson<TilesheetManifest>(ta.text);
                    if (manifest == null || manifest.cells == null) continue;
                    string assetPath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; // unused
                    string folder = ResolveCategoryFolder(ta);
                    if (string.IsNullOrEmpty(folder)) continue;
                    list.Add(new MapEditorUIBuilder.StampDescriptor
                    {
                        ManifestResourcePath = $"Tiles/{folder}/_manifest",
                        CategoryFolder = folder,
                        DisplayLabel = $"{manifest.source} ({manifest.cols}×{manifest.rows})",
                    });
                }
            }
            _stampDescriptorCache = list;
            return list;
        }

        // Resources.LoadAll<TextAsset>("Tiles") returns assets named "_manifest"
        // without exposing the parent folder. Recover it via reflection on the
        // asset's instance — the only stable hint is the asset's own name plus
        // a search through Resources.LoadAll<TextAsset> per known category.
        // Cheaper alternative: just probe the manifest's `source` field for a
        // recognisable folder name. Here we lean on the convention that the
        // category folder name = the slug used in `source` after stripping the
        // trailing "_castle_exterior" / "_map" / "_palette" suffix is brittle —
        // instead, fall back to listing categories from the catalog at boot.
        private static string ResolveCategoryFolder(TextAsset manifestAsset)
        {
#if UNITY_EDITOR
            // In the editor, AssetDatabase can give us the real path; runtime
            // builds use the catalog category list instead.
            var path = UnityEditor.AssetDatabase.GetAssetPath(manifestAsset);
            if (!string.IsNullOrEmpty(path))
            {
                int tilesIdx = path.IndexOf("/Tiles/", System.StringComparison.OrdinalIgnoreCase);
                if (tilesIdx >= 0)
                {
                    string after = path.Substring(tilesIdx + "/Tiles/".Length);
                    int slash = after.IndexOf('/');
                    if (slash > 0) return after.Substring(0, slash);
                }
            }
#endif
            // Runtime fallback: use the manifest source name and assume the
            // folder follows our snake_case convention. This branch is only
            // exercised in standalone builds.
            return string.Empty;
        }

        // Called by the Stamp panel's "Place" button.
        private void BeginStampFlow(string manifestResourcePath, TilemapLayerSetup.TilemapLayer layer)
        {
            var manifestText = Resources.Load<TextAsset>(manifestResourcePath);
            if (manifestText == null)
            {
                _ui?.SetStatus($"Stamp: manifest not found at Resources/{manifestResourcePath}.");
                return;
            }
            var manifest = JsonUtility.FromJson<TilesheetManifest>(manifestText.text);
            if (manifest == null || manifest.cells == null || manifest.cells.Length == 0)
            {
                _ui?.SetStatus("Stamp: manifest empty or malformed.");
                return;
            }

            _activeStampManifest = manifest;
            _stampTargetLayer = layer;
            _isStampFlowActive = true;
            _stampFlowStartedFrame = Time.frameCount;
            _ui?.SetStatus($"Stamp armed: {manifest.source} ({manifest.cols}×{manifest.rows}) on {layer}. Click in world to place.");
        }

        private void CancelStampFlow()
        {
            _isStampFlowActive = false;
            _activeStampManifest = null;
            _ui?.SetStatus("Stamp cancelled.");
        }

        // Called from MapEditorManager.Update on a left-click outside UI when
        // the stamp flow is armed. Resolves the cursor cell on the target
        // tilemap and stamps the manifest contents.
        private void HandleStampClickAtCursor()
        {
            if (!_isStampFlowActive || _activeStampManifest == null) return;
            if (worldGridBuilder == null)
            {
                _ui?.SetStatus("Stamp failed: world grid builder missing.");
                return;
            }
            var tilemap = worldGridBuilder.GetTilemap(_stampTargetLayer);
            if (tilemap == null)
            {
                _ui?.SetStatus($"Stamp failed: layer {_stampTargetLayer} has no tilemap.");
                return;
            }

            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null)
            {
                _ui?.SetStatus("Stamp failed: no main camera.");
                return;
            }
            Vector3 worldPos = cam.ScreenToWorldPoint(MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0f;
            Vector3Int anchor = tilemap.WorldToCell(worldPos);

            int painted = StampManifestAtAnchor(tilemap, anchor, _activeStampManifest);
            _isStampFlowActive = false;
            _activeStampManifest = null;
            _ui?.SetStatus($"Stamped {painted} tiles on {_stampTargetLayer} at ({anchor.x}, {anchor.y}).");
        }

        // Resolves each manifest cell's sprite via the global tile catalog and
        // SetTile()s it onto the target tilemap. Tilesheet rows go top-down,
        // Unity Y goes up — so cell.r=0 paints at anchor.y and grows downward.
        private static int StampManifestAtAnchor(Tilemap tilemap, Vector3Int anchor, TilesheetManifest manifest)
        {
            // Build a transient TileCatalog instance for the lookup. A single
            // BuildFromResources covers all categories, including the one this
            // stamp came from; doing it on demand avoids touching whatever
            // catalog the running tile editor has cached.
            var catalog = TileCatalog.BuildFromResources();

            int painted = 0;
            foreach (var cell in manifest.cells)
            {
                if (cell == null || cell.transparent) continue;

                TileBase tile = null;
                foreach (var entry in catalog.Entries)
                {
                    if (entry.tileName == cell.file) { tile = entry.tile; break; }
                }
                if (tile == null) continue;

                var pos = new Vector3Int(anchor.x + cell.c, anchor.y - cell.r, 0);
                tilemap.SetTile(pos, tile);
                painted++;
            }
            return painted;
        }
    }
}
