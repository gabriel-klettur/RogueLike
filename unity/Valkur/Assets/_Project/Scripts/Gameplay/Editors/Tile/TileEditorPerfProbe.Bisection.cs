using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorPerfProbe : MonoBehaviour
    {        private void ToggleExtraCameras()
        {
            if (_bisectExtraCamerasOff)
            {
                foreach (var c in _disabledCameras) if (c != null) c.enabled = true;
                _disabledCameras.Clear();
                _bisectExtraCamerasOff = false;
                Debug.Log("[PerfProbe] Extra cameras: ON");
            }
            else
            {
                var main = Camera.main;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c == main || !c.enabled) continue;
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
                _bisectExtraCamerasOff = true;
                Debug.Log($"[PerfProbe] Extra cameras: OFF ({_disabledCameras.Count} disabled)");
            }
        }

        private void ToggleSprites()
        {
            if (_bisectSpritesOff)
            {
                foreach (var s in _disabledSprites) if (s != null) s.enabled = true;
                _disabledSprites.Clear();
                _bisectSpritesOff = false;
                Debug.Log("[PerfProbe] Sprites: ON");
            }
            else
            {
                foreach (var s in Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (s == null || !s.enabled) continue;
                    s.enabled = false;
                    _disabledSprites.Add(s);
                }
                _bisectSpritesOff = true;
                Debug.Log($"[PerfProbe] Sprites: OFF ({_disabledSprites.Count} disabled)");
            }
        }

        private void ToggleLights()
        {
            if (_bisectLightsOff)
            {
                foreach (var l in _disabledLights) if (l != null) l.enabled = true;
                _disabledLights.Clear();
                _bisectLightsOff = false;
                Debug.Log("[PerfProbe] Lights2D: ON");
            }
            else
            {
                if (_light2DType != null)
                {
                    var lights = Object.FindObjectsOfType(_light2DType);
                    foreach (var l in lights)
                    {
                        var b = l as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledLights.Add(b);
                    }
                }
                _bisectLightsOff = true;
                Debug.Log($"[PerfProbe] Lights2D: OFF ({_disabledLights.Count} disabled)");
            }
        }

        private void ToggleVolumes()
        {
            if (_bisectVolumesOff)
            {
                foreach (var v in _disabledVolumes) if (v != null) v.enabled = true;
                _disabledVolumes.Clear();
                _bisectVolumesOff = false;
                Debug.Log("[PerfProbe] Volumes: ON");
            }
            else
            {
                if (_volumeType != null)
                {
                    var vols = Object.FindObjectsOfType(_volumeType);
                    foreach (var v in vols)
                    {
                        var b = v as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledVolumes.Add(b);
                    }
                }
                _bisectVolumesOff = true;
                Debug.Log($"[PerfProbe] Volumes: OFF ({_disabledVolumes.Count} disabled)");
            }
        }

        private void TogglePostFx()
        {
            if (_urpCamDataType == null || _urpRenderPostProp == null)
            {
                Debug.LogWarning("[PerfProbe] URP camera data not available — cannot toggle PostFX");
                return;
            }
            if (_bisectPostFxOff)
            {
                for (int i = 0; i < _camsWithPostFx.Count; i++)
                {
                    var c = _camsWithPostFx[i];
                    if (c == null) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null) try { _urpRenderPostProp.SetValue(data, _camsPostFxOriginal[i]); } catch { }
                }
                _camsWithPostFx.Clear();
                _camsPostFxOriginal.Clear();
                _bisectPostFxOff = false;
                Debug.Log("[PerfProbe] PostFX: restored");
            }
            else
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data == null) continue;
                    try
                    {
                        bool original = (bool)_urpRenderPostProp.GetValue(data);
                        _camsWithPostFx.Add(c);
                        _camsPostFxOriginal.Add(original);
                        _urpRenderPostProp.SetValue(data, false);
                    }
                    catch { }
                }
                _bisectPostFxOff = true;
                Debug.Log($"[PerfProbe] PostFX: OFF ({_camsWithPostFx.Count} cameras)");
            }
        }

        private void ToggleExtraTilemaps()
        {
            if (_bisectExtraTilemapsOff)
            {
                foreach (var t in _disabledTilemaps) if (t != null) t.enabled = true;
                _disabledTilemaps.Clear();
                _bisectExtraTilemapsOff = false;
                Debug.Log("[PerfProbe] Tilemaps: ON");
            }
            else
            {
                // Disable everything except ground-ish layer (lowest sortingOrder)
                var tms = Object.FindObjectsOfType<TilemapRenderer>();
                int minOrder = int.MaxValue;
                foreach (var t in tms) if (t.enabled && t.sortingOrder < minOrder) minOrder = t.sortingOrder;
                foreach (var t in tms)
                {
                    if (t == null || !t.enabled || t.sortingOrder == minOrder) continue;
                    t.enabled = false;
                    _disabledTilemaps.Add(t);
                }
                _bisectExtraTilemapsOff = true;
                Debug.Log($"[PerfProbe] Tilemaps: kept order={minOrder}, OFF {_disabledTilemaps.Count}");
            }
        }

        // ── Counts ──────────────────────────────────────────────────────────

    }
}