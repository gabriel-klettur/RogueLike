import os
import tempfile

from roguelike_engine.diagnostics.recorder import DiagnosticsSessionRecorder


def test_finish_handles_writer_errors_and_resets_state(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        # Redirect writes to tmp
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        # Force writer/save calls to raise
        monkeypatch.setattr(
            "roguelike_engine.diagnostics.recorder.write_session",
            lambda data: (_ for _ in ()).throw(RuntimeError("boom")),
        )
        monkeypatch.setattr(
            "roguelike_engine.diagnostics.recorder.write_summary",
            lambda data, agg: (_ for _ in ()).throw(RuntimeError("boom-2")),
        )
        monkeypatch.setattr(
            "roguelike_engine.diagnostics.recorder.save_benchmarks",
            lambda benches, base_dir=None: (_ for _ in ()).throw(RuntimeError("boom-3")),
        )

        rec = DiagnosticsSessionRecorder()
        rec.on_toggle(True, game=None)
        # finish should not raise and must reset state
        rec.finish_if_active(game=None)
        assert rec._active is False
        assert rec._session is None
        assert rec._agg is None
