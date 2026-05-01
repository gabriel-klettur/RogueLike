using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorPerfProbe : MonoBehaviour
    {
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