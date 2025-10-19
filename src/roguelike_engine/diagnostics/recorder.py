import time
import threading
from typing import Optional, Any, Dict, List
import logging
from roguelike_engine.diagnostics.recorder_core.aggregator import (
    MetricsAggregator,
)
from roguelike_engine.diagnostics.recorder_core.snapshot import (
    build_flat_metrics,
    build_perf_tree,
    compute_fps_ft,
    sample_from_perf_log,
)
from roguelike_engine.diagnostics.recorder_core.writer import (
    write_session,
    write_summary,
)
from roguelike_engine.diagnostics.recorder_core.context import (
    now_iso,
    extract_game_context,
)
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
        self._agg: MetricsAggregator | None = None
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
                # Be robust against failures in time.time() (e.g., patched iterator exhausted in tests)
                try:
                    now_ts = float(time.time())
                except Exception:
                    now_ts = start_ts
                self._session["duration_seconds"] = max(0.0, now_ts - start_ts)
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
                # Persist session and summary
                try:
                    session_copy = dict(self._session)
                    out_path = write_session(session_copy)
                    try:
                        logger.info(f"Diagnostics session saved: {out_path}")
                    except Exception:
                        pass
                    try:
                        write_summary(session_copy, self._agg)
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
        # Build stable context snapshot at start
        ctx = extract_game_context(game)
        # Stable session id using UTC ISO string without separators
        sid = now_iso().replace('-', '').replace(':', '')
        self._session = {
            "session_id": f"diag_{sid}",
            "started_at": self._now_iso(),
            "_started_ts": started_ts,
            "sampling_interval_seconds": self._sampling_interval,
            "game_context": ctx,
            "samples": [],
        }
        # Init streaming aggregator
        self._agg = MetricsAggregator()

    def _append_sample(self, model: Any, state: Optional[Any]) -> None:
        if self._session is None:
            return
        # Build metrics flat mapping and tree from current perf_log
        perf_log: Dict[str, List[float]] = getattr(model, 'perf_log', {}) or {}
        flat: Dict[str, float] = build_flat_metrics(perf_log)
        # Accumulate streaming stats per key
        try:
            if isinstance(self._agg, MetricsAggregator):
                for k, v in flat.items():
                    self._agg.update_metric(k, float(v))
        except Exception:
            pass
        tree = build_perf_tree(perf_log)
        # FPS and frame time
        fps, ft_ms = compute_fps_ft(state)
        # Accumulate FPS/FT streaming stats
        try:
            if isinstance(self._agg, MetricsAggregator):
                if fps is not None:
                    self._agg.update_fps(float(fps))
                if ft_ms is not None:
                    self._agg.update_frame_time(float(ft_ms))
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

    # --- Helpers --------------------------------------------------------------
    def _append_sample_from_perf_log(self, perf_log: Dict[str, List[float]] | Any, state: Optional[Any]) -> None:
        """Append a single sample using a raw perf_log mapping (e.g., from Game)."""
        if self._session is None:
            return
        try:
            started_ts = float(self._session.get("_started_ts", 0.0) or 0.0)
            sample = sample_from_perf_log(perf_log, state, started_ts)
            self._session["samples"].append(sample)
        except Exception:
            return

    # --- Helpers --------------------------------------------------------------
    def _extract_game_context(self, game: Optional[Any]) -> Dict[str, Any]:
        # Delegate to shared context extractor for consistency
        return extract_game_context(game)

    @staticmethod
    def _now_iso() -> str:
        return now_iso()


# Global singleton
recorder = DiagnosticsSessionRecorder()
