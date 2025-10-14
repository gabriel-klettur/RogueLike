"""Compatibility shim for tests and legacy imports.

This module re-exports RendererManager and render_diagnostics_overlay so that
`tests` can import `roguelike_game.managers.core.render_manager` and monkeypatch
`render_diagnostics_overlay` safely.
"""
from roguelike_engine.diagnostics import render_diagnostics_overlay  # re-export
from .render.render_manager import RendererManager  # type: ignore[F401]
