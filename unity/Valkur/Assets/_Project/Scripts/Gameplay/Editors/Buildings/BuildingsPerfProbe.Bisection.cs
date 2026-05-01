using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsPerfProbe : MonoBehaviour
    {
        private void ToggleExtraCameras()
        {
            if (_bisectExtraCamerasOff)
            {
                foreach (var c in _disabledCameras) if (c) c.enabled = true;
                _disabledCameras.Clear();
                _bisectExtraCamerasOff = false;
            }
            else
            {
                var main = Camera.main;
                foreach (var c in Camera.allCameras)
                {
                    if (!c || c == main || !c.enabled) continue;
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
                _bisectExtraCamerasOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Extra cameras: {(_bisectExtraCamerasOff ? "OFF" : "ON")}");
        }

        private void ToggleSprites()
        {
            if (_bisectSpritesOff)
            {
                foreach (var s in _disabledSprites) if (s) s.enabled = true;
                _disabledSprites.Clear();
                _bisectSpritesOff = false;
            }
            else
            {
                foreach (var s in Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (!s || !s.enabled) continue;
                    s.enabled = false;
                    _disabledSprites.Add(s);
                }
                _bisectSpritesOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Sprites: {(_bisectSpritesOff ? "OFF" : "ON")}");
        }

        private void ToggleLights()
        {
            if (_bisectLightsOff)
            {
                foreach (var l in _disabledLights) if (l) l.enabled = true;
                _disabledLights.Clear();
                _bisectLightsOff = false;
            }
            else
            {
                if (_light2DType != null)
                {
                    foreach (var obj in Object.FindObjectsOfType(_light2DType))
                    {
                        var b = obj as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledLights.Add(b);
                    }
                }
                _bisectLightsOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Lights2D: {(_bisectLightsOff ? "OFF" : "ON")}");
        }

        private void ToggleVolumes()
        {
            if (_bisectVolumesOff)
            {
                foreach (var v in _disabledVolumes) if (v) v.enabled = true;
                _disabledVolumes.Clear();
                _bisectVolumesOff = false;
            }
            else
            {
                if (_volumeType != null)
                {
                    foreach (var obj in Object.FindObjectsOfType(_volumeType))
                    {
                        var b = obj as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledVolumes.Add(b);
                    }
                }
                _bisectVolumesOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Volumes: {(_bisectVolumesOff ? "OFF" : "ON")}");
        }

        private void TogglePostFx()
        {
            if (_urpCamDataType == null || _urpRenderPostProp == null) return;
            if (_bisectPostFxOff)
            {
                for (int i = 0; i < _camsWithPostFx.Count; i++)
                {
                    var c = _camsWithPostFx[i];
                    if (!c) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null) try { _urpRenderPostProp.SetValue(data, _camsPostFxOriginal[i]); } catch { }
                }
                _camsWithPostFx.Clear();
                _camsPostFxOriginal.Clear();
                _bisectPostFxOff = false;
            }
            else
            {
                foreach (var c in Camera.allCameras)
                {
                    if (!c) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data == null) continue;
                    try
                    {
                        bool orig = (bool)_urpRenderPostProp.GetValue(data);
                        _camsWithPostFx.Add(c);
                        _camsPostFxOriginal.Add(orig);
                        _urpRenderPostProp.SetValue(data, false);
                    }
                    catch { }
                }
                _bisectPostFxOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] PostFX: {(_bisectPostFxOff ? "OFF" : "ON")}");
        }

        private void ToggleBuildingColliders()
        {
            if (_bisectBuildingCollidersOff)
            {
                foreach (var col in _disabledBuildingColliders) if (col) col.enabled = true;
                _disabledBuildingColliders.Clear();
                _bisectBuildingCollidersOff = false;
            }
            else
            {
                foreach (var col in Object.FindObjectsOfType<Collider2D>())
                {
                    if (col == null || !col.enabled) continue;
                    if (col.gameObject.layer != BUILDING_LAYER) continue;
                    col.enabled = false;
                    _disabledBuildingColliders.Add(col);
                }
                _bisectBuildingCollidersOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Building Colliders: {(_bisectBuildingCollidersOff ? "OFF" : "ON")}");
        }

        // ── Sample (1 Hz) ─────────────────────────────────────────────────────

    }
}