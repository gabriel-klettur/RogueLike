using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorPerfProbe : MonoBehaviour
    {
        private void Sample()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Tilemaps
            _tilemapsTotal = 0;
            _tilemapsVisible = 0;
            var trends = Object.FindObjectsOfType<TilemapRenderer>();
            for (int i = 0; i < trends.Length; i++)
            {
                if (!trends[i].enabled) continue;
                _tilemapsTotal++;
                if (trends[i].isVisible) _tilemapsVisible++;
            }

            // SpriteRenderers (excluding tilemap chunks)
            _spritesTotal = 0;
            _spritesVisible = 0;
            var srs = Object.FindObjectsOfType<SpriteRenderer>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (!srs[i].enabled) continue;
                _spritesTotal++;
                if (srs[i].isVisible) _spritesVisible++;
            }

            // ParticleSystems
            _particlesTotal = 0;
            _particlesActive = 0;
            _particlesPlaying = 0;
            _liveParticleCount = 0;
            var pss = Object.FindObjectsOfType<ParticleSystem>();
            for (int i = 0; i < pss.Length; i++)
            {
                _particlesTotal++;
                if (pss[i].gameObject.activeInHierarchy) _particlesActive++;
                if (pss[i].isPlaying) _particlesPlaying++;
                _liveParticleCount += pss[i].particleCount;
            }

            // Light2Ds
            _lightsTotal = 0;
            _lightsActive = 0;
            if (_light2DType != null)
            {
                var lights = Object.FindObjectsOfType(_light2DType);
                for (int i = 0; i < lights.Length; i++)
                {
                    var c = lights[i] as Component;
                    if (c == null) continue;
                    _lightsTotal++;
                    if (c.gameObject.activeInHierarchy) _lightsActive++;
                }
            }

            // NPCs (FSMMonsterBrain) and EntityCulling
            _npcsAlive = 0;
            _npcsUpdating = 0;
            var brainType = System.Type.GetType("Valkur.Gameplay.FSM.FSMMonsterBrain, Valkur.Gameplay");
            var cullType  = System.Type.GetType("Valkur.Gameplay.EntityCulling, Valkur.Gameplay");
            if (brainType != null)
            {
                var brains = Object.FindObjectsOfType(brainType);
                _npcsAlive = brains.Length;
                if (cullType != null)
                {
                    var prop = cullType.GetProperty("ShouldUpdate", BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < brains.Length; i++)
                    {
                        var go = (brains[i] as Component)?.gameObject;
                        if (go == null) continue;
                        var cull = go.GetComponent(cullType);
                        if (cull == null) { _npcsUpdating++; continue; }
                        bool su = prop != null && (bool)prop.GetValue(cull);
                        if (su) _npcsUpdating++;
                    }
                }
                else _npcsUpdating = _npcsAlive;
            }

            // GC allocation rate (per second)
            long now = System.GC.GetTotalMemory(false);
            if (Time.unscaledTime - _gcLastSecondMark >= 1f)
            {
                _gcAllocLastSecondBytes = System.Math.Max(0, now - _gcLastBaseline);
                _gcLastBaseline = now;
                _gcLastSecondMark = Time.unscaledTime;
            }
            _gcAllocBytes = now;

            // Total GameObjects in scene
            var allGos = Object.FindObjectsOfType<Transform>();
            _totalGameObjects = allGos.Length;

            // MonoBehaviours that have an Update method (rough proxy via overriding type)
            var allMbs = Object.FindObjectsOfType<MonoBehaviour>();
            _activeMonoBehaviours = 0;
            for (int i = 0; i < allMbs.Length; i++)
            {
                if (allMbs[i].isActiveAndEnabled) _activeMonoBehaviours++;
            }

            SampleGpuDiagnostics();
            SampleObjectCounts();
        }

        // ── IMGUI ───────────────────────────────────────────────────────────

        private struct MetricCell { public string Label; public string Value; public GUIStyle Style; }
        private readonly List<MetricCell> _cells = new List<MetricCell>(40);

    }
}