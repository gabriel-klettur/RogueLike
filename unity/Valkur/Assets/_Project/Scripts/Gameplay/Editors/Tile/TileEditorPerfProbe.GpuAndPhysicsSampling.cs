using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorPerfProbe : MonoBehaviour
    {
        private void SampleGpuDiagnostics()
        {
#if UNITY_EDITOR
            _drawCalls    = UnityEditor.UnityStats.drawCalls;
            _setPassCalls = UnityEditor.UnityStats.setPassCalls;
            _batches      = UnityEditor.UnityStats.batches;
            _triangles    = UnityEditor.UnityStats.triangles;
            _vertices     = UnityEditor.UnityStats.vertices;
            _renderTextureMemBytes = UnityEditor.UnityStats.renderTextureBytes;
            _textureMemBytes       = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif

            // ── GPU diagnostics ──
            _camerasActive = 0;
            var allCams = Camera.allCameras;
            for (int i = 0; i < allCams.Length; i++) if (allCams[i].isActiveAndEnabled) _camerasActive++;
            _screenW = Screen.width;
            _screenH = Screen.height;
            _orthoSize = _cam.orthographicSize;
            _viewWidthW = _orthoSize * 2f * _cam.aspect;

            // ── Identify cameras (up to 3 names) ──
            _cam0Name = _cam1Name = _cam2Name = "-";
            _cam0Info = _cam1Info = _cam2Info = "";
            int active = 0;
            for (int i = 0; i < allCams.Length && active < 3; i++)
            {
                var c = allCams[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                string tname = c.targetTexture != null ? "RT" : "screen";
                bool postFx = false;
                if (_urpCamDataType != null && _urpRenderPostProp != null)
                {
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null)
                    {
                        try { postFx = (bool)_urpRenderPostProp.GetValue(data); }
                        catch { /* ignore */ }
                    }
                }
                string info = $"{tname} pfx={(postFx ? "Y" : "N")} d={c.depth:F0}";
                switch (active)
                {
                    case 0: _cam0Name = c.name; _cam0Info = info; break;
                    case 1: _cam1Name = c.name; _cam1Info = info; break;
                    case 2: _cam2Name = c.name; _cam2Info = info; break;
                }
                active++;
            }

#if UNITY_EDITOR
            // Detect Scene View visible (it renders the scene every frame too in Editor)
            _sceneViewVisible = UnityEditor.SceneView.lastActiveSceneView != null
                              && UnityEditor.SceneView.lastActiveSceneView.hasFocus
                              || UnityEditor.SceneView.sceneViews.Count > 0;
            _gameViewWidth  = (int)UnityEditor.Handles.GetMainGameViewSize().x;
            _gameViewHeight = (int)UnityEditor.Handles.GetMainGameViewSize().y;
#endif

            // ── Tile count in view ──
            _tilesInView = 0;
            _tilemapColliders = 0;
            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;
            Vector3 cp = _cam.transform.position;
            var tilemaps = Object.FindObjectsOfType<Tilemap>();
            for (int i = 0; i < tilemaps.Length; i++)
            {
                var tm = tilemaps[i];
                if (tm == null || !tm.gameObject.activeInHierarchy) continue;
                var renderer = tm.GetComponent<TilemapRenderer>();
                if (renderer == null || !renderer.enabled) continue;

                // Compute the tilemap-local cell range that overlaps the camera window.
                Vector3 minWorld = new Vector3(cp.x - halfW, cp.y - halfH, 0f);
                Vector3 maxWorld = new Vector3(cp.x + halfW, cp.y + halfH, 0f);
                Vector3Int minCell = tm.WorldToCell(minWorld);
                Vector3Int maxCell = tm.WorldToCell(maxWorld);
                int minX = Mathf.Min(minCell.x, maxCell.x);
                int maxX = Mathf.Max(minCell.x, maxCell.x);
                int minY = Mathf.Min(minCell.y, maxCell.y);
                int maxY = Mathf.Max(minCell.y, maxCell.y);
                int z = minCell.z;

                // Cap iteration to a sane window to avoid pathological tilemaps.
                int xCells = Mathf.Min(maxX - minX + 1, 256);
                int yCells = Mathf.Min(maxY - minY + 1, 256);
                for (int xi = 0; xi < xCells; xi++)
                for (int yi = 0; yi < yCells; yi++)
                {
                    if (tm.HasTile(new Vector3Int(minX + xi, minY + yi, z))) _tilesInView++;
                }

                if (tm.GetComponent<TilemapCollider2D>() != null) _tilemapColliders++;
            }
        }

        private void SampleObjectCounts()
        {
            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;
            Vector3 cp = _cam.transform.position;
            var tilemaps = Object.FindObjectsOfType<Tilemap>();
            var srs = Object.FindObjectsOfType<SpriteRenderer>();
            // ── Collider2D + Rigidbody2D counts ──
            var cols = Object.FindObjectsOfType<Collider2D>();
            _colliders2DActive = cols.Length;
            _colliders2DEnabled = 0;
            for (int i = 0; i < cols.Length; i++) if (cols[i].enabled) _colliders2DEnabled++;

            _rigidbodies2D = Object.FindObjectsOfType<Rigidbody2D>().Length;

            // ── Animators visible ──
            var anims = Object.FindObjectsOfType<Animator>();
            _animators = anims.Length;
            _animatorsVisible = 0;
            for (int i = 0; i < anims.Length; i++)
            {
                if (!anims[i].isActiveAndEnabled) continue;
                // Cheap viewport check
                Vector3 p = anims[i].transform.position;
                if (Mathf.Abs(p.x - cp.x) <= halfW + 2f && Mathf.Abs(p.y - cp.y) <= halfH + 2f)
                    _animatorsVisible++;
            }

            // ── GPU material/shadow/tilemap breakdown ──
            // Tilemap render modes (Chunk vs Individual). Individual = per-tile draw call.
            _tilemapsChunked = 0;
            _tilemapsIndividual = 0;
            for (int i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] == null) continue;
                var tr = tilemaps[i].GetComponent<TilemapRenderer>();
                if (tr == null || !tr.enabled) continue;
                if (tr.mode == TilemapRenderer.Mode.Chunk) _tilemapsChunked++;
                else _tilemapsIndividual++;
            }

            // Sprite material uniqueness (more unique mats = more SetPass = bad batching)
            _uniqueMaterialCount = 0;
            _transparentSpriteCount = 0;
            _spritesNonStaticBatched = 0;
            var matSet = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null || !srs[i].enabled || !srs[i].isVisible) continue;
                var sm = srs[i].sharedMaterial;
                if (sm != null) matSet.Add(sm.GetInstanceID());
                // sprite default shader is alpha-blended already
                _transparentSpriteCount++;
                if (srs[i].HasPropertyBlock()) _spritesNonStaticBatched++;
            }
            _uniqueMaterialCount = matSet.Count;

            // 2D Light shadow casters (URP). Reflection: ShadowCaster2D in
            // UnityEngine.Rendering.Universal namespace.
            _shadowCasterTotal = 0;
            _shadowCaster2DCount = 0;
            var shadowType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.ShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");
            if (shadowType != null)
            {
                var shads = Object.FindObjectsOfType(shadowType);
                _shadowCasterTotal = shads.Length;
                for (int i = 0; i < shads.Length; i++)
                {
                    var c = shads[i] as Component;
                    if (c != null && c.gameObject.activeInHierarchy && c.GetComponent<Behaviour>()?.enabled != false)
                    {
                        // Viewport cull check
                        Vector3 p = c.transform.position;
                        if (Mathf.Abs(p.x - cp.x) <= halfW + 4f && Mathf.Abs(p.y - cp.y) <= halfH + 4f)
                            _shadowCaster2DCount++;
                    }
                }
            }

            // Light2D with shadows enabled (reflection)
            _light2DWithShadows = 0;
            if (_light2DType != null)
            {
                var lights = Object.FindObjectsOfType(_light2DType);
                var shadowProp = _light2DType.GetProperty("shadowsEnabled",
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < lights.Length; i++)
                {
                    var c = lights[i] as Component;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (shadowProp != null)
                    {
                        try
                        {
                            object v = shadowProp.GetValue(c);
                            if (v is bool b && b) _light2DWithShadows++;
                        }
                        catch { /* ignore */ }
                    }
                }
            }

            // Rough overdraw estimate: tilesInView × visible sprites in view, projected to screen pixels.
            // Useful only as a "is it big or small" indicator; not an exact metric.
            float pixelsInView = (float)_screenW * _screenH;
            float coveredPixels = (_tilesInView + _spritesVisible) * 16f * 16f * 4f; // 16PPU, ~4x scale guess
            _estimatedOverdrawMultiplier = pixelsInView > 0 ? coveredPixels / pixelsInView : 0f;
        }
    }
}