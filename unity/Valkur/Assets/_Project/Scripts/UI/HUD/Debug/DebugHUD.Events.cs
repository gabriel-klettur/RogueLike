using System.Globalization;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    public partial class DebugHUD
    {
        private int _seenHitches;
        private int _seenErrors;
        private int _seenGc;
        private int _grade = -1;
        private int _pendingGrade = -1;
        private float _pendingGradeTime;
        private float _copyFeedback;

        /// <summary>A grade has to hold this long before the panel calls it a change.</summary>
        private const float GradeHysteresisSeconds = 1f;
        private const float CopyFeedbackSeconds = 1.2f;

        // -- Events (R8: motes answer events, never states) ----------------------------

        private void SyncEventBaselines()
        {
            var perf = PerformanceMonitor.Instance;
            _seenHitches = perf != null ? perf.Hitches.TotalRecorded : 0;
            _seenErrors = perf != null ? perf.Console.Errors : 0;
            _seenGc = perf != null ? perf.SessionGcCollections : 0;
            _grade = perf != null && !perf.Stats.IsEmpty ? _style.GradeOf(perf.Stats.AvgMs) : -1;
            _pendingGrade = -1;
        }

        /// <summary>
        /// Polls the monitor for what happened since the last frame. Polled rather than
        /// subscribed: the monitor is created per scene and the console count is written from
        /// other threads, so a counter read on the main thread is the one answer with no
        /// lifetime to get wrong.
        /// </summary>
        private void PollEvents(float dt)
        {
            var perf = PerformanceMonitor.Instance;
            if (perf == null || _motes == null) return;
            bool panel = _level >= LevelPanel;

            int hitches = perf.Hitches.TotalRecorded;
            if (hitches > _seenHitches)
            {
                _seenHitches = hitches;
                EmitHitch(panel);
            }

            int errors = perf.Console.Errors;
            if (errors > _seenErrors)
            {
                _seenErrors = errors;
                if (panel) EmitAt(EdgeOf(_stats2), _style.bad, _style.motesOnError, upward: true);
            }

            int gc = perf.SessionGcCollections;
            if (gc > _seenGc)
            {
                _seenGc = gc;
                if (panel) EmitAt(EdgeOf(_stats2), _style.textDim, _style.motesOnGc, upward: true);
            }

            if (perf.Stats.IsEmpty) return;
            int grade = _style.GradeOf(perf.Stats.AvgMs);
            if (grade == _grade) { _pendingGrade = -1; return; }
            if (grade != _pendingGrade)
            {
                _pendingGrade = grade;
                _pendingGradeTime = 0f;
                return;
            }
            _pendingGradeTime += dt;
            if (_pendingGradeTime < GradeHysteresisSeconds) return;
            _grade = grade;
            _pendingGrade = -1;
            EmitRing(BigNumberCentre(), _style.ForFrame(perf.Stats.AvgMs), _style.motesOnStateChange, startRadius: 13f);
        }

        /// <summary>Motes rising from the newest column of whichever graph is showing: the place
        /// the frame just drew itself, so the eye is pulled to the mark it will leave.</summary>
        private void EmitHitch(bool panel)
        {
            var graph = panel ? _graph : _chipGraph;
            if (graph == null) return;
            var p = TexelPositionOf(graph.Root, graph.Width - 1, graph.Height);
            EmitAt(p, _style.bad, _style.motesOnHitch, upward: true);
        }

        private void EmitAt(Vector2 p, Color colour, int count, bool upward)
        {
            float speed = _style.moteSpeedTexels;
            for (int i = 0; i < count; i++)
            {
                float spread = (i - (count - 1) * 0.5f) * 0.35f;
                var v = upward
                    ? new Vector2(spread * speed * 0.6f, speed * (0.7f + 0.3f * ((i * 37) % 10) / 10f))
                    : new Vector2(spread * speed, 0f);
                _motes.Emit(p, v, colour, _style.moteLifeSeconds, i % 3 == 0 ? HudMoteShape.Plus : HudMoteShape.Dot,
                            gravity: speed * 0.8f, drag: 1.5f);
            }
        }

        /// <summary>
        /// A ring of motes born <paramref name="startRadius"/> texels out from the centre, so it
        /// opens AROUND what it marks and never crosses the digits (measured on the first live
        /// capture: a GC puff born on its counter turned "4.9" into an unreadable glyph).
        /// </summary>
        private void EmitRing(Vector2 centre, Color colour, int count, float startRadius = 0f)
        {
            float speed = _style.moteSpeedTexels;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / Mathf.Max(1, count);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                _motes.Emit(centre + dir * startRadius, dir * speed, colour, _style.moteLifeSeconds,
                            HudMoteShape.Dot, drag: 3f);
            }
        }

        /// <summary>The frame's right border beside a row: where a row's event rises from,
        /// over the frame and never over the row's text.</summary>
        private Vector2 EdgeOf(DebugHudRow row)
        {
            if (row == null) return Vector2.zero;
            var p = TexelPositionOf(row.Root, 0f, RowH * 0.5f);
            return new Vector2(Width - 2, p.y);
        }

        private Vector2 BigNumberCentre()
        {
            if (_level == LevelChip && _chipNumbers != null) return TexelPositionOf(_chipNumbers.Root, 10, 5);
            return _bigNumbers != null ? TexelPositionOf(_bigNumbers.Root, 10, 5) : Vector2.zero;
        }

        /// <summary>A point inside <paramref name="rt"/>, in the pixel root's texel space.</summary>
        private Vector2 TexelPositionOf(RectTransform rt, float localX, float localY)
        {
            if (rt == null || _pixels == null) return Vector2.zero;
            var world = rt.TransformPoint(new Vector3(localX, localY, 0f));
            var local = _pixels.InverseTransformPoint(world);
            return new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        private void TickCopyFeedback(float dt)
        {
            if (_copyFeedback <= 0f) return;
            _copyFeedback -= dt;
            if (_copyFeedback <= 0f) _copyLabel.SetText(DebugHudText.Copy);
        }

        // -- The report ----------------------------------------------------------------------

        public string CopyReport()
        {
            string report = BuildReport();
            GUIUtility.systemCopyBuffer = report;
            if (_copyLabel != null)
            {
                _copyLabel.SetText(DebugHudText.Copied);
                _copyFeedback = CopyFeedbackSeconds;
                if (_motes != null && _level >= LevelPanel)
                    EmitRing(TexelPositionOf((RectTransform)_copyButton.transform, 16, 5), _style.good, _style.motesOnCopy,
                             startRadius: 17f);
            }
            return report;
        }

        /// <summary>
        /// A plain-text snapshot for a bug report: where, what the frames looked like, the last
        /// hitches, the console, who was around. Plain ASCII-safe Spanish, so it survives being
        /// pasted into any tracker. What turns "it stutters in town" into something reproducible.
        /// </summary>
        public string BuildReport()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(1024);
            var perf = PerformanceMonitor.Instance;
            var s = perf != null ? perf.Stats : default;

            sb.Append("VALKUR - informe de depuracion\n");
            sb.Append("Fecha: ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv))
              .Append("   Version: ").Append(Application.version)
              .Append("   Unity: ").Append(Application.unityVersion).Append('\n');
            sb.Append("Plataforma: ").Append(Application.platform)
              .Append("   Pantalla: ").Append(Screen.width).Append('x').Append(Screen.height)
              .Append("   Escena: ").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('\n');

            var player = EntityRegistry.Player;
            if (player != null)
            {
                var zones = ResolveZones();
                var pos = player.transform.position;
                sb.Append("Jugador: ").Append(PlayerSelectionState.SelectedPlayerKey)
                  .Append("   Zona: ").Append(zones != null ? zones.CurrentZone : "-")
                  .Append("   Pos: ").Append(pos.x.ToString("0.0", inv)).Append(", ").Append(pos.y.ToString("0.0", inv))
                  .Append("   Postura: ").Append(PlayerStance.IsPeace ? "paz" : "guerra").Append('\n');
            }
            else sb.Append("Jugador: sin jugador en escena\n");

            if (!s.IsEmpty)
            {
                sb.Append("Frames (ultimos ").Append(PerformanceMonitor.StatsWindowSeconds.ToString("0", inv)).Append(" s, ")
                  .Append(s.Frames).Append(" frames): ").Append(s.Fps.ToString("0", inv)).Append(" FPS")
                  .Append("  media ").Append(s.AvgMs.ToString("0.0", inv))
                  .Append("  p50 ").Append(s.P50Ms.ToString("0.0", inv))
                  .Append("  p95 ").Append(s.P95Ms.ToString("0.0", inv))
                  .Append("  p99 ").Append(s.P99Ms.ToString("0.0", inv))
                  .Append("  max ").Append(s.MaxMs.ToString("0.0", inv)).Append(" ms\n");
            }
            else sb.Append("Frames: sin datos\n");

            if (_counters.Running)
            {
                sb.Append("CPU ").Append(Fmt(_counters.MainThreadMs, inv)).Append(" ms  GPU ").Append(Fmt(_counters.GpuMs, inv))
                  .Append(" ms  Lotes ").Append(FmtCount(_counters.Batches))
                  .Append("  SetPass ").Append(FmtCount(_counters.SetPassCalls))
                  .Append("  Asig/frame ").Append(_counters.GcAllocatedInFrame < 0 ? "-" : DebugHudText.Bytes(_counters.GcAllocatedInFrame))
                  .Append("  Memoria ").Append(_counters.UsedMemory < 0 ? "-" : DebugHudText.Bytes(_counters.UsedMemory)).Append('\n');
            }

            if (perf != null)
            {
                sb.Append("GC: ").Append(perf.SessionGcCollections).Append(" colecciones en ")
                  .Append(DebugHudText.Clock(perf.SessionSeconds)).Append(" (")
                  .Append(perf.GcPerMinute.ToString("0.0", inv)).Append("/min)\n");

                var log = perf.Hitches;
                sb.Append("Tirones (").Append(log.TotalRecorded).Append("): ");
                if (log.Count == 0) sb.Append("ninguno");
                for (int i = 0; i < log.Count && i < 4; i++)
                {
                    var h = log.Get(i);
                    if (i > 0) sb.Append(" | ");
                    sb.Append(h.Ms.ToString("0.0", inv)).Append(" ms hace ")
                      .Append(DebugHudText.Clock(Time.realtimeSinceStartup - h.Time));
                    if (h.DuringGc) sb.Append(" (GC)");
                }
                sb.Append('\n');

                sb.Append("Consola: ").Append(perf.Console.Errors).Append(" errores, ")
                  .Append(perf.Console.Warnings).Append(" avisos");
                if (!string.IsNullOrEmpty(perf.Console.LastError))
                    sb.Append(". Ultimo error: ").Append(perf.Console.LastError);
                sb.Append('\n');
            }

            if (player != null)
            {
                DebugHudNearby.Collect(EntityRegistry.Monsters, player.transform.position, _style != null ? _style.nearbyRadius : 15f,
                                       0, EntityFaction.SideOf, _nearby, out int h, out int n, out int a);
                sb.Append("A ").Append((_style != null ? _style.nearbyRadius : 15f).ToString("0", inv)).Append(" u: ")
                  .Append(h).Append(" hostiles, ").Append(n).Append(" neutrales, ").Append(a).Append(" aliados\n");
            }
            return sb.ToString();
        }

        private static string Fmt(float v, CultureInfo inv) => v < 0f ? "-" : v.ToString("0.0", inv);
        private static string FmtCount(long v) => v < 0 ? "-" : v.ToString();
    }
}
