using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace Valkur.UI.HUD
{
    public sealed partial class MinimapWorldBaker
    {
        private readonly HashSet<Renderer> _keepSet = new HashSet<Renderer>();
        private readonly List<Renderer> _hidden = new List<Renderer>(512);
        private readonly List<Light2D> _dimmed = new List<Light2D>(64);
        private readonly List<float> _dimmedIntensity = new List<float>(64);
        private readonly List<SpriteRenderer> _spriteScratch = new List<SpriteRenderer>(8);
        private Light2D _globalLight;
        private float _globalIntensity;
        private Color _globalColor;

        private void AllocateAtlas()
        {
            if (_atlas != null)
            {
                _atlas.Release();
                DestroySafe(_atlas);
            }

            var desc = new RenderTextureDescriptor(_bounds.width * _ppu, _bounds.height * _ppu, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1,
            };
            _atlas = new RenderTexture(desc)
            {
                name = "MinimapTerrainAtlas",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _atlas.Create();

            var prev = RenderTexture.active;
            RenderTexture.active = _atlas;
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
            RenderTexture.active = prev;
        }

        private void EnsureCamera()
        {
            if (_camera != null) return;
            var go = new GameObject("MinimapBakeCamera");
            go.transform.SetParent(_host, false);
            go.hideFlags = HideFlags.DontSave;
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.aspect = 1f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 200f;
            _camera.cullingMask = ~0;
            _camera.useOcclusionCulling = false;
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        private void BakeDirty(Vector2 focus)
        {
            EnsureCamera();
            float budget = Mathf.Max(0.5f, _style.bakeBudgetMs);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int baked = 0;

            PrepareScene();
            try
            {
                do
                {
                    int idx = NearestDirty(focus);
                    if (idx < 0) break;
                    RenderChunk(idx);
                    _dirty[idx] = false;
                    _dirtyCount--;
                    baked++;
                } while (_dirtyCount > 0 && watch.Elapsed.TotalMilliseconds < budget);
            }
            finally
            {
                RestoreScene();
            }

            if (baked > 0)
            {
                _bakedTotal += baked;
                _atlas.GenerateMips();
            }
        }

        private int NearestDirty(Vector2 focus)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (!_dirty[i]) continue;
                int cx = i % _cols, cy = i / _cols;
                float wx = _bounds.xMin + (cx + 0.5f) * _chunk;
                float wy = _bounds.yMin + (cy + 0.5f) * _chunk;
                float d = (wx - focus.x) * (wx - focus.x) + (wy - focus.y) * (wy - focus.y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private void RenderChunk(int index)
        {
            int cx = index % _cols, cy = index / _cols;
            int renderPpu = Mathf.Max(_ppu, _style.renderPixelsPerUnit);
            // Keep the render an exact power-of-two multiple of the bake so every halving is an
            // exact 2x2 box average.
            int factor = 1;
            while (_ppu * factor * 2 <= renderPpu) factor *= 2;
            int size = _chunk * _ppu * factor;

            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = _atlas.sRGB,
                msaaSamples = 1,
            };
            var rt = RenderTexture.GetTemporary(desc);
            rt.filterMode = FilterMode.Bilinear;

            float half = _chunk * 0.5f;
            _camera.orthographicSize = half;
            _camera.transform.position = new Vector3(_bounds.xMin + cx * _chunk + half, _bounds.yMin + cy * _chunk + half, -50f);
            _camera.targetTexture = rt;
            _camera.Render();
            _camera.targetTexture = null;

            // Halve down to the bake resolution.
            var src = rt;
            while (size > _chunk * _ppu)
            {
                size /= 2;
                var d = desc;
                d.width = size; d.height = size;
                var dst = RenderTexture.GetTemporary(d);
                dst.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, dst);
                RenderTexture.ReleaseTemporary(src);
                src = dst;
            }

            Graphics.CopyTexture(src, 0, 0, 0, 0, size, size, _atlas, 0, 0, cx * _chunk * _ppu, cy * _chunk * _ppu);
            RenderTexture.ReleaseTemporary(src);
        }

        // ── Scene preparation ──────────────────────────────────────────────

        private void PrepareScene()
        {
            _keepSet.Clear();
            _hidden.Clear();
            _dimmed.Clear();
            _dimmedIntensity.Clear();

            foreach (var tr in _grid.Grid.GetComponentsInChildren<TilemapRenderer>())
                if (tr.enabled) _keepSet.Add(tr);

            if (_buildings != null)
            {
                var list = _buildings.SpawnedBuildings;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    if (b == null || !b.isActiveAndEnabled) continue;
                    b.GetComponentsInChildren(false, _spriteScratch);
                    for (int k = 0; k < _spriteScratch.Count; k++) _keepSet.Add(_spriteScratch[k]);
                }
            }

            var all = Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (_keepSet.Contains(r) || r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                _hidden.Add(r);
            }

            var lights = Object.FindObjectsOfType<Light2D>();
            _globalLight = null;
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (!l.isActiveAndEnabled) continue;
                if (l.lightType == Light2D.LightType.Global && _globalLight == null)
                {
                    _globalLight = l;
                    _globalIntensity = l.intensity;
                    _globalColor = l.color;
                    l.intensity = 1f;
                    l.color = Color.white;
                    continue;
                }
                _dimmed.Add(l);
                _dimmedIntensity.Add(l.intensity);
                l.intensity = 0f;
            }
        }

        private void RestoreScene()
        {
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i] != null) _hidden[i].forceRenderingOff = false;
            _hidden.Clear();

            for (int i = 0; i < _dimmed.Count; i++)
                if (_dimmed[i] != null) _dimmed[i].intensity = _dimmedIntensity[i];
            _dimmed.Clear();
            _dimmedIntensity.Clear();

            if (_globalLight != null)
            {
                _globalLight.intensity = _globalIntensity;
                _globalLight.color = _globalColor;
                _globalLight = null;
            }
            _keepSet.Clear();
        }
    }
}
