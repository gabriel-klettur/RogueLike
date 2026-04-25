using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsPerfProbe : MonoBehaviour
    {
        private void AddCell(string label, string value, GUIStyle style = null)
            => _cells.Add(new MetricCell { Label = label, Value = value, Style = style ?? _rowStyle });

        private void OnGUI()
        {
            if (!Visible) return;
            if (!_stylesReady) BuildStyles();

            _cells.Clear();

            // ── Frame rate ──
            AddCell("FPS",        $"{_fps,5:F0}  ({_frameMs:F1}ms)", FpsStyle(_fps));
            AddCell("Target FPS", $"{Application.targetFrameRate}  vsync={QualitySettings.vSyncCount}");

            // ── Buildings ──
            AddCell("Buildings",  $"vis {_buildingsVisible}/{_buildingsTotal}",
                                   WarnIfOver(_buildingsTotal, 200));
            AddCell("Bldg Cols",  $"act {_buildingCollidersActive} / en {_buildingCollidersEnabled} / tot {_buildingCollidersTotal}",
                                   WarnIfOver(_buildingCollidersActive, 300));
            AddCell("CollSess",   $"active {_colliderSessionsActive} dirty {_colliderSessionsDirty}",
                                   WarnIfOver(_colliderSessionsDirty, 0));

            // ── Scene ──
            AddCell("Sprites",    $"vis {_spritesVisible}/{_spritesTotal}",   WarnIfOver(_spritesVisible, 600));
            AddCell("Particles",  $"p{_particlesPlaying} live {_liveParticleCount}", WarnIfOver(_liveParticleCount, 3000));
            AddCell("Lights2D",   $"act {_lightsActive}/{_lightsTotal}",      WarnIfOver(_lightsActive, 24));
            AddCell("NPCs (FSM)", $"upd {_npcsUpdating}/{_npcsAlive}");
            AddCell("GameObjects",$"{_totalGameObjects}",                      WarnIfOver(_totalGameObjects, 5000));
            AddCell("MonoBehavs", $"active {_activeMonoBehaviours}",          WarnIfOver(_activeMonoBehaviours, 1500));
            AddCell("GC alloc/s", $"{_gcAllocLastSecondBytes / 1024f:F0} KB", WarnIfOver((int)(_gcAllocLastSecondBytes / 1024), 200));
            AddCell("Colliders2D",$"on {_colliders2DEnabled}/{_colliders2DActive}", WarnIfOver(_colliders2DEnabled, 600));
            AddCell("Rigidbody2D",$"{_rigidbodies2D}",                        WarnIfOver(_rigidbodies2D, 200));
            AddCell("Animators",  $"vis {_animatorsVisible}/{_animators}",    WarnIfOver(_animatorsVisible, 50));

#if UNITY_EDITOR
            AddCell("Draw calls", $"{_drawCalls} sp={_setPassCalls} b={_batches}", WarnIfOver(_drawCalls, 800));
            AddCell("Tris/Verts", $"{_triangles / 1000}k / {_vertices / 1000}k");
#endif

            // ── GPU / cameras ──
            AddCell("Cameras",    $"{_camerasActive} active",                 WarnIfOver(_camerasActive, 2));
            AddCell("Cam0",       $"{Trim(_cam0Name, 12)} {_cam0Info}");
            AddCell("Cam1",       $"{Trim(_cam1Name, 12)} {_cam1Info}");
            AddCell("Cam2",       $"{Trim(_cam2Name, 12)} {_cam2Info}");
#if UNITY_EDITOR
            AddCell("SceneView",  _sceneViewVisible ? "VISIBLE (gpu cost!)" : "hidden",
                                   _sceneViewVisible ? _warnStyle : _goodStyle);
            AddCell("GameView",   $"{_gameViewWidth}x{_gameViewHeight}");
            AddCell("RT mem",     $"{_renderTextureMemBytes / 1024 / 1024} MB", WarnIfOver((int)(_renderTextureMemBytes / 1024 / 1024), 200));
            AddCell("GPU drv",    $"{_textureMemBytes / 1024 / 1024} MB",     WarnIfOver((int)(_textureMemBytes / 1024 / 1024), 1500));
#endif
            AddCell("Screen",     $"{_screenW}x{_screenH}");
            AddCell("View w/u",   $"{_viewWidthW:F1}u  ortho={_orthoSize:F1}", WarnIfOver((int)_viewWidthW, 60));
            AddCell("ShadowCast", $"vis {_shadowCaster2DCount}/{_shadowCasterTotal}", WarnIfOver(_shadowCaster2DCount, 20));
            AddCell("LightShadw", $"{_light2DWithShadows}",                   WarnIfOver(_light2DWithShadows, 2));
            AddCell("Materials",  $"{_uniqueMaterialCount} uniq",             WarnIfOver(_uniqueMaterialCount, 30));

            // ── Bisection status ──
            AddCell("[F2] xCams",   _bisectExtraCamerasOff       ? "OFF" : "on", _bisectExtraCamerasOff       ? _warnStyle : _rowStyle);
            AddCell("[F3] Sprites", _bisectSpritesOff             ? "OFF" : "on", _bisectSpritesOff             ? _warnStyle : _rowStyle);
            AddCell("[F4] Lights",  _bisectLightsOff              ? "OFF" : "on", _bisectLightsOff              ? _warnStyle : _rowStyle);
            AddCell("[F5] Volumes", _bisectVolumesOff             ? "OFF" : "on", _bisectVolumesOff             ? _warnStyle : _rowStyle);
            AddCell("[F6] PostFX",  _bisectPostFxOff              ? "OFF" : "on", _bisectPostFxOff              ? _warnStyle : _rowStyle);
            AddCell("[F7] BldgCol", _bisectBuildingCollidersOff   ? "OFF" : "on", _bisectBuildingCollidersOff   ? _warnStyle : _rowStyle);

            // ── Profiler recorders ──
            if (_recorders != null)
            {
                for (int i = 0; i < _recorders.Length; i++)
                {
                    var rec = _recorders[i].Recorder;
                    string val = (rec != null && rec.isValid)
                        ? $"{_recorderMs[i],5:F2} ms"
                        : "n/a";
                    GUIStyle st = (rec != null && _recorderMs[i] >= 5f) ? _warnStyle : _rowStyle;
                    AddCell(ShortenMarker(_recorders[i].Label), val, st);
                }
            }

            // ── 5-column grid layout ──
            const int   COLS     = 5;
            const float COL_W    = 200f;
            const float ROW_H    = 16f;
            const float HEADER_H = 22f;
            const float PAD      = 8f;

            int   total  = _cells.Count;
            int   rows   = (total + COLS - 1) / COLS;
            float panelW = COLS * COL_W + PAD * 2f;
            float panelH = HEADER_H + rows * ROW_H + PAD * 2f + 4f;
            float x0     = 12f;
            float y0     = Screen.height - panelH - 12f;

            GUI.Box(new Rect(x0, y0, panelW, panelH), GUIContent.none);
            GUI.Label(new Rect(x0 + PAD, y0 + 4f, panelW - PAD * 2f, HEADER_H),
                      "BUILDINGS PERF PROBE", _headerStyle);

            float gridY = y0 + HEADER_H + 4f;
            for (int i = 0; i < total; i++)
            {
                int   col = i % COLS;
                int   row = i / COLS;
                float cx  = x0 + PAD + col * COL_W;
                float cy  = gridY + row * ROW_H;
                var   cell = _cells[i];
                GUI.Label(new Rect(cx,        cy, 80f,          ROW_H), cell.Label, _labelStyle);
                GUI.Label(new Rect(cx + 82f,  cy, COL_W - 84f,  ROW_H), cell.Value, cell.Style);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ShortenMarker(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int idx  = s.LastIndexOf('.');
            string t = idx >= 0 ? s.Substring(idx + 1) : s;
            return t.Length > 18 ? t.Substring(0, 18) : t;
        }

        private static string Trim(string s, int n)
            => string.IsNullOrEmpty(s) ? "-" : (s.Length <= n ? s : s.Substring(0, n));

        private GUIStyle FpsStyle(float fps)
            => fps >= 110f ? _goodStyle : fps >= 60f ? _rowStyle : _warnStyle;

        private GUIStyle WarnIfOver(int v, int threshold) => v > threshold ? _warnStyle : _rowStyle;

        private void BuildStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1f) } };
            _labelStyle  = new GUIStyle(GUI.skin.label) { fontSize = 11,
                normal = { textColor = new Color(0.60f, 0.62f, 0.68f) } };
            _rowStyle    = new GUIStyle(GUI.skin.label) { fontSize = 11,
                normal = { textColor = new Color(0.90f, 0.92f, 0.96f) } };
            _warnStyle   = new GUIStyle(_rowStyle) { normal = { textColor = new Color(1f, 0.85f, 0.2f) } };
            _goodStyle   = new GUIStyle(_rowStyle) { normal = { textColor = new Color(0.45f, 1f, 0.45f) } };
            _stylesReady = true;
        }
    }
}