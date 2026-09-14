using System.Collections.Generic;
using System.Text;

namespace Valkur.Core.Boot
{
    /// <summary>
    /// Reads the boot history and answers the three questions it is kept for: which etapas
    /// are GROWING, which are SHRINKING, and where the time actually goes.
    ///
    /// <para><b>The last boot is compared against the MEDIAN of the ones before it, never
    /// against the previous one alone.</b> The first boot after launching the Editor pages
    /// every asset in and runs seconds slower than the next; a comparison against one run
    /// reports that as a regression every morning and as an optimisation every second launch.
    /// A median of up to ten ignores the outlier on either side.</para>
    ///
    /// <para><b>A change must clear both a percentage AND a floor in milliseconds</b>, because
    /// a 2 ms etapa that becomes 3 ms is +50 % and is noise, and a 3 s etapa that grows 5 %
    /// is 150 ms nobody asked for.</para>
    ///
    /// <para>Editor boots and player-build boots are separate series: an Editor boot carries
    /// the seventeen authoring editors and shader compilation on demand, and mixing the two
    /// makes every switch between them look like a regression.</para>
    /// </summary>
    public static class BootTrend
    {
        public const float ChangePct = 10f;
        public const float ChangeFloorMs = 25f;

        public enum Verdict { Stable, Growing, Shrinking, New }

        public struct PhaseTrend
        {
            public string Phase;
            public float LastMs;
            public float BaselineMs;
            public float DeltaMs;
            public float DeltaPct;
            public float PredictedMs;
            public float PredictionErrorPct;
            public int Samples;
            public Verdict Verdict;
        }

        public struct Hotspot
        {
            public string Phase;
            public string Step;
            public float MedianWallMs;
            public float MedianCpuMs;
            public float SharePct;
            /// <summary>Spread over the window, as a fraction of the median. High is unstable.</summary>
            public float Spread;
        }

        public static Verdict Classify(float lastMs, float baselineMs, int baselineSamples)
        {
            if (baselineSamples <= 0) return Verdict.New;
            float delta = lastMs - baselineMs;
            float pct = baselineMs > 0f ? delta / baselineMs * 100f : 0f;
            if (delta >= ChangeFloorMs && pct >= ChangePct) return Verdict.Growing;
            if (-delta >= ChangeFloorMs && -pct >= ChangePct) return Verdict.Shrinking;
            return Verdict.Stable;
        }

