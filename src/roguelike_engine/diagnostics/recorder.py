import os
import json
import time
import threading
from datetime import datetime
from typing import Optional, Any, Dict, List
import logging

# Build a stable snapshot of the current perf_log in two representations
from .overlay.services import perf_tree as _perf
from roguelike_game.utils.benchmark import save_benchmarks

logger = logging.getLogger(__name__)


class DiagnosticsSessionRecorder:
    """
    Records per-second snapshots of the Diagnostics overlay while it's visible
    and flushes them as a JSON file when closed (F9 toggled off).

    Design goals:
    - Robust: never crash the game on errors.
    - Low overhead: O(1) work per render call plus 1Hz sampling.
    - AI-friendly: structured JSON with both flat metrics and tree.
    - Scalable: bounded memory while recording and predictable file size.
    """

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._active: bool = False
        self._session: Dict[str, Any] | None = None
        self._last_sample_time: float = 0.0
        self._sampling_interval: float = 1.0  # seconds
        # Streaming aggregations for professional summary output
        self._agg: Dict[str, Any] | None = None
        # Store per-second samples to compute unified summary on close (F9 OFF)
        self._store_samples: bool = True

    # --- Lifecycle -----------------------------------------------------------
    def on_toggle(self, enabled: bool, game: Optional[Any] = None) -> None:
        """Hook to be called when F9 toggles the overlay.

        enabled=True -> start new session if not already active.
        enabled=False -> finalize and flush to disk if active.
        """
        try:
            if enabled:
                with self._lock:
                    if not self._active:
                        self._start_session(game)
            else:
                self.finish_if_active(game)
        except Exception:
            # Never raise
            return

    def finish_if_active(self, game: Optional[Any] = None) -> None:
        try:
            with self._lock:
                if not self._active or self._session is None:
                    return
                # finalize
                self._session["ended_at"] = self._now_iso()
                start_ts = float(self._session.get("_started_ts", 0.0) or 0.0)
                self._session["duration_seconds"] = max(0.0, time.time() - start_ts)
                # If no per-second samples were captured (e.g., overlay open < 1s), force one snapshot now
                try:
                    if self._store_samples and not self._session.get("samples"):
                        perf_log = getattr(game, 'perf_log', {}) if game is not None else {}
                        state = getattr(game, 'state', None) if game is not None else None
                        self._append_sample_from_perf_log(perf_log, state)
                except Exception:
                    pass
                # Build benchmarks mapping (name -> list[seconds]) from per-second samples
                try:
                    samples: List[Dict[str, Any]] = list(self._session.get("samples", []))
                    benches: Dict[str, List[float]] = {}
                    for smp in samples:
                        flat_ms = smp.get("metrics_ms") or {}
                        if not isinstance(flat_ms, dict):
                            continue
                        for k, v_ms in flat_ms.items():
                            try:
                                v_sec = float(v_ms) / 1000.0
                            except Exception:
                                continue
                            benches.setdefault(str(k), []).append(v_sec)
                    if benches:
                        # Use unified writer (JSON + .log table) in logs/benchmarks
                        json_path, log_path = save_benchmarks(benches, base_dir=None)
                        try:
                            logger.info(f"Diagnostics benchmarks saved: json='{json_path}' log='{log_path}'")
                        except Exception:
                            pass
                    else:
                        try:
                            logger.info("Diagnostics benchmarks: no samples captured; nothing saved.")
                        except Exception:
                            pass
                except Exception:
                    pass
                # reset
                self._active = False
                self._session = None
                self._last_sample_time = 0.0
                self._agg = None
        except Exception:
            return

    # --- Sampling ------------------------------------------------------------
    def record_tick(
        self,
        model: Any,
        state: Optional[Any] = None,
        camera: Optional[Any] = None,
        map_manager: Optional[Any] = None,
        entities: Optional[Any] = None,
    ) -> None:
        """Call this on each overlay render(). It samples at ~1Hz."""
        try:
            now = time.time()
            with self._lock:
                if not self._active:
                    # Start lazily if not already started
                    self._start_session(None)
                if (now - self._last_sample_time) < self._sampling_interval:
                    return
                self._last_sample_time = now
                self._append_sample(model, state)
        except Exception:
            return

    # --- Internals -----------------------------------------------------------
    def _start_session(self, game: Optional[Any]) -> None:
        self._active = True
        self._last_sample_time = 0.0
        started_ts = time.time()
        ctx = self._extract_game_context(game)
        self._session = {
            "session_id": f"diag_{datetime.utcfromtimestamp(started_ts).strftime('%Y%m%dT%H%M%S')}Z",
            "started_at": self._now_iso(),
            "_started_ts": started_ts,
            "sampling_interval_seconds": self._sampling_interval,
            "game_context": ctx,
            "samples": [],
        }
        # Init streaming aggregators
        self._agg = {
            "metrics": {},  # key -> {count, sum, min, max}
            "fps": {"count": 0, "sum": 0.0, "min": None, "max": None},
            "frame_time_ms": {"count": 0, "sum": 0.0, "min": None, "max": None},
        }

    def _append_sample(self, model: Any, state: Optional[Any]) -> None:
        if self._session is None:
            return
        # Build metrics flat mapping and tree from current perf_log
        perf_log: Dict[str, List[float]] = getattr(model, 'perf_log', {}) or {}
        flat = {}
        try:
            for key, samples in perf_log.items():
                if not samples:
                    continue
                recent = samples[-60:]
                if not recent:
                    continue
                avg_ms = (sum(recent) / len(recent)) * 1000.0
                flat[str(key)] = round(float(avg_ms), 3)
        except Exception:
            pass
        # Accumulate streaming stats per key
        try:
            if isinstance(self._agg, dict):
                metrics_agg = self._agg.get("metrics", {})
                for k, v in flat.items():
                    rec = metrics_agg.get(k)
                    if rec is None:
                        rec = {"count": 0, "sum": 0.0, "min": None, "max": None}
                        metrics_agg[k] = rec
                    rec["count"] += 1
                    rec["sum"] += float(v)
                    rec["min"] = float(v) if rec["min"] is None else min(rec["min"], float(v))
                    rec["max"] = float(v) if rec["max"] is None else max(rec["max"], float(v))
                self._agg["metrics"] = metrics_agg
        except Exception:
            pass
        tree = None
        try:
            tree = _perf.build_perf_tree(perf_log)
        except Exception:
            tree = None
        # FPS and frame time
        fps = None
        ft_ms = None
        try:
            if state is not None and hasattr(state, 'clock') and callable(getattr(state.clock, 'get_fps', None)):
                fps_v = float(state.clock.get_fps())
                if fps_v > 0:
                    fps = round(fps_v, 2)
                    ft_ms = round(1000.0 / fps_v, 2)
                else:
                    fps = 0.0
                    ft_ms = 0.0
        except Exception:
            pass
        # Accumulate FPS/FT streaming stats
        try:
            if isinstance(self._agg, dict):
                if fps is not None:
                    fps_agg = self._agg.get("fps", {"count": 0, "sum": 0.0, "min": None, "max": None})
                    fps_agg["count"] += 1
                    fps_agg["sum"] += float(fps)
                    fps_agg["min"] = float(fps) if fps_agg["min"] is None else min(fps_agg["min"], float(fps))
                    fps_agg["max"] = float(fps) if fps_agg["max"] is None else max(fps_agg["max"], float(fps))
                    self._agg["fps"] = fps_agg
                if ft_ms is not None:
                    ft_agg = self._agg.get("frame_time_ms", {"count": 0, "sum": 0.0, "min": None, "max": None})
                    ft_agg["count"] += 1
                    ft_agg["sum"] += float(ft_ms)
                    ft_agg["min"] = float(ft_ms) if ft_agg["min"] is None else min(ft_agg["min"], float(ft_ms))
                    ft_agg["max"] = float(ft_ms) if ft_agg["max"] is None else max(ft_agg["max"], float(ft_ms))
                    self._agg["frame_time_ms"] = ft_agg
        except Exception:
            pass
        start_ts = float(self._session.get("_started_ts", 0.0) or 0.0)
        if self._store_samples:
            sample = {
                "t_offset_s": round(max(0.0, time.time() - start_ts), 3),
                "timestamp": self._now_iso(),
                "fps": fps,
                "frame_time_ms": ft_ms,
                "metrics_ms": flat,
                "metrics_tree": tree,
            }
            self._session["samples"].append(sample)

    def _flush_to_file(self, data: Dict[str, Any]) -> None:
        # Compute output path
        root = os.getcwd()
        out_dir = os.path.join(root, 'logs')
        os.makedirs(out_dir, exist_ok=True)
        started_at = data.get("started_at", datetime.utcnow().strftime('%Y-%m-%dT%H:%M:%SZ'))
        # Safe filename with timestamp
        ts_label = started_at.replace(':', '').replace('-', '').replace('T', 'T').replace('Z', 'Z')
        fname = f"session_{ts_label}.json"
        out_path = os.path.join(out_dir, fname)
        # Remove internal fields
        data = dict(data)
        data.pop("_started_ts", None)
        # Write
        tmp_path = out_path + '.tmp'
        with open(tmp_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        os.replace(tmp_path, out_path)

    # --- Helpers --------------------------------------------------------------
    def _append_sample_from_perf_log(self, perf_log: Dict[str, List[float]] | Any, state: Optional[Any]) -> None:
        """Append a single sample using a raw perf_log mapping (e.g., from Game)."""
        if self._session is None:
            return
        flat: Dict[str, float] = {}
        try:
            items = getattr(perf_log, 'items', None)
            if callable(items):
                it = perf_log.items()
            else:
                it = []
            for key, samples in it:
                if not samples:
                    continue
                recent = samples[-60:]
                if not recent:
                    continue
                avg_ms = (sum(recent) / len(recent)) * 1000.0
                flat[str(key)] = round(float(avg_ms), 3)
        except Exception:
            pass
        fps = None
        ft_ms = None
        try:
            if state is not None and hasattr(state, 'clock') and callable(getattr(state.clock, 'get_fps', None)):
                fps_v = float(state.clock.get_fps())
                if fps_v > 0:
                    fps = round(fps_v, 2)
                    ft_ms = round(1000.0 / fps_v, 2)
                else:
                    fps = 0.0
                    ft_ms = 0.0
        except Exception:
            pass
        started_ts = float(self._session.get("_started_ts", 0.0) or 0.0)
        sample = {
            "t_offset_s": round(max(0.0, time.time() - started_ts), 3),
            "timestamp": self._now_iso(),
            "fps": fps,
            "frame_time_ms": ft_ms,
            "metrics_ms": flat,
            "metrics_tree": None,
        }
        self._session["samples"].append(sample)
    def _build_summary(self, data: Dict[str, Any]) -> Dict[str, Any]:
        started_at = data.get("started_at")
        ended_at = data.get("ended_at")
        duration = data.get("duration_seconds")
        game_context = data.get("game_context", {})
        agg = self._agg or {"metrics": {}, "fps": {}, "frame_time_ms": {}}
        # Build metrics summary: avg from sum/count
        metrics_sum: Dict[str, Any] = {}
        for k, rec in sorted(agg.get("metrics", {}).items()):
            cnt = int(rec.get("count", 0) or 0)
            if cnt <= 0:
                continue
            s = float(rec.get("sum", 0.0) or 0.0)
            mn = rec.get("min")
            mx = rec.get("max")
            metrics_sum[k] = {
                "avg_ms": round(s / cnt, 3),
                "min_ms": round(float(mn), 3) if mn is not None else None,
                "max_ms": round(float(mx), 3) if mx is not None else None,
                "samples": cnt,
            }
        # FPS/frame time summary
        def _stat(rec):
            cnt = int(rec.get("count", 0) or 0)
            if cnt <= 0:
                return {"avg": None, "min": None, "max": None, "samples": 0}
            s = float(rec.get("sum", 0.0) or 0.0)
            return {
                "avg": round(s / cnt, 3),
                "min": round(float(rec.get("min")), 3) if rec.get("min") is not None else None,
                "max": round(float(rec.get("max")), 3) if rec.get("max") is not None else None,
                "samples": cnt,
            }
        fps_sum = _stat(agg.get("fps", {}))
        ft_sum = _stat(agg.get("frame_time_ms", {}))
        return {
            "session_id": data.get("session_id"),
            "started_at": started_at,
            "ended_at": ended_at,
            "duration_seconds": duration,
            "game_context": game_context,
            "fps": fps_sum,
            "frame_time_ms": ft_sum,
            "metrics": metrics_sum,
        }

    def _flush_summary_files(self, data: Dict[str, Any]) -> None:
        root = os.getcwd()
        out_dir = os.path.join(root, 'logs', 'benchmarks')
        os.makedirs(out_dir, exist_ok=True)
        started_at = data.get("started_at", datetime.utcnow().strftime('%Y-%m-%dT%H:%M:%SZ'))
        ts_label = started_at.replace(':', '').replace('-', '').replace('T', 'T').replace('Z', 'Z')
        summary = self._build_summary(data)
        # JSON summary
        json_path = os.path.join(out_dir, f'session_summary_{ts_label}.json')
        tmp_json = json_path + '.tmp'
        with open(tmp_json, 'w', encoding='utf-8') as f:
            json.dump(summary, f, ensure_ascii=False, indent=2)
        os.replace(tmp_json, json_path)
        # LOG table (human-friendly)
        log_path = os.path.join(out_dir, f'session_summary_{ts_label}.log')
        lines: List[str] = []
        lines.append(f"Session: {summary.get('session_id')}  Started: {summary.get('started_at')}  Ended: {summary.get('ended_at')}  Duration: {summary.get('duration_seconds'):.2f}s")
        gc = summary.get('game_context') or {}
        lines.append(f"Context: map={gc.get('map_name')} world_level={gc.get('world_level')} camera={gc.get('camera')}")
        fps = summary.get('fps') or {}
        ft = summary.get('frame_time_ms') or {}
        lines.append(f"FPS: avg={fps.get('avg')} min={fps.get('min')} max={fps.get('max')} samples={fps.get('samples')}")
        lines.append(f"FrameTime(ms): avg={ft.get('avg')} min={ft.get('min')} max={ft.get('max')} samples={ft.get('samples')}")
        lines.append("")
        # Build table header
        header = ["Metric Key", "Avg(ms)", "Min(ms)", "Max(ms)", "Samples"]
        rows = []
        metrics: Dict[str, Any] = summary.get('metrics', {})
        for k, rec in metrics.items():
            rows.append((k, rec.get('avg_ms'), rec.get('min_ms'), rec.get('max_ms'), rec.get('samples')))
        # Sort by Avg desc
        rows.sort(key=lambda r: (r[1] is None, r[1] if r[1] is not None else 0.0), reverse=True)
        # Compute column widths
        col_w = [len(h) for h in header]
        for r in rows:
            col_w[0] = max(col_w[0], len(str(r[0])))
            col_w[1] = max(col_w[1], len(f"{r[1]}"))
            col_w[2] = max(col_w[2], len(f"{r[2]}"))
            col_w[3] = max(col_w[3], len(f"{r[3]}"))
            col_w[4] = max(col_w[4], len(f"{r[4]}"))
        # Render table
        fmt = f"{{:<{col_w[0]}}}  {{:>{col_w[1]}}}  {{:>{col_w[2]}}}  {{:>{col_w[3]}}}  {{:>{col_w[4]}}}"
        lines.append(fmt.format(*header))
        lines.append("-" * (sum(col_w) + 2 * (len(header) - 1) + 2))
        for r in rows:
            a = "{:.3f}".format(r[1]) if isinstance(r[1], (int, float)) else str(r[1])
            mi = "{:.3f}".format(r[2]) if isinstance(r[2], (int, float)) else str(r[2])
            ma = "{:.3f}".format(r[3]) if isinstance(r[3], (int, float)) else str(r[3])
            lines.append(fmt.format(str(r[0]), a, mi, ma, str(r[4])))
        with open(log_path, 'w', encoding='utf-8') as f:
            f.write("\n".join(lines) + "\n")

    # --- Helpers --------------------------------------------------------------
    def _extract_game_context(self, game: Optional[Any]) -> Dict[str, Any]:
        ctx: Dict[str, Any] = {}
        try:
            if game is None:
                return ctx
            # Map name or current level
            try:
                ctx['map_name'] = getattr(game, 'map_name', None)
            except Exception:
                pass
            try:
                # ECS/world current level if available
                w = getattr(game, 'ecs', None)
                if w and hasattr(w, 'world'):
                    ctx['world_level'] = getattr(w.world, 'current_level', None)
            except Exception:
                pass
            # Camera info
            try:
                cam = getattr(game, 'camera', None)
                if cam:
                    ctx['camera'] = {
                        'zoom': float(getattr(cam, 'zoom', 0.0) or 0.0),
                        'offset_x': float(getattr(cam, 'offset_x', 0.0) or 0.0),
                        'offset_y': float(getattr(cam, 'offset_y', 0.0) or 0.0),
                        'screen_w': int(getattr(cam, 'screen_width', 0) or 0),
                        'screen_h': int(getattr(cam, 'screen_height', 0) or 0),
                    }
            except Exception:
                pass
        except Exception:
            pass
        return ctx

    @staticmethod
    def _now_iso() -> str:
        return datetime.utcnow().strftime('%Y-%m-%dT%H:%M:%SZ')


# Global singleton
recorder = DiagnosticsSessionRecorder()
