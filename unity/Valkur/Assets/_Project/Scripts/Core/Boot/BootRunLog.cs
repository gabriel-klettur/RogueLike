using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// Writes every boot to <c>persistentDataPath/Diagnostics/Boot/</c> and reads the history
    /// back for <see cref="BootTrend"/>.
    ///
    /// <para><b>Why persistentDataPath and not the repository.</b> A timing is a fact about the
    /// MACHINE that produced it — the same reason the bar's calibration lives in PlayerPrefs —
    /// and a player build cannot write beside its own executable. <c>Diagnostics/</c> is the
    /// umbrella for anything the game measures about itself, so a later probe (frame hitches,
    /// memory) lands beside this one instead of inventing a third folder next to
    /// <c>Saves/</c> and <c>Input/</c>.</para>
    ///
    /// <para><b>Four views of one boot, each for a different reader.</b>
    /// <c>boot_runs.csv</c> is one row per boot (open it in a spreadsheet and plot the total);
    /// <c>boot_phases.csv</c> one row per etapa with prediction and error;
    /// <c>boot_steps.csv</c> one row per step for the hotspot hunt;
    /// <c>boot_history.jsonl</c> the full record, one JSON per line, for tooling; and
    /// <c>runs/boot_&lt;id&gt;.txt</c> plus <c>latest.txt</c> the human report.</para>
    ///
    /// <para><b>Nothing here may fail a boot.</b> Every IO path is caught and logged once; a
    /// full disk costs a missing line, never a launch. And it writes only in Play Mode, so an
    /// EditMode fixture that completes a synthetic run cannot pollute the history with a boot
    /// of zero milliseconds — the shape of the particle-instances incident.</para>
    /// </summary>
    public static class BootRunLog
    {
        public const string RunsCsv = "boot_runs.csv";
        public const string PhasesCsv = "boot_phases.csv";
        public const string StepsCsv = "boot_steps.csv";
        public const string HistoryJsonl = "boot_history.jsonl";
        public const string LatestTxt = "latest.txt";
        public const string RunsFolder = "runs";

        /// <summary>Per-boot text reports kept. The CSVs keep everything.</summary>
        private const int KeepRunReports = 60;

        /// <summary>Past this a series file is rotated to <c>.old</c> (one generation).</summary>
        private const long RotateBytes = 8L * 1024 * 1024;

        private const string RunsHeader =
            "run_id,utc,unity,platform,editor,calibrated,steps,frames,failures," +
            "scene_load_ms,activation_ms,boot_wall_ms,inside_steps_ms,outside_steps_ms," +
            "total_ms,predicted_total_ms,error_pct";
        private const string PhasesHeader =
            "run_id,utc,editor,calibrated,order,phase,steps,predicted_ms,actual_ms,error_ms,error_pct";
        private const string StepsHeader =
            "run_id,index,phase,step,substage,failed,frames,predicted_ms,cpu_ms,wall_ms";

        public static string Directory => Path.Combine(Application.persistentDataPath, "Diagnostics", "Boot");

        /// <summary>
        /// Writes <paramref name="record"/> and the text <paramref name="report"/>. Returns the
        /// path of the per-run report, or null when nothing was written.
        /// </summary>
        public static string TryWrite(BootRunRecord record, string report)
        {
            if (record == null || !Application.isPlaying) return null;
            try
            {
                string dir = Directory;
                System.IO.Directory.CreateDirectory(dir);
                string runs = Path.Combine(dir, RunsFolder);
                System.IO.Directory.CreateDirectory(runs);
                WriteReadmeOnce(dir);

                AppendRows(Path.Combine(dir, RunsCsv), RunsHeader, new[] { RunRow(record) });
                AppendRows(Path.Combine(dir, PhasesCsv), PhasesHeader, PhaseRows(record));
                AppendRows(Path.Combine(dir, StepsCsv), StepsHeader, StepRows(record));
                AppendRows(Path.Combine(dir, HistoryJsonl), null, new[] { JsonUtility.ToJson(record) });

                string text = report ?? string.Empty;
                string runPath = Path.Combine(runs, "boot_" + record.runId + ".txt");
                File.WriteAllText(runPath, text, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, LatestTxt), text, new UTF8Encoding(false));
                PruneRunReports(runs);
                return runPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BootRunLog] No se pudo escribir el registro del arranque: {ex.Message}");
                return null;
            }
        }

        // ── Rows ─────────────────────────────────────────────────────────────

        public static string RunRow(BootRunRecord r)
        {
            float err = r.predictedTotalMs > 0f ? (r.totalMs - r.predictedTotalMs) / r.predictedTotalMs * 100f : 0f;
            return Join(r.runId, r.utc, r.unity, r.platform, B(r.editor), B(r.calibrated),
                        I(r.steps), I(r.frames), I(r.failures),
                        F(r.sceneLoadMs), F(r.activationMs), F(r.bootWallMs), F(r.insideStepsMs),
                        F(r.outsideStepsMs), F(r.totalMs), F(r.predictedTotalMs), F(err));
        }

        public static IEnumerable<string> PhaseRows(BootRunRecord r)
        {
            for (int i = 0; i < r.phases.Count; i++)
            {
                var p = r.phases[i];
                float errMs = p.predictedMs > 0f ? p.actualMs - p.predictedMs : 0f;
                float errPct = p.predictedMs > 0f ? errMs / p.predictedMs * 100f : 0f;
                yield return Join(r.runId, r.utc, B(r.editor), B(r.calibrated), I(i), p.name, I(p.steps),
                                  F(p.predictedMs), F(p.actualMs), F(errMs), F(errPct));
            }
        }

        public static IEnumerable<string> StepRows(BootRunRecord r)
        {
            foreach (var s in r.stepRows)
                yield return Join(r.runId, I(s.index), s.phase, s.label, B(s.substage), B(s.failed),
                                  I(s.frames), F(s.predictedMs), F(s.cpuMs), F(s.wallMs));
        }

        // ── Reading back ─────────────────────────────────────────────────────

        public struct PhaseRow
        {
            public string RunId;
            public bool Editor;
            public int Order;
            public string Phase;
            public float PredictedMs;
            public float ActualMs;
        }

        public struct StepRow
        {
            public string RunId;
            public string Phase;
            public string Step;
            public bool Substage;
            public float PredictedMs;
            public float CpuMs;
            public float WallMs;
        }

        public static List<PhaseRow> ReadPhaseRows(string path = null)
        {
            var rows = new List<PhaseRow>();
            foreach (var f in ReadCsv(path ?? Path.Combine(Directory, PhasesCsv)))
            {
                if (f.Count < 9) continue;
                rows.Add(new PhaseRow
                {
                    RunId = f[0], Editor = f[2] == "1", Order = ParseI(f[4]), Phase = f[5],
                    PredictedMs = ParseF(f[7]), ActualMs = ParseF(f[8]),
                });
            }
            return rows;
        }

        public static List<StepRow> ReadStepRows(string path = null)
        {
            var rows = new List<StepRow>();
            foreach (var f in ReadCsv(path ?? Path.Combine(Directory, StepsCsv)))
            {
                if (f.Count < 10) continue;
                rows.Add(new StepRow
                {
                    RunId = f[0], Phase = f[2], Step = f[3], Substage = f[4] == "1",
                    PredictedMs = ParseF(f[7]), CpuMs = ParseF(f[8]), WallMs = ParseF(f[9]),
                });
            }
            return rows;
        }

        private static IEnumerable<List<string>> ReadCsv(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BootRunLog] No se pudo leer {Path.GetFileName(path)}: {ex.Message}");
                yield break;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                yield return SplitCsv(lines[i]);
            }
        }

        /// <summary>RFC-4180 enough: commas, double quotes, doubled quotes inside a field.</summary>
        public static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        public static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            bool needs = field.IndexOf(',') >= 0 || field.IndexOf('"') >= 0 ||
                         field.IndexOf('\n') >= 0 || field.IndexOf('\r') >= 0;
            string clean = field.Replace("\r", " ").Replace("\n", " ");
            return needs ? "\"" + clean.Replace("\"", "\"\"") + "\"" : clean;
        }

        // ── IO ───────────────────────────────────────────────────────────────

        private static void AppendRows(string path, string header, IEnumerable<string> rows)
        {
            if (File.Exists(path) && new FileInfo(path).Length > RotateBytes)
            {
                string old = path + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }

            var sb = new StringBuilder();
            if (header != null && !File.Exists(path)) sb.Append(header).Append('\n');
            foreach (var row in rows) sb.Append(row).Append('\n');
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static void PruneRunReports(string runsDir)
        {
            var files = System.IO.Directory.GetFiles(runsDir, "boot_*.txt");
            if (files.Length <= KeepRunReports) return;
            // The id is a sortable timestamp, so name order IS age order.
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length - KeepRunReports; i++)
            {
                try { File.Delete(files[i]); } catch { /* a report held open by an editor stays */ }
            }
        }

        private static void WriteReadmeOnce(string dir)
        {
            string path = Path.Combine(dir, "README.txt");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
                "Valkur - registro de arranques\n" +
                "==============================\n\n" +
                "Cada vez que la escena de juego termina de arrancar se anaden filas aqui.\n" +
                "Los tiempos son de ESTA maquina; no se comparan entre ordenadores.\n\n" +
                "boot_runs.csv       una fila por arranque: escena, activacion, arranque, total y prevision\n" +
                "boot_phases.csv     una fila por etapa: prevision, real y error\n" +
                "boot_steps.csv      una fila por paso (y sub-etapa): cpu, pared (cpu + fotogramas) y prevision\n" +
                "boot_history.jsonl  el registro completo, un JSON por linea\n" +
                "runs/               informe legible de cada arranque (se guardan los ultimos 60)\n" +
                "latest.txt          el informe del ultimo arranque\n\n" +
                "La prevision es una media movil exponencial de los arranques anteriores.\n" +
                "En la consola del juego: 'boot tendencia' compara el ultimo arranque con la\n" +
                "mediana de los anteriores y senala las etapas que suben, bajan y los pasos mas caros.\n",
                new UTF8Encoding(false));
        }

        // ── Formatting ───────────────────────────────────────────────────────

        private static string Join(params string[] fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(fields[i]));
            }
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static string B(bool v) => v ? "1" : "0";

        private static float ParseF(string s)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private static int ParseI(string s)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }
}