        /// <summary>Run ids in the order they appear (the file is append-only, so that is time order).</summary>
        public static List<string> RunIds(IReadOnlyList<BootRunLog.PhaseRow> rows, bool editor)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var r in rows)
                if (r.Editor == editor && seen.Add(r.RunId)) ids.Add(r.RunId);
            return ids;
        }

        public static List<PhaseTrend> AnalyzePhases(IReadOnlyList<BootRunLog.PhaseRow> rows, bool editor, int window = 10)
        {
            var result = new List<PhaseTrend>();
            if (rows == null || rows.Count == 0) return result;
            var ids = RunIds(rows, editor);
            if (ids.Count == 0) return result;

            string last = ids[ids.Count - 1];
            var baselineIds = new HashSet<string>();
            for (int i = ids.Count - 2; i >= 0 && baselineIds.Count < window; i--) baselineIds.Add(ids[i]);

            var order = new List<string>();
            var lastRow = new Dictionary<string, BootRunLog.PhaseRow>();
            var history = new Dictionary<string, List<float>>();
            foreach (var r in rows)
            {
                if (r.Editor != editor) continue;
                if (r.RunId == last)
                {
                    if (!lastRow.ContainsKey(r.Phase)) order.Add(r.Phase);
                    lastRow[r.Phase] = r;
                }
                else if (baselineIds.Contains(r.RunId))
                {
                    if (!history.TryGetValue(r.Phase, out var list)) history[r.Phase] = list = new List<float>();
                    list.Add(r.ActualMs);
                }
            }

            foreach (var phase in order)
            {
                var r = lastRow[phase];
                history.TryGetValue(phase, out var h);
                int n = h?.Count ?? 0;
                float baseline = n > 0 ? Median(h) : 0f;
                result.Add(new PhaseTrend
                {
                    Phase = phase,
                    LastMs = r.ActualMs,
                    BaselineMs = baseline,
                    DeltaMs = n > 0 ? r.ActualMs - baseline : 0f,
                    DeltaPct = n > 0 && baseline > 0f ? (r.ActualMs - baseline) / baseline * 100f : 0f,
                    PredictedMs = r.PredictedMs,
                    PredictionErrorPct = r.PredictedMs > 0f ? (r.ActualMs - r.PredictedMs) / r.PredictedMs * 100f : 0f,
                    Samples = n,
                    Verdict = Classify(r.ActualMs, baseline, n),
                });
            }
            return result;
        }

        /// <summary>The steps that cost most over the last <paramref name="lastRuns"/> boots, by median wall time.</summary>
        public static List<Hotspot> Hotspots(IReadOnlyList<BootRunLog.StepRow> rows, int lastRuns = 5, int top = 8)
        {
            var result = new List<Hotspot>();
            if (rows == null || rows.Count == 0) return result;

            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var r in rows) if (seen.Add(r.RunId)) ids.Add(r.RunId);
            var window = new HashSet<string>();
            for (int i = ids.Count - 1; i >= 0 && window.Count < lastRuns; i--) window.Add(ids[i]);

            var wall = new Dictionary<string, List<float>>();
            var cpu = new Dictionary<string, List<float>>();
            var phaseOf = new Dictionary<string, string>();
            var totals = new Dictionary<string, float>();
            foreach (var r in rows)
            {
                if (!window.Contains(r.RunId) || r.Substage) continue;
                string key = r.Step;
                if (!wall.TryGetValue(key, out var w)) { wall[key] = w = new List<float>(); cpu[key] = new List<float>(); }
                w.Add(r.WallMs > 0f ? r.WallMs : r.CpuMs);
                cpu[key].Add(r.CpuMs);
                phaseOf[key] = r.Phase;
                totals.TryGetValue(r.RunId, out float t);
                totals[r.RunId] = t + (r.WallMs > 0f ? r.WallMs : r.CpuMs);
            }

            var runTotals = new List<float>(totals.Values);
            float medianTotal = runTotals.Count > 0 ? Median(runTotals) : 0f;

            foreach (var kv in wall)
            {
                float med = Median(kv.Value);
                result.Add(new Hotspot
                {
                    Phase = phaseOf[kv.Key],
                    Step = kv.Key,
                    MedianWallMs = med,
                    MedianCpuMs = Median(cpu[kv.Key]),
                    SharePct = medianTotal > 0f ? med / medianTotal * 100f : 0f,
                    Spread = med > 0f && kv.Value.Count > 1 ? (Max(kv.Value) - Min(kv.Value)) / med : 0f,
                });
            }
            result.Sort((a, b) => b.MedianWallMs.CompareTo(a.MedianWallMs));
            if (result.Count > top) result.RemoveRange(top, result.Count - top);
            return result;
        }

        /// <summary>The console report. Plain ASCII: it is also pasted into chats and issues.</summary>
        public static string Report(IReadOnlyList<BootRunLog.PhaseRow> phaseRows,
                                    IReadOnlyList<BootRunLog.StepRow> stepRows, bool editor, int window = 10)
        {
            var sb = new StringBuilder();
            var ids = RunIds(phaseRows ?? new List<BootRunLog.PhaseRow>(), editor);
            string series = editor ? "Editor" : "build";
            if (ids.Count == 0)
                return $"boot tendencia: no hay arranques registrados de {series} todavia. " +
                       "Se anade uno cada vez que la escena de juego termina de arrancar.";

            // Totals per run, oldest first, for the strip.
            var totals = new Dictionary<string, float>();
            foreach (var r in phaseRows)
            {
                if (r.Editor != editor) continue;
                totals.TryGetValue(r.RunId, out float t);
                totals[r.RunId] = t + r.ActualMs;
            }

            sb.Append("Tendencia del arranque (").Append(series).Append(", ").Append(ids.Count)
              .Append(" arranques registrados)").AppendLine();

            int from = ids.Count > window + 1 ? ids.Count - (window + 1) : 0;
            sb.Append("Totales recientes (s): ");
            for (int i = from; i < ids.Count; i++)
            {
                if (i > from) sb.Append("  ");
                sb.Append((totals[ids[i]] / 1000f).ToString("F2"));
            }
            sb.AppendLine();

            var phases = AnalyzePhases(phaseRows, editor, window);
            sb.AppendLine("Etapa                              ultimo    mediana     delta   prevision  veredicto");
            foreach (var p in phases)
            {
                sb.Append("  ").Append(Pad(p.Phase, 32))
                  .Append(Ms(p.LastMs)).Append(p.Samples > 0 ? Ms(p.BaselineMs) : "        --")
                  .Append(p.Samples > 0 ? Signed(p.DeltaPct).PadLeft(9) + "%" : "         -")
                  .Append(p.PredictedMs > 0f ? (Signed(p.PredictionErrorPct) + "%").PadLeft(11) : "          -")
                  .Append("  ").Append(Word(p.Verdict)).AppendLine();
            }

            var hot = Hotspots(stepRows, 5, 8);
            if (hot.Count > 0)
            {
                sb.AppendLine("Pasos mas caros (mediana de los ultimos 5, pared = cuerpo + fotogramas cedidos):");
                foreach (var h in hot)
                {
                    sb.Append("  ").Append(Ms(h.MedianWallMs)).Append("  ")
                      .Append(h.SharePct.ToString("F1").PadLeft(5)).Append("%  cpu ")
                      .Append(h.MedianCpuMs.ToString("F0").PadLeft(5)).Append(" ms  ")
                      .Append(Pad(h.Phase, 22)).Append(h.Step);
                    if (h.Spread > 0.5f) sb.Append("  [INESTABLE]");
                    if (h.SharePct >= 15f) sb.Append("  [OPTIMIZAR]");
                    sb.AppendLine();
                }
            }
            return sb.ToString().TrimEnd();
        }

        public static string Word(Verdict v)
        {
            switch (v)
            {
                case Verdict.Growing: return "SUBE";
                case Verdict.Shrinking: return "BAJA";
                case Verdict.New: return "nueva";
                default: return "estable";
            }
        }

        public static float Median(List<float> values)
        {
            if (values == null || values.Count == 0) return 0f;
            var copy = new List<float>(values);
            copy.Sort();
            int n = copy.Count;
            return n % 2 == 1 ? copy[n / 2] : 0.5f * (copy[n / 2 - 1] + copy[n / 2]);
        }

        private static float Max(List<float> v) { float m = float.MinValue; foreach (var x in v) if (x > m) m = x; return m; }
        private static float Min(List<float> v) { float m = float.MaxValue; foreach (var x in v) if (x < m) m = x; return m; }
        private static string Ms(float ms) => (ms.ToString("F0") + " ms").PadLeft(10);
        private static string Signed(float v) => (v >= 0f ? "+" : string.Empty) + v.ToString("F1");
        private static string Pad(string s, int n)
        {
            s = s ?? string.Empty;
            return s.Length >= n ? s.Substring(0, n - 1) + " " : s.PadRight(n);
        }
    }
}
